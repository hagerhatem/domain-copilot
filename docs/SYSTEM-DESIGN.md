# System Design — Domain Copilot (D0T3: Healthcare / Cost Governor)

This document has two parts, per the assessment brief:

- **Part A** describes the architecture I *would* build with no constraints — real budget, a
  managed cloud, a team, and production SLAs. It exists to demonstrate I understand what
  "production-grade" actually requires, independent of what I could build for free, alone,
  in the assignment window.
- **Part B** describes what I *actually built*, honestly, with a gap table mapping every
  deferred piece of Part A to why it was cut, what stands in for it today, and what it would
  take to close the gap.

---

## Part A — Unconstrained Target Architecture

### A.1 Context

At production scale, "Domain Copilot" would serve multiple hospital/clinic tenants, handle
real (not synthetic) patient case data, and be subject to HIPAA-equivalent regulatory
obligations. Every design choice below is driven by that: data protection, auditability,
availability, and cost visibility at scale.

### A.2 High-level target architecture

```
                                   ┌─────────────────────────┐
                                   │   Identity Provider       │
                                   │ (Azure AD B2C / Auth0)    │
                                   └────────────┬─────────────┘
                                                │ OIDC/JWT
                                                ▼
 Clinician / Admin ───▶  CDN + WAF  ───▶  Managed API Gateway  ───▶  Backend services (below)
   (Angular SPA)          (Cloudflare/     (Azure APIM / AWS         behind private VNet/VPC
                           Front Door)       API Gateway + Kong)
```

Backend, inside a private network, behind the gateway:

- **Orchestration service** (the existing .NET Clean Architecture core) — deployed as
  containers on a managed, autoscaling compute layer (Azure Container Apps / AKS / ECS
  Fargate), never a single VM.
- **Message broker** (Azure Service Bus / AWS SQS+SNS / Kafka) decouples long-running
  agentic workflow runs from the request/response cycle: `RunClinicalWorkflowUseCase`
  publishes a "run requested" event; workers consume it; SSE progress is fanned out via a
  pub/sub channel (e.g. Redis Streams or Azure SignalR) rather than held in an in-process
  `Channel<T>` as today, so a run survives an individual instance restart and scales across
  replicas.
- **Managed relational store**: Azure SQL Database / Amazon RDS for SQL Server, with
  automated point-in-time backups, geo-replication, and connection pooling (PgBouncer
  equivalent not needed for SQL Server, but Azure SQL's built-in pooling would be used).
- **Managed vector database**: Qdrant Cloud, Azure AI Search (vector mode), or Pinecone —
  managed replication, backups, and horizontal scaling instead of a single self-hosted
  container with a Docker volume.
- **Secrets manager**: Azure Key Vault / AWS Secrets Manager holds the SQL connection
  string, JWT signing key, and LLM API keys; the API reads them via managed identity, never
  from `appsettings.json` or `.env`.
- **LLM access**: routed through a managed LLM gateway (Azure AI Foundry / a self-hosted
  LiteLLM proxy) rather than the app calling provider SDKs directly — centralizes rate
  limiting, key rotation, per-tenant cost attribution, and provider failover.
- **Autoscaling**: HPA (Kubernetes) or built-in autoscale rules (Container Apps) on CPU,
  queue depth, and concurrent SSE connections; separate scale profiles for the API tier
  (bursty, user-facing) and any background workers (steady, cost-sensitive).
- **Full observability stack**: OpenTelemetry Collector shipping traces/metrics/logs to a
  managed backend (Azure Monitor / Datadog / Grafana Cloud), with:
  - distributed tracing across API → orchestrator → agent → LLM call (correlation ID
    already modeled in the MVP; at scale this becomes a proper trace, not just an ID
    threaded through logs),
  - dashboards and alerts on error rate, p95 latency, token spend, budget rejections, and
    refusal rate,
  - centralized structured log aggregation with retention policy aligned to compliance
    requirements.
- **DR / backup**: automated, geo-redundant backups for both SQL Server and the vector
  store; a documented RTO (e.g. 4 hours) and RPO (e.g. 15 minutes); a secondary region on
  warm standby for the API tier; regular restore drills, not just backup existence.
- **CI/CD**: the existing GitHub Actions pipeline extended with a deployment stage —
  build → test → scan → push image → deploy to staging → smoke test → manual gate →
  deploy to production, with blue/green or canary rollout and automatic rollback on
  health-check failure.

### A.3 Cost model at scale (illustrative)

For ~500 active clinicians, ~5,000 workflow runs/day, average 4 agent steps/run:

| Cost driver | Rough monthly cost (illustrative) |
|---|---|
| LLM inference (mixed model routing, ~2M tokens/day) | $3,000–$8,000 depending on provider mix and cache hit rate |
| Managed vector DB (mid-tier cluster) | $300–$1,500 |
| Managed SQL (business-critical tier, geo-replicated) | $500–$2,000 |
| Compute (autoscaling containers, 2 regions) | $800–$3,000 |
| API gateway + WAF + CDN | $200–$800 |
| Observability stack | $300–$1,200 |
| Secrets manager, message broker | $100–$400 |
| **Total (rough order of magnitude)** | **$5,000–$17,000/month** |

These numbers are indicative, not a quote — actual cost depends heavily on model choice,
caching strategy, and negotiated enterprise pricing. The point of including this table is to
show the Cost Governor twist isn't just a toy for the free-tier MVP: at scale, the same
per-user budget enforcement and model-routing logic is what keeps this number from growing
linearly with LLM list prices as usage scales.

---

## Part B — Implemented MVP

### B.1 What actually exists today (verified from the repository)

- **Clean Architecture solution**: `DomainCopilot.Domain`, `.Application`, `.Infrastructure`,
  `.Api`, plus a separate `DomainCopilot.Evaluation` project for the golden-set harness.
- **Local infra via Docker Compose**: SQL Server 2022 (Developer edition), Qdrant, Ollama,
  and the API, each with health checks; the API's own `/health/ready` check gates on SQL
  Server, Qdrant, and the LLM provider all being reachable (`LlmProviderHealthCheck`,
  `QdrantHealthCheck`).
- **Ingestion pipeline**: `IngestDocumentUseCase` with separated extract
  (`PdfPigDocumentExtractor`, `OpenXmlDocxExtractor`) → clean (`PlainTextDocumentCleaner`) →
  chunk (`ClinicalSectionAwareChunkingStrategy`) → embed (`OllamaEmbeddingService`) → index
  (`QdrantVectorStore`) stages, backed by EF Core migrations and an `IngestController`.
- **Retrieval**: `RrfHybridRetrievalService` implementing Reciprocal Rank Fusion over dense
  (Qdrant) and keyword (`SqlServerKeywordSearchService`, SQL full-text) results, plus
  `EvidenceSufficiencyChecker` for the refusal-on-low-evidence behavior
  (`InsufficientEvidenceError`).
- **Three agents + orchestrator**: `GuidelineResearcherAgent`, `SafetyCheckerAgent` (backed
  by a deterministic `SqlDrugInteractionLookup`, not an LLM guess), and
  `DocumentationDrafterAgent`, run through `PipelineOrchestrator` with
  `StepResilienceRunner` (retry/backoff/timeout) and `OrchestratorOptions`.
- **Approval gate**: `ApprovalWorkflowUseCase` plus `ApprovalController`
  (approve/reject/edit-approve endpoints) writing to an audited `ApprovalDecision` entity via
  `EfApprovalAuditWriter`.
- **Cost Governor**: `TokenBudget` / `UsageRecord` domain entities, `SqlTokenBudgetService`
  and `SqlSpendReportService` in Infrastructure, `BudgetExceededError` and
  `NoActiveBudgetPeriodError` domain errors, `CostAwareLlmRouter` for deterministic
  complexity-based model routing, and an `AdminController` spend endpoint.
- **Real-time**: SSE streaming via `RunsController`'s `/stream` endpoint, backed by
  `ChannelAgentProgressReporter`, with `RunCancellationRegistry` for server-side cancellation
  on client disconnect.
- **Access control**: ASP.NET Core Identity with `ApplicationUser`, `Roles`, and
  `DemoUsers`, migrated via `AddIdentityTables`; an `AuthorizationTests` integration test
  suite exists.
- **Angular frontend**: login, ingest, ask (with citations), run-workflow, run-progress
  (SSE-driven), approval-queue, and trace-viewer features — covering the FR-7 surface
  requirement end to end.
- **Tests**: unit tests for each agent, the orchestration use case, evidence sufficiency,
  and a dedicated `PromptInjectionTests` suite; domain tests for `AgentRun` and
  `TokenBudget`; integration tests for authorization, ingestion, and a real (non-stubbed)
  embedding service.
- **Docs so far**: `docs/BRD.md`, `docs/SECURITY.md`, and four ADRs (chunking, orchestration
  pattern, vector store choice, Cost Governor design).

### B.2 Known gaps in what's implemented (found in the evidence itself, not hypothetical)

These aren't Part-A-vs-Part-B infrastructure gaps — they're places where the code doesn't
yet do what the brief or the code's own comments say it should, and belong in
`docs/EVALUATION.md`'s honest failure analysis as well as here:

- **Single LLM provider only.** Only `OllamaLLMProvider` exists under
  `Infrastructure/Llm`. No hosted free-tier adapter (OpenAI/Groq/etc.) was found, so the
  "≥2 implementations with documented fallback chain" requirement (Section 3 of the brief)
  is not yet met — there is currently nothing to fall back *from* or *to*.
- ~~Hybrid retrieval was coded but not active in the running system~~ — **resolved.** The
  SQL Server container now builds from a custom image
  (`docker/sqlserver-fts/Dockerfile`) with the `mssql-server-fts` package installed, instead
  of the stock `mcr.microsoft.com/mssql/server:2022-latest` image, which doesn't ship with
  Full-Text Search. `SkipStartupInitializers` was removed from the `api` service, so
  `SqlFullTextIndexInitializer` now runs on startup and `SqlServerKeywordSearchService`
  participates in `RrfHybridRetrievalService`'s fusion for real, end-to-end via
  `docker compose up`, not just in unit tests.
- **Evaluation harness runs against stubs.** `DomainCopilot.Evaluation` currently wires up
  `StubRetrievalPipeline`, `StubWorkflowPipeline`, and `StubCorpusIndex`, with a TODO in
  `Program.cs` to replace them with the real pipeline. Any hit-rate/groundedness/refusal
  numbers produced right now describe the stub's behavior, not the actual system's.
- **A safety-related TODO in `SafetyCheckerAgent.cs`** (line ~85) references a "safe
  mechanism" that doesn't exist yet, per its own comment. Worth reviewing before claiming
  the Safety Checker's behavior is fully deterministic and complete — I haven't seen the
  surrounding code, so I'm flagging rather than asserting what's missing.

### B.3 Gap table — target architecture vs. implemented MVP

| Target component (Part A) | Implemented? | Why deferred | Interim mitigation | Effort/cost to close |
|---|---|---|---|---|
| Managed API gateway (Azure APIM / Kong) | No | Free-tier-only constraint; no paid gateway tier meets requirements | ASP.NET Core routes requests directly; rate limiting done in-app | ~2–3 days to front with a self-hosted Kong/YARP; managed tier is a recurring cost, not a one-time effort |
| Secrets manager (Key Vault / Secrets Manager) | No | Same free-tier constraint | `.env` file + `appsettings`, excluded from Git, documented in `.env.example` | ~1 day to integrate a self-hosted Vault (OSS) container into Compose; managed cloud version is trivial but paid |
| Message broker (Service Bus / SQS / Kafka) | No | Single-instance MVP doesn't need cross-instance fan-out; adds operational complexity for no benefit at this scale | In-process `Channel<T>` (`ChannelAgentProgressReporter`) for SSE fan-out; works because there's exactly one API instance | ~1 week to introduce Redis Streams or a broker + rework progress reporting to be instance-agnostic |
| Autoscaling compute | No | Local Docker Compose is inherently single-instance | Manual scaling (there is none); acceptable for a demo/assessment workload | Depends entirely on target platform (AKS/Container Apps); ~3–5 days plus ongoing cost |
| Managed vector DB (Qdrant Cloud / Azure AI Search) | No | Free-tier constraint; self-hosted Qdrant meets functional needs at demo scale | Self-hosted Qdrant container with a Docker volume; no replication or managed backup | Migrating to Qdrant Cloud is mostly a connection-string change given the existing `IVectorStore` abstraction — ~1 day, then recurring cost |
| Managed relational DB (Azure SQL / RDS) | No | Free-tier constraint | Self-hosted SQL Server Developer edition in Compose, with a Docker volume | ~1 day to point EF Core at a managed instance; recurring cost |
| Hybrid retrieval (dense + keyword fusion) | **Implemented** | N/A — not deferred; originally blocked because the stock SQL Server image lacks Full-Text Search, now fixed with a custom `mssql-server-fts` image | — (this is the production-shape design, not a stand-in) | Closed. Self-hosted Qdrant (dense) + SQL Server FTS (keyword) fused via RRF, running end-to-end through `docker compose up`; only the *managed hosting* of each store (see rows above/below) remains a free-tier trade-off, not the retrieval logic itself |
| Full observability stack (Grafana/Datadog/App Insights) | Partial | OpenTelemetry wiring exists in principle (per brief), but no external backend is configured — traces/metrics have nowhere to go except local logs | Serilog structured logging + a correlation ID threaded via `CorrelationIdMiddleware`; health checks (`/health/ready`) | ~2 days to stand up a local Grafana/Tempo/Loki stack via Compose (free), or connect to a managed free tier |
| DR / backup, multi-region | No | Out of scope for a local, single-machine assessment deployment | SQL Server and Qdrant data persist in named Docker volumes on the dev machine only; no backup automation | Not meaningfully closeable without a managed/cloud target; would require a cloud deployment first |
| CDN / WAF | No | No public-facing deployment; Angular served locally | N/A — local-only access | Only relevant once/if the optional live deployment (Section 9, item 9 of the brief) is pursued |
| LLM gateway / managed LLM routing | Partial | Free-tier constraint; a self-hosted gateway (LiteLLM) was considered but not built to keep scope bounded | `CostAwareLlmRouter` does deterministic, in-process routing between models based on task complexity — same *goal* as a gateway, different *mechanism* | ~2–3 days to introduce a LiteLLM proxy container if a second (hosted) provider is added |
| CI/CD with staged deployment | Partial | GitHub Actions CI exists (build/lint/test per PR); there is no deployment stage because there is no deployment target | CI runs on every PR; `docker compose up` is the "deployment" | Depends on target platform once one is chosen |

### B.4 Honest interpretation

The MVP is closer to "a correctly Clean-Architected, locally-runnable version of the target
system" than to "the target system with some pieces missing." The abstractions that matter
most for the assessment's acceptance test — `ILLMProvider`, `IVectorStore`,
`IDocumentRepository`, `IHybridRetrievalService` — are in place and Infrastructure-only, so
swapping a self-hosted Qdrant for Qdrant Cloud, or adding a second `ILLMProvider`
implementation, should genuinely be a configuration change plus one new adapter class, not a
rewrite. That's the main architectural claim this document is making, and it's testable.

Where the MVP currently falls short of its *own* stated requirements — not just Part A's
unconstrained ambition — is the single LLM provider, the inactive hybrid retrieval path, and
the stub-backed evaluation harness (Section B.2). Those are worth fixing before the numbers
in `docs/EVALUATION.md` are written up as "real recorded baseline numbers," since right now
part of that pipeline isn't measuring the real system yet.
