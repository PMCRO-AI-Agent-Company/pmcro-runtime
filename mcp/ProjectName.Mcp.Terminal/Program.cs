// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.TERMINAL
// File       : Program.cs
// Identity   : Terminal MCP Server Entry Point
// Law Anchor : ARCH-004, FRAC-MCP-400-001, FRAC-MCP-406-001, EC-002, SAFETY-003
//
// TRANSPORT NOTES — same as Filesystem MCP, do not change without reading:
//
//  FRAC-MCP-400-001 (Stateless=true required):
//    Stateful SSE transport requires an MCP initialize handshake.
//    Phase services calling tool endpoints via PostAsJsonAsync never perform
//    this handshake → HTTP 400 on every tool call.
//    Stateless=true removes the session requirement.
//
//  FRAC-MCP-406-001 (Accept header required on callers):
//    Callers must send: Accept: application/json, text/event-stream
//    Set DefaultRequestHeaders on every named HttpClient that calls this server.
//
// FOUR TERMINAL SLOTS:
//   terminal-1  General (build, test, dotnet)
//   terminal-2  Git operations
//   terminal-3  Package managers (npm, pip, dotnet add)
//   terminal-4  Scraper / Playwright / long-running
//
// TYPE 1 tools (RunCommand, RunScript, KillProcess) must only be dispatched
// by the Orchestrator after HIL approval (EC-002, MAAI-001).
// ═══════════════════════════════════════════════════════════════════════════════

using ModelContextProtocol.AspNetCore;
using ProjectName.Mcp.Terminal.Configuration;
using ProjectName.Mcp.Terminal.Prompts;
using ProjectName.Mcp.Terminal.Resources;
using ProjectName.Mcp.Terminal.Tools;
using ProjectName.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// ── Sandbox root ──────────────────────────────────────────────────────────────
// Aspire injects Parameters__working-root via .WithEnvironment() in AppHost.cs.
// Default falls back two levels above ContentRootPath (solution root in dev).
var workingRoot = builder.Configuration["Parameters:working-root"]
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", ".."));

builder.Services.AddSingleton(new TerminalConfig
{
    WorkingRoot           = workingRoot,
    CommandTimeoutSeconds = int.TryParse(builder.Configuration["Terminal:CommandTimeoutSeconds"], out var t) ? t : 30,
    MaxOutputBytes        = int.TryParse(builder.Configuration["Terminal:MaxOutputBytes"], out var m) ? m : 65536,
});

// ── MCP server ────────────────────────────────────────────────────────────────
// FRAC-MCP-400-001: Stateless=true — callers use PostAsJsonAsync with no
// MCP session handshake.
// UPGRADED 2026-07-12: now registers all three MCP pillars (Tools, Resources,
// Prompts), matching Mcp.Filesystem and Mcp.Playwright — previously Tools-only.
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<TerminalTools>()
    .WithResources<TerminalResources>()
    .WithPrompts<TerminalPrompts>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");

// Diagnostic root
app.MapGet("/", () => new
{
    service      = "ProjectName.Mcp.Terminal",
    status       = "running",
    workingRoot,
    transport    = "Streamable HTTP (Stateless=true)",
    mcpEndpoint  = "/mcp",
    slots        = new[] { "terminal-1", "terminal-2", "terminal-3", "terminal-4" },
    type1Tools   = new[] { "RunCommand", "RunScript", "KillProcess" },
    type2Tools   = new[] { "GetTerminalStatus", "GetEnvironment", "Which" },
    lawAnchors   = new[] { "EC-002", "MAAI-001", "SAFETY-003", "FRAC-MCP-400-001", "FRAC-MCP-406-001" },
    pillars      = new
    {
        pillar1_tools = "Command/Script Execution (TYPE1_PENDING + HIL dispatch)",
        pillar2_resources = "Workspace & Slot Status (terminal://status/*)",
        pillar3_prompts = "Mission-Driven (TerminalMissionBrief)"
    },
});

app.Run();
