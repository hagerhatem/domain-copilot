# ADR-003: Vector Store Choice — Qdrant Alongside SQL Server

## Status

Accepted — 2026-08-28

## Context

The system requires a vector store for dense semantic retrieval over the ingested clinical guideline and drug-reference corpus, used by the Guideline Researcher agent and by the hybrid retrieval fusion method specified in FR-2 (dense via vector search + keyword via SQL Server full-text search). It also requires a relational store for everything that is naturally relational and transactional: user accounts and roles (FR-8), per-user token budgets and spend history (the Cost Governor twist), run/step audit trails and approval-gate decisions (FR-5), and document/chunk metadata (FR-1).

The absolute constraint from the project brief is **no paid subscription, no credit card requirement, free-tier-or-local only**. Three architectural options were considered for where vector search lives:

1. **SQL Server alone, using a native or extension-based vector capability**, avoiding a second database entirely.
2. **A managed cloud vector database** (e.g., a hosted Pinecone, Weaviate Cloud, or similar SaaS offering), even on its free tier.
3. **Qdrant, self-hosted via Docker, alongside SQL Server** as a dedicated relational store — two databases, each doing what it is strongest at.

## Decision

We will use **Qdrant (self-hosted via Docker) for vector storage and dense semantic search**, and **SQL Server (Developer Edition, self-hosted via Docker) for all relational data**: users/roles, token budgets and spend records, run/audit trails, document/chunk metadata, and full-text keyword search for the FR-2 hybrid retrieval fusion. These are two separate database engines running as two separate containers, not a single combined store.

### Why not SQL Server alone with a vector extension

At the time of this decision, SQL Server does not have a mature, production-grade native vector search capability comparable to a dedicated vector database — this is stated directly in the project's mandatory tech stack rationale, and independent verification during this ADR's research did not surface a free, self-hostable SQL Server vector extension with the filtering, indexing (HNSW or equivalent approximate-nearest-neighbor structures), and query-performance characteristics that a RAG system's dense-retrieval path needs at even a modest corpus scale. Bolting an immature or extension-based vector capability onto SQL Server would mean the system's core retrieval quality depends on the least mature part of the relational engine, rather than on a component whose entire purpose is vector search. This is a case where a single-database architecture would be simpler operationally but would ship a materially worse dense-retrieval experience — and dense retrieval quality is not a secondary concern for this project, given that FR-2's mandatory hybrid retrieval and the D0 refusal-on-insufficient-evidence requirement both depend on retrieval actually surfacing the right chunks.

It is also worth being explicit that a single-database design was attractive specifically *because* it would reduce operational complexity, and this ADR does not dismiss that motivation — it is addressed directly in the Consequences section below, because the two-database trade-off is real and should not be understated.

### Why not a managed cloud vector database

A hosted vector database (even a generous free tier) reintroduces exactly the kind of external dependency the project's absolute constraint rules out: an internet-dependent, third-party-account-gated service that could change its free-tier terms, require a credit card for verification, or simply be unavailable during grading if the account, region, or quota has any issue outside the project's control. Self-hosting a free, open-source vector database via Docker keeps the entire system runnable with a single `docker compose up`, with zero external accounts required beyond obtaining free-tier LLM API keys already necessitated by the LLM provider requirement — and even that has a fully offline fallback via Ollama. A managed vector DB would also complicate the "runs entirely offline with a local model, no key" requirement from the README mandate, since the vector store itself would still require network access even if the LLM did not.

### Why Qdrant specifically (among self-hostable open-source options)

Qdrant was selected over other self-hostable open-source vector stores primarily for operational fit with the rest of the mandated stack: it runs as a single, lightweight Docker container with no complex external dependencies of its own, exposes both a REST and gRPC API with a stable, well-documented .NET client story (important given the mandatory .NET 8 backend and the `ILLMProvider`/vector-store-port abstraction required by the Clean Architecture mandate), supports payload-based metadata filtering natively (needed for the FR-2 "filter by guideline version/date" enhancement), and has a straightforward local persistence model suitable for a `docker compose up` development/demo environment without requiring a managed control plane.

## Consequences

### Positive

- Each database does the job it is actually good at: SQL Server for ACID-transactional relational data (budgets, audit trails, user/role permissions — data where consistency and relational integrity genuinely matter) and Qdrant for approximate-nearest-neighbor vector search at the quality and speed a RAG system needs.
- The `ILLMProvider`-style port/adapter boundary (ADR pending for the LLM provider abstraction) extends naturally to a `IVectorStore` port — swapping Qdrant for a different vector store later requires only a new adapter implementation, per the Clean Architecture acceptance test in the project brief, without touching Domain or Application code.
- FR-2's hybrid retrieval (dense + keyword) is naturally expressed as two real, independently-optimized search paths — Qdrant's vector similarity and SQL Server's native full-text search — fused by a documented method, rather than one engine straining to do both jobs adequately.
- Fully free, fully self-hostable, fully offline-capable: no external account, no credit card, no dependency on a cloud vendor's uptime or free-tier policy surviving until submission/grading.

### Negative — and honestly, this is the real cost of this decision

Running two database engines instead of one is **genuinely more operationally complex**, and this ADR does not minimize that:

- **Two schemas to design, migrate, and keep in sync conceptually** — a chunk's metadata must exist consistently in both systems (SQL Server holds the authoritative document/chunk metadata row; Qdrant holds the same chunk's ID plus its embedding and a payload copy of key filterable metadata). A change to chunk metadata during re-ingestion must update both, and nothing in either engine enforces that consistency automatically — this is application-level responsibility, and a real source of bugs if not handled carefully in the ingestion pipeline's idempotent re-ingestion logic (FR-1).
- **Two sets of operational concerns**: two containers to health-check (FR-9's readiness endpoints must check both), two backup/restore stories to reason about (even for a local dev/demo system, `docs/SYSTEM-DESIGN.md`'s Part A unconstrained-architecture discussion has to address disaster recovery for both stores, not one), and two failure modes to handle gracefully (what happens to a query if Qdrant is reachable but SQL Server is not, or vice versa — this is addressed in the graceful-degradation path of ADR-002's Pipeline design, but it is additional design surface that a single-database system would not have).
- **Two client libraries and connection-management concerns** in the Infrastructure layer, and two things that must both be healthy for `docker compose up` to produce a working system — more moving parts than a single-database design, by definition.
- **Local development and CI both take a real hit in startup time and resource usage**: two database containers plus the API and Angular frontend is a heavier `docker compose up` than a single-database equivalent, which matters for a project whose README explicitly promises a "5-Minute Demo Path."

### How Docker Compose mitigates this — but does not eliminate it

Docker Compose is the primary mitigation, and it genuinely does most of the practical work: a single `docker-compose.yml` defines both database containers with their networking, environment variables, and named volumes for persistence, so from the person running the project's perspective, `docker compose up` still starts *everything* with one command regardless of how many containers are behind it. Compose's `depends_on` with health checks ensures the API does not attempt to connect to either store before it is ready, which removes the most common class of "two databases means two things that can be started in the wrong order" failure. A seed/ingest script run after `docker compose up` populates both stores from the same source documents in one pass, so from a user's point of view there is still only one setup command and one seed command, matching the README's promised quick-start experience.

What Docker Compose does **not** mitigate is the actual engineering cost documented above: the dual-write consistency responsibility during ingestion, the two-store health-check and graceful-degradation logic in the API layer, and the added local resource footprint. Compose makes the two databases *convenient to start*; it does not make them *one database*. This ADR's position is that this remaining complexity is a reasonable and worthwhile trade for retrieval quality and hybrid-search capability that a single-database design could not deliver within the project's free-tier constraint — but it is a real cost, not a solved problem, and `docs/SYSTEM-DESIGN.md`'s gap table should list "single unified data layer" as a target-architecture simplification that was deliberately deferred here, with this ADR as the interim mitigation reference.

## Alternatives Considered

**SQL Server alone with a vector extension/capability.** Rejected as the primary store for dense retrieval due to relative immaturity versus a dedicated vector database at the time of this decision, and because it would make the system's most retrieval-critical capability dependent on the least proven part of the chosen relational engine. Revisit this decision if SQL Server's native vector capabilities mature significantly and demonstrate comparable indexing/filtering/performance characteristics to Qdrant — at that point, the operational-complexity savings of a single database could outweigh the current retrieval-quality gap, and this would be a legitimate simplification for a future iteration.

**A managed/hosted cloud vector database (free tier).** Rejected because it reintroduces an external, internet-dependent, account-gated dependency that conflicts with the project's absolute free-tier/no-external-account/fully-offline-capable constraint, and because it would break the fully-local, no-network-required fallback mode that the README is required to document. Revisit only if the project's constraints change to permit or require a genuinely cloud-native target deployment — this is in fact partially addressed already in `docs/SYSTEM-DESIGN.md` Part A, which describes a managed vector DB as part of the unconstrained target architecture, with this ADR's Qdrant choice documented there as the MVP's interim, cost-driven substitute.

**A single self-hosted database attempting to serve both roles via a document-store-with-vector-plugin approach (e.g., a NoSQL document database with vector search bolted on), replacing SQL Server entirely rather than replacing Qdrant.** Briefly considered and rejected: this would abandon the strong relational/transactional guarantees needed for token-budget enforcement (the Cost Governor's hard cut-off, FR-T3, must be enforced deterministically and atomically against a per-user budget — exactly the kind of transactional guarantee a relational engine provides cleanly) in favor of a store optimized for a different workload. This alternative solves the "two databases" complexity concern but at the cost of weakening the data-integrity guarantees the Cost Governor twist specifically depends on, which is a worse trade than the one this ADR accepts.
