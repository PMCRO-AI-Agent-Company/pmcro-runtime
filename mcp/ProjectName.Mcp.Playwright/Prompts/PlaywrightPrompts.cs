// src/Mcps/ProjectName.Mcp.Playwright/Prompts/PlaywrightPrompts.cs
// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTNAME — MCP.PLAYWRIGHT
// File       : Prompts/PlaywrightPrompts.cs
// Identity   : Mission-Driven Prompt Definitions
// Law Anchor : PW-LAW-001, PW-LAW-005, MAAI-001, ARCH-013
// ═══════════════════════════════════════════════════════════════════════════════

using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ProjectName.Mcp.Playwright.Prompts;

[McpServerPromptType]
public sealed class PlaywrightPrompts
{
    [McpServerPrompt(Name = "PlaywrightMissionBrief")]
    [Description("Loads the browser actuator's operating doctrine: TYPE1/TYPE2 tool boundary, HIL requirements, URL safety rules, and serial execution constraint. Call at session start before issuing any browser tool calls.")]
    public static IEnumerable<ChatMessage> GetPlaywrightMissionBrief()
    {
        yield return new ChatMessage(ChatRole.User, """
            You are about to use the ProjectName.Mcp.Playwright browser actuator.
            Before calling any tools, internalize these operating laws:

            LAW ANCHORS
            ───────────
            PW-LAW-001 : URL Safety — only http/https to public hosts. Private/loopback
                         hosts are blocked unless explicitly whitelisted in configuration.
            PW-LAW-003 : Timeout Caps — NavigationTimeout=30s, ActionTimeout=10s, PageLoad=60s.
                         You may not request higher values.
            PW-LAW-005 : Serial Page Execution — only one browser operation runs at a time.
                         Never schedule concurrent page operations.
            MAAI-001   : TYPE 1 tools require HIL (Human-in-the-Loop) approval. Do not
                         interpret a TYPE1_PENDING response as an error — it is correct
                         behavior. Surface it to the Orchestrator for HIL gating.
            EC-002     : Single Dispatcher — only the Orchestrator may approve and
                         re-dispatch TYPE 1 actions after HIL confirmation.

            TYPE 2 TOOLS (call freely — read-only, no HIL required)
            ─────────────────────────────────────────────────────────
            GetSessionStatus  — Browser and session health check
            GetPageTitle      — Current page title
            GetPageContent    — Current page inner text (truncated to MaxContentBytes)

            TYPE 1 TOOLS (return TYPE1_PENDING — HIL approval required before execution)
            ─────────────────────────────────────────────────────────────────────────────
            NavigateTo        — Navigate to a URL
            ClickElement      — Click a CSS selector
            FillInput         — Fill a form input
            SubmitForm        — Submit a form
            TakeScreenshot    — Capture page screenshot

            WORKFLOW
            ────────
            1. Call GetSessionStatus (TYPE 2) to confirm browser state
            2. Plan your TYPE 1 steps and surface them as TYPE1_PENDING to the Orchestrator
            3. After HIL approval, the Orchestrator re-dispatches each TYPE 1 step
            4. Read results via GetPageContent/GetPageTitle (TYPE 2) after each navigation
            """);
    }
}
