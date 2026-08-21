// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.FILESYSTEM
// File       : Program.cs
// Identity   : Filesystem Actuator Boot Sequence
// Law Anchor : ARCH-012, ARCH-013, FS-LAW-001
// ───────────────────────────────────────────────────────────────────────────────

using ProjectName.Mcp.Filesystem.Configuration;
using ProjectName.Mcp.Filesystem.Prompts;
using ProjectName.Mcp.Filesystem.Resources;
using ProjectName.Mcp.Filesystem.Tools;
using ProjectName.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Add standard OpenTelemetry, health checks, and service discovery
builder.AddServiceDefaults();

// ── 1. INFRASTRUCTURE SINGLETONS ─────────────────────────────────────────────
// The Config singleton enforces the Sandbox boundary for all other pillars.
builder.Services.AddSingleton<FilesystemConfig>();

// ── 2. MCP PILLAR SINGLETONS ──────────────────────────────────────────────────
builder.Services.AddSingleton<FilesystemTools>();
builder.Services.AddSingleton<FilesystemResources>();
builder.Services.AddSingleton<FilesystemPrompts>();

// ── 3. MCP SERVER CONFIGURATION ──────────────────────────────────────────────
// Configures the server to use Stateless HTTP Transport. 
// This ensures compatibility with Anthropic's cloud-based Agent Skill pattern.
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithTools<FilesystemTools>()
    .WithResources<FilesystemResources>()
    .WithPrompts<FilesystemPrompts>();

var app = builder.Build();

app.MapDefaultEndpoints();

// ── 4. MCP ENDPOINT MAPPING ──────────────────────────────────────────────────
app.MapMcp("/mcp");

// ── 5. DIAGNOSTIC ROOT ────────────────────────────────────────────────────────
app.MapGet("/", (FilesystemConfig config) => new
{
    service = "ProjectName.Mcp.Filesystem",
    identity = "Anthropic-Aligned Filesystem Actuator",
    status = "Online",
    mcp_endpoint = "/mcp",
    configuration = new
    {
        transport = "Stateless HTTP",
        sandboxRoot = config.SandboxRoot,
        maxFileSize = $"{config.MaxFileSizeBytes / 1024 / 1024}MB"
    },
    pillars = new
    {
        pillar1_tools = "Atomic File I/O (JSON-First)",
        pillar2_resources = "Inventory & Status",
        pillar3_prompts = "Mission-Driven (FilesystemMissionBrief)"
    },
    compliance = new[]
    {
        "FS-LAW-001: Sandbox Escape Prevention Active",
        "RELATIVE-PATHING: Enforced",
        "ATOMIC-OPERATIONS: Enforced"
    }
});

app.Run();