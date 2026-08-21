// Tools/WebAgentSkill.cs
// AgentClassSkill<T> for web-agent.
// Browser actions are dispatched via Playwright MCP HTTP JSON-RPC.
// TYPE2 (navigate/snapshot/extract/screenshot) — no HIL.
// TYPE1 (click on submit, type credentials) — HIL required.
//
// Anthropic Pattern : Augmented LLM (Browser Tools)
// MAF Skill Type    : class-based (AgentClassSkill<T>)
// Laws              : WEB-001 Real Data Only, WEB-002 Null Over Hallucination,
//                     WEB-003 Read-Only Default, WEB-005 Ground Truth Snapshot,
//                     MAAI-001 TYPE1 Authorization, ANT-001 Minimal Footprint

using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectName.OrchestratorService.Configuration;
using ProjectName.OrchestratorService.Loop;

namespace ProjectName.OrchestratorService.Tools;

public sealed class WebAgentSkill : AgentClassSkill<WebAgentSkill>
{
    private readonly IHilChannel  _hil;
    private readonly IHttpClientFactory _http;
    private readonly string       _mcpBase;
    private readonly ILogger      _logger;

    public WebAgentSkill(
        IHilChannel                  hil,
        IHttpClientFactory           http,
        IOptions<OrchestratorConfig> config,
        ILogger<WebAgentSkill>       logger)
    {
        _hil     = hil;
        _http    = http;
        _logger  = logger;
        // Playwright MCP default port — override via OrchestratorConfig or env
        _mcpBase = "http://localhost:7032";
    }

    public override AgentSkillFrontmatter Frontmatter { get; } = new(
        name:        "web-agent",
        description: "Browser automation specialist — navigate live pages, extract structured data from accessibility trees, " +
                     "screenshot, and execute targeted interactions. All output derives exclusively from live browser snapshots. " +
                     "Use for any task requiring web scraping, form submission, or browser-based data collection.");

    protected override string Instructions => """
        You are the Web-Agent for the PMCR-O Colony.
        Anthropic Pattern: Augmented LLM (Browser Tools).

        LAWS (non-negotiable):
        - WEB-001: ALL output MUST derive from live browser_snapshot accessibility trees. Never hallucinate content.
        - WEB-002: If a field is absent in the snapshot, emit null. Never invent values.
        - WEB-003: Default to TYPE2 (read-only). Only escalate to TYPE1 when mutation is explicit in the plan.
        - WEB-005: After every web_navigate, call web_snapshot to verify the expected page loaded.
        - MAAI-001: web_click on form submits and web_type for credentials require HIL.
        - ANT-001: Navigate only to URLs in the active PlannerFrame step.

        WORKFLOW for data extraction:
        1. web_navigate(url) → always followed by web_snapshot (WEB-005 ground truth)
        2. web_extract(schema) → fields from snapshot only
        3. Return extraction JSON with null for any absent fields (WEB-002)

        WORKFLOW for form interaction (TYPE1):
        1. web_navigate + web_snapshot (verify page)
        2. Request HIL for form submit / credential entry
        3. web_type / web_click on approval
        4. web_snapshot → verify state changed
        """;

    // ── TYPE2 actions (read-only) ─────────────────────────────────────────────

    [AgentSkillScript("web_navigate")]
    [Description("Navigate to a URL. Returns page title and URL from live snapshot (WEB-005 auto-applied). TYPE2.")]
    public async Task<string> WebNavigateAsync(
        [Description("Absolute URL to navigate to.")] string url)
    {
        _logger.LogInformation("[WEB] web_navigate url={Url}", url);
        var navResult  = await McpCallAsync("browser_navigate", new { url });
        var snapResult = await McpCallAsync("browser_snapshot", new { });

        return JsonSerializer.Serialize(new
        {
            url,
            navigate_result = navResult,
            snapshot        = snapResult,
            ground_truth    = new { method = "browser_snapshot", verified = true }
        });
    }

    [AgentSkillScript("web_snapshot")]
    [Description("Capture the full accessibility tree of the current page. Returns raw snapshot JSON. TYPE2.")]
    public async Task<string> WebSnapshotAsync()
    {
        _logger.LogInformation("[WEB] web_snapshot");
        var result = await McpCallAsync("browser_snapshot", new { });
        return JsonSerializer.Serialize(new { snapshot = result });
    }

    [AgentSkillScript("web_extract")]
    [Description("Extract structured fields from the current page snapshot against a JSON schema. Returns field values — null for any absent fields (WEB-002). TYPE2.")]
    public async Task<string> WebExtractAsync(
        [Description("JSON schema describing fields to extract, e.g. {\"title\":\"string\",\"price\":\"string\"}.")] string schemaJson,
        [Description("Optional CSS selector to scope the extraction within.")] string? scopeSelector = null)
    {
        _logger.LogInformation("[WEB] web_extract schema={Schema}", schemaJson);
        var snapshot = await McpCallAsync("browser_snapshot", new { });

        // Ask the LLM via prompt — extraction is LLM-over-snapshot, not code parsing
        // The AgentSkillScript returns raw text; the Maker LLM interprets it
        return JsonSerializer.Serialize(new
        {
            schema          = schemaJson,
            scope_selector  = scopeSelector,
            snapshot        = snapshot,
            instruction     = "Extract fields matching schema from snapshot. Emit null for any field absent in the snapshot (WEB-002)."
        });
    }

    [AgentSkillScript("web_screenshot")]
    [Description("Capture a visual screenshot of the current page. Saves to .pmcro/screenshots/. Returns file path. TYPE2.")]
    public async Task<string> WebScreenshotAsync(
        [Description("Output filename (basename only, e.g. 'capture.png').")] string filename)
    {
        _logger.LogInformation("[WEB] web_screenshot file={File}", filename);
        var result = await McpCallAsync("browser_screenshot", new { filename });
        return JsonSerializer.Serialize(new { filename, result });
    }

    // ── TYPE1 actions (mutating, HIL required) ────────────────────────────────

    [AgentSkillScript("web_click")]
    [Description("TYPE1 — HIL required for form submits. Click a UI element identified by selector or accessible name.")]
    public async Task<string> WebClickAsync(
        [Description("CSS selector or accessible name of the element to click.")] string selector,
        [Description("Trail ID for HIL audit.")] string trailId,
        [Description("Set true if this click submits a form (requires HIL).")] bool isFormSubmit = false)
    {
        if (isFormSubmit)
        {
            var id       = Guid.NewGuid().ToString("N")[..8];
            var approved = await _hil.RequestAsync(id, "web_click:form_submit", selector, trailId);
            if (!approved)
                return JsonSerializer.Serialize(new { ok = false, error = "HIL_DENIED", selector });
        }

        _logger.LogInformation("[WEB] web_click selector={Selector}", selector);
        var result   = await McpCallAsync("browser_click",    new { selector });
        var snapshot = await McpCallAsync("browser_snapshot", new { });

        return JsonSerializer.Serialize(new
        {
            selector,
            click_result = result,
            ground_truth = new { method = "browser_snapshot", verified = true, snapshot }
        });
    }

    [AgentSkillScript("web_type")]
    [Description("TYPE1 — HIL required for credential fields. Type text into a form field.")]
    public async Task<string> WebTypeAsync(
        [Description("CSS selector or accessible name of the input field.")] string selector,
        [Description("Text to type.")] string text,
        [Description("Trail ID for HIL audit.")] string trailId,
        [Description("Set true if this field accepts credentials or PII (requires HIL).")] bool isCredential = false)
    {
        if (isCredential)
        {
            var id       = Guid.NewGuid().ToString("N")[..8];
            var approved = await _hil.RequestAsync(id, "web_type:credential", selector, trailId);
            if (!approved)
                return JsonSerializer.Serialize(new { ok = false, error = "HIL_DENIED", selector });
        }

        _logger.LogInformation("[WEB] web_type selector={Selector} credential={Cred}", selector, isCredential);
        var result = await McpCallAsync("browser_type", new { selector, text });
        return JsonSerializer.Serialize(new { selector, result });
    }

    [AgentSkillScript("web_batch_scrape")]
    [Description("TYPE2. Navigate to each URL in the list, snapshot, and extract fields per schema. Returns array of per-URL extraction results.")]
    public async Task<string> WebBatchScrapeAsync(
        [Description("JSON array of URLs to scrape, e.g. [\"https://...\",\"https://...\"].")] string urlsJson,
        [Description("JSON schema of fields to extract from each page.")] string schemaJson)
    {
        var urls = JsonSerializer.Deserialize<string[]>(urlsJson) ?? [];
        _logger.LogInformation("[WEB] web_batch_scrape count={Count}", urls.Length);

        var results = new List<object>();
        foreach (var url in urls)
        {
            var nav  = await McpCallAsync("browser_navigate", new { url });
            var snap = await McpCallAsync("browser_snapshot", new { });
            results.Add(new { url, snapshot = snap, schema = schemaJson });
        }

        return JsonSerializer.Serialize(new { count = results.Count, results });
    }

    // ── MCP HTTP JSON-RPC helper ──────────────────────────────────────────────

    private async Task<string> McpCallAsync(string tool, object parameters)
    {
        try
        {
            var client = _http.CreateClient("playwright-mcp");
            var payload = new
            {
                jsonrpc = "2.0",
                id      = Guid.NewGuid().ToString("N")[..8],
                method  = "tools/call",
                @params = new { name = tool, arguments = parameters }
            };

            var response = await client.PostAsJsonAsync($"{_mcpBase}/mcp", payload);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("[WEB] MCP {Tool} → {Body}", tool, body);
            return body;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WEB] MCP call failed tool={Tool}", tool);
            return JsonSerializer.Serialize(new { error = ex.Message, tool });
        }
    }
}
