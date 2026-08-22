// src/frontend/app/components/HarnessView.tsx
//
// ARCH-HARNESS-HERO-001 (2026-08-21): harness/page.tsx has imported this
// component since ARCH-HARNESS-UI-001 (ChatPanel.tsx's agent-mode toggle),
// but the file never existed -- the /harness route couldn't build. The
// underlying gap: ChatPanel's floating sidebar is a transcript-only
// surface by design (see HIDDEN_INPUT_SLOT in ChatPanel.tsx) because
// ConsoleView's hero-bar was assumed to be the one real place to send a
// message (agent.addMessage + copilotkit.runAgent, the same v2 pattern
// CopilotChat's own send button uses internally). That assumption held for
// "/" but nothing filled the same role for "/harness", so opening the
// sidebar there landed on a header with no way to type.
//
// ARCH-COPILOTCHAT-EMBED-001 (2026-08-21): the hand-rolled hero-bar +
// custom Transcript component (state, addMessage/runAgent plumbing,
// toolCallId matching, safeParseArgs) is replaced here with CopilotKit's
// own embeddable <CopilotChat agentId="Harness" /> (v2's CopilotChatView
// under the hood -- confirmed against dist/copilotkit-Bp6BD8xe.d.mts's
// CopilotChatProps, distinct from CopilotSidebar/CopilotPopup). This is
// the same component the sidebar wraps, so tool-call rendering, streaming,
// and the input box are all handled by the SDK instead of reimplemented --
// less surface to maintain, and it inherits the [data-copilotkit] oklch
// tokens already mapped to the Colony palette in globals.css, so no visual
// drift from the rest of the app. No PMCR-O phase rail, disposition
// badges, or trail tags here -- Harness is MAF's read-only, ungated
// tool-use loop (ARCH-HARNESS-001/002 on the backend), so there's no cycle
// state to visualize and no trail to seal.
"use client";

import { CopilotChat } from "@copilotkit/react-core/v2";

// ARCH-HARNESS-COPY-TRIM-001 (2026-08-22): welcomeMessageText and the
// input placeholder said almost the same thing side by side (both live in
// the same collapsed viewport -- see .harness-shell) -- welcome now names
// what Harness IS, the placeholder (unchanged) carries the actionable
// verb ("Ask Harness to..."), so the two lines read as a pair instead of
// a paraphrase of each other.
const HARNESS_LABELS = {
  chatInputPlaceholder:
    "Ask Harness to inspect files, read resources, or run read-only tools…",
  welcomeMessageText:
    "🔧 MAF's read-only harness loop — no PMCR-O gates, no sealed trail.",
};

export default function HarnessView() {
  return (
    <>
      <header className="product-page-header" aria-labelledby="harness-title">
        <p className="workspace-section-kicker">System · Harness</p>
        <h1 id="harness-title">Harness agent</h1>
        <p>
          MAF&apos;s batteries-included harness loop — multi-turn tool use, todo
          planning, progressive skill loading. Read-only tools only; no
          PMCR-O gates or sealed trails for this surface.
        </p>
      </header>

      {/* ARCH-COPILOTCHAT-EMBED-001: the entire run/activity/transcript
          surface above -- hero-bar form, Activity card, Transcript -- is
          replaced by one embedded CopilotChat. It owns its own agent
          binding, input, streaming, and tool-call rendering; harness-shell
          just gives it a bounded, scrollable frame that matches the rest
          of the workspace's card language (see .harness-shell in
          globals.css). */}
      <div className="harness-shell">
        <CopilotChat agentId="Harness" labels={HARNESS_LABELS} className="harness-chat" />
      </div>
    </>
  );
}
