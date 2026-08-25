# PMCR-O Runtime Agent Instructions

## Mission

Maintain `pmcro-runtime` as the production execution host for the PMCR-O Colony. Use Microsoft Agent Framework (MAF) as the agent/workflow substrate and keep PMCRO governance as the surrounding control plane.

## Architecture laws

- `.pmcro/` is a runtime instance, not a marketplace source tree.
- `pmcro-skills` owns distributable laws, skills, role frames, schemas and marketplace metadata.
- MAF owns agent/workflow execution, Harness, tool invocation, approvals and provider integration.
- MCP servers are capability providers; do not duplicate their implementations in the Orchestrator.
- `pmcro-actuator` is the governance/evidence boundary for tool calls.
- Trails are append-only governed evidence; never manufacture synthetic trail events for UI convenience.
- TYPE 1/world-changing operations require the configured human approval path.

## MAF and MCP rules

- Prefer native `AIAgent`, workflow, Harness and MCP integrations from the pinned MAF version.
- Keep the MAF core/workflow/harness packages on one stable version.
- Keep preview hosting/DevUI packages on the matching preview train.
- Validate MCP protocol negotiation and capability advertisements before enabling newer MCP protocol features.
- Do not claim MCP 2026 Tasks Extension compatibility unless the actual connector/server negotiation has been tested.
- Preserve raw MCP request/response envelopes in governed evidence where the PMCRO contract requires them.

## CodeAct / shell / browser

Code execution, shell access and browser automation are capabilities, not autonomous authority. They must remain behind explicit policy and approval boundaries and emit evidence suitable for Checker/Reflector evaluation.

## Frontend

The frontend must render real runtime snapshots. Do not add fake cycle states, fake tool results, fake trails or hard-coded agent activity merely to make a smoke test look successful.

## Validation gate

Before merging runtime changes:

1. Restore/build the affected .NET solution.
2. Run affected tests.
3. Verify Aspire resource startup and health.
4. Exercise the MCP servers actually used by the changed path.
5. Exercise the CopilotKit/agent endpoint when frontend or agent streaming changes.
6. Confirm trail/evidence invariants.
7. Record failures honestly; do not convert warnings into success.

## Version discipline

`Directory.Packages.props` is the package-version source of truth. For version-sensitive upgrades, verify the published package version and the upstream MAF/MCP documentation before editing the pin. Never mix incompatible MAF release trains without evidence.

## Repository mutation

Changes that alter runtime behavior, dependencies, MCP contracts, governance, or production security are TYPE 1 changes. They require reviewable diffs, tests/evidence, and an explicit commit message describing the change.
