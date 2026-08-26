// src/frontend/app/components/ConsoleView.tsx
//
// ARCH-CONSOLE-TRAILPLAYER-001 (2026-07-20): extracted from app/page.tsx so
// the page itself can become an async Server Component (matching the
// app/directory/page.tsx pattern) that reads real trails via
// lib/trails.ts's loadTrailsByDomain() and hands them down as a plain prop.
// This is what finally wires the Trails section's TrailView -- previously
// hardcoded to `trail={null}` with a comment saying "no read endpoint
// exists yet" (that endpoint, loadTrailsByDomain, has existed since
// ARCH-AGENT-DIRECTORY-002 but was never plumbed into this page).
//
// ARCH-VISUAL-BRIDGE-002 (2026-07-20): A2UI renderer integrated to display
// TrailCard and AgentDomainCard components when the LLM calls render_a2ui.
//
// ARCH-OMODE-MERGE-001 (2026-08-22): Console and Harness were two separate
// routes/nav entries for what the PMCR-O framework itself defines as ONE
// outward-facing agent -- confirmed against
// .pmcro/skills-staging/pmcro-orchestrator/orchestrate/agents/orchestrator.agent.md:
// "I Am the Orchestrator... Only agent that speaks outward to the human
// (COMPANY-001)" -- and orchestration-workflow.md: "The Orchestrator
// selects O-Mode before dispatching Planner... See the O-Mode registry in
// the `pmcro` core plugin." O-Mode is a per-cycle setting the Orchestrator
// itself selects (sealed into 00-omode-audit.json alongside
// autonomy_bounds), not a second agent identity with its own page. This
// merges Harness's embedded CopilotChat into Console as the "Read-Only"
// O-Mode, selected via the same pill-toggle pattern ChatPanel.tsx already
// uses for its sidebar agent switch (.agent-mode-pill CSS, reused
// verbatim). The O-Mode registry's real enumerated names live in the
// `pmcro` core plugin, not materialized in this repo -- Governed/Read-Only
// are what's confirmed today (Governed = full Plan->Make->Check->Reflect,
// HIL-gated, trails sealed; Read-Only = MAF's harness loop, no gates, no
// trail), swappable later if more O-Modes are added to that registry.
// Mode lives in the URL (?mode=readonly) rather than local state alone so
// ChatPanel's floating sidebar (which reads the same param) can't drift
// out of sync with the main screen, and so the mode is bookmarkable/
// shareable like any other Next.js route state.
"use client";

import { Fragment, useEffect, useMemo, useRef, useState } from "react";
import { z } from "zod";
import { useRouter, useSearchParams } from "next/navigation";
import { useAgent, useCopilotKit, useFrontendTool, CopilotChat } from "@copilotkit/react-core/v2";
import DomainSelector, { DOMAINS } from "./DomainSelector";
import TrailView, { type Trail } from "./TrailView";
import A2UIRenderer from "./A2UIRenderer";
import type { SkillSummary } from "../lib/skills";

// ARCH-CANVAS-001 (2026-08-22): SkillSelector and RoundTable removed from
// this screen. Per ARCH-IA-SPLIT-001 the C-Suite roster was already
// supposed to live only in /directory -- but SkillSelector's full catalog
// ("02 · Context") and RoundTable ("04 · Evidence") were still duplicated
// here, which is what the redesign is fixing. Skills: /skills is now the
// only place to browse or select from the catalog. Round Table: /directory
// is the canonical C-Suite view; there is no on-Console substitute -- the
// live PhaseRail below already covers what a person needs to see about the
// *current* cycle without re-rendering the whole roster inline.

// ARCH-AGUI-STATE-001 (2026-07-13): mirrors ProjectName.OrchestratorService's
// Services/PmcroStateBroadcast.cs PmcroCycleStateSnapshot record field-for-field.
// No shared schema between the .NET and TS sides yet -- if that record's shape
// changes, update this type by hand (JsonSerializerOptions.PropertyNamingPolicy
// = CamelCase on the .NET side is what makes these field names line up).
type PmcroPhase =
  | "Planning"
  | "Checking"
  | "Reflecting"
  | "CycleComplete"
  | "Sealed"
  | "Error";

type PmcroCycleState = {
  trailId: string;
  cycle: number;
  phase: PmcroPhase;
  lastAction?: string | null;
  disposition?: string | null;
  allPassed?: boolean | null;
};

// ── Phase rail ───────────────────────────────────────────────────────────
// ARCH-FRONTEND-REDESIGN-001: only these four steps ever get a live node --
// Plan / Check / Reflect / Seal -- because those are the only phases
// PmcroLoop.cs's PmcroStateBroadcast.Publish calls actually emit (verified
// against Loop/PmcroLoop.cs 2026-07-13: Planning at cycle top, Checking at
// Turn B start, Reflecting after the Checker, Sealed after trailWriter.SealAsync).
// "Make" runs silently inside Turn A between Planning and Checking with no
// snapshot of its own, so it's rendered as a label on the connector rather
// than a fifth node that would falsely claim live visibility into it.
const RAIL_STEPS = [
  { key: "Planning", label: "Plan" },
  { key: "Checking", label: "Check" },
  { key: "Reflecting", label: "Reflect" },
  { key: "Sealed", label: "Seal" },
] as const;

function railIndex(phase: PmcroPhase): number {
  switch (phase) {
    case "Planning":
      return 0;
    case "Checking":
      return 1;
    case "Reflecting":
    case "CycleComplete":
      return 2;
    case "Sealed":
      return 3;
    default:
      return 0;
  }
}

function dispositionTone(disposition?: string | null): "pass" | "retry" | "halt" | null {
  switch (disposition) {
    case "Accept":
      return "pass";
    case "Retry":
      return "retry";
    case "Halt":
      return "halt";
    default:
      return null;
  }
}

// ARCH-AGUI-STATE-001: renders PmcroLoop's live phase transitions. Each
// PmcroCycleStateSnapshot the backend publishes is a full STATE_SNAPSHOT (not
// a delta), so agent.state is simply the latest one -- no client-side
// merging needed.
function PhaseRail() {
  // "Orchestrator" matches both the keyed AIAgent name in Program.cs and the
  // agent="Orchestrator" default already set on the <CopilotKit> provider in
  // layout.tsx -- passed explicitly here so this panel doesn't silently break
  // if that default ever changes.
  const { agent } = useAgent({ agentId: "Orchestrator" });
  const state = agent.state as PmcroCycleState | undefined;

  // ARCH-CANVAS-002 (2026-08-22): trailsByDomain is read server-side once
  // per full page load (page.tsx's loadTrailsByDomain()) and handed down as
  // a static prop -- a chat-triggered cycle that seals a NEW trail never
  // re-fetches it, so the Trail Player below silently kept showing
  // whichever trail was on disk at the last full load (confirmed live:
  // ran a cycle via the rail chat, a real new trail sealed on disk, but the
  // player still showed the previous one until a manual browser refresh).
  // router.refresh() re-runs the Server Component tree in place without a
  // client navigation, which is what actually re-reads the trails
  // directory. Only fire on the Planning -> ... -> Sealed transition (a ref
  // tracks the last seen phase) so this doesn't refresh on every render or
  // every intermediate snapshot -- once per completed cycle.
  const router = useRouter();
  const lastPhaseRef = useRef<PmcroPhase | undefined>(undefined);
  useEffect(() => {
    if (state?.phase === "Sealed" && lastPhaseRef.current !== "Sealed") {
      router.refresh();
    }
    lastPhaseRef.current = state?.phase;
  }, [state?.phase, router]);

  // FIX (2026-07-13): during Next.js static prerendering of "/" there's no
  // live AG-UI connection, so agent.state comes back as {} (truthy, but with
  // no fields) rather than undefined -- the earlier `if (!state)` guard alone
  // let `state.trailId.slice(...)` through as undefined.slice() and crashed
  // the build (confirmed via `npm run build`: TypeError reading 'slice').
  // Guard on the one field every real snapshot always carries (phase) instead.
  if (!state?.phase) {
    return (
      <div className="phase-rail">
        <div className="phase-rail-track" aria-hidden="true">
          {RAIL_STEPS.map((step, i) => (
            <Fragment key={step.key}>
              <div className="phase-rail-step" data-status="pending">
                <div className="phase-rail-node" />
                <span className="phase-rail-label">{step.label}</span>
              </div>
              {i < RAIL_STEPS.length - 1 && (
                <div className="phase-rail-connector">
                  {i === 0 && <span className="phase-rail-connector-tag">make</span>}
                </div>
              )}
            </Fragment>
          ))}
        </div>
        <p className="phase-rail-idle">No cycle running yet.</p>
      </div>
    );
  }

  const isError = state.phase === "Error";
  const activeIndex = railIndex(state.phase);
  const tone = dispositionTone(state.disposition);

  return (
    <div className="phase-rail">
      <div className="phase-rail-track">
        {RAIL_STEPS.map((step, i) => {
          const status = isError && i === activeIndex
            ? "halt"
            : i < activeIndex || (i === activeIndex && state.phase === "Sealed")
              ? "done"
              : i === activeIndex
                ? "active"
                : "pending";
          return (
            <Fragment key={step.key}>
              <div className="phase-rail-step" data-status={status}>
                <div className="phase-rail-node">
                  {status === "done" && (
                    <svg viewBox="0 0 12 12" className="phase-rail-check" aria-hidden="true">
                      <path d="M2.5 6.2l2.2 2.2 4.8-5" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
                    </svg>
                  )}
                </div>
                <span className="phase-rail-label">{step.label}</span>
              </div>
              {i < RAIL_STEPS.length - 1 && (
                <div
                  className="phase-rail-connector"
                  data-filled={i < activeIndex ? "true" : "false"}
                >
                  {i === 0 && <span className="phase-rail-connector-tag">make</span>}
                </div>
              )}
            </Fragment>
          );
        })}
      </div>

      <div className="phase-rail-meta">
        <span>trail <strong>{state.trailId ? state.trailId.slice(0, 8) : "—"}</strong></span>
        <span>cycle <strong>{state.cycle}</strong></span>
        {state.lastAction && (
          <span>last action <strong>{state.lastAction}</strong></span>
        )}
        {typeof state.allPassed === "boolean" && (
          <span className={`phase-rail-badge`} data-tone={state.allPassed ? "pass" : "halt"}>
            checker {state.allPassed ? "pass" : "fail"}
          </span>
        )}
        {tone && (
          <span className="phase-rail-badge" data-tone={tone}>
            {state.disposition}
          </span>
        )}
      </div>
    </div>
  );
}

// ── O-Mode selector (ARCH-OMODE-MERGE-001) ──────────────────────────────
// Deliberately visually identical to ChatPanel.tsx's AgentModeToggle
// (.agent-mode-pill) -- same selection, same two options, so a person
// doesn't have to learn a second control that means the same thing.
// Exported so ChatPanel.tsx derives its agentId from this same param/type
// instead of re-declaring a second copy that could silently drift out of
// sync with this one (the whole point of ARCH-OMODE-MERGE-001).
export type OMode = "governed" | "readonly";
export const OMODE_PARAM = "mode";

const OMODES: { id: OMode; label: string; title: string }[] = [
  { id: "governed", label: "Governed", title: "Full PMCR-O cycle (Plan \u2192 Make \u2192 Check \u2192 Reflect), HIL-gated, seals a trail" },
  { id: "readonly", label: "Read-Only", title: "MAF harness loop \u2014 multi-turn tool use, read-only, no PMCR-O gates or trail" },
];

function OModeToggle({ value, onChange }: { value: OMode; onChange: (m: OMode) => void }) {
  return (
    <div className="agent-mode-pills" role="radiogroup" aria-label="O-Mode">
      {OMODES.map((m) => (
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

const HARNESS_LABELS = {
  chatInputPlaceholder:
    "Ask Harness to inspect files, read resources, or run read-only tools…",
  welcomeMessageText:
    "🔧 MAF's read-only harness loop — no PMCR-O gates, no sealed trail.",
};

// ARCH-CANVAS-001 (2026-08-22): governed-mode rail chat labels, parallel to
// ChatPanel.tsx's own Orchestrator entry -- this rail is a second, docked
// CopilotChat surface scoped to Console specifically (see ChatPanel.tsx's
// pathname check for why the floating one stays hidden here).
const ORCHESTRATOR_LABELS = {
  chatInputPlaceholder: "Ask the Colony to inspect, build, test, or explain…",
  welcomeMessageText:
    "👋 The Colony is listening. Planner, Maker, Checker, and Reflector are " +
    "seated — ask anything and watch the round table deliberate.",
};

export default function ConsoleView({
  trailsByDomain,
  skills,
}: {
  trailsByDomain: Record<string, Trail[]>;
  skills: SkillSummary[];
}) {
  // ARCH-OMODE-MERGE-001: mode lives in the URL, not bare useState, so
  // ChatPanel.tsx's floating sidebar (reading the same `?mode=` param) and
  // this screen can never independently drift onto different agents.
  // router.replace (not push) -- switching O-Mode is not a new navigation
  // history entry, same as any other in-place UI toggle.
  const router = useRouter();
  const searchParams = useSearchParams();
  const oMode: OMode = searchParams.get(OMODE_PARAM) === "readonly" ? "readonly" : "governed";
  const setOMode = (m: OMode) => {
    const params = new URLSearchParams(searchParams.toString());
    if (m === "governed") params.delete(OMODE_PARAM);
    else params.set(OMODE_PARAM, m);
    const qs = params.toString();
    router.replace(qs ? `/?${qs}` : "/", { scroll: false });
  };

  const [prompt, setPrompt] = useState("");
  const [sending, setSending] = useState(false);
  const [submittedPrompt, setSubmittedPrompt] = useState<string | null>(null);
  const [runError, setRunError] = useState<string | null>(null);
  // ARCH-DOMAIN-SELECT-001: null = untagged (today's default, resolves to
  // filesystem-agent). A chosen domain id gets prefixed onto the outgoing
  // message as an explicit routing tag the Orchestrator's instructions parse
  // (see Program.cs) -- this is what makes FileTrailWriter name the trail
  // directory after the domain instead of "filesystem-agent", even before any
  // domain-specific skill-loading is wired in.
  const [domain, setDomain] = useState<string | null>(null);

  // ARCH-CANVAS-001 (2026-08-22): selectedSkillIds and briefingPlayTrigger
  // removed along with SkillSelector/RoundTable -- there's no on-Console UI
  // left for either to drive. selectAgent's handler below still navigates
  // to /directory, same as before.

  // ARCH-NEURAL-ACTION-002 (2026-07-20): pins the Trail player to one
  // specific sealed trail by id, independent of the domain tag. Takes
  // priority over the domain-based fallback below when set (playTrail's
  // handler is the only setter -- there's no UI control for this yet, by
  // design; picking a specific trail by id is an LLM-addressable action,
  // not a manual one).
  const [selectedTrailId, setSelectedTrailId] = useState<string | null>(null);

  // ARCH-CONSOLE-TRAILPLAYER-001 (2026-07-20): the trail the Trails section's
  // TrailView renders. If a domain is tagged, show that domain's most
  // recent trail (trailsByDomain entries already come back sorted newest
  // first from loadTrailsByDomain); untagged, fall back to the single most
  // recent trail across every domain. Real data end to end -- no fabricated
  // "current" trail state.
  const latestTrail = useMemo<Trail | null>(() => {
    // ARCH-NEURAL-ACTION-002: an explicit playTrail(uuid) pin wins over
    // both the domain tag and the cross-domain fallback -- it's a direct
    // request for one specific trail, searched across every domain since
    // the id alone doesn't say which domain it sealed under.
    if (selectedTrailId) {
      for (const trails of Object.values(trailsByDomain)) {
        const match = trails.find((t) => t.id === selectedTrailId);
        if (match) return match;
      }
      // Stale/unknown id (e.g. trail pruned since the model last saw it) --
      // fall through to the normal domain-based behavior rather than
      // rendering nothing.
    }
    if (domain) return trailsByDomain[domain]?.[0] ?? null;
    let best: Trail | null = null;
    for (const trails of Object.values(trailsByDomain)) {
      const candidate = trails[0];
      if (!candidate) continue;
      if (!best || (candidate.createdAt ?? "") > (best.createdAt ?? "")) best = candidate;
    }
    return best;
  }, [trailsByDomain, domain, selectedTrailId]);

  // ARCH-FRONTEND-SIDEBAR-004 (2026-07-15): the previous handler only
  // focused the CopilotKit sidebar's input and copied text into it via a
  // native setter -- it never actually dispatched anything, so "Send to
  // Orchestrator" silently did nothing until the person pressed Enter
  // themselves. Fixed using the documented v2 pattern (verified against
  // CopilotKit's own docs/blog examples, 2026-07-15): add a user message to
  // the agent directly, then run it via useCopilotKit()'s copilotkit.runAgent
  // -- the same call CopilotChat's own send button makes internally.
  const { agent } = useAgent({ agentId: "Orchestrator" });
  const { copilotkit } = useCopilotKit();
  // ARCH-OMODE-MERGE-001: router is already declared above for the O-Mode
  // URL param -- reused here, not redeclared (was a duplicate `const router`
  // in this scope, a TS2451 compile error caught on refresh 2026-08-22).

  async function handleHeroSubmit(e: React.FormEvent) {
    e.preventDefault();
    const text = prompt.trim();
    if (!text || sending) return;
    setSending(true);
    setSubmittedPrompt(text);
    setRunError(null);
    try {
      // ARCH-ROUTING-TAGS-001: domain selection is an explicit text tag
      // because AG-UI's message shape has no UI side-channel for it.
      // Program.cs parses the tag before handing the clean intent to the
      // PMCR-O workflow. ARCH-CANVAS-001: the skills tag is gone along with
      // SkillSelector -- skill context now only comes from /skills.
      const prefixes = [domain ? `[domain: ${domain}]` : ""].filter(Boolean);
      const content = [...prefixes, text].join(" ");
      agent.addMessage({
        id: crypto.randomUUID(),
        role: "user",
        content,
      });
      await copilotkit.runAgent({ agent });
      setPrompt("");
    } catch (error) {
      setRunError(error instanceof Error ? error.message : "The agent connection failed. Open the assistant for details.");
    } finally {
      setSending(false);
    }
  }

  // ARCH-NEURAL-ACTION-001 (2026-07-20): registered via useFrontendTool, the
  // real v2 API -- there is no useCopilotAction in @copilotkit/react-core/v2
  // (that's the v1 hook name; confirmed against this package's own
  // dist/v2/index.d.mts export list, which has no such export). Both tools
  // are unscoped (no agentId), so they're callable from any agent turn,
  // matching how DomainSelector's routing tag already works untagged.
  useFrontendTool<{ agentId: string }>({
    name: "selectAgent",
    description:
      "Opens the Colony Directory with one of the C-Suite domains " +
      "(ceo, chief-of-staff, cto, coo, cfo, cro, cmo, clo, chro, " +
      "domain-specialist) pre-selected. Use this to draw the user's " +
      "attention to whichever domain the conversation is currently about.",
    parameters: z.object({
      agentId: z
        .enum(DOMAINS.map((d) => d.id) as [string, ...string[]])
        .describe("The domain id to select, e.g. 'cfo' or 'cto'."),
    }),
    handler: async ({ agentId }) => {
      const match = DOMAINS.find((d) => d.id === agentId);
      if (!match) {
        return `No C-Suite domain with id "${agentId}". Valid ids: ${DOMAINS.map((d) => d.id).join(", ")}.`;
      }
      // ARCH-IA-SPLIT-001 (2026-07-20): the C-Suite grid this used to
      // scroll-and-highlight on the Console page has moved to /directory
      // (see AgentDirectory's initialDomainId prop, read from this exact
      // query param by app/directory/page.tsx).
      router.push(`/directory?agent=${match.id}`);
      return `Opened the Directory with ${match.label} (${match.id}) selected.`;
    },
  });

  // ARCH-CANVAS-001 (2026-08-22): 'playBriefing' removed -- it drove
  // RoundTable's turn-by-turn playback, and RoundTable no longer renders on
  // this page (moved out with SkillSelector, see file-top note). If a
  // Round Table playback action is wanted again later it belongs on
  // /directory where the roster actually lives now, not here.

  // ARCH-NEURAL-ACTION-002 (2026-07-20): 'playTrail' is the second Action
  // Bridge tool. There is deliberately no 'focusAgent' here -- naming-
  // discipline check found that's the exact same capability as the
  // 'selectAgent' tool above (select a C-Suite domain, scroll it into
  // view), just requested under a different name. Adding a second tool
  // for one intent would give the model two names to choose between for
  // the same action, which is worse than adding nothing.
  useFrontendTool<{ uuid: string }>({
    name: "playTrail",
    description:
      "Pins the Trail player (bottom of the Console) to one specific " +
      "sealed or in-progress PMCR-O trail by its id, and scrolls it into " +
      "view. Use this when the user asks to see, open, replay, or pull up " +
      "a specific trail by its id or uuid.",
    parameters: z.object({
      uuid: z.string().describe("The trail's id, e.g. 'd5f17cc3-61f1-4ac9-b73d-09c102d8147e'."),
    }),
    handler: async ({ uuid }) => {
      const found = Object.values(trailsByDomain).some((trails) =>
        trails.some((t) => t.id === uuid),
      );
      if (!found) {
        return `No trail with id "${uuid}" found on disk.`;
      }
      setSelectedTrailId(uuid);
      document.getElementById("trails")?.scrollIntoView({ behavior: "smooth", block: "start" });
      return `Playing trail ${uuid}.`;
    },
  });

  // ARCH-CANVAS-001 (2026-08-22): rail chat is docked per O-Mode (same
  // agentId mapping ChatPanel.tsx uses for the floating one elsewhere).
  const railAgentId = oMode === "governed" ? "Orchestrator" : "Harness";
  const railLabels = oMode === "governed" ? ORCHESTRATOR_LABELS : HARNESS_LABELS;

  return (
    <div className="canvas-shell">
      {/* ── Chat rail ──────────────────────────────────────────────────
          ARCH-CANVAS-001: persistent, docked chat -- not a floating
          overlay -- per the CopilotKit Canvas pattern (chat rail + live
          generative-UI canvas). ChatPanel.tsx's floating sidebar hides
          itself on "/" so there's only ever one chat surface visible here. */}
      <aside className="canvas-rail" aria-label="Colony assistant">
        <div className="canvas-rail-header">
          <p className="canvas-rail-title">PMCR-O Colony</p>
          <OModeToggle value={oMode} onChange={setOMode} />
        </div>
        <div className="canvas-rail-chat">
          <CopilotChat agentId={railAgentId} labels={railLabels} />
        </div>
      </aside>

      {/* ── Canvas ─────────────────────────────────────────────────────
          Live agent-driven workspace: task entry, routing, and real-time
          cycle evidence. No skill catalog or C-Suite roster here anymore
          -- those live at /skills and /directory respectively. */}
      <div className="canvas-pane">
        <header className="workspace-header">
          <div>
            <span className="colony-eyebrow"><span className="dot" /> PMCR-O workspace</span>
            <p className="workspace-kicker">{oMode === "governed" ? "Governed agent execution" : "Read-only tool use"}</p>
            <h1 id="workspace-title" className="workspace-title">
              {oMode === "governed" ? "Turn intent into governed work." : "Explore without governance gates."}
            </h1>
          </div>
          <div className="workspace-metrics" aria-label="Workspace metrics">
            <span><strong>{skills.length}</strong> skills</span>
            <span><strong>{DOMAINS.length}</strong> domains</span>
            <span><strong>4</strong> gates</span>
          </div>
        </header>

        {oMode === "governed" ? (
          <>
            <div className="workspace-intro">
              <h2>What should the Colony work on?</h2>
              <p>Describe the outcome. The Orchestrator will plan, make, check, and reflect with human approval at governed boundaries.</p>
            </div>

            <div className="command-card">
              <p className="command-card-label"><span className="command-dot" /> New governed run</p>
              <form className="hero-bar" onSubmit={handleHeroSubmit}>
                <label className="sr-only" htmlFor="colony-prompt">Task for the PMCR-O Orchestrator</label>
                <input
                  id="colony-prompt"
                  className="hero-input"
                  type="text"
                  aria-describedby="prompt-help"
                  placeholder="Describe the outcome you want the Orchestrator to run…"
                  value={prompt}
                  onChange={(e) => setPrompt(e.target.value)}
                />
                <button type="submit" className="hero-submit" disabled={sending || !prompt.trim()}>
                  {sending ? "Running…" : "Run with Orchestrator"}
                </button>
              </form>
              <p id="prompt-help" className="command-card-hint">Read-only exploration is immediate. File writes and command execution remain human-approved.</p>
            </div>

            <div className="workspace-controls">
              <DomainSelector value={domain} onChange={setDomain} />
              <div className="agent-context-badge"><span className="status-dot" data-live="false" /> Orchestrator · PMCR-O cycle</div>
            </div>

            <section className="workspace-activity" aria-live="polite" aria-labelledby="activity-heading">
              <div className="workspace-section-heading">
                <div>
                  <p className="workspace-section-kicker">02 · Activity</p>
                  <h2 id="activity-heading">Latest request</h2>
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
                    <small>{domain ? `Routed to ${DOMAINS.find((item) => item.id === domain)?.label ?? domain}` : "Default filesystem-agent routing"}</small>
                  </div>
                </div>
              ) : (
                <div className="activity-empty"><span>✦</span><p>Your submitted task and live agent status will appear here.</p></div>
              )}
              {runError && <p className="activity-error" role="alert">{runError}</p>}
            </section>

            <section className="workspace-evidence" aria-labelledby="evidence-heading">
              <div className="workspace-section-heading">
                <div>
                  <p className="workspace-section-kicker">03 · Evidence</p>
                  <h2 id="evidence-heading">Live cycle evidence</h2>
                </div>
                <span className="workspace-section-meta">PMCR-O</span>
              </div>
              <PhaseRail />
            </section>

            {/* ARCH-CONSOLE-TRAILPLAYER-001: real trail data via
                lib/trails.ts's loadTrailsByDomain(). Shows the most recent
                trail for the tagged domain, or the most recent trail
                overall when untagged. Full history for every domain is
                still one click away in the Directory. */}
            <section id="trails" className="colony-section" style={{ margin: "40px 0 0", padding: 0, maxWidth: "none" }}>
              <h2 className="colony-section-title">Trail player</h2>
              {!latestTrail && (
                <p className="colony-hint" style={{ marginTop: 0, marginBottom: 16 }}>
                  No sealed or in-progress trails on disk yet — this fills in as soon as a cycle runs.
                </p>
              )}
              <TrailView trail={latestTrail} />
            </section>
          </>
        ) : (
          <>
            {/* ARCH-OMODE-MERGE-001: Read-Only O-Mode body. No domain tag
                (Program.cs's routing parser is PMCR-O-specific), no
                PhaseRail/Activity (Harness publishes no
                PmcroCycleStateSnapshot), no trail player (Harness seals
                nothing) -- this mode has nothing in common with Governed
                except the outer shell and the rail chat above, now pointed
                at the Harness agent instead of the Orchestrator. */}
            <div className="workspace-intro">
              <h2>Read-only tool use, no governance gates.</h2>
              <p>MAF&apos;s batteries-included harness loop — multi-turn tool use, todo planning, progressive skill loading. Nothing here mutates the repo or seals a trail.</p>
            </div>
            <div className="product-grid" style={{ marginTop: 16 }}>
              <article className="product-card"><span className="workspace-section-kicker">01</span><h2>Read-only tools</h2><p>Filesystem and terminal inspection without PMCR-O mutation gates.</p></article>
              <article className="product-card"><span className="workspace-section-kicker">02</span><h2>Progressive skills</h2><p>Advertise, load, read resources, and request scripts only when needed.</p></article>
              <article className="product-card"><span className="workspace-section-kicker">03</span><h2>Bounded turns</h2><p>Completion marker and iteration cap prevent runaway harness loops.</p></article>
            </div>
          </>
        )}

        {/* ARCH-VISUAL-BRIDGE-002 (2026-07-20): mounts the A2UI renderer so
            the LLM can call render_a2ui with TrailCard or AgentDomainCard
            and see real components rendered on the canvas. */}
        <A2UIRenderer />
      </div>
    </div>
  );
}