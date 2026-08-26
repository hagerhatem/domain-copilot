# Domain Copilot — D0T3 (Healthcare / Cost Governor)

> Agentic RAG platform for clinical evidence & documentation, with an enforced
> per-user token/cost budget ("Cost Governor"). Built for [assessment context]
> using .NET 8, Angular, SQL Server, and Qdrant — 100% free-tier / local, no
> paid services required.

## Variant Derivation

- `Domain = (last two digits of National ID) mod 7 = 0` → **D0: Healthcare — clinical evidence & documentation**
- `Twist = (sum of all digits of National ID) mod 8 = 3` → **T3: Cost Governor**

## Status

🚧 Work in progress — this README will be filled in as each phase lands.

## Prerequisites

- [ ] Docker & Docker Compose
- [ ] (Optional, for local dev outside containers) .NET 8 SDK, Node.js LTS, Angular CLI

## Quick Start

> _TODO: fill in once `docker-compose.yml` and seed scripts exist (Phase 1+)._

```bash
# placeholder
git clone <repo-url>
cd domain-copilot
cp .env.example .env
docker compose -f docker/docker-compose.yml up --build
```

## Environment Variables

> _TODO: full table of every variable, what it does, and how to obtain free API keys — Phase 1+._

## Running Fully Offline (No API Key)

> _TODO: instructions for Ollama-only mode — Phase 1+._

## Tests & Evaluation Harness

> _TODO — Phase 3+._

## Demo Accounts

> _TODO — seeded once auth + roles land._

## 5-Minute Demo Path

> _TODO — numbered walkthrough, filled in near submission._

## Documentation

- [`docs/BRD.md`](docs/BRD.md)
- [`docs/SYSTEM-DESIGN.md`](docs/SYSTEM-DESIGN.md)
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/SECURITY.md`](docs/SECURITY.md)
- [`docs/EVALUATION.md`](docs/EVALUATION.md)
- [`docs/AGENTIC-WORKFLOW.md`](docs/AGENTIC-WORKFLOW.md)
- [`docs/AI-USAGE-LOG.md`](docs/AI-USAGE-LOG.md)
- ADRs: [`docs/adr/`](docs/adr/)

## License

See [LICENSE](LICENSE).