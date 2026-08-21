// src/Mcps/ProjectName.Mcp.Filesystem/Configuration/FileSystemConfig.cs
// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.FILESYSTEM
// File       : Configuration/FilesystemConfig.cs
// Identity   : Sandbox Enforcer & Path Resolver
// Law Anchor : FS-LAW-001 (Strict Path Traversal Prevention)
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace ProjectName.Mcp.Filesystem.Configuration;

/// <summary>
/// Defines the boundaries of the filesystem sandbox.
/// All file paths provided by the Agent MUST be resolved through this class.
/// </summary>
public sealed class FilesystemConfig
{
    /// <summary>
    /// The absolute path of the root sandbox directory.
    /// The LLM cannot access anything outside this path.
    /// </summary>
    public string SandboxRoot { get; }

    /// <summary>
    /// Maximum allowed file size for reading/writing operations (default 10MB).
    /// Prevents OOM errors when processing agent payloads.
    /// </summary>
    public int MaxFileSizeBytes { get; }

    public FilesystemConfig(IConfiguration config)
    {
        // Default to a folder relative to the execution directory if not specified
        var configuredPath = config["Filesystem:SandboxRoot"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "Workspace");

        // Ensure we store the absolute, normalized path
        SandboxRoot = Path.GetFullPath(configuredPath);

        // Ensure the sandbox directory actually exists
        if (!Directory.Exists(SandboxRoot))
        {
            Directory.CreateDirectory(SandboxRoot);
        }

        MaxFileSizeBytes = int.TryParse(config["Filesystem:MaxFileSizeBytes"], out var size)
            ? size
            : 10 * 1024 * 1024; // 10MB default
    }

    /// <summary>
    /// Safely resolves a relative path provided by the LLM into an absolute system path.
    /// FS-LAW-001: Throws UnauthorizedAccessException if path traversal escapes the sandbox.
    /// </summary>
    /// <param name="relativePath">The path requested by the LLM (e.g., "logs/errors.txt")</param>
    /// <returns>The verified, absolute file path.</returns>
    /// <exception cref="UnauthorizedAccessException">If the path attempts to escape the sandbox.</exception>
    public string ResolveAndValidatePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return SandboxRoot;
        }

        // FIX: Strip leading slashes to prevent Path.Combine from treating paths like "/" as the absolute drive root
        relativePath = relativePath.TrimStart('/', '\\');

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return SandboxRoot;
        }

        // 1. Combine the Sandbox Root with the requested path
        string combinedPath = Path.Combine(SandboxRoot, relativePath);

        // 2. Resolve to absolute path (this resolves all "../" and "./" sequences)
        string absolutePath = Path.GetFullPath(combinedPath);

        // 3. SECURE CHECK: Does the resolved absolute path still start with the SandboxRoot?
        // We use StringComparison.OrdinalIgnoreCase for Windows paths.
        // Ensure exact directory matching by adding a trailing separator.
        string sandboxPrefix = SandboxRoot.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? SandboxRoot
            : SandboxRoot + Path.DirectorySeparatorChar;

        string absWithSlash = absolutePath.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? absolutePath
            : absolutePath + Path.DirectorySeparatorChar;

        if (!absWithSlash.StartsWith(sandboxPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Security Violation: Path traversal attempt detected. The path '{relativePath}' resolves outside the allowed sandbox."
            );
        }

        return absolutePath;
    }
}