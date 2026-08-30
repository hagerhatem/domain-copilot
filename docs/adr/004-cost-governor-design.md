# ADR-004: Cost Governor Design (Twist T3)

## Status

Accepted — 2026-08-28

## Context

The T3 twist requires a Cost Governor that is visible in both architecture and UI, with five concrete capabilities: per-user token budgets enforced server-side; deterministic (non-LLM-decided) budget-aware model routing; pre-flight cost estimation before a workflow run; a hard cut-off that actually prevents further runs once a budget is exhausted, not merely a warning; and a per-user, per-run, per-agent-step spend view.

A recurring theme across this project's other ADRs is that anything safety- or integrity-critical must be enforced in deterministic backend code rather than left to an LLM's judgment (ADR-002's fixed Pipeline sequence being the clearest precedent). The Cost Governor is squarely in that category: a budget that an LLM could talk its way around, or a routing decision an LLM could second-guess mid-run, would defeat the entire purpose of the twist. This ADR makes four specific design decisions, each chosen to keep the enforcement point in ordinary, testable, non-LLM backend code.

## Decision

### (a) How per-user token budgets are stored and checked

Budgets are stored in SQL Server (per ADR-003's relational-store role) as first-class transactional data, not as a cache, config value, or anything derived at request time from log aggregation.

**Schema (conceptual):**
- `UserBudgets` table: `UserId`, `PeriodStart`, `PeriodEnd`, `TokenBudget`, `TokensConsumed`, `RowVersion` (concurrency token). One active row per user per budget period (e.g., monthly), so a user's remaining budget is always `TokenBudget - TokensConsumed` for the current period's row, not a computed sum across all history at check time.
- `RunSpendRecords` table: one row per workflow run, `RunId`, `UserId`, `CorrelationId`, `EstimatedTokens` (from pre-flight estimation), `ActualTokensConsumed` (filled in as the run progresses), `Status` (reserved / completed / failed / degraded), `StartedAt`, `CompletedAt`. This is the source of the "per-run" breakdown in the spend view (FR requirement 5 of T3).
- `AgentStepSpendRecords` table: one row per agent step within a run, `RunId`, `AgentName` (Guideline Researcher / Safety Checker / Documentation Drafter), `ModelUsed`, `PromptTokens`, `CompletionTokens`, `Cost` (in whatever unit the routed provider bills, normalized to a common internal unit — see (b)). This is the "per-agent-step" breakdown.

A user's budget check is always a direct read of the current `UserBudgets` row — never an LLM call, never a heuristic estimate, never a cache that could be stale at the moment of a hard cut-off decision. The check itself is a plain SQL comparison (`TokensConsumed + EstimatedTokens <= TokenBudget`), executed inside the same transaction that reserves the spend (see (d)).

### (b) The exact rule for routing sub-tasks to a cheap/local model vs. a stronger model

Routing is a static, deterministic lookup table keyed by **agent identity and task type**, not a runtime LLM decision and not a dynamic cost-based heuristic that could vary its own answer unpredictably. The rule, expressed as configuration (externalized per the project's engineering requirements, not hardcoded):

| Agent / sub-task | Complexity classification | Routed model tier |
|---|---|---|
| Guideline Researcher — corpus search query formulation | Low (structured extraction/query-writing) | Cheap/local (Ollama) |
| Guideline Researcher — relevance summarization of retrieved chunks | Low-to-moderate | Cheap/local (Ollama), hosted free-tier as configured fallback |
| Safety Checker — deterministic lookup | N/A — not an LLM call at all (code-based, per D0's domain pack requirement) | No model — this is the point of the requirement |
| Safety Checker — synthesizing lookup result with retrieved guideline text into a verdict explanation | Moderate | Hosted free-tier model |
| Documentation Drafter — final clinical note generation | High-stakes (this is the one artifact a clinician reviews and approves) | Strongest available model (hosted free-tier's best available model; Ollama's largest local model only as an explicit degraded-mode fallback, flagged in the note's metadata as such) |

This table is the *entire* routing decision — there is no LLM in the loop deciding "is this task simple or complex," because that judgment call is exactly the kind of thing that must be deterministic per this project's design philosophy (see ADR-002). The classification of each sub-task's complexity was made once, at design time, by a human (documented here), not computed per-request. If a new agent or sub-task is added later, its routing tier is added to this table as a code/config change — never inferred at runtime.

The routing table is implemented as part of the `ILLMProvider` selection logic in the Infrastructure layer: given an `AgentName` and `SubTaskType`, a pure function returns which configured provider adapter to use. This keeps the routing decision easily unit-testable in isolation (a contract test can assert "Documentation Drafter always routes to the strong-tier provider" without needing to run the full pipeline).

### (c) How pre-flight cost estimation is calculated before a workflow run

Before a pipeline run begins, the orchestrator computes an estimated token cost using a deterministic formula, not a call to any LLM:

1. **Per-stage token estimate** = (average historical prompt-token count for that agent/sub-task, computed from `AgentStepSpendRecords` over recent completed runs) + (a fixed estimate of the current case's input size, computed by simple tokenization of the case text using the same tokenizer the routed model's provider uses — a mechanical count, not a model call).
2. **Per-stage cost** = per-stage token estimate × the routed model's per-token cost (per the routing table in (b); free-tier hosted models are assigned a nominal internal cost value even though no money changes hands, so that budget accounting is meaningful and comparable across hosted/local routing — this is necessary because "free" does not mean "unlimited" against a *token* budget, which is the actual constrained resource here, not currency).
3. **Total pre-flight estimate** = sum of all three stages' estimated cost, plus a configurable safety margin (e.g., +15%) to account for estimation variance, since actual generation length is not knowable before generation happens.

This total is what is compared against the user's remaining budget in the atomic check described in (d), **before any agent is invoked**. If historical data is unavailable (e.g., a brand-new deployment with no prior runs), the system falls back to a conservative fixed estimate per stage, configured alongside the routing table, until enough real run history accumulates to switch to the historical-average method. Both paths are deterministic arithmetic — no LLM is asked to estimate its own future cost.

### (d) How the hard cut-off is enforced atomically to avoid race conditions

The failure mode this must prevent: a user with, say, 100 tokens of remaining budget fires two workflow requests at nearly the same instant, each pre-flight-estimated at 80 tokens. If both requests read the same "100 remaining" value before either writes back, both could be approved, resulting in a user who has run 160 tokens of work against a 100-token budget — the hard cut-off failing exactly at the moment it matters most.

This is prevented with a **reserve-then-consume pattern inside a single database transaction with a concurrency guard**, not with application-level locking or in-memory counters (which would not be safe across multiple API instances and are not needed given SQL Server is already the source of truth):

1. When a workflow request arrives, the orchestrator computes the pre-flight estimate (per (c)) *before* opening any transaction (this part can happen concurrently for multiple requests with no risk, since it only reads historical averages, not the user's current budget row).
2. The orchestrator then opens a database transaction and, within it: reads the user's `UserBudgets` row using `UPDLOCK, ROWLOCK` query hints (or equivalently, an optimistic-concurrency check against the row's `RowVersion` token with retry-on-conflict — either approach is acceptable; the pessimistic row-lock is simpler to reason about correctness for and is the default choice given the low expected contention per user), checks `TokensConsumed + EstimatedTokens <= TokenBudget`, and if true, immediately writes an updated `TokensConsumed` value (a *reservation*, using the estimate, not yet the actual) and inserts a `RunSpendRecords` row with `Status = 'reserved'`, all within that same transaction, then commits.
3. Because the read-check-write sequence happens inside one transaction with a row lock held on the user's specific budget row, a second concurrent request for the same user is blocked at the row-lock acquisition step until the first transaction commits or rolls back — it cannot read a stale "100 remaining" value while the first request's reservation is in flight. The second request's check then correctly sees the already-reduced remaining budget and is rejected if it would exceed the (now-updated) budget.
4. If the check fails (insufficient remaining budget), the transaction is rolled back with no side effects, and the API returns a typed `BudgetExceededError` to the caller — this is the actual hard cut-off, enforced before any agent, tool call, or LLM request is made, not after the fact.
5. After the workflow run completes (or fails/degrades), a second, separate transaction reconciles the reservation: `TokensConsumed` is adjusted from the estimate to the actual measured token usage (summed from the run's `AgentStepSpendRecords`), and the `RunSpendRecords` row's `Status` is updated to `completed`/`failed`/`degraded` accordingly. This reconciliation step means a run that used less than estimated correctly returns the difference to the user's available budget, and a run that used more than estimated is correctly charged the true amount — the pre-flight estimate is a safe reservation ceiling, not the final billed amount.

### Sequence description of the budget-check flow

```
User submits workflow request
        │
        ▼
Orchestrator computes pre-flight cost estimate (historical averages, no LLM call)
        │
        ▼
BEGIN TRANSACTION
        │
        ▼
SELECT UserBudgets row WITH (UPDLOCK, ROWLOCK) WHERE UserId = @UserId AND period = current
        │
        ▼
Check: TokensConsumed + EstimatedTokens <= TokenBudget ?
        │
   ┌────┴────┐
   NO         YES
   │           │
   ▼           ▼
ROLLBACK    UPDATE TokensConsumed += EstimatedTokens
   │        INSERT RunSpendRecords (Status='reserved')
   │           │
   ▼           ▼
Return       COMMIT
BudgetExceededError │
(run never starts)  ▼
              Run pipeline (ADR-002), recording actual
              per-step token usage as it progresses
                     │
                     ▼
              BEGIN TRANSACTION (reconciliation)
              UPDATE TokensConsumed: estimate → actual
              UPDATE RunSpendRecords.Status = completed/failed/degraded
              COMMIT
```

Every step left of "Run pipeline" is ordinary deterministic backend code with no model call involved; the row lock held between the `SELECT ... UPDLOCK` and the `COMMIT` is what makes two simultaneous requests for the same user serialize correctly instead of racing.

## Consequences

**Positive:**
- Every budget-relevant decision — the check, the routing, the estimate — is deterministic, unit-testable backend logic with no dependency on LLM judgment, consistent with this project's broader design philosophy (ADR-002).
- The row-lock-based reservation pattern correctly prevents the double-spend race condition described above using ordinary relational-database guarantees, without needing a separate distributed-locking system (e.g., Redis-based locks), which would be additional infrastructure this project's free-tier constraint would rather avoid.
- The reserve-then-reconcile pattern means the hard cut-off is conservative-but-fair: it never under-reserves (which could allow overspend) and always corrects itself to the true cost afterward (so users are not permanently over-charged for a pessimistic estimate).
- The static routing table is trivial to audit and explain in the teaching materials this project also requires — "here is the exact rule, in one table" is a much better trainee-facing artifact than "the model decides based on the request."

**Negative / trade-offs:**
- The row-lock approach means concurrent requests *from the same user* are serialized (one waits for the other's transaction to complete), which is a deliberate and acceptable trade-off for correctness, but does mean a user who fires several requests at once experiences them as queued rather than parallel — this should be surfaced honestly in the UI (e.g., a "processing your previous request first" state) rather than silently making the user wonder why a second request appears to hang.
- Historical-average-based pre-flight estimation is only as good as the historical data available; a cold-started system (or a system whose usage pattern shifts significantly, e.g., a much longer case being submitted than any seen before) will have a less accurate estimate until enough representative runs accumulate. The fixed safety margin and conservative fallback-when-no-history path mitigate but do not eliminate this — an estimate is still an estimate, and this should be stated plainly in `docs/EVALUATION.md` rather than presented as more precise than it is.
- Assigning a nominal internal "cost" to free hosted-tier and local Ollama usage (so that budget accounting works in token-equivalent units even when no real money is spent) is a modeling simplification that must be clearly documented — it is a proxy for compute/rate-limit consumption, not an actual dollar cost, and conflating the two in UI copy could mislead a user about what the budget actually represents.

## Alternatives Considered

**In-memory or application-level locking for the hard cut-off (e.g., a per-user mutex in the API process).** Rejected because it does not work correctly across multiple API instances (a requirement this project does not currently have, since the MVP runs a single API container, but which `docs/SYSTEM-DESIGN.md`'s unconstrained target architecture explicitly considers for scale) and because it duplicates a correctness guarantee the relational database already provides for free via row locking. Using the database's own transactional guarantees is simpler and strictly more correct than reimplementing mutual exclusion in application code.

**Optimistic concurrency only (check-then-write with a version token, retry on conflict, no row lock).** A legitimate alternative to the chosen pessimistic row-lock approach, and one that could perform better under high contention across many different users (since it does not hold locks). Not chosen as the default because expected per-user contention is low (a single user rarely fires many simultaneous workflow requests, unlike, say, a high-throughput multi-tenant billing system), making the simpler-to-reason-about pessimistic lock preferable; this could be revisited if load testing in a future iteration shows lock contention becoming a bottleneck.

**LLM-mediated or dynamic cost-based routing (letting a model or a runtime heuristic decide which provider to use for a given request based on its assessed complexity).** Rejected for the same reason routing decisions are rejected elsewhere in this project's design (ADR-002): a budget-relevant decision must be deterministic and auditable, and "let the model judge its own task's complexity" is both circular (the judgment itself costs tokens) and impossible to guarantee consistent or unexploitable. The static table is less flexible but is exactly as flexible as this project's fixed three-agent, fixed-task-type workflow actually requires — no sub-task's complexity classification varies by case in a way the static table cannot already express.

**Charging actual/final cost only, with no pre-flight estimate or reservation (check budget only at the end of a run).** Rejected because it fails the "hard cut-off actually prevents further runs" requirement in its most literal sense: a check performed only after a run completes cannot prevent that run from having happened. Pre-flight estimation and reservation is the only design that can refuse a run *before* it consumes any budget at all, which is what the requirement specifically asks for.
