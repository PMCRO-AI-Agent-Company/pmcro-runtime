// src/ProjectName.OrchestratorApi/Program.cs
// HTTP facade — Scalar API explorer + CopilotKit AG-UI endpoint

using ProjectName.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOllamaClients();

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

// ── Scalar API explorer ────────────────────────────────────────────────────
app.MapOpenApi();
app.MapScalarApiReference();

// ── AG-UI endpoint — CopilotKit-compatible ─────────────────────────────────
app.MapPost("/agui", async (HttpContext context) =>
{
    // Forward to OrchestratorService — trail replay, chat, cycle dispatch
    await Task.CompletedTask;
});

app.Run();