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
// This component is that missing hero-bar, scoped to agentId: "Harness"
// instead of "Orchestrator". No PMCR-O phase rail, disposition badges, or
// trail tags here -- Harness is MAF's read-only, ungated tool-use loop
// (ARCH-HARNESS-001/002 on the backend), so there's no cycle state to
// visualize and no trail to seal.
"use client";

import { useState } from "react";
import { useAgent, useCopilotKit } from "@copilotkit/react-core/v2";
import type { Message } from "@ag-ui/client";

// ARCH-HARNESS-UI-002 (2026-08-22): the transcript panel promised when
// harness-transcript/-turn/-tool-call CSS was added to globals.css, but
// never actually wired into JSX -- lost during the ARCH-HARNESS-HERO-001
// rewrite that fixed the module-not-found build error. Restores it here,
// reading straight off agent.messages (no separate state -- AG-UI already
// keeps the full running transcript on the agent object itself). Tool
// results are matched to their call by toolCallId rather than rendered as
// a flat list, since a single assistant turn can carry several toolCalls
// in one message.
function safeParseArgs(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

function Transcript({ messages }: { messages: Message[] }) {
  // Index tool results by their toolCallId once per render so each
  // assistant tool-call badge can look its result up directly instead of
  // an O(n^2) scan per call.
  const resultsByCallId = new Map<string, string>();
  for (const m of messages) {
    if (m.role === "tool" && "toolCallId" in m) {
      resultsByCallId.set((m as { toolCallId: string }).toolCallId, m.content ?? "");
    }
  }

  const visible = messages.filter((m) => m.role === "user" || m.role === "assistant");
  if (visible.length === 0) return null;

  return (
    <div className="harness-transcript">
      {visible.map((m) => (
        <div className="harness-turn" key={m.id}>
          {m.content && <p className="harness-turn-text">{m.content}</p>}
          {m.role === "assistant" &&
            "toolCalls" in m &&
            m.toolCalls?.map((call) => (
              <div className="harness-tool-call" key={call.id}>
                <span className="harness-tool-call-name">🔧 {call.function.name}</span>
                <pre className="harness-tool-call-args">{safeParseArgs(call.function.arguments)}</pre>
                {resultsByCallId.has(call.id) && (
                  <div className="harness-tool-result">
                    <span className="harness-tool-result-label">Result</span>
                    <pre className="harness-tool-call-args">{resultsByCallId.get(call.id)}</pre>
                  </div>
                )}
              </div>
            ))}
        </div>
      ))}
    </div>
  );
}

export default function HarnessView() {
  const [prompt, setPrompt] = useState("");
  const [sending, setSending] = useState(false);
  const [submittedPrompt, setSubmittedPrompt] = useState<string | null>(null);
  const [runError, setRunError] = useState<string | null>(null);

  // "Harness" matches the keyed AIAgent name in Program.cs and ChatPanel's
  // agentId switch -- explicit here so this hero-bar can't silently drift
  // from the sidebar's mode toggle onto the wrong agent.
  const { agent } = useAgent({ agentId: "Harness" });
  const { copilotkit } = useCopilotKit();

  async function handleHeroSubmit(e: React.FormEvent) {
    e.preventDefault();
    const text = prompt.trim();
    if (!text || sending) return;
    setSending(true);
    setSubmittedPrompt(text);
    setRunError(null);
    try {
      agent.addMessage({
        id: crypto.randomUUID(),
        role: "user",
        content: text,
      });
      await copilotkit.runAgent({ agent });
      setPrompt("");
    } catch (error) {
      setRunError(
        error instanceof Error
          ? error.message
          : "The Harness agent connection failed. Open the assistant for details.",
      );
    } finally {
      setSending(false);
    }
  }

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

      <div className="command-card">
        <p className="command-card-label">
          <span className="command-dot" /> New harness run
        </p>
        <form className="hero-bar" onSubmit={handleHeroSubmit}>
          <label className="sr-only" htmlFor="harness-prompt">
            Task for the Harness agent
          </label>
          <input
            id="harness-prompt"
            className="hero-input"
            type="text"
            aria-describedby="harness-prompt-help"
            placeholder="Ask Harness to inspect files, read resources, or run read-only tools…"
            value={prompt}
            onChange={(e) => setPrompt(e.target.value)}
          />
          <button type="submit" className="hero-submit" disabled={sending || !prompt.trim()}>
            {sending ? "Running…" : "Run with Harness"}
          </button>
        </form>
        <p id="harness-prompt-help" className="command-card-hint">
          Read-only tool use only — Harness has no PMCR-O mutation gates and writes no sealed trail.
        </p>
      </div>

      <section className="workspace-activity" aria-live="polite" aria-labelledby="harness-activity-heading">
        <div className="workspace-section-heading">
          <div>
            <p className="workspace-section-kicker">Activity</p>
            <h2 id="harness-activity-heading">Latest request</h2>
          </div>
          <span className={`activity-status ${sending ? "is-running" : submittedPrompt ? "is-ready" : "is-idle"}`}>
            <span className="activity-status-dot" />
            {sending ? "Running" : submittedPrompt ? "Submitted" : "Waiting"}
          </span>
        </div>
        {submittedPrompt ? (
          <div className="activity-request">
            <span className="activity-request-mark">↗</span>
            <div>
              <p>{submittedPrompt}</p>
            </div>
          </div>
        ) : (
          <div className="activity-empty">
            <span>✦</span>
            <p>Your submitted task and live Harness status will appear here.</p>
          </div>
        )}
        {runError && (
          <p className="activity-error" role="alert">
            {runError}
          </p>
        )}
      </section>

      {/* ARCH-HARNESS-UI-002: live tool-call transcript, restored from the
          dead CSS -- this is the (a)-(c) evidence surface the original
          scope note called for: seeing 🔧 load_skill fire with real args
          and a real result, turn by turn, instead of just a final answer. */}
      <Transcript messages={agent.messages as Message[]} />
    </>
  );
}
