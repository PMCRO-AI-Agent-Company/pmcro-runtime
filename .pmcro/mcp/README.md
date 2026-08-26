# PMCR-O MCP Boundary

`.pmcro/mcp/` is a **capability registry**, not a collection of provider reimplementations.

## Decision

Use authoritative MCP providers when they already exist. PMCR-O should describe the capability it needs and resolve that capability to a provider.

Examples:

- `github` -> official GitHub MCP provider
- `browser` -> Microsoft Playwright MCP
- `mcp-gateway` -> Docker MCP Toolkit/Gateway
- `host-command-execution` -> terminal/native runtime tool, only when explicitly required

Do **not** create a `pmcro-github` or `pmcro-playwright` server merely to proxy an existing provider.

## Docker MCP Toolkit

Docker MCP Toolkit already provides a catalog, profiles, containerized MCP server lifecycle, and a gateway for connecting clients. PMCR-O should consume that capability rather than reproduce Docker's catalog/profile implementation.

A Docker profile can therefore contain the provider servers required for a PMCR-O workload. The PMCR-O registry records the capability-to-provider mapping and governance rules.

## Terminal boundary

A terminal capability is different from Docker MCP management.

Use terminal only when the runtime needs arbitrary host command execution that cannot or should not be expressed through a dedicated capability. It is high risk and should be explicitly permissioned, scoped, audited, and evidence-producing.

Do not grant terminal access simply because Docker is present.

## Custom PMCRO adapters

A custom adapter is justified only when it adds PMCRO-owned semantics such as:

- policy enforcement
- approval gates
- evidence collection
- lifecycle management
- audit/tracing
- domain-specific capability composition

The adapter should remain thin and delegate actual provider functionality to the authoritative MCP/tool surface.

## Secrets

Provider credentials are never stored in this directory. The registry contains references and policy only. Runtime configuration should inject credentials through the application configuration boundary (for example Aspire parameters), backed by the appropriate local/CI/production secret store.
