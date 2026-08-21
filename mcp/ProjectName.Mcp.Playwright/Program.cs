// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.PLAYWRIGHT
// File       : Program.cs
// Identity   : Browser Automation Actuator Boot Sequence
// Law Anchor : ARCH-012, ARCH-013, PW-LAW-005, FRAC-MCP-400-001
// ───────────────────────────────────────────────────────────────────────────────

using ProjectName.Mcp.Playwright.Configuration;
using ProjectName.Mcp.Playwright.Prompts;
using ProjectName.Mcp.Playwright.Resources;
using ProjectName.Mcp.Playwright.Tools;
using ProjectName.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Add standard OpenTelemetry, health checks, and service discovery
builder.AddServiceDefaults();

// ── 1. INFRASTRUCTURE SINGLETONS ─────────────────────────────────────────────
// These manage the physical state and settings of the browser.
builder.Services.AddSingleton<PlaywrightConfig>();
builder.Services.AddSingleton<PlaywrightSessionManager>();

// ── 2. MCP PILLAR SINGLETONS ──────────────────────────────────────────────────
// These represent the three facets of the Model Context Protocol.
builder.Services.AddSingleton<PlaywrightTools>();
builder.Services.AddSingleton<PlaywrightResources>();
builder.Services.AddSingleton<PlaywrightPrompts>();

// ── 3. MCP SERVER CONFIGURATION ──────────────────────────────────────────────
// Configures the server to use Stateless HTTP Transport, making it
// highly compatible with Anthropic's cloud-based agent loops.
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true; // Essential for Deterministic Workflow Agents
    })
    .WithTools<PlaywrightTools>()
    .WithResources<PlaywrightResources>()
    .WithPrompts<PlaywrightPrompts>();

var app = builder.Build();

// Standard MAF/Service endpoints
app.MapDefaultEndpoints();

// ── 4. MCP ENDPOINT MAPPING ──────────────────────────────────────────────────
// This is the primary mounting point for the MCP protocol.
app.MapMcp("/mcp");

// ── 5. DIAGNOSTIC ROOT ────────────────────────────────────────────────────────
// Provides a human-readable manifest of the server's alignment.
app.MapGet("/", () => new
{
    service = "ProjectName.Mcp.Playwright",
    identity = "Anthropic-Aligned Browser Actuator",
    status = "Online",
    mcp_endpoint = "/mcp",
    configuration = new
    {
        transport = "Stateless HTTP",
        browser = "Patchright-Chromium",
        stealth = "Active"
    },
    pillars = new
    {
        pillar1_tools = "Atomic (JSON-First)",
        pillar2_resources = "Stateful (playwright://session/status)",
        pillar3_prompts = "Mission-Driven (PlaywrightMissionBrief)"
    },
    compliance = new[]
    {
        "PW-LAW-001: URL Safety Enforced",
        "PW-LAW-003: Timeout Caps Enforced",
        "PW-LAW-005: Serial Page Execution"
    }
});

app.Run();