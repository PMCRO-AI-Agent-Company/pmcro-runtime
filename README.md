# pmcro-runtime

**Production runtime host for the PMCR-O Colony.**

.NET Aspire orchestration + Microsoft Agent Framework (MAF) loop + Next.js frontend (CopilotKit) that executes governed Plan → Make → Check → Reflect cycles and seals immutable trails.

Colony home: [PMCRO-AI-Agent-Company](https://github.com/PMCRO-AI-Agent-Company)  
Skills & laws: [pmcro-skills](https://github.com/PMCRO-AI-Agent-Company/pmcro-skills)  
.NET domain skills: [dotnet-skills](https://github.com/PMCRO-AI-Agent-Company/dotnet-skills)

---

## What this repo is

| Layer | Location | Role |
|-------|----------|------|
| **AppHost** | `src/ProjectName.AppHost` | Aspire 13.5.1 orchestration entry |
| **OrchestratorService** | `src/services/ProjectName.OrchestratorService` | MAF loop (`PmcroLoop`), trail writer, skill loader, HIL channel, declarative workflows |
| **OrchestratorApi** | `src/ProjectName.OrchestratorApi` | HTTP surface (Cycle, TrailReplay, Skills, RoundTable, Copilot) |
| **ServiceDefaults** | `src/ProjectName.ServiceDefaults` | Shared Aspire / Ollama / OpenTelemetry wiring |
| **Frontend** | `src/frontend` | Next.js + CopilotKit 1.69.0 (ConsoleView, TrailView, A2UIRenderer, AgentDirectory, HarnessView) |
| **MCP servers** | `mcp/` | Filesystem, Playwright, Terminal (and related) |

**Core packages (live pin):**
- Microsoft.Agents.AI family **1.18.0** (stable + matching preview hosting surface)
- Aspire **13.5.1**
- CopilotKit `@copilotkit/react-core|react-ui|runtime` **1.69.0**
- ModelContextProtocol **2.2.0**

---

## Quick start

```bash
# Backend (Aspire)
dotnet run --project src/ProjectName.AppHost

# Frontend
cd src/frontend
npm install
npm run dev
```

Frontend expects the Orchestrator API and CopilotKit route (`app/api/copilotkit/route.ts`) to be reachable. Prefer v2 entry points:

```ts
import { CopilotChat } from "@copilotkit/react-core/v2";
import "@copilotkit/react-ui/v2/styles.css";
```

---

## Cognitive loop

Every governed cycle follows the Colony contract (see `pmcro-skills`):

```
SEED INTENT
    → [EC-001 GATE]
    → PLAN  (ExecutionPlan)
    → MAKE  (MakerFrame)
    → CHECK (CheckerFrame, 3-dimension)
    → REFLECT (verdict: ACCEPT | LOOP | ESCALATE)
    → TRAIL (JSONL under .pmcro/projects/.../trails/)
```

- **TYPE 1** (world-changing) operations require HIL approval (MAAI-001).
- Trails are append-only and immutable once disposition is sealed (EC-010 / EC-012).
- Skills are loaded from marketplace materialization + local skill catalogs.

---

## Frontend surfaces

| Surface | Purpose |
|---------|---------|
| **ConsoleView** | Primary governed UI (PhaseRail, transcript, real snapshots only) |
| **TrailView / Trails page** | Replay sealed cycles |
| **AgentDirectory** | Registered agents |
| **A2UIRenderer** | Declarative / Generative UI band |
| **HarnessView** | Readonly MAF loop (post-OMode; `/harness` redirects to `?mode=readonly`) |
| **RoundTable** | Multi-agent visibility |

Data-sourcing rule: **real snapshots only** — no synthetic evidence.

---

## Related Colony repos

| Repo | Role |
|------|------|
| [pmcro-skills](https://github.com/PMCRO-AI-Agent-Company/pmcro-skills) | Phase plugins, laws, skill-creator, catalog |
| [dotnet-skills](https://github.com/PMCRO-AI-Agent-Company/dotnet-skills) | .NET / C# domain skills |
| [agent-skills](https://github.com/PMCRO-AI-Agent-Company/agent-skills) | Base agentskills.io template |
| [figma-skills](https://github.com/PMCRO-AI-Agent-Company/figma-skills) | Figma domain pack |
| [github-skills](https://github.com/PMCRO-AI-Agent-Company/github-skills) | GitHub domain pack |

---

## Governance

- Colony Laws: `.pmcro/laws/colony-laws.md` (in pmcro-skills)
- MAF version source of truth: `.pmcro/references/maf_versions.md`
- Trail is the product.

---

## License

See [LICENSE.txt](LICENSE.txt).
