# MAF–PMCRO Runtime Boundary

Status: Approved architecture baseline

## Responsibility model

| Layer | Authority |
|---|---|
| PMCRO Orchestrator | High-level Seed Intent, Laws, Constraints, Acceptability, Strategy selection, cycle governance, disposition routing |
| MAF Workflow | Executable workflow topology, executor routing, state, events, checkpoints, resume |
| Agent Skills | Portable expertise, procedures, scripts, references, assets |
| MCP | External tool/resource/service boundary |
| .pmcro | Project/Colony runtime state and trail lineage |
| Trail | Immutable execution evidence and backward-flow learning artifact |

## Canonical cycle

`Seed Intent -> Orchestrator -> Strategy -> MAF Workflow -> Planner -> Maker -> Checker -> Reflector -> Orchestrator -> Next Cycle`

PMCRO governs the cycle; MAF executes the workflow. PMCRO must not create a competing workflow engine when MAF provides the required execution primitive.

## MCP boundary

The initial actuator plane is intentionally limited to:

- Playwright MCP: browser interaction.
- FileSystem MCP: governed workspace/file interaction.
- Terminal MCP: governed process execution.

Additional MCP servers are added only when a capability represents a real external tool/resource boundary. Domain expertise belongs in Agent Skills rather than an MCP server.

## Skills boundary

Skills are portable packages following the Agent Skills model: `SKILL.md`, optional `scripts/`, `references/`, and `assets/`. Skills are loaded progressively and only when relevant. A skill does not become an agent role merely because it contains procedures.

## Approval boundary

`/pmcro-orchestrator:approve` is a PMCRO control command. Its runtime implementation must resolve the pending MAF workflow request/checkpoint and resume that workflow. It must not create a second independent HIL engine.

Approval authorizes a transition; it does not establish task success. Maker execution, Checker verification, and Reflector disposition remain separate.

## Loop boundary

MAF provides workflow execution and bounded agent looping primitives. PMCRO owns the semantic cycle and backward-learning policy. Every autonomous loop remains bounded and evidence-backed.

## Replay boundary

Replay reads immutable historical trails, creates a fresh MAF workflow run/cycle, preserves source lineage, and independently verifies the new result. Historical verdicts are evidence, not proof of the new run.

## Versioning

The runtime should pin/validate compatibility against a versioned PMCRO skill/contract release. It must not blindly consume a mutable marketplace branch at runtime.
