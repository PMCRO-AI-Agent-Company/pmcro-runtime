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
"use client";

import { Fragment, useMemo, useState } from "react";
import { z } from "zod";
import { useRouter } from "next/navigation";
import { useAgent, useCopilotKit, useFrontendTool } from "@copilotkit/react-core/v2";
import DomainSelector, { DOMAINS } from "./DomainSelector";
import RoundTable from "./RoundTable";
import TrailView, { type Trail } from "./TrailView";
import A2UIRenderer from "./A2UIRenderer";
import SkillSelector from "./SkillSelector";
import type { SkillSummary } from "../lib/skills";

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
                <div className="phase-rail-node" />
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

export default function ConsoleView({
  trailsByDomain,
  skills,
}: {
  trailsByDomain: Record<string, Trail[]>;
  skills: SkillSummary[];
}) {
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
  const [selectedSkillIds, setSelectedSkillIds] = useState<string[]>([]);

  // ARCH-NEURAL-ACTION-001 (2026-07-20): LLM-addressable UI state.
  // briefingPlayTrigger is bumped (never read for its value, only its
  // identity-changing) to (re)start RoundTable's turn-by-turn playback.
  // NOTE (ARCH-IA-SPLIT-001, 2026-07-20): selectedAgentId / the on-page
  // C-Suite grid it used to highlight are gone -- that grid now lives only
  // in /directory (see plan point 3: Directory is the canonical place for
  // it, the Console copy was a redundant duplicate). selectAgent's handler
  // below navigates there instead of scrolling a local element.
  const [briefingPlayTrigger, setBriefingPlayTrigger] = useState(0);

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
  const router = useRouter();

  async function handleHeroSubmit(e: React.FormEvent) {
    e.preventDefault();
    const text = prompt.trim();
    if (!text || sending) return;
    setSending(true);
    setSubmittedPrompt(text);
    setRunError(null);
    try {
      // ARCH-ROUTING-TAGS-001: domain and skill selections are explicit text
      // tags because AG-UI's message shape has no UI side-channel for them.
      // Program.cs parses the tags before handing the clean intent to the
      // PMCR-O workflow and native MAF skill provider.
      const prefixes = [
        domain ? `[domain: ${domain}]` : "",
        selectedSkillIds.length > 0 ? `[skills: ${selectedSkillIds.join(", ")}]` : "",
      ].filter(Boolean);
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

  useFrontendTool({
    name: "playBriefing",
    description:
      "Plays back the Colony's Round Table briefing (from " +
      ".pmcro/SESSION-BRIEF.md) turn by turn, scrolling to it and " +
      "highlighting each speaker in sequence. Use this when the user asks " +
      "to hear, replay, or walk through the current session brief.",
    handler: async () => {
      // Bumping the trigger (rather than toggling a boolean) is what lets a
      // second playBriefing() call restart playback from turn 0 even if one
      // was already mid-run -- RoundTable's effect keys off this value
      // changing, not its truthiness.
      setBriefingPlayTrigger((n) => n + 1);
      return "Playing the Round Table briefing.";
    },
  });

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

  return (
    <>
      <section id="console" className="colony-shell" aria-labelledby="workspace-title">
        <header className="workspace-header">
          <div>
            <span className="colony-eyebrow"><span className="dot" /> PMCR-O workspace</span>
            <p className="workspace-kicker">Governed agent execution</p>
            <h1 id="workspace-title" className="workspace-title">Turn intent into governed work.</h1>
          </div>
          <div className="workspace-metrics" aria-label="Workspace metrics">
            <span><strong>{skills.length}</strong> skills</span>
            <span><strong>{DOMAINS.length}</strong> domains</span>
            <span><strong>4</strong> gates</span>
          </div>
        </header>

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
              placeholder="Ask the Colony to inspect, build, test, or explain…"
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

        <section className="workspace-context" aria-labelledby="context-heading">
          <div className="workspace-section-heading">
            <div>
              <p className="workspace-section-kicker">02 · Context</p>
              <h2 id="context-heading">Choose the operating context</h2>
            </div>
            <span className="workspace-section-meta">Optional</span>
          </div>
          <SkillSelector
            skills={skills}
            value={selectedSkillIds}
            onChange={setSelectedSkillIds}
          />
        </section>

        <section className="workspace-activity" aria-live="polite" aria-labelledby="activity-heading">
          <div className="workspace-section-heading">
            <div>
              <p className="workspace-section-kicker">03 · Activity</p>
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
                <small>{domain ? `Routed to ${DOMAINS.find((item) => item.id === domain)?.label ?? domain}` : "Default filesystem-agent routing"} · {selectedSkillIds.length} selected skills</small>
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
              <p className="workspace-section-kicker">04 · Evidence</p>
              <h2 id="evidence-heading">Live cycle evidence</h2>
            </div>
            <span className="workspace-section-meta">PMCR-O</span>
          </div>
          {/* ARCH-ROUND-TABLE-001: live Round Table rendered from
              .pmcro/SESSION-BRIEF.md and real trail data. */}
          <RoundTable playTrigger={briefingPlayTrigger} />
          <PhaseRail />
        </section>
      </section>

      {/* ARCH-CONSOLE-TRAILPLAYER-001 (2026-07-20): now wired to real
          trail data via lib/trails.ts's loadTrailsByDomain(), read
          server-side in page.tsx and passed down as trailsByDomain --
          replaces the old hardcoded `trail={null}` placeholder. Shows the
          most recent trail for the tagged domain, or the most recent
          trail overall when untagged. Full history for every domain is
          still one click away in the Directory.
          ARCH-IA-SPLIT-001 (2026-07-20): this now sits directly beneath
          Console instead of at the bottom of a five-section scroll -- the
          static Subject agents / C-Suite / Harness / Skills sections that
          used to separate it from the hero moved out to /platform and
          /directory. */}
      <section id="trails" className="colony-section">
        <h2 className="colony-section-title">Trail player</h2>
        {!latestTrail && (
          <p className="colony-hint" style={{ marginTop: 0, marginBottom: 16 }}>
            No sealed or in-progress trails on disk yet — this fills in as soon as a cycle runs.
          </p>
        )}
        <TrailView trail={latestTrail} />
      </section>

      <p className="colony-hint" style={{ maxWidth: 960, margin: "56px auto 0", padding: "0 24px" }}>
        Open the assistant in the corner to give the Orchestrator a task —
        e.g. &ldquo;list the files in src/services&rdquo; or &ldquo;check the
        status of the terminal agent&rdquo;.
      </p>

      {/* ARCH-VISUAL-BRIDGE-002 (2026-07-20): mounts the A2UI renderer so the LLM
          can call render_a2ui with TrailCard or AgentDomainCard and see real
          components in the CopilotKit chat surface. */}
      <A2UIRenderer />
    </>
  );
}