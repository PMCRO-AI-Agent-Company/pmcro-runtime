# I Am the PMCR-O Runtime Engine
## Z:\pmcro-runtime — .NET 11, Aspire 13.4+, Ollama GPU, MAF WorkflowBuilder

> **The One-Line Truth:**
> A seed intent enters the Orchestrator. The loop runs. The Trail is the product.

## Runtime Stack

```
pmcro-runtime/
├── src/
│   ├── ProjectName.AppHost/          ← Aspire orchestration — Ollama GPU + MCP servers
│   ├── ProjectName.ServiceDefaults/  ← Shared: health, resilience, OTEL, Ollama clients
│   ├── ProjectName.OrchestratorService/ ← MAF WorkflowBuilder — P→M→C→R in-process
│   └── ProjectName.OrchestratorApi/  ← HTTP facade (Scalar + CopilotKit AG-UI)
├── mcp/
│   ├── ProjectName.Mcp.Filesystem/   ← MCP read/list files
│   ├── ProjectName.Mcp.Terminal/     ← MCP execute shell
│   └── ProjectName.Mcp.Playwright/   ← MCP browser automation (lazy)
└── .pmcro/                           ← Governance layer
    ├── PMCRO.md                       ← this file
    ├── trails/orchestrator/           ← sealed trail frames
    ├── laws/                          ← colony-laws.md + type2-allowlist
    └── config.json                    ← runtime config
```

## How to Run

```shell
dotnet run --project src/ProjectName.AppHost
```

Ollama auto-starts as GPU Docker container, pulls qwen3:8b on first run.
Aspire dashboard at https://localhost:15291

## Governance

This runtime loads the PMCR-O governance layer from `.pmcro/`:
- `.pmcro/laws/colony-laws.md` — EC-001 through TOOL-001
- `.pmcro/config.json` — MaxLoops=5, checker thresholds
- MAF WorkflowBuilder enforces SEQUENTIAL-001: phases run in order

## The Loop Gets Smarter

Every cycle writes trail frames. The Ollama model learns from earned constraints.
More cycles → more constraints → smarter runtime.