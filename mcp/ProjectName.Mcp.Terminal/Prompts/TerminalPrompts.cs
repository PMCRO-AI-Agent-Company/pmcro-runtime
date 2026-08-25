// src/Mcps/ProjectName.Mcp.Terminal/Prompts/TerminalPrompts.cs
// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.TERMINAL
// File       : Prompts/TerminalPrompts.cs
// Identity   : Terminal Mission Briefs (Pillar Three)
// Law Anchor : EC-002, MAAI-001, SAFETY-003
// ───────────────────────────────────────────────────────────────────────────────
// ADDED 2026-07-12: closes the same Tools-only gap TerminalResources.cs closes —
// mirrors FilesystemMissionBrief / PlaywrightMissionBrief so terminal-agent cycles
// get the same "how to use this actuator well" priming the other two subject
// agents already receive by default.
// ═══════════════════════════════════════════════════════════════════════════════

using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.Collections.Generic;
using System.ComponentModel;

namespace ProjectName.Mcp.Terminal.Prompts;

/// <summary>
/// Pillar Three — Mission brief defining the Agent's operational constraints
/// and logic for the terminal actuator.
/// </summary>
[McpServerPromptType]
public sealed class TerminalPrompts
{
    [McpServerPrompt(Name = "TerminalMissionBrief")]
    [Description("Essential guidance for running commands and scripts. Load this before planning any desktop-commander__start_process, RunScript, or desktop-commander__kill_process action.")]
    public static IEnumerable<ChatMessage> GetTerminalMissionBrief()
    {
        yield return new ChatMessage(ChatRole.User, """
            You are operating the Terminal MCP Actuator. Every mutating action here requires
            a human to approve it before it actually runs. You MUST follow these protocols:

            ── 🧩 THE TERMINAL LOOP ────────────────────────────────────────────────
            1. OBSERVE: Read 'terminal://status/workspace' for the working root and limits,
               and 'terminal://status/slots' for the slot layout and TYPE1/TYPE2 boundary.
            2. DISCOVER: Use 'Which(command)' (TYPE2, no approval needed) to confirm an
               executable actually exists on PATH before planning a run around it.
            3. ACT: Call 'desktop-commander__start_process' (single command) or 'RunScript'
               (script file) for ONE atomic action. This returns TYPE1_PENDING — it does
               NOT execute yet.
            4. WAIT: The Orchestrator surfaces the pending request for HIL approval. Only
               after approval does the real command run and produce real stdout/stderr.
            5. VERIFY: Parse the JSON response. Confirm 'success' is true and check exit_code
               — a zero exit_code is the only reliable signal a command actually succeeded.

            ── ⚖️ THE TERMINAL LAWS (Server-Enforced) ───────────────────────────────
            - MAAI-001 (HIL Gate): 'desktop-commander__start_process', 'RunScript', and
              'desktop-commander__kill_process' ALWAYS return TYPE1_PENDING first. This
              server never executes them directly — only the Orchestrator dispatches them,
              and only after human approval.
            - SAFETY-003 (Sandbox): workingDirectory is always resolved relative to
              WorkingRoot and cannot escape it — do not attempt '../' traversal.
            - Slots are informational labels for log/audit readability only. They do not
              provide execution isolation — do not treat two different slots as safe to
              run conflicting operations in parallel.

            ── 📊 ERROR HANDLING ────────────────────────────────────────────────────
            - If 'success' is false, check 'error' first — "TYPE1_PENDING" means the action
              is awaiting approval, not that it failed.
            - A non-zero exit_code with success:false is a genuine command failure — read
              stderr before retrying, and do not blindly repeat the identical command.
            - HIL_DENIED means a human explicitly rejected the action — do not immediately
              retry the same command; reconsider whether it should run at all.

            ── 🧹 SCOPE DISCIPLINE ──────────────────────────────────────────────────
            - Plan exactly one atomic command per cycle. Do not chain multiple commands
              with '&&' or ';' to work around the one-action-per-cycle constraint.
            - Prefer 'Which' to confirm a tool exists before ever planning a RunCommand
              that depends on it — a failed Which is cheap (TYPE2); a failed RunCommand
              still costs a human an approval decision.
            """);
    }
}
