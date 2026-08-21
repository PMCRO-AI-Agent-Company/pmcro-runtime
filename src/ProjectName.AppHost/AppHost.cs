// src/ProjectName.AppHost/AppHost.cs
// PMCR-O Runtime — .NET 11, Aspire 13.4+, Ollama GPU, MAF WorkflowBuilder
// Anthropic Pattern: Orchestrator-Workers with MCP actuation

var builder = DistributedApplication.CreateBuilder(args);

var repoRoot = builder.AddParameter("repoRoot");

// ── Ollama GPU container — persistent, local model ─────────────────────────
var ollama = builder
    .AddOllama("ollama-server")
    .WithGPUSupport(OllamaGpuVendor.Nvidia)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("ollama-data")
    .WithEnvironment("OLLAMA_CONTEXT_LENGTH", "16384")
    .WithEnvironment("OLLAMA_FLASH_ATTENTION", "0");

var model = ollama.AddModel("model-orchestrator", "qwen3:8b");

// ── MCP actuator servers ───────────────────────────────────────────────────
var mcpFilesystem = builder
    .AddProject<Projects.ProjectName_Mcp_Filesystem>("mcp-filesystem")
    .WithEnvironment("Filesystem__SandboxRoot", repoRoot);

var mcpTerminal = builder
    .AddProject<Projects.ProjectName_Mcp_Terminal>("mcp-terminal")
    .WithEnvironment("Parameters__working-root", repoRoot);

var mcpPlaywright = builder
    .AddProject<Projects.ProjectName_Mcp_Playwright>("mcp-playwright")
    .WithEnvironment("Playwright__Headless", "false");

// ── OrchestratorService — full PMCR-O cycle in-process ─────────────────────
var orchestrator = builder
    .AddProject<Projects.ProjectName_OrchestratorService>("orchestratorservice")
    .WithReference(ollama)
    .WithReference(model)
    .WithReference(mcpFilesystem)
    .WithReference(mcpTerminal)
    .WithReference(mcpPlaywright)
    .WithEnvironment("Orchestrator__FileSystemRoot", repoRoot)
    .WaitFor(model)
    .WaitFor(mcpFilesystem)
    .WaitFor(mcpTerminal);

// ── OrchestratorApi — HTTP facade (Scalar + AG-UI) ─────────────────────────
var api = builder
    .AddProject<Projects.ProjectName_OrchestratorApi>("orchestratorapi")
    .WithReference(ollama)
    .WithReference(model)
    .WithReference(mcpFilesystem)
    .WithReference(mcpTerminal)
    .WithReference(mcpPlaywright)
    .WithReference(orchestrator)
    .WaitFor(model)
    .WaitFor(orchestrator);

// ── DevUI Dashboard ────────────────────────────────────────────────────────
var devUI = builder.AddDevUI("pmcro-devui");
devUI.WithAgentService(orchestrator);

builder.Build().Run();