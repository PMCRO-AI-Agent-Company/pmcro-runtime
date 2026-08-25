# MCP Server Boundaries

## Decision

Do not create MCP servers simply to increase the server count. Create a server when a capability has a clear external-system boundary, independent lifecycle, security boundary, or reusable tool/resource surface.

Microsoft Agent Framework supports local and hosted MCP tools, while Agent Skills are intended for portable instructions, resources, and procedures. Workflows should own deterministic multi-step execution and checkpoint/resume behavior. Therefore PMCRO keeps these concerns separate.

## Current servers

- **Playwright MCP** — browser/computer interaction boundary.
- **FileSystem MCP** — governed project/file operations.
- **Terminal MCP** — governed process/shell execution.

These are sufficient as the initial actuator/tool plane.

## Add an MCP server when

1. The capability is a reusable tool/resource service rather than merely instructions.
2. It has a meaningful security or authorization boundary.
3. It needs an independent lifecycle or deployment surface.
4. Multiple agents/workflows need the same capability.
5. The tool surface benefits from MCP discovery and standardized invocation.

## Prefer a Skill when

The value is primarily domain knowledge, procedures, templates, references, or repeatable guidance. Skills should use the Agent Skills progressive-disclosure model (`SKILL.md`, references, assets, scripts) rather than becoming MCP servers without a tool boundary.

## Prefer a Workflow when

The requirement is an explicit, deterministic multi-step process, especially where side effects, human approval, checkpointing, retries, or resume semantics matter.

## Potential future MCP boundaries

These are candidates, not automatic additions:

- **GitHub MCP** — repository/issues/PR operations when the runtime needs an execution-facing GitHub tool boundary. Read-only access can remain a skill/integration if no independent actuator boundary is needed.
- **Web/Search MCP** — only if the runtime needs a governed provider-neutral search/fetch actuator. Do not create it merely because agents need research instructions.
- **Secrets/Identity broker** — only when credentials must be mediated through a dedicated security boundary; never expose raw secrets through a generic tool server.
- **Observability/Telemetry MCP** — only if external agents need a standardized telemetry query/control surface; internal runtime telemetry should remain native runtime infrastructure.

## Governance rule

Every MCP server must have an explicit tool inventory, authorization policy, audit events, failure semantics, and mapping to the PMCRO capability that is allowed to invoke it. MCP availability never grants an agent authority by itself.

## Sources

- Microsoft Agent Framework: MCP tools and tool integration.
- Microsoft Agent Framework: Agent Skills and the skills-vs-workflows boundary.
