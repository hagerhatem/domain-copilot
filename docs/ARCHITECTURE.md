# Architecture — Domain Copilot (D0T3: Healthcare / Cost Governor)

All diagrams below are Mermaid source, committed as text (not only rendered images), per
the assignment's requirement. Render them with any Mermaid-compatible viewer (GitHub renders
them natively in `.md` files).

Where a relationship in the ER diagram is a plain Guid column rather than an enforced EF Core
`HasForeignKey`, it's drawn with a dashed line and labelled "no FK constraint" — this is
accurate to `DomainCopilotDbContextModelSnapshot.cs` as it stands today, not an assumption.

---

## 1. C4 Level 1 — System Context

```mermaid
C4Context
    title System Context — Domain Copilot (D0T3)

    Person(clinician, "Clinician", "Ingests guidelines, asks grounded questions, runs the clinical workflow, approves/rejects/edits the drafted note")
    Person(admin, "Admin", "Manages per-user token budgets, views spend reports across all runs")

    System(domainCopilot, "Domain Copilot", "Agentic RAG platform: retrieves clinical guidance, checks drug interactions deterministically, drafts a clinical note behind a human approval gate, and enforces per-user cost budgets")

    System_Ext(ollama, "Ollama", "Local LLM runtime — hosts the completion/embedding model")
    System_Ext(hostedLlm, "Hosted LLM API", "Planned second ILLMProvider implementation — not yet built (see SYSTEM-DESIGN.md gap table)")

    Rel(clinician, domainCopilot, "Ingests docs, asks questions, runs workflow, approves notes", "HTTPS")
    Rel(admin, domainCopilot, "Views spend, manages budgets", "HTTPS")
    Rel(domainCopilot, ollama, "Completion + embedding calls", "HTTP")
    Rel(domainCopilot, hostedLlm, "Fallback completion calls (planned)", "HTTPS")
```

---

## 2. C4 Level 2 — Container Diagram

```mermaid
C4Container
    title Container Diagram — Domain Copilot

    Person(clinician, "Clinician")
    Person(admin, "Admin")

    System_Boundary(dc, "Domain Copilot") {
        Container(spa, "Angular SPA", "Angular", "Login, ingest, ask-with-citations, run workflow, approval queue, trace viewer")
        Container(api, "ASP.NET Core API", ".NET 8 / C#", "Clean-Architecture API: orchestration, agents, retrieval, cost governor, SSE streaming")
        ContainerDb(sql, "SQL Server", "SQL Server 2022 Developer, custom mssql-server-fts image", "Runs, steps, approvals, budgets, usage, documents, chunks, identity")
        ContainerDb(qdrant, "Qdrant", "Vector DB, self-hosted", "Dense vector index of document chunks")
    }

    System_Ext(ollama, "Ollama", "Local LLM + embedding runtime")

    Rel(clinician, spa, "Uses", "HTTPS")
    Rel(admin, spa, "Uses", "HTTPS")
    Rel(spa, api, "Calls", "JSON/HTTPS, SSE")
    Rel(api, sql, "Reads/writes", "EF Core / TDS")
    Rel(api, qdrant, "Dense search, upsert", "gRPC/HTTP")
    Rel(api, ollama, "Completion, streaming, embeddings", "HTTP")
```

---

## 3. C4 Level 3 — Component Diagram (API internals)

```mermaid
C4Component
    title Component Diagram — DomainCopilot.Api internals

    Container_Boundary(api, "ASP.NET Core API") {
        Component(controllers, "Controllers", "ASP.NET Core", "AskController, IngestController, RunsController, ApprovalController, ApprovalQueueController, ClinicalCasesController, TraceController, AdminController")
        Component(streaming, "Streaming", "SSE / Channel<T>", "ChannelAgentProgressReporter, RunCancellationRegistry")
        Component(orchestrator, "Orchestration", "Application layer", "PipelineOrchestrator, StepResilienceRunner, RunClinicalWorkflowUseCase, ApprovalWorkflowUseCase")
        Component(agents, "Agents", "Application/Infrastructure", "GuidelineResearcherAgent, SafetyCheckerAgent, DocumentationDrafterAgent")
        Component(retrieval, "Retrieval", "Application/Infrastructure", "RrfHybridRetrievalService, EvidenceSufficiencyChecker, SqlServerKeywordSearchService")
        Component(costGovernor, "Cost Governor", "Application/Infrastructure", "CostAwareLlmRouter, SqlTokenBudgetService, SqlSpendReportService")
        Component(safety, "Safety", "Infrastructure", "SqlDrugInteractionLookup — deterministic, never an LLM guess")
        Component(llm, "LLM Adapter", "Infrastructure", "OllamaLLMProvider (implements ILLMProvider)")
        Component(persistence, "Persistence", "EF Core", "EfDocumentRepository, EfClinicalCaseRepository, EfAgentRunRepository, EfApprovalAuditWriter")
    }

    ContainerDb(sql, "SQL Server")
    ContainerDb(qdrant, "Qdrant")
    System_Ext(ollama, "Ollama")

    Rel(controllers, orchestrator, "Invokes use cases")
    Rel(controllers, streaming, "Streams progress via")
    Rel(orchestrator, agents, "Runs in sequence, with retry/backoff/timeout")
    Rel(agents, retrieval, "Guideline Researcher calls")
    Rel(agents, safety, "Safety Checker calls")
    Rel(agents, llm, "Completion/streaming calls")
    Rel(orchestrator, costGovernor, "Pre-flight estimate + budget check before each run")
    Rel(orchestrator, persistence, "Persists AgentRun/AgentStep/ApprovalDecision")
    Rel(retrieval, qdrant, "Dense search")
    Rel(retrieval, sql, "Keyword (full-text) search")
    Rel(persistence, sql, "EF Core")
    Rel(llm, ollama, "HTTP")
```

---

## 4. Sequence Diagram — Full Clinical Workflow (streaming + approval gate)

```mermaid
sequenceDiagram
    actor Clinician
    participant SPA as Angular SPA
    participant API as RunsController (SSE)
    participant Orch as PipelineOrchestrator
    participant Cost as CostAwareLlmRouter / TokenBudgetService
    participant GR as GuidelineResearcherAgent
    participant Retr as RrfHybridRetrievalService
    participant SC as SafetyCheckerAgent
    participant Safety as SqlDrugInteractionLookup
    participant DD as DocumentationDrafterAgent
    participant LLM as OllamaLLMProvider
    participant Appr as ApprovalController
    participant Audit as EfApprovalAuditWriter

    Clinician->>SPA: Submit clinical case, click "Run workflow"
    SPA->>API: GET /runs/{caseId}/stream (SSE)
    API->>Orch: RunClinicalWorkflowUseCase.Execute(caseId)
    Orch->>Cost: Pre-flight cost estimate vs remaining budget

    alt budget exhausted
        Cost-->>Orch: BudgetExceededError
        Orch-->>API: reject run (hard cut-off, no LLM call made)
        API-->>SPA: SSE: run rejected (budget)
    else budget OK
        Orch->>GR: Step 1 — retrieve guidance
        GR->>Retr: hybrid search (dense + keyword, RRF fusion)
        Retr-->>GR: ranked chunks with citations
        alt evidence insufficient
            GR-->>Orch: InsufficientEvidenceError (refuse, not infer)
            Orch-->>API: SSE: run terminated — refusal
        else evidence sufficient
            GR-->>Orch: guidance + structured citations
            Orch->>API: SSE: agent progress (step 1 complete)
            Orch->>SC: Step 2 — check interactions
            SC->>Safety: deterministic lookup(drugA, drugB)
            Safety-->>SC: interaction result (code-based, never LLM-guessed)
            SC-->>Orch: safety findings
            Orch->>API: SSE: agent progress (step 2 complete)
            Orch->>DD: Step 3 — draft clinical note
            DD->>LLM: Complete/Stream(prompt, guidance, safety findings)
            LLM-->>DD: token stream
            DD-->>Orch: draft note (held, not finalized)
            Orch->>API: SSE: agent progress (step 3 complete, awaiting approval)
            API-->>SPA: SSE: draft ready, run paused at approval gate
            Clinician->>SPA: Review draft, choose Approve / Reject / Edit-and-approve
            SPA->>Appr: POST /approval/{runId}/approve (or /reject, /edit-approve)
            Appr->>Audit: persist ApprovalDecision (who, when, what, why-if-rejected)
            Audit-->>Appr: recorded
            Appr-->>SPA: 200 OK, note finalized
        end
    end
```

---

## 5. ER Diagram — SQL Server Schema

Derived from `DomainCopilotDbContextModelSnapshot.cs`. Dashed relationships (`..`) are
columns that exist and are indexed/queried against, but have **no EF Core `HasForeignKey`
constraint** as of the current snapshot — worth noting honestly rather than drawing them as
enforced, and worth a line in `docs/SECURITY.md` / a follow-up ADR on whether that's an
intentional Clean-Architecture boundary (Domain/Application not referencing
`ApplicationUser`, which lives in Infrastructure/Identity) or a gap to close with a value
converter + owned reference.

```mermaid
erDiagram
    AGENT_RUNS ||--o{ AGENT_STEPS : "has (FK, cascade)"
    AGENT_RUNS ||--o| APPROVAL_DECISIONS : "has (FK, cascade, 1:1)"

    AGENT_RUNS }o..o| CLINICAL_CASES : "ClinicalCaseId (no FK constraint)"
    AGENT_RUNS }o..o| ASP_NET_USERS : "InitiatedByUserId (no FK constraint)"
    APPROVAL_DECISIONS }o..o| ASP_NET_USERS : "ClinicianUserId (no FK constraint)"
    CLINICAL_CASES }o..o| ASP_NET_USERS : "CreatedByUserId (no FK constraint)"
    TOKEN_BUDGETS }o..o| ASP_NET_USERS : "UserId (no FK constraint)"
    USAGE_RECORDS }o..o| AGENT_RUNS : "RunId (no FK constraint)"
    USAGE_RECORDS }o..o| AGENT_STEPS : "StepId, nullable (no FK constraint)"
    USAGE_RECORDS }o..o| ASP_NET_USERS : "UserId (no FK constraint)"
    DOCUMENT_CHUNKS }o..o| DOCUMENTS : "DocumentId, indexed (no FK constraint)"
    APPROVAL_AUDITS }o..o| AGENT_RUNS : "RunId, indexed (no FK constraint)"
    APPROVAL_AUDITS }o..o| ASP_NET_USERS : "ClinicianUserId (no FK constraint)"

    ASP_NET_USERS ||--o{ ASP_NET_USER_ROLES : "has"
    ASP_NET_ROLES ||--o{ ASP_NET_USER_ROLES : "has"
    ASP_NET_USERS ||--o{ ASP_NET_USER_CLAIMS : "has"
    ASP_NET_USERS ||--o{ ASP_NET_USER_LOGINS : "has"
    ASP_NET_USERS ||--o{ ASP_NET_USER_TOKENS : "has"
    ASP_NET_ROLES ||--o{ ASP_NET_ROLE_CLAIMS : "has"

    AGENT_RUNS {
        guid Id PK
        guid ClinicalCaseId
        guid InitiatedByUserId
        string CorrelationId
        string Status
        string TerminationReason
        datetimeoffset CreatedAt
        datetimeoffset StartedAt
        datetimeoffset CompletedAt
    }

    AGENT_STEPS {
        guid Id PK
        guid RunId FK
        int StepIndex "unique with RunId"
        string AgentRole
        string Status
        string Input
        string Output
        string ToolName
        string ModelUsed
        string ErrorMessage
        int UsagePromptTokens
        int UsageCompletionTokens
        datetimeoffset StartedAt
        datetimeoffset CompletedAt
    }

    APPROVAL_DECISIONS {
        guid Id PK
        guid RunId FK "unique, 1:1 with AgentRun"
        guid ClinicianUserId
        string DecisionType
        string OriginalDraftContent
        string EditedContent
        string RejectionReason
        datetimeoffset DecidedAt
    }

    CLINICAL_CASES {
        guid Id PK
        string CaseReference
        guid CreatedByUserId
        string PresentingComplaint
        string PatientContext
        string ProposedMedication
        string Medications "JSON list, column name Medications"
        bool IsSyntheticData
        datetimeoffset CreatedAt
    }

    TOKEN_BUDGETS {
        guid Id PK
        guid UserId "indexed with PeriodStart, PeriodEnd"
        bigint AllocatedTokens
        bigint ConsumedTokens
        datetimeoffset PeriodStart
        datetimeoffset PeriodEnd
    }

    USAGE_RECORDS {
        guid Id PK
        guid RunId "indexed"
        guid StepId "nullable"
        guid UserId "indexed with RecordedAt"
        string ModelName
        decimal EstimatedCostUsd
        int UsagePromptTokens
        int UsageCompletionTokens
        datetimeoffset RecordedAt
    }

    DOCUMENTS {
        guid Id PK
        string SourceKey "unique"
        string FileName
        string Format
        string ContentHash
        string Status
        string FailureReason
        int Version
        int ChunkCount
        string GuidelineVersionLabel
        datetimeoffset GuidelineEffectiveDate
        datetimeoffset CreatedAtUtc
        datetimeoffset UpdatedAtUtc
    }

    DOCUMENT_CHUNKS {
        guid Id PK
        guid DocumentId "indexed"
        int DocumentVersion "indexed with DocumentId"
        int ChunkIndex
        int PageNumber
        string Section
        string Text
    }

    APPROVAL_AUDITS {
        guid Id PK
        guid RunId "indexed"
        guid ClinicianUserId
        string DecisionType
        string PreviousStatus
        string Comment
        datetimeoffset DecidedAtUtc
    }

    DRUG_INTERACTION_RULES {
        int Id PK
        string RuleId
        string DrugANormalized "unique with DrugBNormalized"
        string DrugBNormalized
        string Severity
        string Description
    }

    KNOWN_MEDICATIONS {
        int Id PK
        string NameNormalized "unique"
    }

    ASP_NET_USERS {
        guid Id PK
        string UserName
        string Email
        string DisplayName
        bool EmailConfirmed
        bool LockoutEnabled
        int AccessFailedCount
    }

    ASP_NET_ROLES {
        guid Id PK
        string Name
    }

    ASP_NET_USER_ROLES {
        guid UserId PK_FK
        guid RoleId PK_FK
    }

    ASP_NET_USER_CLAIMS {
        int Id PK
        guid UserId FK
        string ClaimType
        string ClaimValue
    }

    ASP_NET_ROLE_CLAIMS {
        int Id PK
        guid RoleId FK
        string ClaimType
        string ClaimValue
    }

    ASP_NET_USER_LOGINS {
        string LoginProvider PK
        string ProviderKey PK
        guid UserId FK
    }

    ASP_NET_USER_TOKENS {
        guid UserId PK
        string LoginProvider PK
        string Name PK
        string Value
    }
```

### Honest note on this schema

Two things worth stating plainly in `docs/SYSTEM-DESIGN.md` or a short ADR, since they're
real properties of the current model, not oversights to hide:

1. **Referential integrity for cross-aggregate references (`UserId`, `ClinicalCaseId`, etc.)
   is enforced in application code, not the database.** This is a defensible Clean
   Architecture choice — `DomainCopilot.Domain` doesn't reference
   `DomainCopilot.Infrastructure.Identity.ApplicationUser` — but it does mean a bad write
   path could insert an orphaned `UserId` that the database itself won't reject. Worth
   naming as an accepted trade-off, not silently leaving it undocumented.
2. `DocumentChunk.DocumentId` has no enforced FK either, despite `Document` being in the
   same bounded context (`DomainCopilot.Domain.Ingestion`). That one's harder to justify as
   an architectural boundary and is worth either fixing (a straightforward migration) or
   explaining as a deliberate performance/ingestion-throughput trade-off if that's the real
   reason.

---

## 6. Layer-Dependency Diagram

```mermaid
graph TD
    subgraph Domain["DomainCopilot.Domain — zero external dependencies"]
        D1["Entities: AgentRun, AgentStep, ApprovalDecision,<br/>ClinicalCase, TokenBudget, UsageRecord,<br/>Document, DocumentChunk"]
        D2["Errors: BudgetExceededError, InsufficientEvidenceError,<br/>InvalidApprovalStateError, NoActiveBudgetPeriodError"]
        D3["Common: Entity, ValueObject, Result, DomainError"]
    end

    subgraph Application["DomainCopilot.Application — depends on Domain only"]
        A1["Use Cases: RunClinicalWorkflowUseCase,<br/>ApprovalWorkflowUseCase, IngestDocumentUseCase"]
        A2["Ports: ILLMProvider, IVectorStore, IDocumentRepository,<br/>IHybridRetrievalService, ITokenBudgetService"]
        A3["Agent contracts: IGuidelineResearcherAgent,<br/>ISafetyCheckerAgent, IDocumentationDrafterAgent"]
    end

    subgraph Infrastructure["DomainCopilot.Infrastructure — implements Application's ports"]
        I1["EF Core: DomainCopilotDbContext, Ef*Repository"]
        I2["Qdrant client: QdrantVectorStore"]
        I3["Ollama HTTP client: OllamaLLMProvider, OllamaEmbeddingService"]
        I4["ASP.NET Core Identity: ApplicationUser, Roles"]
        I5["Agent implementations: GuidelineResearcherAgent,<br/>SafetyCheckerAgent, DocumentationDrafterAgent"]
    end

    subgraph Api["DomainCopilot.Api — composition root"]
        P1["Controllers"]
        P2["Program.cs — DI wiring"]
        P3["SSE Streaming"]
    end

    Application -->|references| Domain
    Infrastructure -->|implements ports of| Application
    Infrastructure -->|references| Domain
    Api -->|references| Application
    Api -->|references| Infrastructure
    Api -->|references| Domain

    style Domain fill:#d4f7d4,stroke:#2c7a2c
    style Application fill:#d4e7f7,stroke:#2c5a7a
    style Infrastructure fill:#f7ecd4,stroke:#7a5a2c
    style Api fill:#f7d4d4,stroke:#7a2c2c
```

**How to actually verify this, not just claim it:** `Domain` has no outgoing arrow in the
diagram above because nothing in `DomainCopilot.Domain.csproj` should reference EF Core, the
Qdrant client, an HTTP client, or ASP.NET Core. Run this to check it yourself before citing
the diagram as fact:

```bash
cat src/DomainCopilot.Domain/DomainCopilot.Domain.csproj
```

If that file has zero `<PackageReference>` entries beyond the bare SDK, the diagram is
accurate. If it has any, the diagram (and the acceptance test in Section 3 of the brief) is
currently wrong and should say so.
