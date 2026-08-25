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
            2. DISCOVER: Use 'desktop-commander__get_file_info' to check file sizes and timestamps.
               Use 'desktop-commander__start_search' to find files by name pattern, or
               'GrepContent' to find files by text content, before assuming a path exists.
            3. ACT: Perform ONE atomic operation ('desktop-commander__read_file',
               'desktop-commander__write_file', or 'desktop-commander__list_directory').
               There is no delete tool on this actuator — files cannot be removed, only
               overwritten.
            4. VERIFY: Parse the JSON tool response. Confirm 'success' is true.

            ── ⚖️ THE FS-LAWS (Server-Enforced) ─────────────────────────────────────
            - FS-LAW-001 (Sandbox): You only have access to the 'Workspace' directory.
            - RELATIVE PATHS ONLY: Never use 'C:\' or '/etc/'. Use 'data/config.json'.
            - NO TRAVERSAL: Attempts to use '../' to escape the sandbox will trigger a security exception.
            - ATOMIC TEXT: This actuator is optimized for text/JSON data. Do not attempt to write binary blobs.

            ── 📊 ERROR HANDLING ───────────────────────────────────────────────────
            - If 'success' is false, the 'error' field will tell you why (e.g., "File not found").
            - DO NOT assume a file exists just because you wrote it in a previous turn; 
              always verify via 'desktop-commander__list_directory' or 'desktop-commander__get_file_info'
              if the loop resets.

            ── 🧠 SKILL PACKS ──────────────────────────────────────────────────────
            - Skill packs live under 'skills/' — each subdirectory with a SKILL.md is a loadable skill.
            - Use 'ListSkills' to discover available skill packs (returns name, path, and any .json data files).
            - Use 'LoadSkill(skillName)' to load a skill's SKILL.md plus all its sibling .json
              data files (e.g. earned-constraints.json, brand-profile.json) in a single call —
              prefer this over separate 'desktop-commander__read_file' calls when activating a skill.

            ── 🧹 CLEANLINESS ──────────────────────────────────────────────────────
            - Organize related data into subdirectories (e.g., 'logs/', 'output/', 'temp/').
            - There is no delete tool on this actuator. Temporary artifacts cannot be removed —
              only overwritten with 'desktop-commander__write_file'. Plan file layout accordingly
              rather than relying on cleanup after the fact.
            """);
    }
}
