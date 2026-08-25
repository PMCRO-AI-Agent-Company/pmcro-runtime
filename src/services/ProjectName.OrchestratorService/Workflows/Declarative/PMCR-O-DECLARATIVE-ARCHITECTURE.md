# PMCR-O on Microsoft Agent Framework Declarative Workflows

## Strategic architecture

MAF is the workflow runtime. PMCR-O is the governance model executed by that runtime.

- **Planner**: MAF agent executor producing one atomic, checkable action.
- **Maker/Subject**: MAF agent executor using the verified MCP tool surface.
- **Checker**: MAF agent executor evaluating explicit criteria against normalized evidence.
- **Gate**: deterministic workflow policy; a criterion without evidence coverage cannot pass.
- **Reflector**: MAF agent executor producing Accept/Retry/Halt and the next bounded intent.
- **Orchestrator**: MAF workflow execution engine, not a second custom loop.

## Layer ownership

| Layer | Responsibility |
|---|---|
| CopilotKit/Next.js | UI and transport |
| Orchestrator API | application boundary/session ingress |
| MAF Declarative Workflow | execution graph, sequencing, branching, checkpointable run |
| PMCR-O services | governance policy, trail/evidence persistence, HIL, skill/runtime integration |
| MCP | external capabilities |
| Ollama/qwen3:8b | local model provider |

## Migration constraint

Do not delete the legacy `PmcroLoop` or hand-built workflow until declarative parity is proven by automated runtime tests. The declarative path must first eliminate the Gate 3 evidence-coverage defect observed in the running Colony.

## Critical evidence invariant

`MCP success -> normalized execution artifact -> Checker input -> CheckItem coverage -> Gate decision` must be an explicit, testable chain. Raw agent text is not considered durable evidence by itself.
