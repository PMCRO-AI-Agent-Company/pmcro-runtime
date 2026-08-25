# pmcro-runtime

**Production runtime host for the PMCR-O Colony.**

.NET Aspire orchestration + Microsoft Agent Framework (MAF) loop + Next.js/CopilotKit frontend that executes governed Plan → Make → Check → Reflect cycles and seals immutable trails.

## Current platform baseline

The runtime is pinned to the current published MAF 1.19.0 stable core/workflow/harness line, the matching 1.19.0 preview hosting/DevUI surface, and ModelContextProtocol 2.2.0. Version-sensitive decisions are recorded in `Directory.Packages.props` and must be revalidated before future upgrades.

| Layer | Location | Role |
|---|---|---|
| **AppHost** | `src/ProjectName.AppHost` | Aspire orchestration entry |
| **OrchestratorService** | `src/services/ProjectName.OrchestratorService` | MAF loop, trail writer, skill loader, HIL channel, declarative workflows |
| **OrchestratorApi** | `src/ProjectName.OrchestratorApi` | Cycle, trail replay, skills, round-table and Copilot surfaces |
| **ServiceDefaults** | `src/ProjectName.ServiceDefaults` | Aspire / Ollama / OpenTelemetry wiring |
| **Frontend** | `src/frontend` | Next.js + CopilotKit governed UI |
| **MCP servers** | `mcp/` | Filesystem, Playwright, Terminal and related capability providers |

### MAF / MCP boundary

PMCRO uses Microsoft Agent Framework for agent/workflow execution rather than implementing a competing agent runtime. MAF provides local and hosted MCP tool integration, Harness, approvals and observability; PMCRO adds governance, trail semantics, acceptability gates and the PMCR-O cycle around those primitives.

The MCP servers remain capability providers. Their calls cross the `pmcro-actuator` evidence boundary and are preserved as governed evidence rather than being reimplemented inside the Orchestrator.

> **Compatibility note:** the current MAF .NET MCP connector has an upstream gap around the MCP 2026-07-28 Tasks Extension. Keep the runtime on the stable MCP 2.2.0 SDK while explicitly testing protocol negotiation before enabling 2026 task-only server behavior.

## Quick start

```bash
dotnet run --project src/ProjectName.AppHost

cd src/frontend
npm install
npm run dev
```

## Cognitive loop

```text
SEED INTENT
    → EC-001 GATE
    → PLAN  (ExecutionPlan)
    → MAKE  (MakerFrame)
    → CHECK (CheckerFrame)
    → REFLECT (ACCEPT | LOOP | ESCALATE)
    → TRAIL (append-only JSONL)
```

TYPE 1 world-changing operations require HIL approval. Trails are immutable after disposition is sealed. Skills are materialized from the marketplace and local catalogs.

## Frontend surfaces

- **ConsoleView** — primary governed UI with real snapshots only
- **TrailView / Trails** — replay sealed cycles
- **AgentDirectory** — registered agents
- **A2UIRenderer** — declarative UI surface
- **HarnessView** — readonly MAF harness visibility
- **RoundTable** — multi-agent visibility

No synthetic evidence is permitted in the governed UI.

## Related repositories

- `PMCRO-AI-Agent-Company/pmcro-skills` — laws, phase plugins, orchestration and strategy skills
- `PMCRO-AI-Agent-Company/dotnet-skills` — .NET/C# domain skills
- `PMCRO-AI-Agent-Company/agent-skills` — base Agent Skills authoring/template repository
- `PMCRO-AI-Agent-Company/github-skills` — GitHub operations and Actions/skills automation
- `PMCRO-AI-Agent-Company/figma-skills` — Figma domain pack

## Governance

The runtime is governed by the PMCR-O laws and contracts from `pmcro-skills`. `.pmcro/` is the Colony runtime instance and must remain separate from the distributable marketplace source repository.

## Security

MCP, shell/CodeAct and mutating GitHub operations are capability boundaries. Read-only inspection may be automatic; mutations require explicit governed approval and evidence. Third-party skills must be inspected before installation because Agent Skills can contain prompt injections or executable content.

See `AGENTS.md` for repository execution rules.
