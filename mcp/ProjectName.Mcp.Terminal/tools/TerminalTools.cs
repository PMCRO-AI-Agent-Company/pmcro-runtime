// src/Mcps/ProjectName.Mcp.Terminal/Tools/TerminalTools.cs
// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.TERMINAL
// File       : Tools/TerminalTools.cs
// Identity   : Terminal Actuator — Command Execution & Environment Inspection
// Law Anchor : EC-002, MAAI-001, SAFETY-003, FRAC-MCP-400-001, FRAC-MCP-406-001
// ───────────────────────────────────────────────────────────────────────────────
//
// ARCH-TERM-HIL-001 (2026-07-12, closure of a real HIL bypass): this file's own
// header previously claimed "TYPE 1 tools (RunCommand, RunScript, KillProcess)
// must only be dispatched by the Orchestrator after HIL approval" — but the old
// RunCommand/RunScript method bodies called Process.Start immediately and
// unconditionally, with no TYPE1_PENDING gate at all. Only KillProcess actually
// returned a Pending stub, and even that stub had no real execution path anywhere
// (DispatchType1Async had nowhere to route an approved kill). Net effect: any
// terminal-agent cycle that reached RunCommand executed for real regardless of
// DevUiHilChannel's approve/deny state — the HIL fix applied to the Orchestrator's
// gate (DEV-GODMODE-001 disable) never actually closed this hole, because this
// server-side gate didn't exist.
//
// Fixed here using the SAME pattern already proven and running for
// filesystem-agent's WriteFile (ARCH-FS-HIL-001) and playwright-agent's
// NavigateTo/ClickElement/FillInput/TakeScreenshot (ARCH-NEW-001):
//   - RunCommand / RunScript / KillProcess (maker-facing, exposed to the LLM via
//     GetMakerTools) now ALWAYS return a TYPE1_PENDING stub. They never touch
//     Process.Start / Process.Kill.
//   - ExecuteRunCommand / ExecuteRunScript / ExecuteKillProcess are separate
//     [McpServerTool]-decorated methods that do the real work. They ARE real MCP
//     tools (reachable over the same JSON-RPC transport the Orchestrator's
//     McpToolCache uses) but are deliberately NEVER added to GetMakerTools() /
//     AgentToolCatalog, so the maker LLM can never select them directly —
//     PmcroLoop.DispatchType1Async is the only caller, and only after
//     IHilChannel.RequestAsync returns true.
//
// TYPE 2 (no HIL required — read-only / informational):
//   GetTerminalStatus, GetEnvironment, Which
//
// TYPE 1 (maker-facing tools ALWAYS return TYPE1_PENDING; real execution only via
// the matching Execute* tool, itself dispatched only by the Orchestrator after
// HIL approval per MAAI-001 / EC-002 Single Dispatcher):
//   RunCommand  -> ExecuteRunCommand
//   RunScript   -> ExecuteRunScript
//   KillProcess -> ExecuteKillProcess
//
// FOUR TERMINAL SLOTS (documented in Program.cs, tracked here for status only):
//   terminal-1  General (build, test, dotnet)
//   terminal-2  Git operations
//   terminal-3  Package managers (npm, pip, dotnet add)
//   terminal-4  Scraper / Playwright / long-running
// ═══════════════════════════════════════════════════════════════════════════════

using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ProjectName.Mcp.Terminal.Configuration;
using System.ComponentModel;
using System.Text.Json;

namespace ProjectName.Mcp.Terminal.Tools;

[McpServerToolType]
public sealed class TerminalTools(TerminalConfig config, ILogger<TerminalTools> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly string[] Slots = ["terminal-1", "terminal-2", "terminal-3", "terminal-4"];
    private static readonly string[] Type1Tools = ["desktop-commander__start_process", "RunScript", "desktop-commander__kill_process"];
    private static readonly string[] Type2Tools = ["desktop-commander__list_sessions", "GetEnvironment", "Which"];

    private static string Result(bool success, object? data = null, string? error = null) =>
        JsonSerializer.Serialize(new { success, data, error }, JsonOptions);

    // Normalizes lazy LLM slot references ("1", "2", "terminal1", etc.) to the
    // canonical "terminal-N" form so a missing hyphen doesn't hard-fail a call.
    private static string? NormalizeSlot(string? slot)
    {
        if (string.IsNullOrWhiteSpace(slot)) return slot;

        var trimmed = slot.Trim();
        if (Slots.Contains(trimmed)) return trimmed;

        if (int.TryParse(trimmed, out var n) && n is >= 1 and <= 4)
            return $"terminal-{n}";

        var lowered = trimmed.ToLowerInvariant();
        if (lowered.StartsWith("terminal") && int.TryParse(lowered.Replace("terminal", "").Replace("-", "").Replace("_", ""), out var m) && m is >= 1 and <= 4)
            return $"terminal-{m}";

        return trimmed;
    }

    private static string Pending(string tool, object requestedAction) =>
        JsonSerializer.Serialize(new
        {
            success = false,
            data = (object?)null,
            error = "TYPE1_PENDING",
            type1_pending = new
            {
                tool,
                requested_action = requestedAction,
                law_anchor = "MAAI-001 / ARCH-TERM-HIL-001",
                note = "TYPE 1 tools require HIL approval and are dispatched only by the " +
                       "Orchestrator (EC-002, Single Dispatcher). This server does not " +
                       "execute processes directly from the maker-facing tool. The " +
                       "Orchestrator must surface this request for HIL approval, then call " +
                       $"the matching Execute{tool} tool to perform the real action."
            }
        }, JsonOptions);

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 2 — GetTerminalStatus
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "desktop-commander__list_sessions")]
    [Description("Returns terminal-mcp server status: working root, configured limits, slot layout, and TYPE1/TYPE2 tool boundary.")]
    public string GetTerminalStatus()
    {
        try
        {
            var root = config.ResolveAndValidatePath(null);
            return Result(true, new
            {
                working_root = root,
                command_timeout_seconds = config.CommandTimeoutSeconds,
                max_output_bytes = config.MaxOutputBytes,
                slots = Slots,
                type1_tools = Type1Tools,
                type2_tools = Type2Tools,
                law_anchors = new[] { "EC-002", "MAAI-001", "SAFETY-003", "FRAC-MCP-400-001", "FRAC-MCP-406-001", "ARCH-TERM-HIL-001" }
            });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 2 — GetEnvironment
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "GetEnvironment")]
    [Description("Returns selected environment variables visible to the terminal-mcp process. If no names are provided, returns a small set of commonly-useful variables (PATH, DOTNET_ROOT, NGROK_PUBLIC_URL, etc.) rather than the full environment block.")]
    public string GetEnvironment(string[]? names = null)
    {
        try
        {
            string[] defaults =
            [
                "PATH", "DOTNET_ROOT", "DOTNET_VERSION",
                "NGROK_PUBLIC_URL", "ASPNETCORE_ENVIRONMENT",
                "OLLAMA_HOST"
            ];

            var requested = (names is { Length: > 0 }) ? names : defaults;

            var values = requested.ToDictionary(
                n => n,
                n => Environment.GetEnvironmentVariable(n)
            );

            logger.LogInformation("[Terminal] Read {Count} environment variable(s)", values.Count);
            return Result(true, new { variables = values });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 2 — Which
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "Which")]
    [Description("Locates an executable on PATH (like Unix 'which' / PowerShell 'Get-Command'). Returns the first matching path found, or success:true with found:false if not located. Read-only — does not execute the target.")]
    public string Which(string command)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(command))
                return Result(false, error: "command must be a non-empty executable name");

            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            var separators = OperatingSystem.IsWindows() ? new[] { ';' } : new[] { ':' };
            var extensions = OperatingSystem.IsWindows()
                ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM").Split(';')
                : [""];

            foreach (var dir in pathVar.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var ext in extensions)
                {
                    var candidate = Path.Combine(dir, command + ext);
                    if (File.Exists(candidate))
                    {
                        logger.LogInformation("[Terminal] Which({Command}) -> {Path}", command, candidate);
                        return Result(true, new { found = true, path = candidate });
                    }
                }
            }

            logger.LogInformation("[Terminal] Which({Command}) -> not found", command);
            return Result(true, new { found = false, path = (string?)null });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 1 — RunCommand (ALWAYS TYPE1_PENDING — see ExecuteRunCommand)
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "desktop-commander__start_process")]
    [Description("Requests execution of a single command in the working root. Always returns TYPE1_PENDING — the Orchestrator must obtain HIL approval, then call ExecuteRunCommand to actually run it.")]
    public string RunCommand(string command, string? args = null, string? workingDirectory = null, string? slot = null)
    {
        var normSlot = NormalizeSlot(slot);
        logger.LogInformation("[Terminal] RunCommand requested (TYPE1_PENDING): {Command} {Args} on {Slot}",
            command, args, normSlot ?? "(unassigned)");
        return Pending("desktop-commander__start_process", new
        {
            command,
            args,
            working_directory = workingDirectory,
            slot = normSlot ?? "(unassigned)"
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ORCHESTRATOR-ONLY — ExecuteRunCommand (dispatched after HIL approval)
    // ARCH-TERM-HIL-001: the ONLY path that actually starts a process for RunCommand.
    // Never in GetMakerTools — never reachable via LLM tool selection.
    // PmcroLoop.DispatchType1Async calls it after IHilChannel.RequestAsync returns true.
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "ExecuteRunCommand")]
    [Description("ORCHESTRATOR-ONLY: Execute a command after HIL approval (ARCH-TERM-HIL-001). Not for LLM tool selection. Returns stdout, stderr, exit code, and elapsed ms.")]
    public async Task<string> ExecuteRunCommand(string command, string? args = null, string? workingDirectory = null, string? slot = null)
    {
        try
        {
            var resolvedDir = config.ResolveAndValidatePath(workingDirectory);

            slot = NormalizeSlot(slot);
            if (slot is not null && slot != "(unassigned)" && !Slots.Contains(slot))
                return Result(false, error: $"Unknown slot '{slot}'. Valid slots: {string.Join(", ", Slots)}");

            logger.LogInformation("[Terminal] ExecuteRunCommand: {Command} {Args} in {Dir} on {Slot}",
                command, args, resolvedDir, slot ?? "(unassigned)");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = command,
                Arguments              = args ?? string.Empty,
                WorkingDirectory       = resolvedDir,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(config.CommandTimeoutSeconds));
            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(cts.Token);
            sw.Stop();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (stdout.Length + stderr.Length > config.MaxOutputBytes)
            {
                var budget = config.MaxOutputBytes / 2;
                stdout = stdout.Length > budget ? stdout[..budget] + "...[truncated]" : stdout;
                stderr = stderr.Length > budget ? stderr[..budget] + "...[truncated]" : stderr;
            }

            logger.LogInformation("[Terminal] ExecuteRunCommand exit={Exit} elapsed={Ms}ms", proc.ExitCode, sw.ElapsedMilliseconds);

            return Result(proc.ExitCode == 0, new
            {
                exit_code       = proc.ExitCode,
                stdout          = stdout.TrimEnd(),
                stderr          = stderr.TrimEnd(),
                elapsed_ms      = sw.ElapsedMilliseconds,
                working_directory = resolvedDir,
                slot            = slot ?? "(unassigned)",
            });
        }
        catch (OperationCanceledException)
        {
            return Result(false, error: $"Command timed out after {config.CommandTimeoutSeconds}s");
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 1 — RunScript (ALWAYS TYPE1_PENDING — see ExecuteRunScript)
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "RunScript")]
    [Description("Requests execution of a script file (.ps1 on Windows, .sh on Linux) sandboxed to WorkingRoot. Always returns TYPE1_PENDING — the Orchestrator must obtain HIL approval, then call ExecuteRunScript to actually run it.")]
    public string RunScript(string scriptPath, string? args = null, string? workingDirectory = null, string? slot = null)
    {
        var normSlot = NormalizeSlot(slot);
        logger.LogInformation("[Terminal] RunScript requested (TYPE1_PENDING): {Script} {Args} on {Slot}",
            scriptPath, args, normSlot ?? "(unassigned)");
        return Pending("RunScript", new
        {
            script_path = scriptPath,
            args,
            working_directory = workingDirectory,
            slot = normSlot ?? "(unassigned)"
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ORCHESTRATOR-ONLY — ExecuteRunScript (dispatched after HIL approval)
    // ARCH-TERM-HIL-001: the ONLY path that actually runs a script. Never in
    // GetMakerTools — never reachable via LLM tool selection.
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "ExecuteRunScript")]
    [Description("ORCHESTRATOR-ONLY: Execute a script after HIL approval (ARCH-TERM-HIL-001). Not for LLM tool selection. Returns stdout, stderr, exit code, and elapsed ms.")]
    public async Task<string> ExecuteRunScript(string scriptPath, string? args = null, string? workingDirectory = null, string? slot = null)
    {
        try
        {
            var resolvedScript = config.ResolveAndValidatePath(scriptPath);
            var resolvedDir    = config.ResolveAndValidatePath(workingDirectory);

            slot = NormalizeSlot(slot);
            if (slot is not null && slot != "(unassigned)" && !Slots.Contains(slot))
                return Result(false, error: $"Unknown slot '{slot}'. Valid slots: {string.Join(", ", Slots)}");

            if (!File.Exists(resolvedScript))
                return Result(false, error: $"Script not found: {resolvedScript}");

            var ext = Path.GetExtension(resolvedScript).ToLowerInvariant();
            var (interpreter, interpArgs) = ext switch
            {
                ".ps1" => ("pwsh", $"-NonInteractive -File \"{resolvedScript}\" {args ?? string.Empty}"),
                ".sh"  => ("bash",  $"\"{resolvedScript}\" {args ?? string.Empty}"),
                ".bat" or ".cmd" => ("cmd.exe", $"/c \"{resolvedScript}\" {args ?? string.Empty}"),
                _      => (resolvedScript, args ?? string.Empty),
            };

            logger.LogInformation("[Terminal] ExecuteRunScript: {Interpreter} {Script} {Args} in {Dir} on {Slot}",
                interpreter, resolvedScript, args, resolvedDir, slot ?? "(unassigned)");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = interpreter,
                Arguments              = interpArgs,
                WorkingDirectory       = resolvedDir,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(config.CommandTimeoutSeconds));
            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(cts.Token);
            sw.Stop();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (stdout.Length + stderr.Length > config.MaxOutputBytes)
            {
                var budget = config.MaxOutputBytes / 2;
                stdout = stdout.Length > budget ? stdout[..budget] + "...[truncated]" : stdout;
                stderr = stderr.Length > budget ? stderr[..budget] + "...[truncated]" : stderr;
            }

            logger.LogInformation("[Terminal] ExecuteRunScript exit={Exit} elapsed={Ms}ms", proc.ExitCode, sw.ElapsedMilliseconds);

            return Result(proc.ExitCode == 0, new
            {
                exit_code        = proc.ExitCode,
                stdout           = stdout.TrimEnd(),
                stderr           = stderr.TrimEnd(),
                elapsed_ms       = sw.ElapsedMilliseconds,
                script_path      = resolvedScript,
                working_directory = resolvedDir,
                slot             = slot ?? "(unassigned)",
            });
        }
        catch (OperationCanceledException)
        {
            return Result(false, error: $"Script timed out after {config.CommandTimeoutSeconds}s");
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TYPE 1 — KillProcess (ALWAYS TYPE1_PENDING — see ExecuteKillProcess)
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "desktop-commander__kill_process")]
    [Description("TYPE 1 — Requests termination of a running process by PID. Requires HIL approval (MAAI-001). Always returns TYPE1_PENDING; the Orchestrator must obtain HIL approval, then call ExecuteKillProcess to actually terminate it.")]
    public string KillProcess(int processId, string? slot = null)
    {
        try
        {
            if (processId <= 0)
                return Result(false, error: "processId must be a positive integer");

            var normSlot = NormalizeSlot(slot);
            if (normSlot is not null && !Slots.Contains(normSlot))
                return Result(false, error: $"Unknown slot '{slot}'. Valid slots: {string.Join(", ", Slots)}");

            logger.LogInformation("[Terminal] KillProcess requested (TYPE1_PENDING): PID {Pid} on {Slot}",
                processId, normSlot ?? "(unassigned)");

            return Pending("KillProcess", new
            {
                process_id = processId,
                slot = normSlot ?? "(unassigned)"
            });
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ORCHESTRATOR-ONLY — ExecuteKillProcess (dispatched after HIL approval)
    // ARCH-TERM-HIL-001: closes a gap that existed even before this fix — the old
    // KillProcess was a Pending-only stub with NO execution path anywhere, so an
    // approved kill would have silently done nothing. This is the first real
    // implementation. Never in GetMakerTools — never reachable via LLM tool selection.
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "ExecuteKillProcess")]
    [Description("ORCHESTRATOR-ONLY: Terminate a process by PID after HIL approval (ARCH-TERM-HIL-001). Not for LLM tool selection.")]
    public string ExecuteKillProcess(int processId, string? slot = null)
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(processId);
            var name = proc.ProcessName;
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(5000);
            logger.LogInformation("[Terminal] ExecuteKillProcess: PID {Pid} ({Name}) terminated on {Slot}",
                processId, name, slot ?? "(unassigned)");
            return Result(true, new { process_id = processId, process_name = name, terminated = true, slot = slot ?? "(unassigned)" });
        }
        catch (ArgumentException)
        {
            return Result(false, error: $"No running process found with PID {processId}");
        }
        catch (Exception ex) { return Result(false, error: ex.Message); }
    }
}
