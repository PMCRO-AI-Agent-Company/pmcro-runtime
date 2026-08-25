# pmcro-runtime

**Production runtime host for the PMCR-O Colony.**

.NET Aspire orchestration + Microsoft Agent Framework (MAF) + Next.js/CopilotKit frontend that executes governed Plan → Make → Check → Reflect cycles and seals immutable trails.

## Current platform baseline

The runtime is pinned to the current published MAF 1.19.0 stable core/workflow/harness line, the matching 1.19.0 preview hosting/DevUI surface, and ModelContextProtocol 2.2.0. Version-sensitive decisions are recorded in `Directory.Packages.props` and must be revalidated before future upgrades.

| Layer | Location | Role |
|---|---|---|
| **AppHost** | `src/ProjectName.AppHost` | Aspire orchestration entry |
| **OrchestratorService** | `src/services/ProjectName.OrchestratorService` | MAF execution, trail writer, skill loader, HIL channel, declarative workflows |
| **OrchestratorApi** | `src/ProjectName.OrchestratorApi` | Cycle, trail replay, skills, round-table and Copilot surfaces |
| **ServiceDefaults** | `src/ProjectName.ServiceDefaults` | Aspire / Ollama / OpenTelemetry wiring |
| **Frontend** | `src/frontend` | Next.js + CopilotKit governed UI |
| **MCP servers** | `mcp/` | Filesystem, Playwright, Terminal and related capability providers |

### MAF / MCP / PMCRO boundary

PMCRO uses Microsoft Agent Framework for agent and workflow execution rather than implementing a competing agent runtime. MAF provides workflow execution, MCP integration, Harness, approvals, state/events and observability; PMCRO adds governance, trail semantics, acceptability gates and PMCR-O disposition around those primitives.

The **MAF declarative workflow is the canonical PMCR-O execution path** for supported actions. The application should not introduce another hand-written Plan → Make → Check → Reflect executor when the declarative runner is available. `PmcroLoop` remains only as a migration/reference implementation until the declarative path has passed live regression equivalence.

The MCP servers remain capability providers. Tool execution and results must cross the governed evidence boundary and become structured workflow data before Checker evaluation. Console logs, UI transcript text, or unrelated buffers are not authoritative evidence.

> **Compatibility note:** the current MAF .NET MCP connector has an upstream gap around the MCP 2026-07-28 Tasks Extension. Keep the runtime on the stable MCP 2.2.0 SDK while explicitly testing protocol negotiation before enabling 2026 task-only server behavior.

## Evidence invariant

The authoritative cycle is:

```text
SEED INTENT
    → PLAN
    → DECLARED ACTION / MCP TOOL
    → STRUCTURED EXECUTION EVIDENCE
    → CHECK (same success criteria + same evidence)
    → GATE
    → REFLECT
    → ACCEPT | RETRY | ESCALATE | HALT
    → SEALED TRAIL
```

A successful MCP invocation is not automatically proof that the requested outcome is satisfied. Maker execution MUST produce structured evidence containing the selected action/tool, inputs or safe redaction, execution status, result/evidence reference, errors, and trail/cycle correlation. Checker coverage MUST cover the actual success criteria. A missing CheckItem is a coverage-generation failure, not proof that an artifact is missing.

TYPE 1 world-changing operations require HIL approval. Trails are immutable after disposition is sealed. Skills are materialized from the marketplace and local catalogs.

## Quick start

```bash
dotnet run --project src/ProjectName.AppHost

cd src/frontend
npm install
npm run dev
```

## Frontend surfaces

- **ConsoleView** — primary governed UI with real snapshots only
- **TrailView / Trails** — replay sealed cycles
- **AgentDirectory** — registered agents
- **A2UIRenderer** — declarative UI surface
- **HarnessView** — readonly MAF harness visibility
- **RoundTable** — multi-agent visibility

CopilotKit/AG-UI is a UI/transport boundary. It must not become a second agent runtime or independently route PMCR-O phases.

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
