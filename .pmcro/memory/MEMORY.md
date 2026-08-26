# PMCR-O Memory Boundary

Memory is durable knowledge, not live workflow state.

## Shared Colony memory

`memory/shared/` contains validated knowledge reusable across roles and cycles.

Examples:
- architectural decisions
- validated conventions
- durable lessons
- stable domain facts

## Agent memory

`agent-memory/<role>/` contains role-scoped retained context.

Agent memory must:
- use stable logical role identifiers;
- avoid secrets and sensitive credentials;
- never masquerade as shared Colony truth;
- be promoted to shared memory only after validation and explicit governance.

## Episodic history

Cycle records and evidence remain historical artifacts. They are not automatically promoted into durable memory.

## Promotion gate

`cycle -> evidence -> validation -> memory candidate -> approval -> memory`

The Orchestrator may request promotion, but a failed or unverified cycle must not become authoritative memory.
