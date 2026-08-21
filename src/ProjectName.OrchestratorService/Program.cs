// src/ProjectName.OrchestratorService/Program.cs
// PMCR-O Runtime — SEQUENTIAL-001 loop via Ollama IChatClient.
// Governance loaded: identity.json → config.json → colony-laws.md → pmcro-framework/SKILL.md.

using ProjectName.ServiceDefaults;
using ProjectName.OrchestratorService;
using ProjectName.OrchestratorService.Configuration;
using ProjectName.OrchestratorService.Loop;
using ProjectName.OrchestratorService.Services;
using OllamaSharp;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// ── Configuration ──────────────────────────────────────────────────────
builder.Services.Configure<OrchestratorConfig>(
    builder.Configuration.GetSection(OrchestratorConfig.SectionName));

// ── Ollama IChatClient ─────────────────────────────────────────────────
static IChatClient BuildOllamaClient(IServiceProvider sp)
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var endpoint = cfg.GetConnectionString("ollama-server") ?? "http://localhost:11434";
    if (endpoint.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase))
        endpoint = endpoint["Endpoint=".Length..];
    if (!endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        endpoint = "http://" + endpoint;

    var model = cfg["Orchestrator:ModelId"] ?? "qwen3:8b";
    var client = new OllamaApiClient(new Uri(endpoint)) { SelectedModel = model };

    return ((IChatClient)client).AsBuilder()
        .UseFunctionInvocation(configure: c => c.MaximumIterationsPerRequest = 1)
        .Build();
}

builder.Services.AddSingleton<IChatClient>(BuildOllamaClient);
builder.Services.AddKeyedSingleton<IChatClient>("orchestrator", (sp, _) => sp.GetRequiredService<IChatClient>());

// ── PMCR-O services ────────────────────────────────────────────────────
builder.Services.AddSingleton<ITrailWriter, FileTrailWriter>();
builder.Services.AddSingleton<DevUiHilChannel>();
builder.Services.AddSingleton<IHilChannel>(sp => sp.GetRequiredService<DevUiHilChannel>());
builder.Services.AddSingleton<McpToolCache>();
builder.Services.AddSingleton<ProjectName.OrchestratorService.Skills.SkillManifestReader>();
builder.Services.AddSingleton<PmcroLoop>();

var app = builder.Build();

// ── Cycle endpoint ─────────────────────────────────────────────────────
app.MapPost("/api/cycle", async (
    PmcroLoop loop,
    IServiceProvider sp,
    CycleRequest req,
    CancellationToken ct) =>
{
    var trailId = string.IsNullOrWhiteSpace(req.TrailId)
        ? Guid.NewGuid().ToString()
        : req.TrailId;

    var subjectName = req.SubjectAgent ?? "filesystem-agent";
    var chatClient = sp.GetRequiredKeyedService<IChatClient>("orchestrator");
    // Subject agent is the Maker-side worker; until MCP agents are wired,
    // use a lightweight ChatClient agent so the loop can run end-to-end.
    var subjectAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
    {
        Name = subjectName,
        ChatOptions = new ChatOptions
        {
            Instructions = $"You are the {subjectName} subject agent for PMCR-O Maker turns."
        }
    });

    var result = await loop.RunAsync(
        req.SeedIntent,
        trailId,
        req.Project ?? "project1",
        subjectName,
        subjectAgent,
        ct);

    return Results.Ok(result);
});

// ── HIL endpoints ──────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapPost("/hil/approve", (string id, DevUiHilChannel hil) =>
    {
        hil.Resolve(id, true);
        return Results.Ok(new { approved = true });
    });

    app.MapPost("/hil/deny", (string id, DevUiHilChannel hil) =>
    {
        hil.Resolve(id, false);
        return Results.Ok(new { approved = false });
    });
}

// ── Health endpoint ────────────────────────────────────────────────────
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    governance = ".pmcro/ loaded",
    loop = "SEQUENTIAL-001",
    model = app.Configuration["Orchestrator:ModelId"] ?? "qwen3:8b"
}));

app.MapDefaultEndpoints();
app.Run();

// ── DTO ─────────────────────────────────────────────────────────────────
public sealed record CycleRequest(
    string SeedIntent,
    string? TrailId = null,
    string? Project = null,
    string? SubjectAgent = null);
