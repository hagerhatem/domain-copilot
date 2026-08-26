# Business Requirements Document (BRD)
## Domain Copilot — D0T3 (Healthcare: Clinical Evidence & Documentation + Cost Governor)

**Status:** Draft — living document, updated as implementation progresses
**Owner:** [Your name]
**Variant:** D0T3
- `Domain = (last two digits of National ID) mod 7 = 0` → **D0: Healthcare — clinical evidence & documentation**
- `Twist = (sum of all digits of National ID) mod 8 = 3` → **T3: Cost Governor**

---

## 1. Context & Problem Statement

Clinicians preparing a case note or verifying a treatment decision routinely need to cross-reference multiple sources: institutional clinical guidelines, drug interaction/contraindication references, and the specifics of the patient case in front of them. In practice this means:

- Manually searching PDF/DOCX guideline documents that are long, frequently revised, and inconsistently indexed.
- Re-deriving drug interaction checks from memory or a separate lookup tool, with no single audit trail connecting the check to the note that relied on it.
- Drafting the clinical note itself, a task that is mechanical but time-consuming, and one where copy-paste errors or unsupported claims are a known source of documentation risk.
- No institutional visibility into how much computational/LLM cost is being spent per clinician, per case, or per month — a problem for any organization introducing AI-assisted documentation under a fixed budget.

**Problem statement:** Clinicians lack a single tool that retrieves the *correct, current, version-traceable* guidance for a case, checks safety-critical facts deterministically rather than by LLM guess, drafts a defensible note, and puts a human explicitly in control of anything that gets finalized — all while an administrator can see and cap what it costs to run.

Domain Copilot addresses this as an assessment project demonstrating enterprise-grade engineering discipline (Clean Architecture, observability, security, cost governance) on a **fully synthetic** corpus and case set, on a **zero-paid-service** stack.

---

## 2. Personas

### 2.1 Clinician (primary user)
A physician or nurse practitioner using the system to prepare a clinical note for a (synthetic) patient case. Needs fast, cited answers to guideline questions, a clear warning when evidence is insufficient, and full control over anything that gets written into the final record — nothing is finalized without their explicit sign-off. Time-pressured; will not tolerate silent hallucination or an opaque "black box" answer with no source.

### 2.2 Admin / Compliance Officer (secondary user)
Responsible for oversight rather than clinical judgment: managing user accounts and roles, setting and adjusting per-user token/cost budgets, reviewing the audit trail of approvals/rejections on drafted notes, and reviewing spend across the organization. Needs the system to *actually* enforce budget and approval rules server-side — not just display a friendly warning that a determined user can bypass.

### 2.3 (Supporting) Corpus Curator — same person as the Clinician/Admin in this assessment, modeled as a distinct role for realism
Responsible for ingesting and versioning the clinical guideline corpus (PDF/DOCX), confirming ingestion succeeded per document, and re-ingesting when a guideline version changes. Cares about idempotent re-ingestion and clear per-document failure reporting — not wanting a bad PDF to silently corrupt the index.

---

## 3. Measurable Objectives

| ID | Objective | Target |
|---|---|---|
| OBJ-1 | Golden-set answer quality | ≥90% of the ≥25-question golden set answered with either a correct, citation-backed answer or a correct refusal (no confident wrong answer) |
| OBJ-2 | Adversarial robustness | 100% of the ≥5 adversarial golden-set cases (out-of-corpus, ambiguous, injected-prompt, conflicting-source) handled correctly — i.e., refused, flagged, or resisted as designed, not silently answered |
| OBJ-3 | Retrieval quality | Retrieval hit-rate ≥ a documented baseline threshold (recorded honestly in `docs/EVALUATION.md`, not asserted without measurement) |
| OBJ-4 | Zero un-approved finalizations | 0 clinical notes reach "finalized" status without a recorded Clinician approval/edit-approval decision — enforced in code, verified by test |
| OBJ-5 | Budget enforcement | 100% of workflow runs attempted after a user's budget is exhausted are blocked server-side before any LLM call is made, and this is demonstrable via an automated test, not just manual observation |
| OBJ-6 | Cost estimation accuracy | Pre-flight cost estimate for a run is within a documented tolerance (e.g., ±20%, to be measured and reported honestly) of actual metered spend for that run |
| OBJ-7 | Streaming responsiveness | Token-level output begins streaming to the client within a documented latency bound (e.g., first token ≤ X seconds under local Ollama, ≤ Y seconds under hosted free-tier), and client-initiated cancellation measurably stops server-side LLM/tool work, not just the UI spinner |
| OBJ-8 | Free-tier constraint compliance | 100% of the system's required services run with no paid subscription and no credit card — verified by a documented full run using only the local Ollama fallback, no hosted API key present |

---

## 4. Functional & Non-Functional Requirements

Each requirement below is mapped to the corresponding FR-x from the assignment brief (Section 6) for traceability.

### 4.1 Functional Requirements

| ID | Requirement | Acceptance Criteria | Maps to |
|---|---|---|---|
| BR-01 | The system shall ingest clinical guideline and patient-case documents in at least 2 formats (PDF, DOCX) through explicit, separable pipeline stages. | Ingesting a sample PDF and a sample DOCX both succeed; each stage (extract, clean, chunk, embed, index) is independently invokable/loggable; each document/chunk is stored with source, section, page, and version metadata. | FR-1 |
| BR-02 | Re-ingesting the same document version shall be idempotent. | Running ingestion twice on an unchanged document produces no duplicate chunks in Qdrant or SQL Server; a changed document version produces a new version record without deleting the old one. | FR-1 |
| BR-03 | Ingestion shall report per-document status (success/failure) with a reason on failure. | A deliberately malformed PDF is ingested; the system reports failure for that document without halting ingestion of the remaining corpus. | FR-1 |
| BR-04 | The system shall use a documented, justified chunking strategy suited to clinical guideline structure (e.g., section/heading-aware chunking). | `docs/ARCHITECTURE.md`/ADR documents the chosen chunk size, overlap, and boundary logic with a stated rationale referencing guideline document structure. | FR-2 |
| BR-05 | Retrieval shall combine dense vector search (Qdrant) and keyword search (SQL Server full-text) via a documented fusion method. | A query with an exact drug name or code returns correct keyword-matched chunks even when dense embedding similarity alone would rank them lower; fusion logic is unit-tested. | FR-2 |
| BR-06 | Retrieval shall support at least one additional enhancement (e.g., metadata filtering by guideline version/date). | A query scoped to "current guideline only" excludes superseded versions of the same guideline; verified by test. | FR-2 |
| BR-07 | Every answer shall include structured citations traceable to the exact source chunk (document, section, page, version). | Any answer in the UI displays a citation that resolves to the specific chunk used; citation IDs are logged and queryable by run ID. | FR-2 |
| BR-08 | The system shall refuse to answer when retrieved evidence is insufficient or ambiguous, rather than inferring an answer. | A golden-set out-of-corpus question and an ambiguous question both produce an explicit refusal response (not a low-confidence guess) with a stated reason. | FR-2 |
| BR-09 | A golden set of ≥25 Q/A pairs, including ≥5 adversarial cases, shall be maintained and runnable via an automated harness. | `docs/EVALUATION.md` reports real hit-rate, groundedness, and refusal-correctness numbers from an actual harness run, with honest interpretation of failures. | FR-3 |
| BR-10 | The multi-agent workflow shall include ≥3 specialized agents (Guideline Researcher, Safety Checker, Documentation Drafter) plus an orchestrator, each with a restricted tool allow-list and typed input/output contract. | Each agent's tool allow-list is enforced in code (calling a disallowed tool is rejected, not merely undocumented); contract types are defined and validated. | FR-4 |
| BR-11 | The Safety Checker agent shall verify drug interactions/contraindications via a deterministic, code-based lookup — never an LLM guess. | Given a known interacting drug pair from the synthetic reference data, the Safety Checker flags it via the lookup service even if the LLM is stubbed/unavailable. | FR-4 |
| BR-12 | At least one tool shall be a write/side-effecting tool (finalizing a clinical note), and it shall never execute without passing the human approval gate. | Attempting to programmatically invoke the note-finalization tool without a recorded Clinician approval decision fails/is rejected; covered by an integration test. | FR-4, Business Rule BRULE-1 |
| BR-13 | Orchestration shall follow a named, justified pattern (Pipeline) with a max-iteration breaker, per-step timeout, retry with backoff, and graceful degradation to plain RAG on agent failure. | A forced agent failure (e.g., simulated timeout) results in the run degrading to a plain-RAG answer rather than crashing or hanging; max-iteration and timeout values are configurable and tested. | FR-5 |
| BR-14 | Every run shall be inspectable step-by-step by run ID, including the approval gate decision (approve/reject/edit-and-approve) with a full audit record (who, when, what, why-if-rejected). | Querying a completed run by ID returns every agent step, tool call, and the approval decision with timestamp and actor identity. | FR-5 |
| BR-15 | The system shall stream tokens to the client via SSE with live agent-progress events (not a static spinner), and client-initiated cancellation shall stop server-side work. | The UI displays incremental token output and named progress events (e.g., "Guideline Researcher retrieving..."); cancelling mid-run measurably halts the in-flight LLM/tool call server-side (verified via logs/metrics, not just UI state). | FR-6 |
| BR-16 | The system shall expose a documented HTTP API (OpenAPI/Swagger) and a functional Angular UI covering ingest, ask-with-citations, run-the-workflow, approval-gate actions, and trace viewing, with persistent session history. | Swagger UI lists all endpoints with request/response schemas; each listed UI capability is demonstrably present and session history survives a page reload. | FR-7 |
| BR-17 | The system shall enforce authentication (JWT) and at least two roles (Clinician, Admin) with genuinely different server-side permissions. | An Admin-only endpoint (e.g., budget configuration) returns 403 for a Clinician-role token; verified by an automated test, not just UI hiding of the button. | FR-8 |
| BR-18 | The system shall propagate a correlation ID through request → orchestrator → agent → LLM call, persist per-request token/cost accounting, and expose health/readiness endpoints. | A single correlation ID appears in logs/traces across all layers for one request; `/health` and `/ready` endpoints exist and reflect actual dependency status (SQL Server, Qdrant, LLM provider reachability). | FR-9 |

### 4.2 Cost Governor (Twist T3) Requirements

| ID | Requirement | Acceptance Criteria | Maps to |
|---|---|---|---|
| BR-19 | Per-user token budgets shall be stored and enforced server-side in SQL Server, never trusted from the client. | Manipulating client-side state/requests cannot increase the effective budget; budget balance is read from and decremented in the server-side data store. | Twist T3 |
| BR-20 | Model routing between cheap/local and stronger models shall be deterministic, code-based logic — never delegated to the LLM itself. | Routing decisions are unit-testable pure functions of task metadata (e.g., "final note drafting" always routes to the stronger model; "simple extraction" always routes to the cheap/local model), independent of any LLM output. | Twist T3 |
| BR-21 | The system shall estimate token/cost usage for a full agentic workflow run before executing it, and compare the estimate to the user's remaining budget. | Before a run starts, an estimated cost is computed and displayed/logged; a run whose estimate exceeds remaining budget is blocked pre-execution with a clear reason. | Twist T3 |
| BR-22 | When a user's budget is exhausted, the system shall actually prevent further workflow runs, enforced in deterministic backend code — not merely a UI warning. | With budget forced to zero via the database, an attempt to start a new run is rejected server-side (e.g., HTTP 402/403 with a `BudgetExceededError`) even if the request bypasses the UI entirely (e.g., via direct API call). | Twist T3 |
| BR-23 | The system shall provide a spend view showing historical spend per user, broken down by run and by agent step. | The spend endpoint/screen returns, for a given user, a list of runs each with total cost and a per-agent-step cost breakdown that sums to the run total. | Twist T3 |

### 4.3 Non-Functional Requirements

| ID | Requirement | Acceptance Criteria | Maps to |
|---|---|---|---|
| BR-24 | The entire system shall run with no paid subscription and no credit card requirement. | A full demo run is completed using only local Ollama (no hosted API key configured), documented step-by-step in the README. | Section 2 constraint |
| BR-25 | Swapping the LLM provider, embedding model, or vector store shall require only configuration changes plus one new adapter class. | Demonstrated by adding a second `ILLMProvider` implementation without touching `Domain` or `Application` project code (verified by `git diff` scoped to `Infrastructure` + config only). | Section 3 acceptance test |
| BR-26 | The corpus shall contain only fully synthetic data — no real patient information under any circumstances. | All ingested documents and patient case files are authored/generated as fictional; `docs/BRD.md` and `README.md` explicitly state this; a manual review checklist confirms no real PII before each corpus update is committed. | Section 4 constraint |
| BR-27 | Security controls shall address OWASP Web Top 10 and OWASP LLM Top 10, including resistance to indirect prompt injection via ingested documents. | `docs/SECURITY.md` documents each control; ≥3 injection test cases embedded in ingested documents are demonstrated not to alter agent behavior or bypass the approval gate. | Section 7 |
| BR-28 | No secrets shall ever be committed to Git history. | A full-history secret scan (e.g., gitleaks/truffleHog) run before submission reports zero findings; `.env.example` contains no real values. | Section 7 |

---

## 5. Out of Scope

- **Real patient data or EHR/EMR integration** of any kind (e.g., HL7/FHIR connectivity, real hospital systems) — the system operates exclusively on synthetic case files.
- **Regulatory certification** as a medical device or clinical decision support tool (e.g., FDA/CE marking); this is an academic/assessment build, not a deployable clinical product.
- **Multi-language support** — the corpus, UI, and evaluation are English-only.
- **Mobile-native applications** — the Angular UI targets desktop/tablet browsers only; no dedicated iOS/Android app.
- **Multi-tenant organizational hierarchy** beyond the two roles (Clinician, Admin) — no department-level or facility-level budget hierarchies.
- **Real-time collaborative editing** of a clinical note by multiple clinicians simultaneously — the approval workflow is single-clinician, single-decision.
- **Payment/billing integration** — the Cost Governor tracks and enforces token/cost budgets internally; it does not connect to any real billing or invoicing system.
- **Production-grade autoscaling, managed cloud infrastructure, or high-availability deployment** — covered only conceptually in `docs/SYSTEM-DESIGN.md` Part A (unconstrained target architecture), not implemented in the MVP.
- **Automated guideline-currency monitoring** (e.g., auto-detecting when an external guideline body publishes an update) — guideline versioning is manual, triggered by re-ingestion.

---

## 6. Business Rules

- **BRULE-1:** No clinical note may be finalized without an explicit Clinician approval, edit-and-approval, or rejection decision recorded against that specific draft. There is no "auto-approve" path, including for low-risk-seeming cases.
- **BRULE-2:** No destructive or write/side-effecting tool call (in particular, note finalization) may execute without first passing the human approval gate — this is enforced at the tool-invocation layer, not merely by convention in agent prompts.
- **BRULE-3:** The Safety Checker agent's drug interaction/contraindication verdicts must always come from the deterministic lookup service; an LLM's own assertion about a drug interaction is never treated as authoritative and must be cross-checked against the lookup before being included in a note.
- **BRULE-4:** If retrieved evidence is insufficient, ambiguous, or conflicting, the system must refuse to answer or must flag the conflict explicitly — it must never merge conflicting sources into a single confident-sounding answer.
- **BRULE-5:** A user's token/cost budget is authoritative only as stored server-side in SQL Server; no client-supplied budget or usage figure is ever trusted for enforcement decisions.
- **BRULE-6:** A workflow run may not begin execution if its pre-flight cost estimate exceeds the user's remaining budget; this check happens before any LLM call is made, not after.
- **BRULE-7:** Every approval-gate decision (approve/reject/edit-and-approve) must be attributed to an authenticated user identity, timestamped, and — for rejections — accompanied by a reason. This audit record is immutable once written.
- **BRULE-8:** No real personal or patient data may be introduced into the corpus, patient case files, or logs at any point; only synthetic data is permitted, and this rule takes precedence over convenience during testing or demoing.

---

## 7. Assumptions

- The evaluator/instructor accepts a fully synthetic corpus and patient case set as satisfying the "clinical evidence" requirement, since no real clinical data is available or permitted.
- Free-tier hosted LLM API quotas (e.g., Groq, OpenAI free credits) will be sufficient for development and demo purposes, but may be rate-limited or exhausted without notice — hence the mandatory Ollama fallback.
- A single developer (me) is both the primary implementer and the sole "Clinician"/"Admin" test user during development; multi-user concurrent load is not a design target beyond what's needed to demonstrate per-user budget isolation.
- SQL Server Developer Edition and Qdrant's open-source self-hosted mode are acceptable "free-tier" interpretations under the assessment's constraints, since both are free for non-production use and require no credit card.
- The assessment values honest documentation of gaps and trade-offs at least as much as feature completeness — this assumption directly shapes how `docs/SYSTEM-DESIGN.md` and this BRD are written (Part A/Part B split, explicit deferred items).
- Local Ollama models (e.g., Llama 3, Phi-3) run acceptably on available development hardware within a reasonable latency for demo purposes; if not, this will be documented as a limitation rather than silently worked around.

---

## 8. Risks

| Risk | Description | Mitigation |
|---|---|---|
| **RISK-1: Hallucinated dosage/contraindication information** | The single highest-stakes risk in this domain — an LLM confidently stating an incorrect dosage or missing a contraindication could, in a real (non-synthetic) deployment, cause patient harm. | Deterministic, code-based Safety Checker lookup (never LLM-guessed) per BRULE-3; mandatory refusal behavior on insufficient evidence (BRULE-4); human approval gate as final backstop (BRULE-1/2); adversarial golden-set cases specifically targeting this failure mode (BR-09). |
| **RISK-2: Budget exhaustion mid-workflow** | A user could exhaust their token budget partway through a multi-agent run (e.g., after the Guideline Researcher and Safety Checker steps but before Documentation Drafter), leaving a partial, unusable result and an unclear cost/refund situation. | Pre-flight cost estimation against remaining budget before the run starts (BR-21); per-step cost accounting so a mid-run shortfall is visible and attributable (BR-23); documented policy (to be finalized in `docs/adr/0004-cost-governor-design.md`) on whether partial runs are billed, refunded, or budget-reserved upfront. |
| **RISK-3: Free-tier API quota exhaustion or instability** | Hosted free-tier LLM/embedding APIs may throttle, change terms, or become unavailable without notice, jeopardizing demo reliability. | Documented, tested fallback chain to local Ollama (Section 3); evaluation harness and demo path both validated to work in Ollama-only mode (OBJ-8). |
| **RISK-4: Indirect prompt injection via ingested documents** | A malicious or adversarially-crafted guideline/case document could attempt to manipulate agent behavior during retrieval or drafting (e.g., embedded instructions telling the Drafter to bypass the approval gate). | ≥3 documented injection test cases in the golden set (BR-09, OBJ-2); per-agent tool allow-lists enforced in code, not prompt-only (BR-10); approval gate is a code-level gate on the tool call itself, not a prompt-level convention (BRULE-2). |
| **RISK-5: Chunking/retrieval quality insufficient for clinical precision** | Poor chunk boundaries (e.g., splitting a dosage table mid-row) could cause correct-looking but incomplete citations. | Section-aware chunking strategy justified in ADR-0003; hit-rate and groundedness measured and reported honestly in `docs/EVALUATION.md`, including failure cases, rather than asserted. |
| **RISK-6: Synthetic corpus not realistic enough to stress-test the system meaningfully** | An overly simple synthetic corpus could make retrieval and safety-checking trivially easy, undermining the evaluation's credibility. | Corpus deliberately includes ≥30 documents with overlapping/conflicting guidance across versions, at least one deliberately ambiguous case, and injected adversarial content, to force genuine refusal and conflict-handling behavior. |
| **RISK-7: Scope/time overrun given the breadth of FR-1 through FR-9** | The full checklist is extensive for a single-developer academic timeline; some items risk being under-built. | Explicit "Partial/Deferred" status is a valid, expected outcome per the traceability matrix below; `docs/SYSTEM-DESIGN.md` Part B gap table documents what was cut and why, per the assessment's stated reward for candor. |
| **RISK-8: Local Ollama model quality/latency insufficient for a convincing demo** | Small local models may produce weaker clinical drafting quality or slower streaming than the hosted free-tier model, especially on modest hardware. | Both providers benchmarked and documented honestly in `docs/EVALUATION.md`; routing logic (BR-20) deliberately sends high-stakes drafting to the stronger available model when budget allows, with the trade-off stated explicitly rather than hidden. |

---

## 9. Traceability Matrix

*To be filled in progressively as implementation proceeds. Status values: `Implemented` / `Partial` / `Deferred`. Evidence should reference a PR, test, file, or doc section.*

| BR ID | Requirement (short) | Status | Evidence |
|---|---|---|---|
| BR-01 | Multi-format ingestion pipeline | | |
| BR-02 | Idempotent re-ingestion | | |
| BR-03 | Per-document ingestion status reporting | | |
| BR-04 | Documented chunking strategy | | |
| BR-05 | Hybrid dense + keyword retrieval | | |
| BR-06 | Metadata filtering enhancement | | |
| BR-07 | Structured, traceable citations | | |
| BR-08 | Refusal on insufficient evidence | | |
| BR-09 | Golden set + evaluation harness | | |
| BR-10 | ≥3 agents + orchestrator, typed contracts, tool allow-lists | | |
| BR-11 | Deterministic Safety Checker lookup | | |
| BR-12 | Approval-gated write tool | | |
| BR-13 | Pipeline orchestration with resilience controls | | |
| BR-14 | Step-by-step run inspection + audit | | |
| BR-15 | SSE streaming + real cancellation | | |
| BR-16 | OpenAPI + Angular UI coverage | | |
| BR-17 | JWT auth + role-based permissions | | |
| BR-18 | Correlation ID + cost accounting + health endpoints | | |
| BR-19 | Server-side per-user token budgets | | |
| BR-20 | Deterministic model routing | | |
| BR-21 | Pre-flight cost estimation | | |
| BR-22 | Hard budget cut-off | | |
| BR-23 | Spend view (per run, per agent step) | | |
| BR-24 | Zero-paid-service operation | | |
| BR-25 | Provider/store swap via config + one adapter | | |
| BR-26 | Fully synthetic corpus | | |
| BR-27 | OWASP Web + LLM Top 10 controls | | |
| BR-28 | No secrets in Git history | | |

---

*This document will be revised as scope decisions are made during implementation; all changes should be reflected here and cross-referenced in `docs/SYSTEM-DESIGN.md` Part B's gap table where a requirement ends up Partial or Deferred.*