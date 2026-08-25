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

The runtime does not own a second canonical `skills/` tree.

Canonical PMCRO skills are supplied by the sibling repository:

```text
../pmcro-skills/.agents/skills
```

Additional repositories can be supplied through `Orchestrator:SkillPaths`.

The runtime's `.pmcro/skills-staging` directory is generated/cache state only. It must never be treated as the source of truth.

## 3. MAF skill boundary

MAF owns Agent Skills discovery, progressive disclosure, resource loading, and skill tools through `AgentSkillsProvider`.

PMCRO owns:

- skill-source configuration;
- governance and authorization policy;
- Trail/evidence recording;
- marketplace/catalog metadata;
- runtime lifecycle.

The runtime must not maintain a second implementation of `load_skill`, `read_skill_resource`, or `run_skill_script`.

## 4. Materialization

`MarketplaceSkillsMaterializer` is a compatibility adapter between configured external skill repositories and the generated MAF staging root. It does not define skill semantics.

`MarketplaceSkillsWatcherService` watches configured skill repositories and refreshes staging after source changes.

## 5. Aspire service boundary

The AppHost currently runs the PMCRO phase agents in-process inside `OrchestratorService` through MAF WorkflowBuilder. Planner, Maker, Checker, and Reflector are not separate Aspire services.

MCP remains a separate capability boundary:

- `mcp-filesystem`
- `mcp-playwright`
- `mcp-terminal`

Service-to-service URLs are resolved through Aspire service discovery, not fixed ports.

## 6. Agent boundary

```text
Orchestrator = high-level goal + strategy + cycle control
Planner      = minimum validated plan
Maker        = executes the selected atomic action
Checker      = validates evidence/result
Reflector    = disposition + learning + next-cycle decision
```

The Orchestrator does not replace the MAF workflow engine and does not directly perform Maker work.

## 7. Approval boundary

`/pmcro-orchestrator:approve` is a PMCRO control command. Approval must authorize a pending MAF transition/request; it must not create an independent boolean approval engine.

```text
PMCRO approve
  -> validate law/constraint/acceptability
  -> answer pending MAF request
  -> workflow resumes
  -> Trail records the authorization
```

Approval is not evidence of success. Checker and Reflector remain authoritative for result validation and disposition.

## 8. UI boundary

```text
CopilotKit
  -> AG-UI
  -> MAF-hosted agent/workflow surface
  -> PMCRO state/trails
```

CopilotKit is presentation/application UX. It does not own PMCRO governance or workflow state.

## 9. Forbidden duplication

Do not reintroduce:

- `pmcro-runtime/skills/` as a second canonical skill source;
- a custom skill lifecycle that duplicates MAF Agent Skills;
- a second HIL/approval engine;
- phase-specific Aspire services merely to represent Planner/Maker/Checker/Reflector;
- fixed MCP ports when Aspire service discovery can provide the endpoint;
- marketplace metadata as a substitute for the canonical Agent Skills source.

## 10. Verification gate

Before considering the runtime implementation complete:

1. `Orchestrator:SkillPaths` resolves `../pmcro-skills/.agents/skills` from the runtime root.
2. MAF discovers the staged skills and exposes the native skill tools.
3. A source `SKILL.md` change refreshes staging without restarting the runtime.
4. Aspire AppHost starts the orchestrator and MCP resources with service discovery.
5. Planner -> Maker -> Checker -> Reflector executes through the MAF workflow.
6. `/pmcro-orchestrator:approve` resumes the authoritative pending request rather than bypassing MAF state.
7. AG-UI/CopilotKit receives the runtime's agent events/state without becoming a second orchestrator.
8. Trails contain the evidence needed to replay and evaluate the cycle.
