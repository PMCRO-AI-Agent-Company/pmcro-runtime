// src/ProjectName.AppHost/AppHost.cs
// Anthropic Pattern: Workflow Orchestrator (MAF-native, single-service).
//
// ARCH-COPILOTKIT-001 (2026-07-11): AddNextJsApp is [Experimental] in
// Aspire.Hosting.JavaScript as of Aspire 13 — suppressing ASPIREJAVASCRIPT001
// per Microsoft's own docs is the documented way to use it, not a workaround
// for a real problem.
#pragma warning disable ASPIREJAVASCRIPT001
//
// Architecture change (2026-06-27):
//   BEFORE: Five Aspire projects — OrchestratorService + four phase services
//           (PlannerService, MakerService, CheckerService, ReflectorService).
//           OrchestratorService drove a gRPC fan-out to each phase service.
//   AFTER:  One Aspire project — OrchestratorService only.
//           All four phases run in-process via MAF WorkflowBuilder sequential graph.
//           No phase service projects. No gRPC phase clients. No inter-service WaitFor.

var builder = DistributedApplication.CreateBuilder(args);

var repoRoot = builder.AddParameter("repoRoot");

// ── Ollama persistent GPU container ───────────────────────────────────────────
var ollama = builder
    .AddOllama("ollama-server")
    .WithGPUSupport(OllamaGpuVendor.Nvidia)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("ollama-data")
    // Context length: 16384 tokens needed for qwen3:8b with full skill manifests
    // (bumped from default 4096 on 2026-06-20, see trail 9c361c27 analysis).
    .WithEnvironment("OLLAMA_CONTEXT_LENGTH", "16384")
    // BUG-OLLAMA-SIGSEGV-001 (2026-07-11): repeated SIGSEGV in
    // ggml_backend_sched_reserve during model load (runner.go reserveWorstCaseGraph)
    // on RTX 4070 Laptop GPU w/ 16384 ctx + flash attention enabled. Confirmed via
    // docker logs ollama-server-12b3b73e crash dump, 2026-07-11 ~17:14:58Z. Disabling
    // flash attention as first mitigation attempt — NOT yet verified fixed, no sealed
    // trail exists for this fix. If crashes persist, next lever is reducing
    // OLLAMA_CONTEXT_LENGTH below 16384.
    .WithEnvironment("OLLAMA_FLASH_ATTENTION", "0");

var modelOrchestrator = ollama.AddModel("model-orchestrator", "qwen3:8b");

// ── MCP actuator servers ───────────────────────────────────────────────────────
var mcpFilesystem = builder
    .AddProject<Projects.ProjectName_Mcp_Filesystem>("mcp-filesystem")
    .WithEnvironment("Filesystem__SandboxRoot", repoRoot);

var mcpPlaywright = builder
    .AddProject<Projects.ProjectName_Mcp_Playwright>("mcp-playwright")
    // God Mode: run the browser headed so the operator can watch it drive.
    .WithEnvironment("Playwright__Headless", "false");

var mcpTerminal = builder
    .AddProject<Projects.ProjectName_Mcp_Terminal>("mcp-terminal")
    .WithEnvironment("Parameters__working-root", repoRoot);

// ── OrchestratorService — owns the full PMCRO cycle in-process ────────────────
// All four phases (Planner, Maker, Checker, Reflector) run inside this one service
// as MAF AIAgents wired into a WorkflowBuilder sequential graph via PmcroLoop.
// No phase service references. No gRPC client WaitFors.
var orchestratorService = builder
    .AddProject<Projects.ProjectName_OrchestratorService>("orchestratorservice")
    .WithReference(ollama)
    .WithReference(modelOrchestrator)
    .WithReference(mcpFilesystem)    // McpToolCache — filesystem MCP wired and proven
    .WithReference(mcpPlaywright)    // McpToolCache — forward-compat, not yet active
    .WithReference(mcpTerminal)      // McpToolCache — forward-compat, not yet active
    .WithEnvironment("Orchestrator__FileSystemRoot", repoRoot)
    // MaxLoops override removed 2026-07-13: this hardcoded "3" was silently
    // shadowing appsettings.json's "MaxLoops": 5 (env vars beat appsettings.json
    // in .NET config precedence), contradicting GTDDD-MANDATE ("every value here
    // is sourced from appsettings.json / environment -- no hardcoded limits in
    // code"). appsettings.json is now the single source of truth for MaxLoops.
    .WaitFor(modelOrchestrator)
    .WaitFor(mcpFilesystem)
    .WaitFor(mcpTerminal);
// EC-ASPIRE-001: mcpPlaywright intentionally NOT in WaitFor.
// Playwright is a lazy actuator — its browser never launches until ExecuteNavigateTo fires.
// A WaitFor here cascades mcp-playwright crashes into a full colony stall.
// OrchestratorService tolerates a dead playwright MCP: CallMcp returns an error string,
// Reflector writes Disposition:Retry. Browser install can happen while the rest runs.

// ── OrchestratorApi — HTTP facade over OrchestratorService ─────────────────────
// Thin REST/chat surface (Scalar, CopilotKit-compatible /copilot/chat, trail replay).
// Wired to the same Ollama model + MCP servers so it can replay sealed trails.
var orchestratorApi = builder
    .AddProject<Projects.ProjectName_OrchestratorApi>("orchestratorapi")
    .WithReference(ollama)
    .WithReference(modelOrchestrator)
    .WithReference(mcpFilesystem)
    .WithReference(mcpPlaywright)
    .WithReference(mcpTerminal)
    .WithReference(orchestratorService)
    // ARCH-TRAILS-001 (2026-07-11): Trails__Root override removed — it was hardcoded
    // to a stale S:\.pmcro\trails from before the sandbox root moved to B:\pmcro-cline.
    // TrailReader.TrailsRoot falls back to env.ContentRootPath + ".pmcro\trails" when
    // no config override is set, which correctly resolves to the real on-disk trails
    // under B:\pmcro-cline\.pmcro\trails. See Services/TrailReader.cs.
    .WaitFor(modelOrchestrator)
    .WaitFor(orchestratorService);


// ── DevUI Dashboard ────────────────────────────────────────────────────────────
var devUI = builder.AddDevUI("pmcro-devui");
devUI.WithAgentService(orchestratorService);

// ── CopilotKit frontend (Next.js) ─────────────────────────────────────────────
// ARCH-COPILOTKIT-001 (2026-07-11): Next.js App Router app under src/frontend.
// The CopilotKit runtime (app/api/copilotkit/route.ts) runs server-side inside
// this Next.js process and bridges to OrchestratorService's real AG-UI endpoint
// (see ARCH-AGUI-001 in OrchestratorService/Program.cs) via an HttpAgent — the
// browser never talks to OrchestratorService directly, only to this Next.js app.
// AGUI_SERVER_URL is read server-side via process.env in route.ts (safe: never
// bundled into client JS, unlike NEXT_PUBLIC_* vars).
var frontend = builder.AddNextJsApp("frontend", "../frontend")
    .WithReference(orchestratorService)
    .WithEnvironment("AGUI_SERVER_URL", ReferenceExpression.Create($"{orchestratorService.GetEndpoint("http")}/agui"))
    // ARCH-HARNESS-003 (2026-07-22): second AG-UI endpoint for the parallel
    // HarnessAgent surface (see ARCH-HARNESS-001/002 in
    // OrchestratorService/Program.cs). Read-only, HIL-gated separately from
    // PmcroLoop -- selectable in the frontend via useAgent({ agentId: "Harness" })
    // per CopilotKit's documented multi-agent pattern, not auto-used by the
    // prebuilt chat components (only the single "Orchestrator" entry is).
    .WithEnvironment("AGUI_HARNESS_SERVER_URL", ReferenceExpression.Create($"{orchestratorService.GetEndpoint("http")}/agui/harness"))
    .WithExternalHttpEndpoints()
    .WaitFor(orchestratorService);
// ARCH-A2UI-001 (2026-07-15) CORRECTION: an earlier pass briefly added
// .WithReference(ollama) + A2UI_OLLAMA_BASE_URL here, on the assumption that
// A2UI's Dynamic Schema mode needs a second, independently-configured LLM.
// That assumption came from public CopilotKit docs for a newer/different SDK
// generation than what's actually installed here (checked directly against
// node_modules/@ag-ui/a2ui-middleware/dist/index.d.ts and
// node_modules/@copilotkit/runtime/dist/v2/runtime/core/runtime.d.mts). In
// THIS installed version, runtime.a2ui is just A2UIMiddlewareConfig --
// injectA2UITool injects a structured render_a2ui tool into the EXISTING
// agent's own tool list; the Orchestrator (already Ollama/qwen3:8b via
// MAF/.NET, already tool-calling) calls it directly. No second model, no
// extra Aspire wiring needed. Reverted -- see docs/adr/0002 for the
// corrected finding.

builder.Build().Run();