// src/Mcps/ProjectName.Mcp.Terminal/Resources/TerminalResources.cs
// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.TERMINAL
// File       : Resources/TerminalResources.cs
// Identity   : Terminal State Provider (Pillar Two)
// Law Anchor : EC-002, MAAI-001, SAFETY-003
// ───────────────────────────────────────────────────────────────────────────────
// ADDED 2026-07-12: Terminal MCP previously implemented Tools only (Pillar One),
// unlike Mcp.Filesystem and Mcp.Playwright which both implement all three MCP
// pillars (see their Program.cs "pillars" diagnostic block). This mirrors the
// exact FilesystemResources pattern — expose read-only state as resources so an
// agent can "Observe" (slot occupancy, TYPE1/TYPE2 boundary, working root) without
// spending a tool call on GetTerminalStatus for information that doesn't change
// mid-cycle.
// ═══════════════════════════════════════════════════════════════════════════════

using ModelContextProtocol.Server;
using ProjectName.Mcp.Terminal.Configuration;
using System.ComponentModel;
using System.Text.Json;

namespace ProjectName.Mcp.Terminal.Resources;

/// <summary>
/// Pillar Two — Exposes the terminal environment (working root, limits, slot
/// layout, TYPE1/TYPE2 boundary) as MCP resources.
/// </summary>
[McpServerResourceType]
public sealed class TerminalResources(TerminalConfig config)
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    private static readonly string[] Slots = ["terminal-1", "terminal-2", "terminal-3", "terminal-4"];
    private static readonly string[] Type1Tools = ["RunCommand", "RunScript", "KillProcess"];
    private static readonly string[] Type2Tools = ["GetTerminalStatus", "GetEnvironment", "Which"];

    /// <summary>
    /// Terminal server status and constraints — same data GetTerminalStatus
    /// returns, exposed as a resource so it can be read without a tool call.
    /// </summary>
    [McpServerResource(
        UriTemplate = "terminal://status/workspace",
        Name = "TerminalWorkspaceStatus",
        Title = "Terminal Sandbox Status",
        MimeType = "application/json")]
    [Description("Provides the working root, command timeout, and output size limits terminal tools operate under.")]
    public string GetWorkspaceStatus()
    {
        try
        {
            var root = config.ResolveAndValidatePath(null);
            return JsonSerializer.Serialize(new
            {
                workingRoot = root,
                commandTimeoutSeconds = config.CommandTimeoutSeconds,
                maxOutputBytes = config.MaxOutputBytes,
                security = new
                {
                    enforcement = "Strict (SAFETY-003)",
                    pathTraversal = "Blocked",
                    executionModel = "TYPE1 tools return TYPE1_PENDING; Orchestrator dispatches after HIL approval (MAAI-001)"
                }
            }, _json);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = "Could not resolve working root", message = ex.Message }, _json);
        }
    }

    /// <summary>
    /// The four terminal slots and the TYPE1/TYPE2 tool boundary, so an agent can
    /// plan which slot to name and which tools will require HIL approval before
    /// it ever calls one.
    /// </summary>
    [McpServerResource(
        UriTemplate = "terminal://status/slots",
        Name = "TerminalSlotLayout",
        Title = "Terminal Slot Layout & Tool Boundary",
        MimeType = "application/json")]
    [Description("Returns the four terminal slot names, their intended purpose, and the TYPE1/TYPE2 tool classification (which tools require HIL approval).")]
    public string GetSlotLayout() =>
        JsonSerializer.Serialize(new
        {
            slots = new[]
            {
                new { name = "terminal-1", purpose = "General (build, test, dotnet)" },
                new { name = "terminal-2", purpose = "Git operations" },
                new { name = "terminal-3", purpose = "Package managers (npm, pip, dotnet add)" },
                new { name = "terminal-4", purpose = "Scraper / Playwright / long-running" }
            },
            type1Tools = Type1Tools,
            type2Tools = Type2Tools,
            note = "slot is informational only — it does not isolate execution, it's a label for log/audit readability. TYPE1 tools always require HIL approval regardless of slot."
        }, _json);
}
