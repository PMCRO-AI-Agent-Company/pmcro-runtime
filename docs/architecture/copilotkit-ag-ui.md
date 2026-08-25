# CopilotKit + AG-UI + PMCRO

## Boundary

CopilotKit is the application/UI layer. AG-UI is the protocol boundary between the UI/runtime and the Microsoft Agent Framework backend. PMCRO remains the governance layer and does not delegate Laws, Acceptability, trail sealing, or backward-flow semantics to the UI.

```text
CopilotKit UI
    |
    | AG-UI
    v
ASP.NET Core / AG-UI endpoint
    |
    v
MAF Agent / Workflow
    |
    +--> PMCRO Orchestrator governance
    +--> Planner -> Maker -> Checker -> Reflector
    +--> MCP tools
    |
    v
PMCRO Trail / checkpoint state
```

## Approval

`/pmcro-orchestrator:approve` is a PMCRO control-plane command. The preferred runtime implementation is to map it to the pending MAF workflow/HITL approval or checkpoint state and resume that workflow. Do not create a second independent approval state machine merely for the UI.

CopilotKit/AG-UI can surface approval requests, streaming state, tool calls, and resume interactions. The backend remains authoritative for authorization and workflow state.

## Shared state

Expose only governed runtime state to the UI. Trail evidence, Laws, Constraints, gate state, current role, cycle, and disposition may be projected as read-only or explicitly controlled state. Secrets, credentials, identity material, and unrestricted filesystem state must not be exposed through shared UI state.

## MCP Apps

MCP Apps are an optional UI integration. They do not change the PMCRO MCP boundary. MCP servers remain capability/tool providers; CopilotKit middleware may render compatible MCP Apps in the frontend.

## Source references

- Microsoft Agent Framework AG-UI integration: https://learn.microsoft.com/en-us/agent-framework/integrations/ag-ui/
- CopilotKit Microsoft Agent Framework integration: https://docs.copilotkit.ai/ms-agent-dotnet

Compatibility must be validated against the pinned package versions in this repository before upgrades.
