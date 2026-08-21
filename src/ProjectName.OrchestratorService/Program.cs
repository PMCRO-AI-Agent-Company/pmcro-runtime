// src/ProjectName.OrchestratorService/Program.cs
using ProjectName.ServiceDefaults;
using ProjectName.OrchestratorService;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddOllamaClients();

builder.Services.AddSingleton<PmcroLoop>();
builder.Services.AddSingleton<McpToolCache>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();