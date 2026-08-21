// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.FILESYSTEM
// File       : Resources/FilesystemResources.cs
// Identity   : Workspace State Provider (Pillar Two)
// Law Anchor : FS-LAW-001 (Sandbox Enforcement)
// ───────────────────────────────────────────────────────────────────────────────

using ModelContextProtocol.Server;
using ProjectName.Mcp.Filesystem.Configuration;
using System.ComponentModel;
using System.Text.Json;

namespace ProjectName.Mcp.Filesystem.Resources;

/// <summary>
/// Pillar Two — Exposes the filesystem environment as MCP resources.
/// Helps the agent "Observe" the workspace before "Acting".
/// </summary>
[McpServerResourceType]
public sealed class FilesystemResources(FilesystemConfig config)
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    /// <summary>
    /// Workspace status and constraints.
    /// </summary>
    [McpServerResource(
        UriTemplate = "filesystem://workspace/status",
        Name = "WorkspaceStatus",
        Title = "Filesystem Sandbox Status",
        MimeType = "application/json")]
    [Description("Provides the sandbox root path and security constraints (max file size, traversal rules).")]
    public string GetWorkspaceStatus() =>
        JsonSerializer.Serialize(new
        {
            sandboxRoot = config.SandboxRoot,
            maxFileSizeBytes = config.MaxFileSizeBytes,
            security = new
            {
                enforcement = "Strict (FS-LAW-001)",
                pathTraversal = "Blocked",
                ioOperations = "Atomic text-based"
            },
            notes = "Only paths relative to the sandboxRoot are valid for tool calls."
        }, _json);

    /// <summary>
    /// Root Inventory.
    /// A quick glance at the root directory to help the agent plan its next move.
    /// </summary>
    [McpServerResource(
        UriTemplate = "filesystem://workspace/inventory",
        Name = "WorkspaceInventory",
        Title = "Root Directory Inventory",
        MimeType = "application/json")]
    [Description("Returns a top-level list of files and folders currently in the sandbox root.")]
    public string GetWorkspaceInventory()
    {
        try
        {
            var entries = Directory.GetFileSystemEntries(config.SandboxRoot)
                .Select(e => new
                {
                    name = Path.GetFileName(e),
                    type = Directory.Exists(e) ? "directory" : "file",
                    lastModified = File.GetLastWriteTimeUtc(e)
                });

            return JsonSerializer.Serialize(new
            {
                path = "/",
                items = entries
            }, _json);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = "Could not list root directory", message = ex.Message }, _json);
        }
    }
}