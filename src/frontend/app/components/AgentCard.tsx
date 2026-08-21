// src/frontend/app/components/AgentCard.tsx
//
// ARCH-VISUAL-BRIDGE-001 (2026-07-20): extracted from PMCRO_UI's
// src/app/App.tsx, where `AgentCard` exists only as an inline TypeScript
// interface + a same-file `Card` render function -- there is no
// AgentCard.tsx in that source repo to "port" 1:1. This file separates the
// two concerns PMCRO_UI fused together and drops the fields that had no
// backing data in this app (skills/tasks/outputs/trails arrays were
// hand-authored mock content in PMCRO_UI, not read from anything real).
//
// Ported to this repo's actual conventions, not PMCRO_UI's:
//   - No Tailwind utility classes (this app has no Tailwind pipeline --
//     see globals.css) -- styled with the --colony-* custom properties
//     already used by Sidebar.tsx / DomainSelector.tsx / page.tsx.
//   - No lucide-react (not a dependency here; Sidebar.tsx hand-rolls its
//     icons as small inline SVG functions, so this file does the same
//     rather than introducing a second icon system).
//   - Data shape matches DomainSelector.tsx's real `Domain` type
//     (id/abbr/label/color, mirroring catalog/skills.json) plus an
//     optional live loop/trail summary, instead of PMCRO_UI's fictional
//     richer mock (skills, tasks, outputs) which nothing in this app
//     produces yet.
//
// NOTE: renders a plain <div> (not a conditional button/div element type) --
// switching the underlying tag by a runtime boolean doesn't type-check
// cleanly under this repo's strict TypeScript config (`type="button"` isn't
// a valid prop on `div`). Interactivity is layered on via role/tabIndex/
// onKeyDown instead.
"use client";

export type LoopRole = "Planner" | "Maker" | "Checker" | "Reflector" | "Orchestrator";

export const LOOP_ROLE_COLOR: Record<LoopRole, string> = {
  Planner: "var(--colony-accent)",
  Maker: "var(--colony-accent-2)",
  Checker: "var(--colony-retry)",
  Reflector: "#A78BFA",
  Orchestrator: "#F472B6",
};

// Mirrors DomainSelector.tsx's Domain shape so an AgentCard can be built
// directly from DOMAINS -- no separate/duplicated roster.
export type AgentCardData = {
  id: string;
  abbr: string;
  label: string;
  color: string;
  /** One-line description of the domain's Owns scope, if known. */
  description?: string;
  /** Current PMCR-O loop role, if a live cycle is running for this domain. */
  loopState?: LoopRole;
  /** Count of sealed trails on disk for this domain, if known. */
  trailCount?: number;
  /** ARCH-AGENT-DIRECTORY-001 (2026-07-20): real scope topics parsed from
   * DomainSelector.tsx's Domain.tags -- not the fictional skills/tasks/
   * outputs arrays PMCRO_UI hand-authored for its own AgentCard mock. */
  tags?: string[];
  /** ARCH-DIRECTORY-VISUAL-001 (2026-07-20): status dot tone derived from
   * this domain's most recent real trail disposition (see AgentDirectory.tsx
   * -- dispositionTone() on trailsByDomain[id]?.[0]), not a fabricated
   * Active/Idle/Busy concept the backend has no way to report. undefined
   * when the domain has no trails on disk yet. */
  statusTone?: "pass" | "retry" | "halt" | null;
};

function IconChevron() {
  return (
    <svg viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path d="M6 3.5L10.5 8L6 12.5" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export default function AgentCard({
  agent,
  onClick,
  selected = false,
}: {
  agent: AgentCardData;
  onClick?: (agent: AgentCardData) => void;
  /** ARCH-NEURAL-ACTION-001 (2026-07-20): true when this card is the target
   * of the LLM-driven `selectAgent` frontend tool (or a manual click wired
   * to the same state) -- reuses the color-tint treatment DomainSelector's
   * active pill already uses, so selection reads consistently across the app. */
  selected?: boolean;
}) {
  const interactive = typeof onClick === "function";

  return (
    <div
      role={interactive ? "button" : undefined}
      tabIndex={interactive ? 0 : undefined}
      onClick={interactive ? () => onClick!(agent) : undefined}
      onKeyDown={
        interactive
          ? (e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                onClick!(agent);
              }
            }
          : undefined
      }
      className="colony-card agent-card"
      data-tone={agent.trailCount ? "accent" : undefined}
      data-selected={selected}
      style={{
        textAlign: "left",
        width: "100%",
        cursor: interactive ? "pointer" : "default",
        ...(selected
          ? { borderColor: agent.color, background: agent.color + "14" }
          : undefined),
      }}
    >
      <div className="agent-card-head">
        <span className="agent-card-avatar-wrap">
          <span className="agent-card-badge" style={{ background: agent.color + "20", color: agent.color }}>
            {agent.abbr}
          </span>
          {agent.statusTone && (
            <span className="agent-card-status-dot" data-tone={agent.statusTone} aria-hidden="true" />
          )}
        </span>
        <span className="agent-card-title">{agent.label}</span>
        {interactive && (
          <span className="agent-card-chevron" aria-hidden="true">
            <IconChevron />
          </span>
        )}
      </div>

      {agent.description && <p className="agent-card-description">{agent.description}</p>}

      {agent.tags && agent.tags.length > 0 && (
        <div className="agent-card-tags">
          {agent.tags.map((t) => (
            <span className="agent-card-tag" key={t}>{t}</span>
          ))}
        </div>
      )}

      <div className="agent-card-meta">
        {agent.loopState ? (
          <span
            className="agent-card-loop-pill"
            style={{ color: LOOP_ROLE_COLOR[agent.loopState], background: LOOP_ROLE_COLOR[agent.loopState] + "18" }}
          >
            {agent.loopState}
          </span>
        ) : (
          <span className="agent-card-loop-pill agent-card-loop-pill--idle">Idle</span>
        )}
        {typeof agent.trailCount === "number" && (
          <span className="agent-card-trail-count">
            {agent.trailCount} trail{agent.trailCount === 1 ? "" : "s"}
          </span>
        )}
      </div>
    </div>
  );
}
