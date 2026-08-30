# ADR-002: Orchestration Pattern for the D0 Multi-Agent Workflow

## Status

Accepted — 2026-08-28

## Context

FR-4 and FR-5 require an orchestrated multi-agent workflow with at least three specialized agents plus an orchestrator, each with a restricted tool allow-list and typed input/output contracts, and a named, justified orchestration pattern. The D0 (Healthcare) domain pack defines exactly three agents with a fixed, stated data dependency between them:

1. **Guideline Researcher** — given a case summary, retrieves relevant clinical guidance from the ingested corpus (tools: search corpus, fetch document by id). Output: a set of cited guideline excerpts relevant to the case.
2. **Safety Checker** — given the case's current medications and any newly proposed medication, checks drug interactions and contraindications against a deterministic, code-based lookup, *and* against the guideline excerpts the Guideline Researcher retrieved. Output: a structured safety verdict (no interaction / flagged interaction / insufficient evidence) with supporting citations.
3. **Documentation Drafter** — given the case, the retrieved guidance, and the safety verdict, drafts the clinical note. Its "write" tool is the one tool in the whole system gated by human approval (FR-4's required write/side-effecting tool).

Critically, **each agent's required input is the previous agent's output**, and there is no case in the D0 workflow where an earlier step needs to be revisited based on a later step's findings, nor any case where two agents' work can proceed independently and be merged (there is exactly one Safety Checker verdict per case, not several to reconcile). This is a straight-line data dependency, not a branching or iterative one.

Three orchestration patterns were considered for how the orchestrator invokes these three agents:

- **Pipeline** — a fixed, linear sequence of agent calls where each agent's output becomes the next agent's input, with no dynamic replanning of the sequence itself.
- **Supervisor** — a central controller agent that dynamically decides, at each step, which of several available agents to invoke next (and whether to invoke one at all), typically used when the task requires branching, conditional agent selection, or an agent may need to be re-invoked based on another agent's findings.
- **Planner-Executor** — a planning agent first decomposes the task into a dynamic plan (a sequence or DAG of steps not known in advance), which an executor then carries out, typically used for open-ended or variable-shaped tasks where the number and identity of steps depends on the specific input.

## Decision

We will use a **Pipeline orchestration pattern**: a fixed, linear sequence — Guideline Researcher → Safety Checker → Documentation Drafter → Approval Gate — implemented as an explicit, statically-defined sequence in the orchestrator, not a dynamically planned or supervisor-selected one.

### Why Pipeline fits D0 specifically

The Supervisor pattern's value comes from *deciding* which agent to invoke and in what order, based on the evolving state of the task. D0's workflow does not have that kind of decision to make: the sequence Guideline Researcher → Safety Checker → Documentation Drafter is fixed by the clinical logic of the task itself (you cannot check drug safety meaningfully before you know what the guidelines say about the case, and you cannot draft a note before both steps that inform its content are complete). Introducing a Supervisor here would add an LLM-mediated decision point — "which agent should run next?" — where the answer is always the same, for every case. That is pure overhead: extra LLM calls, extra latency, extra token cost (directly working against the Cost Governor twist's per-run budget), and an extra place where a non-deterministic model could make an unnecessary and unjustified routing choice on a workflow that has no routing decision to make.

The Planner-Executor pattern's value comes from decomposing a task into a plan whose *shape* is not known ahead of time — different inputs might require a different number of steps or a different arrangement of them. D0's three-agent shape does not vary by case: every case goes through the same three stages regardless of whether it is a straightforward glycemic-control question or a multi-comorbidity anticoagulation case. Using a planning agent to "discover" a sequence that is always identical would, again, be spending LLM calls (and thus budget, under the Cost Governor's per-step accounting) to reconstruct something already known at design time.

Choosing Pipeline is also the choice that most directly supports D0's central risk-avoidance goal. A fixed, auditable sequence means every run's step order is deterministic and identical to every other run's, which makes it straightforward to guarantee — in code, not by LLM judgment — that the Safety Checker's deterministic lookup and the Guideline Researcher's citations are *always* consulted before the Documentation Drafter runs, and that the Documentation Drafter's write tool *always* reaches the human approval gate before finalizing. A Supervisor could, in principle, be prompted or manipulated (including via the indirect-prompt-injection risk documented in the synthetic case corpus, e.g. SYN-015) into skipping a step; a Pipeline has no such step to skip because "what runs next" is not an LLM decision at all — it is orchestrator code.

### How the required controls map onto a Pipeline

The four mandated orchestration controls (FR-5) are implemented as follows in a linear pipeline:

- **Max-iteration breaker.** Because there is no loop in a straight-line pipeline, "max iterations" applies at two narrower, well-defined points instead of an open-ended agent loop: (1) each individual agent's internal tool-calling loop (e.g., Guideline Researcher issuing repeated corpus searches) is capped at a small fixed number of tool calls before it must return its best available result or escalate to insufficient-evidence handling, and (2) the pipeline as a whole runs exactly once per case — there is no pipeline-level retry-the-whole-sequence loop, only the per-step retry described below. This keeps the breaker's scope matched to where iteration can actually occur, rather than bolting a generic "loop guard" onto a pattern that has no loop.
- **Per-step timeout.** Each of the three agent stages has its own timeout (configurable per stage, since Guideline Researcher's corpus search and Safety Checker's deterministic lookup have different expected latencies than Documentation Drafter's generation). A timeout on any stage fails that stage explicitly rather than allowing the pipeline to hang; the failure is surfaced as a typed error (see `InsufficientEvidenceError` and related domain errors) rather than a silent empty result being passed forward.
- **Retry-with-backoff.** Applied per stage, not to the pipeline as a whole: a transient failure in the Guideline Researcher's retrieval call (e.g., a Qdrant connection blip) is retried with exponential backoff up to a small fixed cap before that stage is marked failed. Retries never span stage boundaries — the Safety Checker is never "retried" by re-running the Guideline Researcher — because doing so would blur the fixed sequence into something closer to a Supervisor's dynamic re-invocation, which is precisely the flexibility this pattern deliberately forgoes.
- **Graceful degradation to plain RAG.** If the Safety Checker or Documentation Drafter stage fails after retries are exhausted, the pipeline degrades to returning the Guideline Researcher's retrieved, cited guidance directly to the clinician as plain RAG output — explicitly labeled as such, with no safety verdict or drafted note attached — rather than either hanging indefinitely or fabricating a safety verdict/note it cannot support. This degrade-path is itself a fixed, known step in the pipeline (not a dynamically decided fallback), consistent with the pattern: the orchestrator knows in advance exactly what "degraded" output looks like for this workflow, because there is only one possible workflow shape to degrade from.

Every stage transition, retry, timeout, and degradation event is logged against the run's correlation ID (FR-9), so a pipeline's fixed structure also makes the "every run inspectable step-by-step by run ID" requirement (FR-5) simpler to satisfy — there is exactly one possible path through the system, so the trace viewer has a fixed set of stages to render rather than needing to reconstruct a dynamically-chosen path after the fact.

## Consequences

**Positive:**
- No LLM-mediated routing decisions on a workflow that has no routing decision to make — lower latency, lower token cost per run (directly benefits the Cost Governor twist), and no non-deterministic step-selection to audit.
- The fixed sequence structurally guarantees that Safety Checker and Guideline Researcher always run before Documentation Drafter, and that the approval gate is always reached before a note is finalized — this is enforced by orchestrator code, not by trusting an LLM (or resisting a prompt injection) to make the right call.
- Simpler to implement, test, and reason about than a Supervisor or Planner-Executor: contract tests (FR-4 requirement) only need to verify one fixed sequence of typed handoffs, not a space of possible agent-selection decisions.
- Directly supports full step-by-step run inspection (FR-5) since the set of possible stages is closed and known ahead of time.

**Negative / trade-offs:**
- No flexibility if a future domain pack (or a future extension of D0) needs conditional branching — e.g., "only run the Safety Checker if new medications were actually proposed" — a Pipeline can express simple conditional *skips* of a stage, but cannot express genuinely dynamic agent selection without either hardcoding the condition into the pipeline (acceptable for known, anticipated branches) or migrating toward a Supervisor pattern (a larger architectural change).
- If D0's workflow shape changes in the future to require reordering agents based on intermediate results (which the current domain pack does not require), this ADR's decision would need to be revisited; the Pipeline pattern is a good fit for the *current, specified* workflow, not a general-purpose choice for all future multi-agent extensions of this codebase.
- The graceful-degradation path, while simple to define for this one workflow shape, still needs its own explicit test coverage (a degraded run is a distinct code path, not just "the pipeline stopping"), which is additional but bounded implementation and test surface.

## Alternatives Considered

**Supervisor.** Rejected as the primary pattern. Its central value — dynamically deciding which agent to invoke next — has no use in a workflow where the next agent is always the same, known agent. Adopting it here would introduce an unnecessary LLM decision point, adding cost, latency, and a new (if narrow) attack surface for prompt injection to exploit, without providing any capability the fixed sequence lacks for this domain pack. Worth revisiting only if a future domain pack genuinely requires conditional agent selection based on run-time findings.

**Planner-Executor.** Rejected as the primary pattern for the same underlying reason: the plan for D0's workflow is always the same three-step plan, known at design time, so paying the cost of a planning step to "discover" it on every run provides no benefit. This pattern would be justified for a domain pack whose step count or step identity genuinely varies by input case — D0 as specified does not have that property.

**Hybrid (Pipeline with a lightweight Supervisor only at the degrade-path decision point).** Briefly considered: using a small Supervisor-style decision only to decide *whether* to degrade to plain RAG after a stage failure, rather than hardcoding the degrade condition. Rejected as unnecessary complexity for this iteration — the degrade condition ("Safety Checker or Documentation Drafter stage failed after retries") is itself deterministic and known in advance, so expressing it as fixed orchestrator logic is simpler and equally correct, with no LLM judgment required at that decision point either. This could be reconsidered if degrade conditions become more numerous or genuinely ambiguous in a future iteration.
