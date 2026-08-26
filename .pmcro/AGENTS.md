# PMCR-O Runtime Contract

`.pmcro/` is the Colony control plane: governance, intent state, execution references, memory, evidence, and provider policy. It is not a replacement for Microsoft Agent Framework, MCP, Docker MCP Toolkit, Aspire, or provider-specific infrastructure.

## Separation of concerns

- **Laws** govern what may never be violated.
- **Agents** define roles; skills define reusable capabilities.
- **Workflows** define deterministic execution graphs.
- **State** records execution truth and checkpoints.
- **Memory** stores durable shared knowledge.
- **Agent memory** stores role-scoped retained context.
- **Evidence** proves completion and claims.
- **Artifacts** store produced outputs.
- **Evaluation** measures behavior and correctness.
- **Capabilities** describe what the Colony may request.
- **Providers** identify the authoritative implementation of a capability.
- **Policies** constrain permissions, network, approvals, and risk.
- **Configuration** declares external parameters and environment mappings without storing secret values.

## Runtime invariants

1. Every autonomous cycle has a Frame and Trail.
2. Every completion claim requires Evidence.
3. Checker gates completion; Reflector records improvement opportunities.
4. Execution state and durable memory remain distinct.
5. Shared memory and agent memory remain distinct.
6. Declarative MAF workflows are preferred when execution order must be explicit.
7. Programmatic MAF workflows are reserved for custom execution logic.
8. Version-sensitive implementation decisions require current authoritative-source validation.
9. Prefer authoritative provider MCP servers over PMCRO reimplementations.
10. Docker MCP Toolkit may provide catalogs, profiles, server lifecycle, gateway routing, and credential handling without a PMCRO Docker wrapper.
11. Terminal/host command execution is a distinct high-risk capability and must be explicitly permissioned.
12. A PMCRO adapter is justified only when it adds PMCRO-owned policy, evidence, lifecycle, audit, or domain semantics.
13. Provider credentials are injected through the runtime configuration boundary and never committed to `.pmcro/`.
14. `.pmcro/` contains references, policies, and contracts—not live secrets, tokens, passwords, or private keys.
15. The Orchestrator routes capabilities; it does not duplicate provider implementations.
16. Docker MCP profiles/catalogs are infrastructure configuration and may be referenced from `.pmcro`, but Docker remains the provider of MCP lifecycle management.
