# DomainCopilot.Evaluation

FR-3 evaluation harness for Domain Copilot (D0T3). Loads `eval/golden-set.json`,
runs every question through a retrieval pipeline and a workflow pipeline, and
writes a markdown report with retrieval hit-rate, groundedness, and
refusal/hedge/gate correctness.

## Where this goes in the repo

Put this folder at `src/DomainCopilot.Evaluation/`, alongside
`DomainCopilot.Domain`, `.Application`, `.Infrastructure`, and `.Api`. Then:

```bash
dotnet sln add src/DomainCopilot.Evaluation/DomainCopilot.Evaluation.csproj
```

`eval/golden-set.json` should already be sitting at the repo root per the
earlier setup (sibling to `src/`, not inside it).

## Running it today (stub pipeline)

No NuGet packages are referenced - this builds with just the .NET 8 SDK:

```bash
dotnet run --project src/DomainCopilot.Evaluation -- \
  --golden-set eval/golden-set.json \
  --output eval/report.md
```

**This will run end-to-end right now**, but it is not measuring your real
system yet. `Pipeline/StubRetrievalPipeline.cs` does naive lexical overlap
against a handful of manually-written blurbs (not real Qdrant/SQL hybrid
retrieval), and `Pipeline/StubWorkflowPipeline.cs` always returns a fixed
placeholder answer that never refuses and never invokes the approval gate.
The report will (correctly) show most refusal/hedge/gate checks failing -
that is the expected, honest signal for "the real pipeline isn't wired in
yet," not a bug in the harness.

## Swapping in the real pipeline

Once ingestion/retrieval (Phase 1-2) and the multi-agent orchestrator exist:

1. Add a `ProjectReference` to `DomainCopilot.Application` in the `.csproj`.
2. Implement `Contracts.IRetrievalPipeline` and `Contracts.IWorkflowPipeline`
   as thin adapters over the real Application-layer ports (or point these
   interfaces directly at the real ports and delete the local copies -
   either is fine, the metrics code doesn't care).
3. In `Program.cs`, replace the two `new Stub...Pipeline()` lines in the
   composition root with the real adapters, and flip `usingStubPipeline` to
   `false`.
4. Delete `Pipeline/StubRetrievalPipeline.cs`, `Pipeline/StubWorkflowPipeline.cs`,
   and `Pipeline/StubCorpusIndex.cs`.

Nothing in `GoldenSet/`, `Metrics/`, or `Reporting/` should need to change.
If it does, that's a sign the stub interfaces didn't match the real port
shapes closely enough - worth a quick ADR note either way.

## What each metric actually measures

- **Retrieval hit-rate**: for each question, did at least one of
  `expected_source_documents` appear in the top-K retrieved chunks? Reported
  as both "any" (primary FR-3 metric) and "all" (stricter, useful for the
  multi-document comorbidity/cross-guideline cases where citing only one of
  two relevant guidelines is a real partial miss).
- **Groundedness**: `Metrics/GroundednessScorer.cs` ships a crude
  claim-to-chunk lexical overlap heuristic (`HeuristicGroundednessScorer`).
  It will under-score good answers that paraphrase heavily and can
  over-score answers that just repeat retrieved text verbatim. Treat scores
  as a triage signal, not ground truth. `IGroundednessScorer` is the
  extension point for a real LLM-as-judge implementation once `ILLMProvider`
  exists - swap the one line in `Program.cs`'s composition root.
- **Refusal/hedge/gate correctness**: golden-set.json now carries an explicit
  `expected_behavior` field per item (`refuse` / `answer_with_hedge` /
  `answer_normally` / `gate_compliance_check`), added specifically so this
  harness doesn't have to infer intent from the `category` string. The
  `answer_with_hedge` check additionally looks for a small set of hedge-language
  markers as a heuristic signal - same "triage, not truth" caveat applies.

## CLI options

```
--golden-set <path>              Required. Path to golden-set.json.
--output <path>                  Default: eval/report.md
--top-k <int>                    Default: 5
--groundedness-threshold <double> Default: 0.30
```

## Honest-gaps note for docs/EVALUATION.md

The report's final section ("Honest Interpretation") deliberately leaves the
per-failure root-cause analysis as a TODO for you to fill in by hand rather
than auto-summarizing it - the assignment brief explicitly wants real
interpretation, not averaged-away numbers.
