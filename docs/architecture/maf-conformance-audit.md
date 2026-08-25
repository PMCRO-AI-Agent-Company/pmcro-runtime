# MAF / MCP / CodeAct / Harness Conformance Audit

Date: 2026-08-25
Status: verified against the current runtime repository and Microsoft Agent Framework documentation

## Executive finding

PMCRO runtime is **substantially MAF-native**, but it is not yet fully native at every boundary.

The current implementation has three distinct states:

- **MAF:** YES — real MAF agents, WorkflowBuilder workflows, hosting, AG-UI, skills, Hyperlight/CodeAct packages, and Harness integration are present.
- **MCP servers:** YES — Playwright, FileSystem, and Terminal are correctly separated as external actuator services and are reachable through Aspire service discovery. However, the Orchestrator currently wraps their HTTP JSON-RPC endpoints in `McpToolCache`/`AIFunctionFactory` instead of using MAF's documented MCP client/tool adapter path.
- **CodeAct:** YES — Hyperlight CodeAct is wired as a governed execution capability and is intentionally restricted to read-only host tools. This is the correct safety direction.
- **Harness:** YES — the runtime has the MAF Harness package and a dedicated `ollama-harness` client with a larger function-iteration budget. The Harness must remain an execution substrate, not become a second PMCRO orchestrator.

## What is already correct

### 1. MAF owns the agent substrate

`ProjectName.OrchestratorService.csproj` references the MAF agent, workflow, hosting, AG-UI, Hyperlight, DevUI, OpenAI, and Harness packages. The runtime creates real `AIAgent` instances and builds MAF `WorkflowBuilder` graphs.

Planner, Maker, Checker, and Reflector are therefore not merely prompt labels; they are MAF agent/workflow participants.

### 2. MCP is an actuator boundary

The three current servers are appropriate:

- `mcp-filesystem`
- `mcp-terminal`
- `mcp-playwright`

Aspire service discovery is used instead of fixed runtime ports. The MCP services own the external capability boundary; PMCRO owns authorization, evidence, and policy.

### 3. CodeAct is isolated

The CodeAct agent exposes one model-facing `execute_code` capability backed by Hyperlight. Only read-oriented filesystem tools are reachable from inside the sandbox. Mutating actions remain outside the sandbox and require the governed PMCRO/HIL path.

This matches the current MAF CodeAct guidance: CodeAct is an execution mechanism, not a replacement orchestration framework.

### 4. Harness is a substrate

The separate `ollama-harness` client intentionally has a larger function-invocation budget than the split-turn PMCRO client. This prevents the PMCRO phase agents from entering uncontrolled tool-call loops while preserving the autonomous multi-step behavior that makes Harness useful.

The Harness should be used for autonomous agent execution, skills, memory, planning/todos, approvals, and bounded looping where appropriate. PMCRO remains the governance layer.

## Gaps to close

### Gap A — MCP integration is not fully MAF-native

`McpToolCache` currently constructs HTTP clients and sends MCP JSON-RPC requests directly, then wraps individual methods with `AIFunctionFactory.Create`.

Microsoft's current Agent Framework guidance supports connecting through the official MCP C# SDK and converting discovered MCP tools to the agent tool surface. The next implementation should therefore introduce a native MCP client adapter per server and eliminate duplicate hand-written MCP invocation where practical.

Target:

```text
MCP Server
   -> official MCP C# client/session
   -> discovered MCP tools
   -> MAF AITool surface
   -> AIAgent / Workflow
```

The governance layer should still wrap or intercept authorization and evidence; native MCP discovery must not bypass PMCRO Acceptability.

### Gap B — the macro PMCR-O loop still contains hand-written orchestration

`PmcroCycleWorkflow` uses MAF `WorkflowBuilder` for the Planner→Maker and Checker→Reflector turns, but the outer cycle/retry loop is still an ordinary C# `for` loop.

The target architecture should move the **macro cycle topology** into MAF WorkflowBuilder/declarative workflow semantics, including bounded retry, branching on gate disposition, checkpointing, and resume.

PMCRO should supply the governed state and gate decisions; MAF should execute and persist the workflow topology.

### Gap C — approval should become a first-class MAF request/resume boundary

The current `IHilChannel`/DevUI channel is a valid presentation and policy boundary, but the production implementation should map governed approvals to MAF `RequestInfo`/request-response events and workflow checkpoints. Do not create a second independent approval state machine.

## Required architecture

```text
                     CopilotKit / AG-UI
                              |
                              v
                       MAF hosted surface
                              |
                       PMCRO Orchestrator
                              |
                 +------------+-------------+
                 |                          |
          MAF Workflow                 PMCRO Law /
       execution topology             Acceptability
                 |                          |
        +--------+--------+                 |
        |        |        |                 |
     Planner   Maker   Checker <------------+
        |        |        |
        +--------+--------+
                 |
             Reflector
                 |
          Retry / Accept / Halt
                 |
          MAF checkpoint/resume
                 |
        +--------+---------+----------------+
        |                  |                |
   MCP FileSystem     MCP Terminal     MCP Playwright
        |
   official MCP client/tool adapter
```

## Model policy

The current local baseline remains `qwen3:8b` because it fits the developer GPU and is a useful fast regression baseline. The model must be configuration-driven so a stronger local/cloud model can be benchmarked without changing the MAF/PMCRO architecture.

Model quality is evaluated by PMCR-O task success, tool correctness, criterion/check coverage, evidence quality, retry quality, and latency — not parameter count alone.

## Conformance gate

A future runtime change is MAF-native only when:

1. MAF owns agent execution and workflow topology.
2. MCP tools are discovered through the official MCP integration rather than duplicated JSON-RPC wrappers where feasible.
3. CodeAct remains a bounded execution provider with explicit sandbox and tool permissions.
4. Harness is used as an agent runtime substrate, not as a competing PMCRO state machine.
5. Human approval pauses/resumes MAF workflow state.
6. Checkpoints capture enough state to resume a long-running PMCR-O cycle.
7. AG-UI/CopilotKit observes and controls the runtime without owning orchestration semantics.
8. PMCRO Laws, Constraints, Acceptability, Trails, and P/M/C/R governance remain above the execution substrate.
