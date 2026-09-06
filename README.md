# Domain Copilot — D0T3 (Healthcare / Cost Governor)

> Agentic RAG platform for clinical evidence & documentation, with an enforced
> per-user token/cost budget ("Cost Governor"). Built for a post-graduate
> instructor technical assessment using .NET 8, Angular, SQL Server, and
> Qdrant — 100% free-tier / local, no paid services required.

## Variant Derivation

- `Domain = (last two digits of National ID) mod 7 = 0` → **D0: Healthcare — clinical evidence & documentation**
- `Twist = (sum of all digits of National ID) mod 8 = 3` → **T3: Cost Governor**

## Status

🚧 Work in progress. Core layering (Domain / Application / Infrastructure / Api),
ingestion pipeline, hybrid retrieval, the 3-agent pipeline + orchestrator, the
approval gate, the Cost Governor, EF Core migrations, identity/auth, and CI are
implemented. Still open: `docs/EVALUATION.md` with real (non-stub) numbers,
`docs/AGENTIC-WORKFLOW.md`, `docs/AI-USAGE-LOG.md`, `teaching/` materials, demo
videos, `LICENSE`, `CONTRIBUTING.md`, `CODEOWNERS`, and PR/Issue templates.

## Architecture

```
src/DomainCopilot.Domain          → Pure C# entities, value objects, domain errors
src/DomainCopilot.Application     → Use cases, ports (ILLMProvider, IVectorStore, ...), agents contracts
src/DomainCopilot.Infrastructure  → EF Core + SQL Server, Qdrant client, Ollama LLM provider, cost governor, safety lookup
src/DomainCopilot.Api             → ASP.NET Core controllers, SSE streaming, JWT auth, health checks
src/DomainCopilot.Evaluation      → Golden-set runner + metrics (retrieval hit-rate, groundedness, refusal correctness)
frontend/domain-copilot-ui        → Angular 21 UI (login, ingest, ask, run workflow, approval queue, trace viewer)
tests/                            → Domain, Application, and Integration test projects
```

See `docs/ARCHITECTURE.md` for C4 diagrams, the sequence diagram of the agentic
workflow, and the ADRs.

## Prerequisites

- Docker & Docker Compose
- Node.js LTS + npm (to run the Angular frontend — **not yet wired into
  `docker-compose.yml`**, see note below)
- (Optional, for local dev outside containers) .NET 8 SDK, Angular CLI

## Quick Start

`docker-compose.yml` currently brings up **SQL Server, Qdrant, Ollama, and the
API only**. The Angular frontend is run separately with the Angular CLI —
containerizing it is still open work.

```bash
git clone <repo-url>
cd domain-copilot
cp .env.example .env
# edit .env and set SQL_SA_PASSWORD and JWT_KEY (see below)

docker compose up --build
```

`.env.example` documents both required variables (`SQL_SA_PASSWORD` and
`JWT_KEY`, with the constraints each one has to satisfy) — see the
"Environment Variables" section below for details. `.env` itself is
gitignored and must never be committed.

Once the API container reports healthy (`/health/ready`), run EF Core
migrations against the SQL Server container if they haven't been applied yet,
then start the frontend in a second terminal:

```bash
cd frontend/domain-copilot-ui
npm ci
npm start
```

The API listens on `http://localhost:8080`, the Angular dev server on
`http://localhost:4200` (already whitelisted in CORS via `Cors:AllowedOrigins`
in `appsettings.example.json`).

## Environment Variables

| Variable | Used by | Purpose | Example / notes |
|---|---|---|---|
| `SQL_SA_PASSWORD` | `docker-compose.yml` (sqlserver, api) | SQL Server `sa` password | Must meet SQL Server's password complexity rules |
| `JWT_KEY` | `docker-compose.yml` → API `Jwt:Key` | Signing key for JWT auth | 32+ random chars. **Dev-only placeholder currently committed** in `appsettings.Development.json` (`dev-only-insecure-key-...`) — never reuse it outside local dev |
| `ConnectionStrings__SqlServer` | API (set inline in `docker-compose.yml`) | EF Core connection string | Points at the `sqlserver` service inside Compose |
| `Qdrant__Host` / `Qdrant__Port` | API | Qdrant gRPC endpoint | `qdrant` / `6334` inside Compose |
| `Llm__Ollama__BaseUrl` | API | Ollama base URL | `http://ollama:11434` inside Compose |
| `SkipStartupInitializers` | API | Temporary flag | Skips a startup check for SQL Server Full-Text Search, which isn't enabled by default in the `mssql/server:2022-latest` image — see inline comment in `docker-compose.yml`. Retrieval currently falls back to dense-only (Qdrant) search until FTS is enabled in the container |

Non-secret defaults (Qdrant collection name/vector size, chunking sizes,
allowed CORS origins) live in `src/DomainCopilot.Api/appsettings.example.json`
and are safe to commit as-is.

## Running Fully Offline (No API Key)

The system is designed to run entirely on the local **Ollama** provider with
no hosted API key. `ILLMProvider` has two implementations selected via
configuration (see `docs/adr/` for the fallback-chain design); the Ollama
implementation (`OllamaLLMProvider`) is the one exercised end-to-end in
`docker-compose.yml` today. Pull the model(s) into the `ollama` container
before running a full workflow, e.g.:

```bash
docker compose exec ollama ollama pull llama3
```

## Tests & Evaluation Harness

```bash
# Unit + integration tests
dotnet test src/DomainCopilot.sln

# Evaluation harness (golden set)
dotnet run --project src/DomainCopilot.Evaluation
```

- Test projects: `DomainCopilot.Domain.Tests`, `DomainCopilot.Application.Tests`
  (includes dedicated `Security/PromptInjectionTests.cs`), `DomainCopilot.Integration.Tests`.
- Golden set: `eval/golden-set.json` — **25/25 Q/A pairs**, including the
  required adversarial categories (out-of-corpus, ambiguous/insufficient info,
  prompt injection via an ingested document, cross-guideline tension standing
  in for "conflicting sources" — see the `known_gap` note in the golden set's
  `_meta` block for why, and the two options considered to close it).
- **`eval/report.md` reflects a stub run, not the real system**: it explicitly
  states retrieval used a naive lexical-overlap placeholder and the workflow
  used a fixed placeholder response, and warns these numbers must not be
  reported as the FR-3 baseline. Re-running the harness against the real
  Qdrant + SQL Server hybrid retrieval and the real orchestrator, and writing
  up the honest results in `docs/EVALUATION.md`, is still open work.

## Demo Accounts

Seeded via EF Core migration `HasData` (`AddIdentityTables`), so they exist
immediately after migrations run — no manual seeding step needed.

| Role | Email | Password |
|---|---|---|
| Clinician | `clinician@demo.local` | `Demo#12345` |
| Admin | `admin@demo.local` | `Demo#12345` |

These are local dev/demo credentials only, never real accounts.

## Seed Corpus

`src/seed-data/` currently contains:

- **15 PDFs** — FDA drug labels (8), WHO essential medicines list (1), and
  clinical guidelines from NICE, WHO, IDSA, and ISPAD (6).
- **15 synthetic case files (.docx)** — straightforward, ambiguous,
  out-of-corpus, and one adversarial (prompt-injection) case.
- 3 standalone prompt-injection test fixtures under `seed-data/injection-tests/`.

This satisfies the ≥30 document / ≥2 format requirement (FR corpus
requirement), pending confirmation of total page count (≥150 pages).

## 5-Minute Demo Path

> TODO — will be written once the full `docker compose up` + migrate + seed
> flow above has been verified end-to-end on a clean machine.

## Documentation

- [`docs/BRD.md`](docs/BRD.md)
- [`docs/SYSTEM-DESIGN.md`](docs/SYSTEM-DESIGN.md)
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/SECURITY.md`](docs/SECURITY.md)
- `docs/EVALUATION.md` — **not yet written**; see "Tests & Evaluation Harness" above for current (stub) status
- `docs/AGENTIC-WORKFLOW.md` — **not yet written**
- `docs/AI-USAGE-LOG.md` — **not yet written**
- ADRs: [`docs/adr/`](docs/adr/) — chunking strategy, orchestration pattern, vector store choice, Cost Governor design

## CI

GitHub Actions (`.github/workflows/ci.yml`) runs on every PR and push to
`main`: .NET restore/format-check/build/test with coverage, a .NET
vulnerable-package scan, Angular install/lint/build, `npm audit`, and a
full-history secret scan via **gitleaks** (config in `.gitleaks.toml`). A
`ci-success` job aggregates all of the above into a single required status
check.

## Known Gaps (honest, as of this commit)

- Angular frontend not yet containerized / added to `docker-compose.yml`.
- SQL Server Full-Text Search not enabled in the container image; hybrid
  retrieval currently degrades to dense-only (Qdrant) search
  (`SkipStartupInitializers=true`).
- Evaluation numbers in `eval/report.md` are from a stub pipeline, not the
  real retrieval/orchestrator — `docs/EVALUATION.md` with real numbers is
  still pending.
- Repo hygiene items required by the assessment brief not yet present:
  `LICENSE`, `CONTRIBUTING.md`, `CODEOWNERS`, PR/Issue templates.

## License

`LICENSE` file not yet added to the repository — pending.