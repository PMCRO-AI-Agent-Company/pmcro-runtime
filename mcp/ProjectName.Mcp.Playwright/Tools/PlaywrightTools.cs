// src/Mcps/ProjectName.Mcp.Playwright/Tools/PlaywrightTools.cs
// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.PLAYWRIGHT
// File       : Tools/PlaywrightTools.cs
// Identity   : Browser Actuator — Atomic JSON-First Tool Implementations
// Law Anchor : PW-LAW-001, PW-LAW-003, PW-LAW-005, MAAI-001, EC-002
// ───────────────────────────────────────────────────────────────────────────────
// TYPE 2 (no HIL required — read-only):
//   GetSessionStatus, GetPageContent, GetPageSnapshot, GetPageTitle
//
// TYPE 1 (HIL approval required — return TYPE1_PENDING, MAAI-001):
//   NavigateTo, ClickElement, FillInput, SubmitForm, TakeScreenshot
// ═══════════════════════════════════════════════════════════════════════════════

using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ModelContextProtocol.Server;
using ProjectName.Mcp.Playwright.Configuration;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace ProjectName.Mcp.Playwright.Tools;

[McpServerToolType]
public sealed class PlaywrightTools(
    PlaywrightConfig config,
    PlaywrightSessionManager session,
    ILogger<PlaywrightTools> logger)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly string[] Type1Tools =
        ["NavigateTo", "ClickElement", "FillInput", "SubmitForm", "TakeScreenshot"];

    private static readonly string[] Type2Tools =
        ["GetSessionStatus", "GetPageContent", "GetPageSnapshot", "GetPageTitle", "GetLastDownload"];

    private static string Result(bool success, object? data = null, string? error = null) =>
        JsonSerializer.Serialize(new { success, data, error }, JsonOptions);

    private static string Pending(string tool, object requestedAction) =>
        JsonSerializer.Serialize(new
        {
            success = false,
            data    = (object?)null,
            error   = "TYPE1_PENDING",
            type1_pending = new
            {
                tool,
                requested_action = requestedAction,
                law_anchor       = "MAAI-001",
                note = "TYPE 1 tools require HIL approval and are dispatched only by the " +
                       "Orchestrator (EC-002, Single Dispatcher). This server does not " +
                       "execute browser mutations directly. The Orchestrator must surface " +
                       "this request for HIL approval before any execution path is invoked."
            }
        }, JsonOptions);

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 2 — GetSessionStatus
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "GetSessionStatus")]
    [Description("Returns the current browser session status: launched state, active page, current URL, configured timeouts, and TYPE1/TYPE2 tool boundary. Read-only — does not mutate browser state.")]
    public string GetSessionStatus()
    {
        try
        {
            var status = session.GetStatus();
            return Result(true, new
            {
                is_launched           = status.IsLaunched,
                has_active_page       = status.HasActivePage,
                current_url           = status.CurrentUrl,
                navigation_timeout_ms = status.NavigationTimeoutMs,
                action_timeout_ms     = status.ActionTimeoutMs,
                headless              = status.Headless,
                type1_tools           = Type1Tools,
                type2_tools           = Type2Tools,
                law_anchors           = new[] { "PW-LAW-001", "PW-LAW-003", "PW-LAW-005", "MAAI-001", "EC-002" }
            });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 2 — GetPageTitle
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "GetPageTitle")]
    [Description("Returns the title of the currently loaded page. Read-only. Returns success:false if no page is loaded or browser is not launched.")]
    public async Task<string> GetPageTitle()
    {
        try
        {
            if (!session.IsLaunched)
                return Result(false, error: "Browser not launched. No active page.");

            using var _ = await session.AcquireLockAsync();
            var page  = session.GetPage();
            var title = await page.TitleAsync();
            logger.LogInformation("[Playwright] GetPageTitle -> '{Title}'", title);
            return Result(true, new { title, url = page.Url });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 2 — GetPageContent
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "GetPageContent")]
    [Description("Returns the inner text content of the current page (not raw HTML). Content is truncated to MaxContentBytes if needed. Read-only — does not navigate or mutate browser state.")]
    public async Task<string> GetPageContent()
    {
        try
        {
            if (!session.IsLaunched)
                return Result(false, error: "Browser not launched. Navigate to a URL first (TYPE 1 — requires HIL).");

            using var _ = await session.AcquireLockAsync();
            var page  = session.GetPage();
            var text  = await page.InnerTextAsync("body");

            if (Encoding.UTF8.GetByteCount(text) > config.MaxContentBytes)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                text = Encoding.UTF8.GetString(bytes, 0, config.MaxContentBytes) + "\n[TRUNCATED]";
            }

            logger.LogInformation("[Playwright] GetPageContent: {Bytes} bytes from {Url}",
                Encoding.UTF8.GetByteCount(text), page.Url);

            return Result(true, new { url = page.Url, content = text });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 2 — GetPageSnapshot
    // YAML aria-snapshot (role/name/state, with [ref=eN] element references),
    // the same pattern Microsoft's official @playwright/mcp calls browser_snapshot.
    // Structured text the Planner/Maker can act against directly instead of raw
    // innerText or pixel coordinates from a screenshot.
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "GetPageSnapshot")]
    [Description("Returns a YAML aria-snapshot of the current page: each element's role, accessible name, and state in a structured hierarchy, with [ref=eN] identifiers for direct element reference. Read-only. Preferred over GetPageContent when the Maker needs to identify actionable elements (roles, refs) rather than plain text -- same shape as Microsoft's official browser_snapshot tool.")]
    public async Task<string> GetPageSnapshot()
    {
        try
        {
            if (!session.IsLaunched)
                return Result(false, error: "Browser not launched. Navigate to a URL first (TYPE 1 — requires HIL).");

            using var _ = await session.AcquireLockAsync();
            var page = session.GetPage();

            // Page.AriaSnapshotAsync is the current API (Page.Accessibility was
            // removed from Playwright entirely after being deprecated for 3 years).
            // AriaSnapshotMode.Ai adds [ref=eN] element references, matching the
            // output shape Microsoft's official @playwright/mcp browser_snapshot uses.
            var yaml = await page.AriaSnapshotAsync(new PageAriaSnapshotOptions
            {
                Mode = AriaSnapshotMode.Ai
            });

            if (Encoding.UTF8.GetByteCount(yaml) > config.MaxContentBytes)
            {
                var bytes = Encoding.UTF8.GetBytes(yaml);
                yaml = Encoding.UTF8.GetString(bytes, 0, config.MaxContentBytes) + "\n[TRUNCATED]";
                logger.LogWarning("[Playwright] GetPageSnapshot: snapshot exceeded MaxContentBytes, truncated");
            }

            logger.LogInformation("[Playwright] GetPageSnapshot: {Bytes} bytes from {Url}",
                Encoding.UTF8.GetByteCount(yaml), page.Url);

            return Result(true, new { url = page.Url, snapshot = yaml });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 2 — GetLastDownload (EC-PW-002 fix)
    // Read-only observer over PlaywrightSessionManager's Download-event capture.
    // No HIL needed: this reports on a download the browser already triggered
    // (as a side effect of a prior TYPE1 click/etc), it doesn't cause anything.
    // success:true with data:null means no download has occurred this session —
    // that is a legitimate, distinct outcome from a capture failure (data.error
    // set) or a real captured file (data.saved_path set). Callers must check
    // which case they got rather than assume any non-error result means a file
    // exists on disk.
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "GetLastDownload")]
    [Description("Returns the most recent browser-triggered download captured this session (filename, saved path, byte size, source URL), or null if none has occurred. Read-only — does not mutate browser state. Use this to verify whether a click that might trigger a download (e.g. a 'Generate PDF' button) actually produced a file, since such clicks return success from ExecuteClickElement regardless of whether a download followed.")]
    public string GetLastDownload()
    {
        try
        {
            var last = session.GetLastDownload();
            if (last is null)
                return Result(true, data: null);

            return Result(true, new
            {
                suggested_filename = last.SuggestedFilename,
                saved_path         = last.SavedPath,
                bytes              = last.Bytes,
                url                = last.Url,
                captured_at_utc    = last.CapturedAtUtc,
                error              = last.Error
            });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ORCHESTRATOR-ONLY — ExecuteNavigateTo (dispatched after HIL approval)
    // ARCH-NEW-001: This method is the ONLY path that actually moves the browser.
    // It is never in GetMakerTools — never reachable via LLM tool selection.
    // PmcroLoop.DispatchType1Async calls it after IHilChannel.RequestAsync returns true.
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "ExecuteNavigateTo")]
    [Description("ORCHESTRATOR-ONLY: Execute browser navigation after HIL approval (ARCH-NEW-001). Not for LLM tool selection.")]
    public async Task<string> ExecuteNavigateTo(string url)
    {
        try
        {
            var validated = config.ResolveAndValidateUrl(url);
            using var _ = await session.AcquireLockAsync();
            await session.EnsureLaunchedAsync();
            var page = session.GetPage();
            logger.LogInformation("[Playwright] ExecuteNavigateTo: {Url}", validated);
            var response = await page.GotoAsync(validated, new PageGotoOptions
            {
                Timeout = config.NavigationTimeoutMs
            });
            var status = response?.Status ?? 0;
            var title  = await page.TitleAsync();
            logger.LogInformation(
                "[Playwright] Navigation complete: url={Url} status={Status} title='{Title}'",
                page.Url, status, title);
            return Result(true, new { url = page.Url, title, http_status = status });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ORCHESTRATOR-ONLY — ExecuteTakeScreenshot (dispatched after HIL approval)
    // ARCH-NEW-001: mirrors ExecuteNavigateTo. This is the ONLY path that actually
    // captures a screenshot. It is never in GetMakerTools — never reachable via
    // LLM tool selection. PmcroLoop.DispatchType1Async calls it after
    // IHilChannel.RequestAsync returns true for tool == "TakeScreenshot".
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "ExecuteTakeScreenshot")]
    [Description("ORCHESTRATOR-ONLY: Execute screenshot capture after HIL approval (ARCH-NEW-001). Not for LLM tool selection.")]
    public async Task<string> ExecuteTakeScreenshot(bool fullPage = false, string? outputPath = null)
    {
        try
        {
            using var _ = await session.AcquireLockAsync();
            await session.EnsureLaunchedAsync();
            var page = session.GetPage();

            // PW-LAW-006: outputPath is client/LLM-supplied and MUST NOT be trusted as an
            // absolute filesystem path. ResolveScreenshotPath strips directory components and
            // confines the write to config.ScreenshotDir, rejecting traversal attempts.
            var path = config.ResolveScreenshotPath(outputPath);

            logger.LogInformation(
                "[Playwright] ExecuteTakeScreenshot: fullPage={FullPage} path={Path}", fullPage, path);

            var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = fullPage,
                Path     = path
            });

            logger.LogInformation(
                "[Playwright] Screenshot complete: path={Path} bytes={Bytes} url={Url}",
                path, bytes.Length, page.Url);

            return Result(true, new { path, bytes = bytes.Length, url = page.Url });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 1 — NavigateTo (TYPE1_PENDING)
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "NavigateTo")]
    [Description("TYPE 1 — Navigate the browser to a URL. Requires HIL approval (MAAI-001). Returns TYPE1_PENDING; the Orchestrator must obtain HIL approval before execution.")]
    public string NavigateTo(string url)
    {
        try
        {
            var validated = config.ResolveAndValidateUrl(url);
            logger.LogInformation("[Playwright] NavigateTo requested (TYPE1_PENDING): {Url}", validated);
            return Pending("NavigateTo", new { url = validated, navigation_timeout_ms = config.NavigationTimeoutMs });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 1 — ClickElement (TYPE1_PENDING)
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "ClickElement")]
    [Description("TYPE 1 — Click a page element by CSS selector. Requires HIL approval (MAAI-001). Returns TYPE1_PENDING; the Orchestrator must obtain HIL approval before execution.")]
    public string ClickElement(string selector, string? description = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(selector))
                return Result(false, error: "selector must be a non-empty CSS selector string");

            logger.LogInformation("[Playwright] ClickElement requested (TYPE1_PENDING): {Selector}", selector);
            return Pending("ClickElement", new
            {
                selector,
                description       = description ?? "(no description)",
                action_timeout_ms = config.ActionTimeoutMs
            });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 1 — FillInput (TYPE1_PENDING)
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "FillInput")]
    [Description("TYPE 1 — Fill a form input by CSS selector. Requires HIL approval (MAAI-001). Returns TYPE1_PENDING; the Orchestrator must obtain HIL approval before execution.")]
    public string FillInput(string selector, string value, string? description = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(selector))
                return Result(false, error: "selector must be a non-empty CSS selector string");

            logger.LogInformation("[Playwright] FillInput requested (TYPE1_PENDING): {Selector}", selector);
            // FIX-10: carry the raw value in the TYPE1_PENDING payload so
            // DispatchType1Async has something to dispatch. Previously only
            // value_length was echoed, which meant the approved action could
            // never actually be executed with real content. Acceptable in
            // DEV-GODMODE-001 (auto-approve) — revisit before disabling God Mode,
            // since this puts form values into HIL request logs.
            return Pending("FillInput", new
            {
                selector,
                value,
                value_length      = value?.Length ?? 0,
                description       = description ?? "(no description)",
                action_timeout_ms = config.ActionTimeoutMs
            });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 1 — SubmitForm (TYPE1_PENDING)
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "SubmitForm")]
    [Description("TYPE 1 — Submit a form by CSS selector. Requires HIL approval (MAAI-001). Returns TYPE1_PENDING; the Orchestrator must obtain HIL approval before execution.")]
    public string SubmitForm(string selector, string? description = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(selector))
                return Result(false, error: "selector must be a non-empty CSS selector string");

            logger.LogInformation("[Playwright] SubmitForm requested (TYPE1_PENDING): {Selector}", selector);
            return Pending("SubmitForm", new
            {
                selector,
                description       = description ?? "(no description)",
                action_timeout_ms = config.ActionTimeoutMs
            });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 1 — TakeScreenshot (TYPE1_PENDING)
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "TakeScreenshot")]
    [Description("TYPE 1 — Capture a screenshot of the current page. Requires HIL approval (MAAI-001). Returns TYPE1_PENDING; the Orchestrator must obtain HIL approval before execution.")]
    public string TakeScreenshot(bool fullPage = false, string? outputPath = null)
    {
        try
        {
            logger.LogInformation("[Playwright] TakeScreenshot requested (TYPE1_PENDING): fullPage={FullPage}", fullPage);
            return Pending("TakeScreenshot", new
            {
                full_page   = fullPage,
                output_path = outputPath ?? "(auto-generated)"
            });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ORCHESTRATOR-ONLY — ExecuteClickElement (dispatched after HIL approval)
    // ARCH-NEW-001: mirrors ExecuteNavigateTo/ExecuteTakeScreenshot. Only path that
    // actually clicks. Never in GetMakerTools — never reachable via LLM tool selection.
    // PmcroLoop.DispatchType1Async calls it after IHilChannel.RequestAsync returns true.
    [McpServerTool(Name = "ExecuteClickElement")]
    [Description("ORCHESTRATOR-ONLY: Execute an element click after HIL approval (ARCH-NEW-001). Not for LLM tool selection.")]
    public async Task<string> ExecuteClickElement(string selector, string? description = null)
    {
        try
        {
            using var _ = await session.AcquireLockAsync();
            await session.EnsureLaunchedAsync();
            var page = session.GetPage();
            logger.LogInformation("[Playwright] ExecuteClickElement: {Selector}", selector);
            await page.ClickAsync(selector, new PageClickOptions { Timeout = config.ActionTimeoutMs });
            return Result(true, new { selector, url = page.Url });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ORCHESTRATOR-ONLY — ExecuteFillInput (dispatched after HIL approval)
    // ARCH-NEW-001: mirrors ExecuteNavigateTo/ExecuteTakeScreenshot. Only path that
    // actually fills a form field. Never in GetMakerTools. PmcroLoop.DispatchType1Async
    // calls it after IHilChannel.RequestAsync returns true.
    [McpServerTool(Name = "ExecuteFillInput")]
    [Description("ORCHESTRATOR-ONLY: Execute a form-input fill after HIL approval (ARCH-NEW-001). Not for LLM tool selection.")]
    public async Task<string> ExecuteFillInput(string selector, string value, string? description = null)
    {
        try
        {
            using var _ = await session.AcquireLockAsync();
            await session.EnsureLaunchedAsync();
            var page = session.GetPage();
            logger.LogInformation("[Playwright] ExecuteFillInput: {Selector}", selector);
            await page.FillAsync(selector, value, new PageFillOptions { Timeout = config.ActionTimeoutMs });
            return Result(true, new { selector, url = page.Url });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }
}
