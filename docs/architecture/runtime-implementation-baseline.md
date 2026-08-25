# PMCRO Runtime Implementation Baseline

Status: active implementation contract  
Date: 2026-08-25

## 1. Runtime identity

`pmcro-runtime` is a .NET Aspire distributed application. Aspire AppHost defines the distributed application topology and local development lifecycle; it is not the PMCRO reasoning engine.

```text
Aspire AppHost
  -> OrchestratorService
  -> MCP actuator services
  -> OrchestratorApi
  -> CopilotKit/Next.js frontend

OrchestratorService
  -> Microsoft Agent Framework (MAF)
  -> PMCRO Workflow / cycle governance
  -> Trails
```

## 2. Skill ownership

The runtime does not own a second canonical `skills/` tree. Canonical skills live in sibling domain repositories and are materialized into `.pmcro/skills-staging` only as generated runtime state.

Current curated roots are:

```text
../pmcro-skills/plugins/pmcro-maf/skills
../pmcro-skills/plugins/pmcro-orchestrator/skills
../pmcro-skills/plugins/pmcro-strategy/skills
../dotnet-skills/plugins/dotnet-maf/skills
../github-skills/plugins/github/skills
../figma-skills/plugins/figma/skills
```

The catalog in `.agents/plugins/marketplace.json` is for discovery and cross-repository inventory. `stage=true` documents intended runtime eligibility; `Orchestrator:SkillPaths` remains the explicit execution allowlist.

## 3. MAF skill boundary

MAF owns Agent Skills discovery, progressive disclosure, resource loading, caching/deduplication, and skill tools through `AgentSkillsProvider`. MAF supports file, inline/class, and MCP-based skill sources.

PMCRO owns:

- skill-source allowlisting;
- governance and authorization policy;
- Trail/evidence recording;
- marketplace/catalog metadata;
- runtime lifecycle.

The runtime must not maintain a second implementation of `load_skill`, `read_skill_resource`, or `run_skill_script`.

## 4. Materialization

`MarketplaceSkillsMaterializer` is a compatibility adapter between configured external skill repositories and the generated MAF staging root. It does not define skill semantics.

`MarketplaceSkillsWatcherService` watches configured skill repositories and refreshes staging after source changes.

MAF deduplicates duplicate skill names with first-source precedence. Therefore the runtime allowlist is intentionally curated; adding every plugin indiscriminately can shadow equally named skills.

## 5. Current package baseline

The August 2026 production baseline is:

- `Microsoft.Agents.AI`: 1.19.0
- `Microsoft.Agents.AI.Harness`: 1.19.0
- `ModelContextProtocol`: 2.2.0
- `Aspire.Hosting`: 13.5.1

Version-sensitive changes must be revalidated against current Microsoft documentation and NuGet before implementation.

## 6. Aspire service boundary

The AppHost currently runs the PMCRO phase agents in-process inside `OrchestratorService` through MAF WorkflowBuilder. Planner, Maker, Checker, and Reflector are not separate Aspire services.

MCP remains a separate capability boundary:

- `mcp-filesystem`
- `mcp-playwright`
- `mcp-terminal`

Service-to-service URLs are resolved through Aspire service discovery, not fixed ports.

## 7. Agent boundary

```text
Orchestrator = high-level goal + strategy + cycle control
Planner      = minimum validated plan
Maker        = executes the selected atomic action
Checker      = validates evidence/result
Reflector    = disposition + learning + next-cycle decision
```

The Orchestrator does not replace the MAF workflow engine and does not directly perform Maker work.

## 8. Execution capabilities

Use the native MAF abstraction that matches the requirement:

- Harness for long-running interactive agent sessions.
- Agent Skills for focused reusable capability/knowledge.
- WorkflowBuilder for guaranteed execution order and checkpoints.
- MCP for external tools/capabilities.
- CodeAct for controlled tool-heavy sandbox execution when appropriate.
- Tool approval middleware for authorization.
- OpenTelemetry/Aspire for operational telemetry.

PMCRO governs how these participate in a cycle; it does not reimplement them.

## 9. Approval boundary

`/pmcro-orchestrator:approve` is a PMCRO control command. Approval must authorize a pending MAF transition/request; it must not create an independent boolean approval engine.

```text
PMCRO approve
  -> validate law/constraint/acceptability
  -> answer pending MAF request
  -> workflow resumes
  -> Trail records the authorization
```

Approval is not evidence of success. Checker and Reflector remain authoritative for result validation and disposition.

## 10. UI boundary

```text
CopilotKit
  -> AG-UI
  -> MAF-hosted agent/workflow surface
  -> PMCRO state/trails
```

CopilotKit is presentation/application UX. It does not own PMCRO governance or workflow state.

## 11. Forbidden duplication

Do not reintroduce:

- `pmcro-runtime/skills/` as a second canonical skill source;
- a custom skill lifecycle that duplicates MAF Agent Skills;
- a second HIL/approval engine;
- phase-specific Aspire services merely to represent Planner/Maker/Checker/Reflector;
- fixed MCP ports when Aspire service discovery can provide the endpoint;
- marketplace metadata as a substitute for the canonical Agent Skills source;
- broad skill auto-approval for untrusted sources;
- CodeAct as a replacement for individually approved sensitive operations.

## 12. Verification gate

Before considering the runtime implementation complete:

1. Every configured `SkillPath` exists and contains valid `SKILL.md` files.
2. MAF discovers the staged skills and exposes native skill tools.
3. Duplicate skill names are understood and intentionally ordered.
4. A source `SKILL.md` change refreshes staging without restarting the runtime.
5. Aspire AppHost starts the orchestrator and MCP resources with service discovery.
6. Planner -> Maker -> Checker -> Reflector executes through the MAF workflow.
7. `/pmcro-orchestrator:approve` resumes the authoritative pending request rather than bypassing MAF state.
8. AG-UI/CopilotKit receives runtime agent events/state without becoming a second orchestrator.
9. Trails contain the evidence needed to replay and evaluate the cycle.
10. Build, targeted tests, Actions, and runtime smoke tests all pass before release.
