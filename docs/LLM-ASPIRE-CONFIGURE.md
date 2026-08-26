# LLM: configure pmcro-runtime with Aspire

**Repo:** https://github.com/PMCRO-AI-Agent-Company/pmcro-runtime  
**Related skills/data:** https://github.com/PMCRO-AI-Agent-Company/pmcro-skills (branch `pmcro/github-plugin-align`)

## What this repo already is

| Piece | Path |
| --- | --- |
| Aspire AppHost | `src/ProjectName.AppHost/AppHost.cs` |
| Orchestrator (P→M→C→R in-process) | `src/services/ProjectName.OrchestratorService` |
| API / trail replay | `src/ProjectName.OrchestratorApi` |
| Ollama + GPU | AppHost `AddOllama` + `qwen3:8b` |
| MCP | `mcp/` Filesystem, Playwright, Terminal |
| Frontend | `src/frontend` |
| Colony state | `.pmcro/` |

Do **not** invent a second agent runtime. Extend AppHost + queue + trails.

## Goals for LLM sessions

1. Claim work from `.pmcro/queue/pending` (create if missing).
2. Wire **Python fine-tune worker** for trail → SFT JSONL → optional train.
3. Keep Ollama as default inference; later swap/add fine-tuned model endpoint.
4. Emit `### ORCHESTRATOR` / `PLANNER` / `MAKER` / `CHECKER` / `REFLECTOR` every cycle.
5. Seal trails under `.pmcro/trails/`; no secrets in git.

## AppHost extension (target)

In `AppHost.cs`, after existing projects, add something equivalent to:

```csharp
// Python fine-tune worker (export trails → JSONL; dry-run train)
var finetune = builder.AddPythonApp("pmcro-finetune", "../../python/pmcro_finetune")
    .WithArgs("-m", "pmcro_finetune", "export",
        "--trails", ".pmcro/trails",
        "--out", "data/pmcro-sft.jsonl",
        "--pass-only")
    .WithEnvironment("PMCRO_ROOT", repoRoot);
```

Queue commands:

| `command.name` | Action |
| --- | --- |
| `finetune.export` | `python -m pmcro_finetune export ...` |
| `finetune.train` | `python -m pmcro_finetune train --dry-run ...` |

Training-data format: sibling repo `pmcro-skills` → `docs/colony/training/`.

## Activate prompt (paste into any LLM)

```text
/pmcr-o:activate

You are configuring https://github.com/PMCRO-AI-Agent-Company/pmcro-runtime
(Aspire AppHost + OrchestratorService + .pmcro).

Emit ### ORCHESTRATOR first, then PLANNER, MAKER, CHECKER, REFLECTOR each cycle.
Orchestrator = high-level goals + MEMORY. Planner = bare minimum on one validated resource.
Do not ask the human to continue; stop only for secrets or host cutoff.

High-level goals:
1. Extend AppHost to host python/pmcro_finetune (export/train dry-run).
2. Ensure .pmcro/queue/pending drives finetune.export then finetune.train seeds.
3. Trails stay under .pmcro/trails; export to data/pmcro-sft.jsonl (SFT messages format).
4. Do not replace MAF/PmcroLoop; only add worker + queue routing.
5. Reference pmcro-skills docs/colony/training for JSONL schema.

Start: read src/ProjectName.AppHost/AppHost.cs and .pmcro/state; claim or create queue seeds.
```

## Related PR (skills)

https://github.com/PMCRO-AI-Agent-Company/pmcro-skills/pull/8 — training JSONL samples, queue message shapes, Cloudflare activate.
