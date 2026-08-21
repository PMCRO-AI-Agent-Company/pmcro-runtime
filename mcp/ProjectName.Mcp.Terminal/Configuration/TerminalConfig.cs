// src/Mcps/ProjectName.Mcp.Terminal/Configuration/TerminalConfig.cs
// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.TERMINAL
// File       : Configuration/TerminalConfig.cs
// Identity   : Sandbox Enforcer & Command Execution Limits
// Law Anchor : EC-002, MAAI-001, SAFETY-003 (Terminal Boundary Enforcement)
// ───────────────────────────────────────────────────────────────────────────────
//
// Mirrors FilesystemConfig's ResolveAndValidatePath sandboxing pattern so that
// any path arguments passed to terminal tools (e.g. working directory overrides)
// are constrained to WorkingRoot the same way filesystem-mcp constrains file I/O
// to SandboxRoot.
//
// Constructed directly in Program.cs via object-initializer syntax:
//   new TerminalConfig { WorkingRoot = ..., CommandTimeoutSeconds = ..., MaxOutputBytes = ... }
// so all three properties must have public setters.
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using System.IO;

namespace ProjectName.Mcp.Terminal.Configuration;

/// <summary>
/// Defines the boundaries and limits for terminal command execution.
/// All TYPE 1 (RunCommand, RunScript, KillProcess) and TYPE 2
/// (GetTerminalStatus, GetEnvironment, Which) tools read these limits.
/// </summary>
public sealed class TerminalConfig
{
    /// <summary>
    /// The absolute path that terminal commands are rooted at by default.
    /// Aspire injects this via Parameters:working-root (FRAC-MCP-400-001 context);
    /// Program.cs falls back to two levels above ContentRootPath in dev.
    /// </summary>
    public required string WorkingRoot { get; set; }

    /// <summary>
    /// Maximum wall-clock time (seconds) a single command may run before
    /// being treated as timed out. Default 30s per Program.cs.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum combined stdout+stderr bytes captured per command before
    /// truncation. Default 64KB per Program.cs (65536).
    /// </summary>
    public int MaxOutputBytes { get; set; } = 65536;

    /// <summary>
    /// Safely resolves a relative path against WorkingRoot.
    /// SAFETY-003: Throws UnauthorizedAccessException if the resolved path
    /// would escape WorkingRoot — mirrors FilesystemConfig.ResolveAndValidatePath.
    /// </summary>
    /// <param name="relativePath">
    /// A path relative to WorkingRoot (e.g. "src/Mcps/ProjectName.Mcp.Terminal").
    /// Null or empty resolves to WorkingRoot itself.
    /// </param>
    /// <returns>The verified, absolute directory or file path.</returns>
    /// <exception cref="UnauthorizedAccessException">
    /// If the path attempts to escape WorkingRoot.
    /// </exception>
    public string ResolveAndValidatePath(string? relativePath)
    {
        var root = Path.GetFullPath(WorkingRoot);

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return root;
        }

        relativePath = relativePath.TrimStart('/', '\\');

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return root;
        }

        string combined = Path.Combine(root, relativePath);
        string absolute = Path.GetFullPath(combined);

        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? root
            : root + Path.DirectorySeparatorChar;

        string absWithSlash = absolute.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? absolute
            : absolute + Path.DirectorySeparatorChar;

        if (!absWithSlash.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(absolute, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"SAFETY-003 violation: path traversal attempt detected. " +
                $"The path '{relativePath}' resolves outside WorkingRoot ('{root}')."
            );
        }

        return absolute;
    }
}
