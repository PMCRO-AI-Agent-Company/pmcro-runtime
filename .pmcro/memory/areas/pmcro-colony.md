# PMCRO Colony — Memory Sync

Synced from Claude chat memory (/areas/pmcro-colony.md) via Desktop Commander, 2026-08-22.
Source of truth for architecture/governance/migration history across sessions.

## Overview
- PMCRO Colony is a production-grade, self-referential multi-agent orchestration system implementing the PMCR-O (Plan/Make/Check/Reflect/Orchestrate) framework
- Implemented in .NET with Microsoft Agent Framework (MAF), .NET Aspire, OllamaSharp (local Ollama inference), and MCP (Model Context Protocol) servers
- Follows strict governance conventions: typed trail frames, earned constraints, and HIL (Human-in-the-Loop) gating
- Core architecture built around MAF `WorkflowBuilder`, Aspire, and gRPC

## Migration status
- Migrating the codebase from `B:\pmcro-cline` to a Dev Drive at `W:\ProjectName`
- `W:` is still in early scaffold; `B:` remains the mature active instance
- Portability Law bans literal drive-letter paths (W-PORTABILITY-001, at Standard lifecycle stage per PAT-002)

## Governance conventions (active `.clinerules`)
- No shell echo for writes — use native MCP write tools
- TYPE1 mutations require explicit approval before execution
- All trail sealing must happen in the same session as the edit
- Portability Law banning literal drive-letter paths (W-PORTABILITY-001)

## Repo relationship (current understanding, most recent first)
- `Z:\pmcro-runtime` — current live, actively-worked project (2026-08-21 onward). Aspire AppHost, src\ProjectName.AppHost, src\frontend, .pmcro\trails, .pmcro\skills-staging, .pmcro\agent-memory
- `Z:\PMCRO-AI-Agent-Company` — prior Colony memory scaffold location (2026-08-08): `.pmcro/memory/` (profile.md + areas/topics/people), `.pmcro/agent-memory/`, `.agents/{rules,commands,output-styles,workflows,roster}/`; root file is `AGENTS.md` not `CLAUDE.md`
- `W:\PMCR_O\PMCR-O-Marketplace` — earlier "PMCRO Marketplace" repo (2026-08-06): .pmcro\, src\frontend, plugins\pmcro-csuite/pmcro-engine/pmcro-specialty, mcp\, .claude-plugin\marketplace.json
- `W:\PMCR-O` — earlier "the project" (2026-08-05): .NET/MAF runtime, src\, mcp\, docs\, AI-Knowledge-Corpus\ (previously called `W:\ProjectName`)
- `W:\PLUGINS` — earlier canonical reference convention (2026-08-05): Claude Code plugin marketplace layout (.claude-plugin\marketplace.json, catalog\Tools\AI-Company\skills\<domain>\skills\domain-scope\SKILL.md, clean .pmcro\trails\) (previously called `W:\pmcro-ai-company`)
- Long-term origin: `S:\project1` and `A:\` → `B:\pmcro-cline` → `W:\ProjectName` / `W:\PMCR-O` → current `Z:\pmcro-runtime`

## Aspire AppHost boot bugs resolved (2026-08-21, Z:\pmcro-runtime)
- Full stack (ollama-server, model-orchestrator, mcp-filesystem/playwright/terminal, orchestratorservice, orchestratorapi, Next.js frontend) confirmed booting cleanly end-to-end from a plain terminal `dotnet run` in `Z:\pmcro-runtime\src\ProjectName.AppHost`
- Bug 1 (DCP orchestration binary not found): global.json pins .NET SDK to `11.0.100-preview.6.26359.118`; runtime `DcpDependencyCheck.GetDcpInfoAsync` looks for the DCP binary without an extension, but the NuGet package only ships `dcp.exe`. Fix: copy `tools\dcp.exe` to `tools\dcp` in the NuGet cache (`C:\Users\org.tooensure\.nuget\packages\aspire.hosting.orchestration.win-x64\13.5.1\tools\`)
- Bug 2 (IDE-execution-handoff 500): launching AppHost from an IDE debug-attach session hands process-spawning to the IDE's debug adapter; when unresponsive, DCP falls back to a broken bare `dotnet.exe` call. Fix: always launch via plain `dotnet run` from an ordinary terminal, no active IDE debug session
- Also hit routine MSB3027/file-lock errors from stale child processes (orchestratorservice, mcp-*, ProjectName.AppHost, dcp, node) holding DLL locks — resolved by killing all before rebuilding

## Live orchestrator-cycle verification via browser (2026-08-22, Playwright MCP)
- Frontend confirmed rendering correctly in a real browser: "PMCR-O AI Agent Company" title, HTTP 200, single Governed/Read-Only O-Mode toggle, docked chat rail — matches the intended ARCH-OMODE-MERGE-001/ARCH-CANVAS-001 redesign (previously only compile-checked via `tsc --noEmit`, not visually confirmed)
- Clicked "Run with Orchestrator" end-to-end in the live UI: initially looked stuck on "Running…" but this was real Ollama/qwen3:8b inference time, not a bug — a real trail sealed on disk: `1156649a-2ac5-451a-95fc-cd34dc8c2f56`, Disposition "Accept", CycleNumber 1, cycle_summary factually correct, and the frontend's Trail Player picked it up live on a fresh page load
- No bug found in the "Run with Orchestrator" flow — frontend, CopilotKit runtime, AG-UI wiring, `AGUI_SERVER_URL` injection (AppHost.cs), Ollama, and the MCP filesystem actuator all worked correctly end-to-end
- Confirmed `mcp-playwright` and `mcp-terminal` — previously noted as failing to start — are now both Running

## Frontend bug fixed (2026-08-22): duplicate placeholder text
- Found via live browser test: the Console hero task-submission input (`id=colony-prompt`, ConsoleView.tsx line 542) and the docked chat rail input (`ORCHESTRATOR_LABELS.chatInputPlaceholder`, line 289) shared the identical placeholder string, causing automation/screen-reader ambiguity
- Fixed: hero input placeholder changed to "Describe the outcome you want the Orchestrator to run…"; chat rail placeholder left unchanged
- Verified via source diff, clean `tsc --noEmit`, and live browser DOM check
- Trail sealed: `.pmcro\trails\frontend-agent\f219f056-9692-4556-876a-ad3ce7c9e892\`, Disposition ACCEPT, Cycle 01

## Trail-writing schema correction (2026-08-22, self-caught)
- Hand-authored PMCR-O trail files (not going through the real .NET OrchestratorService) must match the real `FileTrailWriter` schema documented at the top of `src/frontend/app/lib/trails.ts` — PascalCase C#-record shapes:
  - `01-plan.jsonl` = `{Steps:[{Index,Action,SubjectAgent,ActionType}],SuccessCriteria}`
  - `01-make.jsonl` = `{StepResults:[{StepIndex,Action,Output,Ok}]}`
  - `01-check.jsonl` = `{CheckItems:[{StepIndex,Passed,Criterion,FailureEvidence}]}`
  - `01-reflect.jsonl` = `{Disposition,FinalOutput,RawReflection,HaltReason,RetryContext}`
  - `disposition.json` = `{Disposition,FinalOutput,RetryContext,HaltReason,CycleNumber,NextSeedIntent}`
- NOT the invented snake_case `{seq,content}` shape the old (already-replaced) reader expected
- A trail written in the wrong shape renders "No disposition"/"No plan entries" in the Trail Player even when the trail is real and ACCEPT-sealed on disk
