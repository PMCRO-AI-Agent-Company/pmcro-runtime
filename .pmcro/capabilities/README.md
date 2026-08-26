# Capabilities

Capabilities are PMCRO contracts describing what an agent/workflow may request. They are intentionally provider-neutral.

Provider selection is resolved through `../providers/registry.yaml` and `../mcp/registry.yaml`.

## Core capability contracts

- `github`: repository, issue, PR, commit and code operations through the authoritative GitHub MCP provider.
- `browser`: browser automation through Microsoft Playwright MCP.
- `containers`: container/MCP lifecycle through Docker MCP Toolkit.
- `mcp-gateway`: catalog/profile/gateway operations through Docker MCP Toolkit.
- `host-command-execution`: explicit terminal execution; critical risk.
- `filesystem`: workspace file operations using native runtime/MAF tools.
- `memory`: PMCRO durable memory operations.
- `evidence`: completion evidence collection.
- `lifecycle`: seal/retry/escalate/halt operations owned by the PMCRO runtime.

Do not create a capability merely because a provider exposes a tool. Create it when the capability is meaningful to PMCRO routing and governance.
