// src/Mcps/ProjectName.Mcp.Playwright/Resources/PlaywrightResources.cs
// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.PLAYWRIGHT
// File       : Resources/PlaywrightResources.cs
// Identity   : Stateful Browser Context — MCP Resource Provider
// Law Anchor : PW-LAW-005, ARCH-013
// ───────────────────────────────────────────────────────────────────────────────
// URI: playwright://session/status
// Read-only pull target — no browser mutation.
// ═══════════════════════════════════════════════════════════════════════════════

using ModelContextProtocol.Server;
using ProjectName.Mcp.Playwright.Configuration;
using System.Text.Json;

namespace ProjectName.Mcp.Playwright.Resources;

[McpServerResourceType]
public sealed class PlaywrightResources(PlaywrightSessionManager session)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };

    [McpServerResource(
        UriTemplate = "playwright://session/status",
        Name        = "PlaywrightSessionStatus",
        MimeType    = "application/json")]
    public string GetSessionStatusResource()
    {
        var status = session.GetStatus();
        return JsonSerializer.Serialize(new
        {
            resource        = "playwright://session/status",
            is_launched     = status.IsLaunched,
            has_active_page = status.HasActivePage,
            current_url     = status.CurrentUrl,
            configuration   = new
            {
                navigation_timeout_ms = status.NavigationTimeoutMs,
                action_timeout_ms     = status.ActionTimeoutMs,
                headless              = status.Headless
            },
            type1_tools = new[] { "NavigateTo", "ClickElement", "FillInput", "SubmitForm", "TakeScreenshot" },
            type2_tools = new[] { "GetSessionStatus", "GetPageContent", "GetPageSnapshot", "GetPageTitle", "GetLastDownload" },
            law_anchors = new[] { "PW-LAW-001", "PW-LAW-003", "PW-LAW-005", "MAAI-001", "EC-002" }
        }, JsonOptions);
    }
}
