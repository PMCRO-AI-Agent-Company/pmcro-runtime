// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.FILESYSTEM
// File       : Prompts/FilesystemPrompts.cs
// Identity   : Filesystem Mission Briefs (Pillar Three)
// Law Anchor : FS-LAW-001 (Sandbox Enforcement)
// ───────────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.Collections.Generic;
using System.ComponentModel;

namespace ProjectName.Mcp.Filesystem.Prompts;

/// <summary>
/// Pillar Three — Mission briefs that define the Agent's operational 
/// constraints and logic for the filesystem.
/// </summary>
[McpServerPromptType]
public sealed class FilesystemPrompts
{
    [McpServerPrompt(Name = "FilesystemMissionBrief")]
    [Description("Essential guidance for workspace manipulation. Load this before reading, writing, or organizing files.")]
    public static IEnumerable<ChatMessage> GetFilesystemMissionBrief()
    {
        yield return new ChatMessage(ChatRole.User, """
            You are operating the Filesystem MCP Actuator. This is a secure, sandboxed environment.
            To ensure data integrity and prevent errors, you MUST follow these protocols:

            ── 🧩 THE FILESYSTEM LOOP ──────────────────────────────────────────────
            1. OBSERVE: Read 'filesystem://workspace/inventory' to see existing files.
            2. DISCOVER: Use 'GetFileMetadata' to check file sizes and timestamps.
            3. ACT: Perform ONE atomic operation (ReadFile, WriteFile, or DeleteFile).
            4. VERIFY: Parse the JSON tool response. Confirm 'success' is true.

            ── ⚖️ THE FS-LAWS (Server-Enforced) ─────────────────────────────────────
            - FS-LAW-001 (Sandbox): You only have access to the 'Workspace' directory.
            - RELATIVE PATHS ONLY: Never use 'C:\' or '/etc/'. Use 'data/config.json'.
            - NO TRAVERSAL: Attempts to use '../' to escape the sandbox will trigger a security exception.
            - ATOMIC TEXT: This actuator is optimized for text/JSON data. Do not attempt to write binary blobs.

            ── 📊 ERROR HANDLING ───────────────────────────────────────────────────
            - If 'success' is false, the 'error' field will tell you why (e.g., "File not found").
            - DO NOT assume a file exists just because you wrote it in a previous turn; 
              always verify via 'ListDirectory' or 'GetFileMetadata' if the loop resets.

            ── 🧠 SKILL PACKS ──────────────────────────────────────────────────────
            - Skill packs live under 'skills/' — each subdirectory with a SKILL.md is a loadable skill.
            - Use 'ListSkills' to discover available skill packs (returns name, path, and any .json data files).
            - Use 'LoadSkill(skillName)' to load a skill's SKILL.md plus all its sibling .json
              data files (e.g. earned-constraints.json, brand-profile.json) in a single call —
              prefer this over separate ReadFile calls when activating a skill.

            ── 🧹 CLEANLINESS ──────────────────────────────────────────────────────
            - Organize related data into subdirectories (e.g., 'logs/', 'output/', 'temp/').
            - Use 'DeleteFile' to remove temporary artifacts and keep the context window clean.
            """);
    }
}
