// src/frontend/app/components/ChatPanel.tsx
//
// ARCH-IA-SPLIT-001 (2026-07-20): extracted from ConsoleView.tsx as part of
// splitting the single mega-scroll page into Console (/) + Platform
// (/platform) + Directory (/directory). The CopilotKit assistant panel is
// the one piece of chrome that makes sense on every route, not just
// Console -- someone on /directory or /platform should still be able to
// open the assistant and give the Orchestrator a task. Moved up into
// layout.tsx (rendered once, globally) instead of being re-mounted per page.
//
// ARCH-ROUND-TABLE-CHAT-001 (2026-07-21): PMCR-O agents speak in first
// person ("I am the Planner..."). Agent identity in chat messages is
// handled via CSS — the globals.css `.copilot-agent-*` rules scan message
// text for "I am the [Agent]" patterns and render colored identity chips.
// No custom Markdown renderer needed; CopilotKit v2's markdown prop isn't
// a stable override surface. The CSS approach is simpler and doesn't risk
// breaking on SDK updates.
//
// Slot-suppression comments preserved verbatim from the original
// ConsoleView.tsx.
//
// ARCH-HARNESS-UI-001 (2026-07-22): adds the frontend selector flagged as
// deliberately deferred in ARCH-HARNESS-001/003 (Program.cs, AppHost.cs) --
// the backend has exposed "Harness" as a second, independently-loopable
// AIAgent (Microsoft.Agents.AI.Harness, read-only, its own
// CompletionMarkerLoopEvaluator per ARCH-HARNESS-002) since 2026-07-22, but
// nothing in the UI could reach it: CopilotKit's <CopilotKit agent="...">
// provider prop (layout.tsx) only sets the app-wide DEFAULT agent, and the
// nav rail's "#harness" link (Sidebar.tsx) is an unrelated same-page scroll
// anchor into a "Platform" section, not an agent switch. Per CopilotKit's
// documented multi-agent pattern, CopilotSidebar takes its own `agentId`
// prop (confirmed against the installed package's CopilotChatProps type,
// dist/copilotkit-Bp6BD8xe.d.mts) which overrides the provider default for
// just this one chat surface -- so switching modes here does NOT touch
// PhaseRail/Sidebar's separate `useAgent({ agentId: "Orchestrator" })`
// calls, which stay pinned to the PMCR-O cycle regardless of what the chat
// panel is talking to.
//
// Defaults to "Orchestrator" every load (no localStorage persistence, unlike
// the sidebar's collapse state) -- deliberately, so Harness stays opt-in per
// AppHost.cs's "not auto-used by the harness" framing rather than becoming
// a sticky default someone forgets they switched on.
"use client";

import { useState } from "react";
import { usePathname } from "next/navigation";
import { CopilotSidebar, CopilotModalHeader } from "@copilotkit/react-core/v2";

const HIDDEN_INPUT_SLOT = { style: { display: "none" } };

type ChatAgentId = "Orchestrator" | "Harness";

const AGENT_MODES: { id: ChatAgentId; label: string; title: string }[] = [
  { id: "Orchestrator", label: "PMCR-O", title: "PMCR-O split-turn cycle (Planner \u2192 Maker \u2192 Checker \u2192 Reflector), HIL-gated" },
  { id: "Harness", label: "Harness", title: "MAF harness loop \u2014 multi-turn tool use, read-only, no PMCR-O gating" },
];

const AGENT_LABELS: Record<ChatAgentId, { modalHeaderTitle: string; welcomeMessageText: string }> = {
  Orchestrator: {
    modalHeaderTitle: "PMCR-O Round Table",
    welcomeMessageText:
      "\ud83d\udc4b The Colony is listening. Planner, Maker, Checker, and Reflector " +
      "are seated \u2014 ask anything and watch the round table deliberate.",
  },
  Harness: {
    modalHeaderTitle: "Harness Agent",
    welcomeMessageText:
      "\ud83d\udd27 Running MAF's batteries-included harness loop \u2014 multi-turn tool " +
      "use, todo planning, progressive skill loading. Read-only tools only; " +
      "no PMCR-O gates or trails for this surface.",
  },
};

function AgentModeToggle({ value, onChange }: { value: ChatAgentId; onChange: (id: ChatAgentId) => void }) {
  return (
    <div className="agent-mode-pills" role="radiogroup" aria-label="Chat agent">
      {AGENT_MODES.map((m) => (
        <button
          key={m.id}
          type="button"
          className="agent-mode-pill"
          data-active={value === m.id}
          title={m.title}
          role="radio"
          aria-checked={value === m.id}
          onClick={() => onChange(m.id)}
        >
          {m.label}
        </button>
      ))}
    </div>
  );
}

export default function ChatPanel() {
  const pathname = usePathname();
  const [agentId, setAgentId] = useState<ChatAgentId>(pathname === "/harness" ? "Harness" : "Orchestrator");
  const labels = AGENT_LABELS[agentId];

  return (
    <CopilotSidebar
      agentId={agentId}
      labels={labels}
      input={HIDDEN_INPUT_SLOT}
      defaultOpen={false}
      header={{
        // FIX (2026-07-22): `header` is SlotValue<typeof CopilotModalHeader>
        // = `typeof CopilotModalHeader | string | Partial<ComponentProps<...>>`
        // (verified against dist/copilotkit-Bp6BD8xe.d.mts's `type SlotValue`)
        // -- NOT itself a render function. Passing a bare function here type-
        // checks against the wrong branch of that union (component override)
        // and fails. The render-prop form belongs one level down, as the
        // `children` prop OF CopilotModalHeader, passed via this partial-
        // props object.
        children: ({ titleContent, closeButton, drawerLauncher }) => (
          <div className="agent-mode-header">
            <div className="agent-mode-header-row">
              {titleContent}
              {drawerLauncher}
              {closeButton}
            </div>
            <AgentModeToggle value={agentId} onChange={setAgentId} />
          </div>
        ),
      }}
    />
  );
}
