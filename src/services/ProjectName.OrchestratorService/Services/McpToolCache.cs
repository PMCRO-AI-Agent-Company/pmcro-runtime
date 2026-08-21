// Services/McpToolCache.cs
// Cherry-picked from C:\Users\org.tooensure\Downloads\ProjectName_PMCRO_CycleQ\ProjectName
// (src\Services\ProjectName.AgentService\Services\McpToolCache.cs), trimmed to the
// filesystem-mcp surface that is actually wired and proven in this AppHost.
// terminal/playwright clients are kept for forward-compat but their tools are not
// exposed by GetMakerTools() yet — those MCP services are still WaitFor-blocked
// at startup (see AppHost.cs) and unproven here.

using Microsoft.Extensions.AI;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProjectName.OrchestratorService.Services;

/// <summary>
/// Represents a single MCP tool call result captured during an agent turn.
/// Used by EC-TOOLAGENT-001 to synthesize artifacts when an agent produces
/// no text output after calling tools (qwen3:8b behaviour).
/// </summary>
public sealed record CapturedToolResult(string Tool, string Result);

public sealed class McpToolCache(IHttpClientFactory httpClientFactory, ILogger<McpToolCache> logger)
{
    private readonly HttpClient _fsClient = httpClientFactory.CreateClient("mcp-filesystem");
    private readonly HttpClient _termClient = httpClientFactory.CreateClient("mcp-terminal");
    private readonly HttpClient _pwClient = httpClientFactory.CreateClient("mcp-playwright");

    // EC-TOOLAGENT-001: Capture buffer for tool results per named subject-agent.
    // When qwen3:8b calls a tool but produces no text afterward, PmcroLoop drains
    // this buffer to synthesize a minimal artifact. Keyed by subjectAgentName.
    // Thread-safe: ConcurrentDictionary + ConcurrentQueue.
    private readonly ConcurrentDictionary<string, ConcurrentQueue<CapturedToolResult>> _captureBuffers = new();

    // ── ARCH-NEW-002: Verified Resource Catalog ──────────────────────────────
    // Ground truth for PLAN-002 (Resource Grounding). Injected into the Planner's
    // prompt at build time so it can only choose an "action"/"agent_or_tool" value
    // that maps to a tool that actually exists and is actually reachable by the
    // Maker. Fixes the 2026-07-03 playwright pilot bug where the Planner invented
    // "get_h1_text"/"get_link_destination" — names that mapped to nothing, so the
    // Maker LLM fabricated results instead of erroring. Keep in sync with
    // GetMakerTools() below.
    public static readonly Dictionary<string, (string Name, string Description)[]> AgentToolCatalog = new()
    {
        ["filesystem-agent"] =
        [
            ("ReadFile", "Read the full contents of a file"),
            ("ListDirectory", "List the contents of a directory"),
            ("SearchFiles", "Search for files by glob pattern"),
            ("GrepContent", "Search file contents by pattern"),
            ("GetFileInfo", "Get file metadata (size, timestamps, type)"),
            ("WriteFile", "Write or overwrite a file (mutative)"),
        ],
        ["terminal-agent"] =
        [
            ("RunCommand", "Run a single shell command (EC-TOOLAGENT-002: only tool exposed)"),
        ],
        ["playwright-agent"] =
        [
            ("NavigateTo", "TYPE1 — Navigate the browser to a URL. Requires HIL approval, returns TYPE1_PENDING."),
            ("GetSessionStatus", "TYPE2 — Get browser session status (launched, active page, current URL)"),
            ("GetPageTitle", "TYPE2 — Get the title of the currently loaded page"),
            ("GetPageContent", "TYPE2 — Get the inner text content of the currently loaded page"),
            ("GetPageSnapshot", "TYPE2 — Get a structured aria-snapshot (roles, names, [ref=eN]) of the currently loaded page"),
        ],
        // ARCH-CODEACT-001 (2026-07-12): codeact-agent is wrapped by HyperlightCodeActProvider,
        // which owns tool exposure — the model only ever sees ONE tool, execute_code. The tools
        // listed in GetMakerTools("codeact-agent") (GetReadTools()) are reachable from INSIDE the
        // sandbox via call_tool(name, **kwargs), never directly. Deliberately read-only: approval
        // is per execute_code call, not per call_tool(...) inside it, so a mutating tool here would
        // let one HIL approval authorize an unbounded sequence of writes — see harness-codeact skill.
        ["codeact-agent"] =
        [
            ("execute_code", "TYPE2 (sandboxed) — write and run a short Python program inside an isolated Hyperlight micro-VM; read-only filesystem tools (ReadFile, ListDirectory, SearchFiles, GrepContent, GetFileInfo) are reachable via call_tool(name, **kwargs). No mutating tools are exposed inside this sandbox."),
        ],
    };

    /// <summary>
    /// Returns the verified-resource catalog for the named subject agent as a JSON
    /// array of {name, description}, for injection into the Planner's prompt
    /// (PLAN-002 grounding). Unknown agents get an empty catalog.
    /// </summary>
    public string GetVerifiedResourcesJson(string subjectAgent)
    {
        var tools = AgentToolCatalog.TryGetValue(subjectAgent, out var t) ? t : [];
        var arr = tools.Select(x => new { name = x.Name, description = x.Description });
        return JsonSerializer.Serialize(arr);
    }

    /// <summary>
    /// ADR-013: Fetch a prompt from the named MCP server.
    /// Used to fetch FilesystemMissionBrief instead of hand-composing inline.
    /// </summary>
    public async Task<string> GetPromptAsync(string server, string promptName)
    {
        var client = server switch
        {
            "filesystem" => _fsClient,
            "terminal" => _termClient,
            "playwright" => _pwClient,
            _ => throw new ArgumentException($"Unknown MCP server: {server}")
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = JsonContent.Create(new { jsonrpc = "2.0", id = Guid.NewGuid().ToString(), method = "prompts/get", @params = new { name = promptName } })
            };
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            var resp = await client.SendAsync(request);
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();

            // Extract JSON from SSE framing if present
            var rawJson = raw;
            if (raw.Contains("\ndata:") || raw.StartsWith("data:"))
            {
                var dataLine = raw.Split('\n')
                    .FirstOrDefault(l => l.StartsWith("data:"));
                rawJson = dataLine is not null ? dataLine["data:".Length..].Trim() : raw;
            }

            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("messages", out var messages))
            {
                foreach (var msg in messages.EnumerateArray())
                {
                    if (msg.TryGetProperty("content", out var contentObj) && contentObj.ValueKind == JsonValueKind.Object &&
                        contentObj.TryGetProperty("type", out var t) && t.GetString() == "text" &&
                        contentObj.TryGetProperty("text", out var text))
                        return text.GetString() ?? string.Empty;
                }
            }
            return rawJson;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MCP Prompt] {Server}/{Prompt} fetch failed", server, promptName);
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Drain all captured tool results for the named subject agent.
    /// Returns the list and clears the buffer atomically for the next cycle.
    /// Called by PmcroLoop after each cycle regardless of whether synthesis was needed.
    /// </summary>
    public List<CapturedToolResult> DrainCapturedResults(string subjectAgentName)
    {
        if (!_captureBuffers.TryGetValue(subjectAgentName, out var queue))
            return [];
        var results = new List<CapturedToolResult>();
        while (queue.TryDequeue(out var r))
            results.Add(r);
        return results;
    }

    /// <summary>
    /// TYPE2 (read-only) filesystem tools — safe to expose to any phase agent
    /// without a HIL gate.
    /// </summary>
    public List<AITool> GetReadTools() =>
    [
        AIFunctionFactory.Create(ReadFile,      "ReadFile"),
        AIFunctionFactory.Create(ListDirectory, "ListDirectory"),
        AIFunctionFactory.Create(SearchFiles,   "SearchFiles"),
        AIFunctionFactory.Create(GrepContent,   "GrepContent"),
        AIFunctionFactory.Create(GetFileInfo,   "GetFileInfo"),
    ];

    /// <summary>
    /// Returns the full tool set for the named subject-agent.
    /// ARCH-NEW-001 (revised): Tool scoping is enforced by loading only the tools
    /// appropriate for the named subject-agent, not by withholding TYPE1 tools
    /// from MakerAgent entirely. This aligns with Anthropic's Tool Use pattern
    /// where the agent receives tools and invokes them directly with real results.
    /// When new MCP servers are added, add a new case here.
    /// </summary>
    public List<AITool> GetMakerTools(string subjectAgent = "filesystem-agent") =>
        subjectAgent switch
        {
            "filesystem-agent" =>
            [
                .. GetReadTools(),
                AIFunctionFactory.Create(WriteFile, "WriteFile"),
            ],
            "terminal-agent" =>
            [
                // EC-TOOLAGENT-002: qwen3:8b ignores tool-selection instructions and defaults
                // to whichever tool appears most "safe" (Which over RunCommand).
                // Fix: expose ONLY RunCommand. One tool = zero ambiguity. Which/GetEnvironment
                // are still available via CallMcpCapturing for direct calls if needed later.
                AIFunctionFactory.Create(RunCommand, "RunCommand"),
            ],
            "playwright-agent" =>
            [
                // EC-TOOLAGENT-002 (superseded 2026-07-03, ARCH-NEW-002): originally
                // restricted to NavigateTo only. That caused the Planner to plan steps
                // (get_h1_text, get_link_destination) with no backing tool, and the
                // Maker fabricated results instead of erroring. Now mirrors
                // filesystem-agent's proven multi-tool pattern: all 5 tools are exposed,
                // but PLAN-002 grounding (GetVerifiedResourcesJson) constrains the
                // Planner to pick exactly ONE per cycle from this real list, so
                // tool-selection ambiguity is bounded by the plan, not by tool scoping.
                AIFunctionFactory.Create(NavigateTo,         "NavigateTo"),
                AIFunctionFactory.Create(GetSessionStatus,   "GetSessionStatus"),
                AIFunctionFactory.Create(GetPageTitle,       "GetPageTitle"),
                AIFunctionFactory.Create(GetPageContent,     "GetPageContent"),
                AIFunctionFactory.Create(GetPageSnapshot,    "GetPageSnapshot"),
            ],
            // ARCH-CODEACT-001: NOT assigned directly to ChatOptions.Tools in Program.cs —
            // consumed instead by HyperlightCodeActProviderOptions.Tools, which exposes them
            // to the sandboxed Python program via call_tool(...), not to the model directly.
            "codeact-agent" => GetReadTools(),
            _ => GetReadTools(),  // unknown subject-agent gets read-only
        };

    /// <summary>
    /// TYPE1 (mutative) tools — exposed ONLY to the Orchestrator for subject-agent
    /// dispatch. Never handed to MakerAgent directly.
    /// ARCH-NEW-001: TYPE 1 tools are dispatched exclusively via the Orchestrator
    /// to the named subject-agent. MakerAgent emits a domain_action in its artifact;
    /// the Orchestrator reads it and routes to filesystem-agent, terminal-agent, etc.
    /// When new MCP servers are added (terminal, playwright), their mutative tools
    /// are added here and routed the same way — never via GetMakerTools().
    /// </summary>
    public List<AITool> GetType1Tools() =>
    [
        AIFunctionFactory.Create(WriteFile, "WriteFile"),
        // Future: AIFunctionFactory.Create(ExecCommand, "ExecCommand"),   // mcp-terminal
        // Future: AIFunctionFactory.Create(BrowserClick, "BrowserClick"), // mcp-playwright
    ];

    // EC-TOOLAGENT-001: All filesystem tools route through CallMcpCapturing so
    // DrainCapturedResults("filesystem-agent") has real evidence when qwen3:8b
    // calls tools but produces 0ch text afterward.
    // (Made public so the integration harness can exercise the real read path directly.)
    public async Task<string> ReadFile(string path) => await CallMcpCapturing("filesystem-agent", _fsClient, "desktop-commander__read_file", new { path });
    // ARCH-FS-HIL-001 (2026-07-11, closure of filesystem-agent HIL gap):
    // The maker-facing WriteFile now does NOT call the MCP server directly. Instead it
    // returns a TYPE1_PENDING stub that PmcroLoop.DispatchType1Async intercepts, runs
    // through the HIL approval gate (DevUiHilChannel), then executes for real via
    // FilesystemExecuteWriteFile. This mirrors exactly how playwright-agent's NavigateTo
    // already routes through the same gate — mutating filesystem writes now require the
    // same human approval as browser navigation, instead of bypassing HIL entirely.
    // (Made public so the integration harness can invoke the maker-facing stub directly
    // to verify ARCH-FS-HIL-001's TYPE1_PENDING contract — it is still only ever bound to
    // the Maker's WriteFile AIFunction in production, never called directly by the loop.)
    public Task<string> WriteFile(string path, string content)
    {
        var pending = new System.Text.Json.Nodes.JsonObject
        {
            ["type1_pending"] = new System.Text.Json.Nodes.JsonObject
            {
                ["tool"] = "WriteFile",
                ["requested_action"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["path"] = path,
                    ["content"] = content
                }
            }
        };
        return Task.FromResult(pending.ToJsonString());
    }

    /// <summary>
    /// ARCH-FS-HIL-001: real WriteFile executor, called by PmcroLoop.DispatchType1Async
    /// ONLY after HIL approval. Never invoked via the maker LLM's tool selection.
    /// </summary>
    public Task<string> FilesystemExecuteWriteFile(string path, string content) =>
        CallMcpCapturing("filesystem-agent", _fsClient, "desktop-commander__write_file", new { path, content });
    private async Task<string> ListDirectory(string path = "") => await CallMcpCapturing("filesystem-agent", _fsClient, "desktop-commander__list_directory", new { path });
    private async Task<string> SearchFiles(string pattern, string path = "", int maxResults = 100) => await CallMcpCapturing("filesystem-agent", _fsClient, "desktop-commander__start_search", new { pattern, path, maxResults });
    private async Task<string> GrepContent(string pattern, string path = "", string filePattern = "*", bool useRegex = false, int maxResults = 200) => await CallMcpCapturing("filesystem-agent", _fsClient, "GrepContent", new { pattern, path, filePattern, useRegex, maxResults });
    private async Task<string> GetFileInfo(string path) => await CallMcpCapturing("filesystem-agent", _fsClient, "desktop-commander__get_file_info", new { path });

    // ── terminal-agent tools ──────────────────────────────────────────────────
    private async Task<string> RunCommand(string command, string? args = null, string? workingDirectory = null, string? slot = null)
        => await CallMcpCapturing("terminal-agent", _termClient, "desktop-commander__start_process", new { command, args, workingDirectory, slot });
    private async Task<string> RunScript(string scriptPath, string? args = null, string? workingDirectory = null, string? slot = null)
        => await CallMcpCapturing("terminal-agent", _termClient, "RunScript", new { scriptPath, args, workingDirectory, slot });
    private async Task<string> KillProcess(int processId, string? slot = null)
        => await CallMcpCapturing("terminal-agent", _termClient, "desktop-commander__kill_process", new { processId, slot });
    private async Task<string> GetTerminalStatus()
        => await CallMcpCapturing("terminal-agent", _termClient, "desktop-commander__list_sessions", new { });
    private async Task<string> GetEnvironment(string[]? names = null)
        => await CallMcpCapturing("terminal-agent", _termClient, "GetEnvironment", new { names });
    private async Task<string> Which(string command)
        => await CallMcpCapturing("terminal-agent", _termClient, "Which", new { command });

    /// <summary>
    /// ARCH-TERM-HIL-001: Execute terminal RunCommand after HIL approval.
    /// Called by PmcroLoop.DispatchType1Async — never via LLM tool selection.
    /// Mirrors PlaywrightExecuteNavigateTo.
    /// </summary>
    public Task<string> TerminalExecuteRunCommand(string command, string? args, string? workingDirectory, string? slot) =>
        CallMcpCapturing("terminal-agent", _termClient, "ExecuteRunCommand", new { command, args, workingDirectory, slot });

    /// <summary>
    /// ARCH-TERM-HIL-001: Execute terminal RunScript after HIL approval.
    /// Called by PmcroLoop.DispatchType1Async — never via LLM tool selection.
    /// </summary>
    public Task<string> TerminalExecuteRunScript(string scriptPath, string? args, string? workingDirectory, string? slot) =>
        CallMcpCapturing("terminal-agent", _termClient, "ExecuteRunScript", new { scriptPath, args, workingDirectory, slot });

    /// <summary>
    /// ARCH-TERM-HIL-001: Execute terminal KillProcess after HIL approval. This closes
    /// a gap that existed even before this fix — KillProcess previously had NO real
    /// execution path anywhere, approved or not.
    /// Called by PmcroLoop.DispatchType1Async — never via LLM tool selection.
    /// </summary>
    public Task<string> TerminalExecuteKillProcess(int processId, string? slot) =>
        CallMcpCapturing("terminal-agent", _termClient, "ExecuteKillProcess", new { processId, slot });

    /// <summary>
    /// EC-AUTOAPPROVE-TERM-001: safety-net commit taken immediately BEFORE an
    /// AutoMutating terminal command is allowed to run unattended (see
    /// TerminalCommandPolicy). Calls ExecuteRunCommand directly -- bypassing
    /// RunCommand's TYPE1_PENDING stub and DispatchType1Async's HIL gate entirely
    /// -- so this never recurses back through the approval flow it exists to make
    /// safe to skip. --allow-empty means a no-op snapshot (nothing changed yet)
    /// still succeeds instead of failing the surrounding dispatch. Failure to
    /// snapshot is logged but does NOT block the underlying command -- a missing
    /// commit is a smaller problem than an autonomous loop stalling on its own
    /// safety net.
    /// </summary>
    public async Task GitSafetySnapshot(string? workingDirectory, string trailId)
    {
        try
        {
            await TerminalExecuteRunCommand("git", "add -A", workingDirectory, null);
            var message = $"[pmcro-auto] safety snapshot before auto-approved command -- trail={trailId}";
            await TerminalExecuteRunCommand("git", $"commit -m \"{message}\" --allow-empty", workingDirectory, null);
            logger.LogInformation("[EC-AUTOAPPROVE-TERM-001] Git safety snapshot taken -- trail={Trail} dir={Dir}", trailId, workingDirectory ?? "(default)");
            DrainCapturedResults("terminal-agent");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[EC-AUTOAPPROVE-TERM-001] Git safety snapshot failed -- trail={Trail} -- proceeding anyway", trailId);
        }
    }

    // ── playwright-agent tools ────────────────────────────────────────────────
    // TYPE2 (read-only): GetSessionStatus, GetPageTitle, GetPageContent
    // TYPE1 (HIL pending, returns TYPE1_PENDING): NavigateTo, ClickElement, FillInput, SubmitForm, TakeScreenshot
    // All route through CallMcpCapturing for EC-TOOLAGENT-001 synthesis.
    // TYPE1 tools are wrappers only — execution requires HIL approval wiring (future task).
    private async Task<string> GetSessionStatus()
        => await CallMcpCapturing("playwright-agent", _pwClient, "GetSessionStatus", new { });
    private async Task<string> GetPageTitle()
        => await CallMcpCapturing("playwright-agent", _pwClient, "GetPageTitle", new { });
    private async Task<string> GetPageContent()
        => await CallMcpCapturing("playwright-agent", _pwClient, "GetPageContent", new { });
    private async Task<string> GetPageSnapshot()
        => await CallMcpCapturing("playwright-agent", _pwClient, "GetPageSnapshot", new { });
    // ARCH-NEW-001: execution variant — calls after HIL approval, never via LLM
    private async Task<string> ExecuteNavigateTo(string url)
        => await CallMcpCapturing("playwright-agent", _pwClient, "ExecuteNavigateTo", new { url });
    private async Task<string> NavigateTo(string url)
        => await CallMcpCapturing("playwright-agent", _pwClient, "NavigateTo", new { url });
    private async Task<string> ClickElement(string selector, string? description = null)
        => await CallMcpCapturing("playwright-agent", _pwClient, "ClickElement", new { selector, description });
    private async Task<string> FillInput(string selector, string value, string? description = null)
        => await CallMcpCapturing("playwright-agent", _pwClient, "FillInput", new { selector, value, description });
    private async Task<string> SubmitForm(string selector, string? description = null)
        => await CallMcpCapturing("playwright-agent", _pwClient, "SubmitForm", new { selector, description });
    private async Task<string> TakeScreenshot(bool fullPage = false, string? outputPath = null)
        => await CallMcpCapturing("playwright-agent", _pwClient, "TakeScreenshot", new { fullPage, outputPath });

    /// <summary>
    /// Calls an MCP tool and captures the result into the subject-agent's capture buffer.
    /// EC-TOOLAGENT-001: used for all terminal-agent tools so PmcroLoop can synthesize
    /// a rawArtifact when qwen3:8b calls tools but produces no text response afterward.
    /// </summary>
    private async Task<string> CallMcpCapturing(string agentName, HttpClient client, string tool, object args)
    {
        var result = await CallMcp(client, tool, args);
        var queue = _captureBuffers.GetOrAdd(agentName, _ => new ConcurrentQueue<CapturedToolResult>());
        queue.Enqueue(new CapturedToolResult(tool, result));
        return result;
    }

    private async Task<string> CallMcp(HttpClient client, string tool, object args)
    {
        try
        {
            var request = new { jsonrpc = "2.0", id = Guid.NewGuid().ToString(), method = "tools/call", @params = new { name = tool, arguments = args } };

            // Fix (2026-06-20): MCP Streamable HTTP transport requires the client
            // to declare it can accept either a plain JSON response or an SSE
            // stream. Without this, mcp-filesystem returned 406 Not Acceptable on
            // every call (WriteFile/ReadFile/ListDirectory all failed identically
            // in the first real cycle run, trail 05baa3f7...). PostAsJsonAsync sets
            // Content-Type but does not set Accept, so it has to be added explicitly.
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = JsonContent.Create(request)
            };
            // mcp-dotnet SDK with Stateless=true requires both Accept headers or returns 406.
            // The server responds with SSE framing ("event: message\ndata: {...}\n\n") even
            // in stateless mode, so we must strip the SSE envelope and parse only the data: line.
            httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            var resp = await client.SendAsync(httpRequest);
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            logger.LogDebug("[MCP] {Tool} raw body={Body}", tool, raw);

            // Extract JSON from SSE framing if present ("data: {...}").
            // Plain JSON responses pass through unchanged.
            var rawJson = raw;
            if (raw.Contains("\ndata:") || raw.StartsWith("data:"))
            {
                var dataLine = raw.Split('\n')
                    .FirstOrDefault(l => l.StartsWith("data:"));
                rawJson = dataLine is not null ? dataLine["data:".Length..].Trim() : raw;
            }

            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("content", out var contentArray))
            {
                foreach (var block in contentArray.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var t) && t.GetString() == "text" && block.TryGetProperty("text", out var text))
                        return text.GetString() ?? string.Empty;
                }
            }
            return rawJson;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MCP] {Tool} call failed", tool);
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// ARCH-NEW-001: Execute playwright NavigateTo after HIL approval.
    /// Called by PmcroLoop.DispatchType1Async — never via LLM tool selection.
    /// Drain the capture buffer after if needed (same as WhichPreflight pattern).
    /// </summary>
    public Task<string> PlaywrightExecuteNavigateTo(string url) =>
        CallMcpCapturing("playwright-agent", _pwClient, "ExecuteNavigateTo", new { url });

    /// <summary>
    /// ARCH-NEW-001: Execute playwright TakeScreenshot after HIL approval.
    /// Called by PmcroLoop.DispatchType1Async — never via LLM tool selection.
    /// Mirrors PlaywrightExecuteNavigateTo.
    /// </summary>
    public Task<string> PlaywrightExecuteTakeScreenshot(bool fullPage, string? outputPath) =>
        CallMcpCapturing("playwright-agent", _pwClient, "ExecuteTakeScreenshot", new { fullPage, outputPath });

    /// <summary>
    /// ARCH-NEW-001: Execute playwright ClickElement after HIL approval.
    /// Called by PmcroLoop.DispatchType1Async — never via LLM tool selection.
    /// Mirrors PlaywrightExecuteNavigateTo. NOTE: this calls an "ExecuteClickElement"
    /// MCP tool name — a matching ORCHESTRATOR-ONLY server tool must exist in
    /// PlaywrightTools.cs (mirroring ExecuteNavigateTo/ExecuteTakeScreenshot) so the
    /// LLM-callable TYPE1_PENDING stub and the post-approval executor stay separate.
    /// </summary>
    public Task<string> PlaywrightExecuteClickElement(string selector, string? description) =>
        CallMcpCapturing("playwright-agent", _pwClient, "ExecuteClickElement", new { selector, description });

    /// <summary>
    /// ARCH-NEW-001: Execute playwright FillInput after HIL approval.
    /// Called by PmcroLoop.DispatchType1Async — never via LLM tool selection.
    /// Mirrors PlaywrightExecuteNavigateTo. Requires a matching ORCHESTRATOR-ONLY
    /// "ExecuteFillInput" MCP tool on the server side (see ClickElement note above).
    /// </summary>
    public Task<string> PlaywrightExecuteFillInput(string selector, string value, string? description) =>
        CallMcpCapturing("playwright-agent", _pwClient, "ExecuteFillInput", new { selector, value, description });

    /// <summary>
    /// EC-PREFLIGHT-001: Direct Which() call for use by PmcroLoop before the LLM turn.
    /// Bypasses the agent entirely — result is injected into cycleIntent as ground truth.
    /// Results ARE captured (via CallMcpCapturing) but PmcroLoop drains them immediately
    /// after so they don't pollute the cycle's EC-TOOLAGENT-001 synthesis buffer.
    /// </summary>
    public Task<string> WhichPreflight(string command) =>
        CallMcpCapturing("terminal-agent", _termClient, "Which", new { command });

    /// <summary>
    /// ARCH-DECLARATIVE-004 (2026-08-06): moved here from PmcroLoop.cs (was
    /// private) so PmcroLoop, PmcroCycleWorkflow, and DeclarativeCycleRunner all
    /// synthesize execution_report JSON from the SAME real captured-tool-call
    /// ground truth, instead of each trusting the subject agent's raw LLM text to
    /// already be in the execution_report.tool_calls shape. Behavior unchanged
    /// from the original — pure move + visibility change.
    /// </summary>
    public string SynthesizeArtifact(string subjectAgentName)
    {
        var capturedResults = DrainCapturedResults(subjectAgentName);
        var synthesized = new System.Text.Json.Nodes.JsonObject
        {
            ["artifact_type"] = "synthesized_from_tool_calls",
            ["artifact"] = capturedResults.Count > 0 ? capturedResults.Last().Result : "No output produced.",
            ["execution_report"] = new System.Text.Json.Nodes.JsonObject
            {
                ["steps_executed"] = capturedResults.Count,
                ["tool_calls"] = new System.Text.Json.Nodes.JsonArray(
                    capturedResults.Select((r, i) => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
                    {
                        ["step_id"] = i + 1,
                        ["tool"] = r.Tool,
                        ["result"] = r.Result
                    }).ToArray()),
                ["note"] = "EC-TOOLAGENT-001/ARCH-DECLARATIVE-004: artifact synthesized from real captured tool calls"
            }
        };
        return synthesized.ToJsonString();
    }

    /// <summary>
    /// ARCH-DECLARATIVE-004: true only if raw already contains a genuine
    /// execution_report.tool_calls JSON block (the LLM self-reported in the
    /// expected schema). Used to decide whether to trust raw text as-is or
    /// synthesize from real McpToolCache capture data instead.
    /// </summary>
    public static bool HasExecutionReport(string raw)
    {
        try
        {
            var fs = raw.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            var start = raw.IndexOf('{');
            var candidate = start >= 0 ? raw[start..] : raw;
            using var doc = JsonDocument.Parse(candidate[..(candidate.LastIndexOf('}') + 1)]);
            return doc.RootElement.TryGetProperty("execution_report", out var rep) && rep.TryGetProperty("tool_calls", out _);
        }
        catch { return false; }
    }

    public async Task ProbeAsync()
    {
        foreach (var (name, client) in new (string, HttpClient)[] { ("fs", _fsClient), ("term", _termClient), ("pw", _pwClient) })
        {
            try
            {
                using var probeRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
                {
                    Content = JsonContent.Create(new { jsonrpc = "2.0", id = "probe", method = "tools/list", @params = new { } })
                };
                probeRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                probeRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
                var r = await client.SendAsync(probeRequest);
                logger.LogInformation("[MCP] {Name}: {Status}", name, r.IsSuccessStatusCode ? "OK" : "ERR");
            }
            catch { logger.LogWarning("[MCP] {Name}: FAIL", name); }
        }
    }
}