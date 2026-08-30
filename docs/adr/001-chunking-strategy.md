# ADR-001: Chunking Strategy for Clinical Guideline Documents

## Status

Accepted — 2026-08-28

## Context

Domain Copilot ingests three categories of source material (see `seed-data/manifest.json`):

1. **Clinical practice guidelines** (WHO, NICE, IDSA, ISPAD — PDF) — long, narrative documents with a table of contents and numbered recommendation sections (e.g., "1.5 Managing hyperglycaemia", "Contraindications", "Monitoring").
2. **FDA drug labels** (PDF) — highly structured, near-identical section headers across every document: Indications and Usage, Dosage and Administration, Contraindications, Warnings and Precautions, Drug Interactions, Use in Specific Populations.
3. **Synthetic patient case files** (DOCX) — short, uniformly templated documents (Case Metadata, Patient Summary, Clinical Question, Expected Behavior).

The retrieval quality of this system is judged against a specific, named failure mode from the D0 (Healthcare) domain pack: **confident hallucination of dosage or contraindication information**. Correct behavior is refusal in the face of insufficient evidence, not inference. This constraint drives the chunking decision more than general retrieval-quality concerns do, because chunking is the first place in the pipeline where a dosage statement can become detached from the condition, drug, or population it qualifies.

Two candidate strategies were evaluated:

- **Fixed-size chunking with overlap** — split text into ~400–600 token windows with a ~15% overlap, regardless of document structure.
- **Section-aware (structure-based) chunking** — split at recognized section boundaries (headings such as "Contraindications", "Dosage and Administration", numbered guideline recommendations), keeping each section's content together as one or more chunks, with fixed-size chunking used only *within* an oversized section.

## Decision

We will use **section-aware chunking as the primary strategy**, with a **documented fallback to fixed-size chunking with overlap** for any document (or document region) where section headers cannot be reliably detected.

### Why section-aware chunking, specifically for D0

A drug label's Dosage and Administration section frequently states a dose that is only valid conditional on information stated in an adjacent-but-distinct section — for example, a standard adult dose in "Dosage and Administration" versus a renal-impairment-adjusted dose in "Use in Specific Populations," or a dose that is stated as contraindicated outright in "Contraindications." Fixed-size windows are drawn without regard to these boundaries. A 500-token window can easily end mid-sentence at the close of "Dosage and Administration" and pick up the first few sentences of "Drug Interactions" in the same chunk, or conversely, split the qualifying renal-dose caveat into a different chunk than the headline dose it modifies.

When such a chunk is retrieved and cited, the system (or a clinician skimming the citation) sees a dosage statement without its controlling qualifier. This is precisely the shape of the central risk called out for D0: not the model inventing a number from nothing, but the model — or a downstream reader — treating a partial, decontextualized true statement as if it were the complete, unconditional one. Section-aware chunking keeps a labeled section's content (and its internal qualifiers) together as a retrieval unit, so a chunk tagged `section: "Dosage and Administration"` for a given drug is far more likely to include the caveats that section actually contains, rather than an arbitrary byte-range that happens to start there.

Section-aware chunking also gives the **Safety Checker agent** a much stronger tool contract: it can request "the Contraindications section for drug X" as a targeted, typed retrieval rather than hoping a semantically similar fixed-size window happened to contain the word "contraindicated." Combined with the deterministic drug-interaction lookup (FR-4, Safety Checker's non-LLM code path), section-aware retrieval is the RAG-side complement that keeps the *guideline-citation* half of the answer similarly grounded, rather than only the structured-lookup half.

### Chunking algorithm (summary)

1. **Structure detection pass.** Parse the extracted document text for heading patterns:
   - FDA labels: numbered/named sections following the standardized labeling format (e.g., "4 CONTRAINDICATIONS", "7 DRUG INTERACTIONS") — high reliability, consistent across all ingested labels.
   - NICE/WHO/IDSA/ISPAD guidelines: numbered headings and sub-headings from the document's own table of contents, matched against extracted heading-style text runs (font size/weight where available from PDF structure, or numbering patterns as a fallback).
2. **Section-bounded chunks.** Each detected section becomes one chunk if it fits within the target chunk size (~300–800 tokens, chosen to comfortably hold a single guideline recommendation or label subsection). Oversized sections (e.g., a multi-page "Warnings and Precautions") are split further using fixed-size chunking *within* that section, preserving the section label as metadata on every resulting sub-chunk and adding a `chunk_index_within_section` so the Guideline Researcher can request neighboring chunks if a sub-chunk alone is insufficient.
3. **Metadata attached to every chunk:** `document_id`, `section_title`, `section_path` (e.g., "Dosage and Administration > Renal Impairment"), `page_number(s)`, `document_version/date`, `chunk_index_within_section`. This metadata is what makes structured citations (FR-2) traceable to an exact chunk rather than an exact document.
4. **Fallback path.** If the structure-detection pass finds no reliable headings (this is expected for the synthetic patient case DOCX files, which are short enough not to need section splitting, and would also apply to any future guideline PDF with non-standard formatting), the document falls back to fixed-size chunking with ~15% overlap, and each chunk is tagged `chunking_method: "fixed-size-fallback"` in its metadata. This tag is surfaced in the evaluation harness (FR-3) so that retrieval-quality metrics can be broken out by chunking method, making the trade-off visible rather than hidden.

## Consequences

**Positive:**
- Dosage/contraindication statements retain their qualifying context far more often than under fixed-size chunking, directly reducing the risk of confidently presenting a partial truth as complete.
- Structured citations (FR-2) can point to a named section ("Contraindications," page 4) rather than an arbitrary offset, which is more meaningful to a clinician reviewing the Documentation Drafter's output at the approval gate.
- The Safety Checker and Guideline Researcher agents can issue more precise tool calls (e.g., "fetch Contraindications section for drug X") rather than relying purely on semantic similarity.
- The `chunking_method` metadata tag makes the fixed-size fallback's usage rate and retrieval-quality impact directly measurable rather than an invisible implementation detail — this is honestly reportable in `docs/EVALUATION.md`.

**Negative / trade-offs:**
- Section-aware chunking requires a heading-detection step per source type (FDA labels vs. WHO/NICE/IDSA/ISPAD guidelines vs. synthetic cases), which is more implementation effort than a single uniform fixed-size splitter, and is not fully format-agnostic — a new guideline source with unfamiliar formatting may silently fall back to fixed-size chunking with no code error, only a metadata flag, so this must be monitored rather than assumed correct.
- Section sizes are uneven: a short "Indications" section and a long "Warnings and Precautions" section do not chunk to comparable sizes, which can bias retrieval toward returning very small, low-context chunks for short sections. This is mitigated by allowing small adjacent sections to be minimally padded with their heading text only (not merged with unrelated sections), rather than truly variable-size chunks that vary by an order of magnitude.
- Fixed-size fallback chunks lose the section-label metadata, so their citations are necessarily coarser (page number and document version only). This is an accepted, documented gap rather than a silent one.

## Alternatives Considered

**Fixed-size chunking with overlap only (rejected as sole strategy).** Simple to implement uniformly across all document types and formats, requires no per-source-type parsing logic, and is trivially robust to any input format. Rejected as the *primary* strategy because it is exactly the approach most likely to separate a dosage statement from its qualifying condition — the specific failure mode D0's central risk calls out. Retained as the documented fallback because its simplicity and format-agnosticism are genuinely valuable when structure detection fails, and a fallback that never triggers is worse than one that is honestly used and measured.

**Fully semantic/embedding-based chunking (e.g., splitting at points of maximal semantic-similarity drop between adjacent sentences).** Would adapt to document structure without relying on heading detection, and could in principle work on documents with no visible headings at all. Rejected for this corpus because (a) it requires an additional embedding pass purely for the chunking decision, adding latency and cost that competes directly with the Cost Governor twist's token-budget constraints, and (b) on structured documents like FDA labels, semantic-similarity boundaries do not reliably align with the regulatory section boundaries that actually carry the clinical qualifiers we need — a "Dosage and Administration" section can contain semantically varied sentences (a headline dose, then a renal caveat, then a hepatic caveat) that a similarity-drop detector might split apart even though they belong together as one retrievable unit for the Safety Checker's purposes. Section-aware chunking is a better fit specifically because these documents *already have* an authoritative structure (author-imposed, not merely stylistic) that a purely statistical method would be reverse-engineering at extra cost.

**Document-level retrieval only (no chunking).** Considered and quickly rejected: FDA labels and guideline PDFs run to many pages, well beyond a single retrieval unit's useful context window, and would make structured, chunk-level citations (a hard FR-2 requirement) impossible.
