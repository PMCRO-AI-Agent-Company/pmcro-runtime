// Loop/TerminalCommandPolicy.cs
// ═══════════════════════════════════════════════════════════════════════════════
// EC-AUTOAPPROVE-TERM-001 (2026-07-12): tiered command policy for terminal-agent
// RunCommand. This is deliberately NOT a blanket HIL bypass — it classifies a
// requested command into one of three buckets so routine build/test/inspect
// commands can proceed unattended (the actual day-to-day cost of "100%
// autonomy"), while anything irreversible, externally-visible, or simply
// unrecognized still stops for a human (MAAI-001 default-deny).
//
// Denylist ALWAYS wins over allowlist, regardless of the base command — a
// recognized-safe tool used with a dangerous flag (e.g. "git push --force")
// is never silently auto-approved just because "git" is otherwise trusted.
//
// Scope: this policy applies ONLY to terminal-agent's RunCommand. RunScript and
// KillProcess remain unconditionally HIL-gated — arbitrary script *content* and
// process termination are not safely classifiable from a command line alone.
// filesystem-agent WriteFile and playwright-agent actions are untouched by this
// file and keep going through the full HIL gate exactly as before.
// ═══════════════════════════════════════════════════════════════════════════════

using System.Text.RegularExpressions;

namespace ProjectName.OrchestratorService.Loop;

public static class TerminalCommandPolicy
{
    public enum Classification
    {
        /// Side-effect-free. Auto-approved, no snapshot needed.
        AutoReadOnly,
        /// Safe and reversible, but changes working-tree/output state.
        /// Auto-approved, but preceded by a git safety-snapshot commit.
        AutoMutating,
        /// Everything else. Always requires HIL approval (default-deny).
        RequiresHil
    }

    // (base command, first-argument/subcommand) pairs that are read-only —
    // inspect state without changing anything.
    private static readonly HashSet<(string Base, string Sub)> ReadOnlySubcommands = new()
    {
        ("dotnet", "--version"), ("dotnet", "--list-sdks"), ("dotnet", "--info"),
        ("git", "status"), ("git", "diff"), ("git", "log"), ("git", "branch"), ("git", "show"), ("git", "remote"),
        ("npm", "--version"), ("npm", "list"), ("npm", "ls"),
        ("node", "--version"),
        ("which", ""), ("pwd", ""), ("whoami", ""),
    };

    // (base command, first-argument/subcommand) pairs that mutate state but are
    // safe + reversible enough to auto-approve WITH a preceding git snapshot commit.
    private static readonly HashSet<(string Base, string Sub)> AutoMutatingSubcommands = new()
    {
        ("dotnet", "build"), ("dotnet", "test"), ("dotnet", "restore"), ("dotnet", "format"),
        ("npm", "install"), ("npm", "test"), ("npm", "run"),
    };

    // Regex fragments matched against the FULL command line (base + args). If any
    // hit, the result is RequiresHil no matter what the base command otherwise
    // classified as. Denylist always wins over allowlist.
    private static readonly Regex[] DangerPatterns =
    [
        new(@"\bpush\b[^\n]*(--force|-f\b)", RegexOptions.IgnoreCase),
        new(@"\breset\b[^\n]*--hard", RegexOptions.IgnoreCase),
        new(@"\bclean\b[^\n]*-[a-z]*x[a-z]*d", RegexOptions.IgnoreCase),   // git clean -xd/-fdx variants
        new(@"\bcheckout\b[^\n]*--\s*\.", RegexOptions.IgnoreCase),        // discard working-tree changes
        new(@"(^|\s)rm(\s|$)", RegexOptions.IgnoreCase),
        new(@"(^|\s)del(\s|$)", RegexOptions.IgnoreCase),
        new(@"remove-item", RegexOptions.IgnoreCase),
        new(@"\bformat\b\s+[a-zA-Z]:", RegexOptions.IgnoreCase),           // disk format (not "dotnet format")
        new(@"\bpublish\b", RegexOptions.IgnoreCase),                      // dotnet publish / npm publish
        new(@"\bnuget\b[^\n]*\bpush\b", RegexOptions.IgnoreCase),
        new(@"\bshutdown\b", RegexOptions.IgnoreCase),
        new(@"restart-computer", RegexOptions.IgnoreCase),
        new(@"(^|\s)sudo(\s|$)", RegexOptions.IgnoreCase),
        new(@"curl[^\n]*\|\s*(bash|sh|pwsh)", RegexOptions.IgnoreCase),
        new(@"\biwr\b[^\n]*\|\s*iex", RegexOptions.IgnoreCase),
    ];

    public static Classification Classify(string command, string? args)
    {
        var fullLine = $"{command} {args}".Trim();

        foreach (var pattern in DangerPatterns)
        {
            if (pattern.IsMatch(fullLine))
                return Classification.RequiresHil;
        }

        var baseCmd = command.Trim();
        var firstArg = (args ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        if (ReadOnlySubcommands.Contains((baseCmd, firstArg)))
            return Classification.AutoReadOnly;

        if (AutoMutatingSubcommands.Contains((baseCmd, firstArg)))
            return Classification.AutoMutating;

        // Unknown subcommand of an otherwise-recognized base, or an unrecognized
        // base entirely: default-deny, requires HIL.
        return Classification.RequiresHil;
    }
}
