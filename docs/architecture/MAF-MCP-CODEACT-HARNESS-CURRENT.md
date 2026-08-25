# PMCRO Runtime — Current MAF Architecture

**Validation date:** 2026-08-25

This document is the current-state architecture contract for the runtime. Microsoft Agent Framework is the execution substrate; PMCRO is the governance layer.

## 1. Microsoft Agent Framework

The runtime uses MAF agents, `WorkflowBuilder`, workflow executors, native Agent Skills, AG-UI/hosting, Harness, and Hyperlight CodeAct. The .NET MAF workflow model is graph-based: executors are connected by typed edges and conditions, and workflow execution emits lifecycle events and supports checkpointing. New macro-cycle behavior should therefore be represented in the MAF graph instead of adding another bespoke workflow engine.

## 2. MCP boundary

Playwright, FileSystem, and Terminal are capability-provider services. Aspire owns their process/service discovery lifecycle. They remain separate from the Orchestrator.

The consumption rule is:

```text
MCP server
  -> official ModelContextProtocol C# SDK
  -> McpClient / HttpClientTransport
  -> McpClientTool (AIFunction/AITool)
  -> MAF agent / workflow
```

Do not introduce new code that manually constructs MCP JSON-RPC `initialize`, `tools/list`, or `tools/call` requests. Existing legacy adapters must be migrated behind the native client boundary incrementally. The current `McpToolCache` contains legacy direct JSON-RPC paths and is therefore a migration target, not the desired long-term architecture.

## 3. MCP transport

The HTTP MCP services should expose the Streamable HTTP transport at `/mcp`. The official SDK's `HttpClientTransport` should use `HttpTransportMode.StreamableHttp` or `AutoDetect` as appropriate. Stateless MCP servers are preferred when no server-side session state is required; stateful sessions should be used only where subscriptions, unsolicited messages, or per-client isolation require them.

## 4. CodeAct

Hyperlight is the CodeAct sandbox. CodeAct is an execution capability, not the PMCRO orchestration engine. Host tools exposed through CodeAct must remain narrow and policy-controlled. Mutating operations must not be reachable through a broad host-tool surface when a single approval could authorize an unbounded sequence of mutations.

## 5. Harness

Harness remains the batteries-included agent execution substrate. PMCRO must not recreate Harness persistence, looping, skills, approvals, or context-management primitives. PMCRO governs participation, acceptability, evidence capture, and disposition.

## 6. PMCRO macro-cycle

The target topology is:

```text
PMCRO laws / constraints / acceptability
                |
                v
        MAF WorkflowBuilder
                |
        Planner -> Maker -> Checker -> Reflector
                |                 |
              MCP/Tools       evidence
                |                 |
                +---- checkpoint-+
                         |
                   HITL request/response
                         |
              ACCEPT / RETRY / ESCALATE / HALT
```

`PmcroLoop` is currently a compatibility/orchestration shell around MAF sub-workflows. It must not grow into a second workflow runtime. Macro-cycle topology, bounded retry, checkpoint/resume, and explicit HITL request/response should migrate into MAF workflow semantics while retaining PMCRO laws and disposition logic.

## 7. Checkpoint and HITL contract

A checkpoint must preserve enough state to resume the governed cycle and must include the workflow's pending requests. A restored workflow may re-emit pending request events; the PMCRO UI should surface those as governed HITL interactions rather than treating a disconnected browser session as a failed run.

The trail records both the PMCRO disposition and the MAF workflow/checkpoint identifiers so the two layers remain auditable without conflating their authorities.

## 8. Frontend contract

The frontend is an AG-UI consumer. It should render workflow lifecycle events, tool activity, checkpoint/resume state, HITL requests, and final PMCRO disposition as distinct concepts. It must not infer `ACCEPT` merely because a model response arrived or an HTTP request returned `200`.

## 9. Required migration sequence

1. Keep the currently proven Aspire MCP services running independently.
2. Replace Orchestrator-side hand-written MCP JSON-RPC tool discovery/calls with the official C# MCP client and `McpClientTool` instances.
3. Preserve PMCRO tool catalog/acceptability metadata as a governance layer around discovered tools.
4. Move macro-cycle retry/checkpoint/HITL topology from the compatibility loop into `WorkflowBuilder`.
5. Make trail records reference MAF workflow events/checkpoints rather than duplicating workflow state.
6. Run the UI smoke sequence again and verify that normal completion, tool calls, retry, HITL, checkpoint resume, and HALT are all observable end-to-end.

## Authoritative references

- Microsoft Agent Framework workflows and graph model
- Microsoft Agent Framework workflow checkpoints and resuming
- Microsoft Agent Framework workflow human-in-the-loop request/response
- Microsoft Agent Framework MCP tools with the official MCP C# SDK
- Microsoft Agent Framework Hyperlight CodeAct
- Official Model Context Protocol C# SDK
