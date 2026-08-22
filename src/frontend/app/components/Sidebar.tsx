// src/frontend/app/components/Sidebar.tsx
"use client";

import { useEffect, useState } from "react";
import { useAgent } from "@copilotkit/react-core/v2";
import Link from "next/link";
import { usePathname } from "next/navigation";

// ARCH-FRONTEND-SIDEBAR-003 (2026-07-15): enterprise pass on top of the
// ARCH-FRONTEND-SIDEBAR-002 static rail --
//   1. Collapse/expand toggle (icon-only rail at 64px).
//   2. Scroll-spy active state via IntersectionObserver instead of a
//      hardcoded `active: true` on Console.
//   3. A live status footer wired to the real Orchestrator agent.state,
//      not decorative numbers -- same `state?.phase` presence guard
//      PhaseRail already uses in page.tsx (ARCH-AGUI-STATE-001), so this
//      never claims a connection or cycle progress the backend hasn't
//      actually published.
//
// PmcroCycleState is duplicated from page.tsx rather than imported -- no
// shared schema module exists yet between the two (same gap noted in
// page.tsx's own comment on this type). Keep both in sync by hand until
// that's factored out.
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

function IconConsole() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 5h16v14H4z" />
      <path d="M8 10l3 2.5L8 15" />
      <path d="M13 15h3" />
    </svg>
  );
}

function IconHarness() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <path d="M13 3L5 13h5l-1 8 8-10h-5l1-8z" />
    </svg>
  );
}

function IconTrails() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <path d="M8 3v4a1 1 0 0 0 1 1h7" />
      <path d="M17 3H8.5A2.5 2.5 0 0 0 6 5.5v13A2.5 2.5 0 0 0 8.5 21h8a2.5 2.5 0 0 0 2.5-2.5V8l-5-5z" />
      <path d="M9.5 13h5M9.5 16.5h5" />
    </svg>
  );
}

function IconAgents() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="9" cy="7" r="2" />
      <circle cx="15" cy="7" r="2" />
      <path d="M5 19v-2a4 4 0 0 1 4-4h6a4 4 0 0 1 4 4v2" />
      <path d="M12 12v7" />
    </svg>
  );
}

function IconSkills() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 2L4 7v10l8 5 8-5V7l-8-5z" />
      <path d="M12 12v7" />
      <path d="M8 10.5v4.5M16 10.5v4.5" />
    </svg>
  );
}

// ARCH-AGENT-DIRECTORY-001 (2026-07-20): distinct from the four icons above,
// which all mark hash-anchor sections on this single scrolling page --
// Directory is a real Next.js route (app/directory/page.tsx), a full
// separate screen with its own internal navigation state, not a section of
// the console page. Given its own icon and its own NAV_GROUPS entry below,
// deliberately excluded from ALL_SECTION_IDS's hash-only scroll-spy list.
function IconDirectory() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <rect x="4" y="4" width="7" height="7" rx="1.5" />
      <rect x="13" y="4" width="7" height="7" rx="1.5" />
      <rect x="4" y="13" width="7" height="7" rx="1.5" />
      <rect x="13" y="13" width="7" height="7" rx="1.5" />
      <circle cx="16.5" cy="16.5" r="2.25" fill="currentColor" stroke="none" />
    </svg>
  );
}

function IconChevron() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M15 6l-6 6 6 6" />
    </svg>
  );
}

// FIX (2026-07-20): NAV_GROUPS previously had `as const`, which gives each
// group's `items` array its own distinct literal-tuple type -- fine for the
// render map below, but NAV_GROUPS.flatMap((g) => g.items) then has to
// unify three structurally-different tuple types into one, and TS's
// flatMap inference can't do that (TS2322/TS18046 on build, confirmed via
// `npx tsc --noEmit`). An explicit NavItem/NavGroup type sidesteps the
// union-of-tuples problem entirely -- items is just `readonly NavItem[]`
// on every group, so flatMap has one concrete element type to infer.
type NavItem = { href: string; label: string; icon: () => React.JSX.Element };
type NavGroup = { label: string; items: readonly NavItem[] };

const NAV_GROUPS: readonly NavGroup[] = [
  {
    label: "Create",
    // ARCH-OMODE-MERGE-001 (2026-08-22): Harness's own nav entry/route
    // (/harness) is retired -- it's now the "Read-Only" O-Mode selected
    // via the pill toggle on this same Console screen (?mode=readonly),
    // not a second page. See ConsoleView.tsx's OModeToggle.
    items: [
      { href: "/", label: "Console", icon: IconConsole },
    ],
  },
  {
    label: "Explore",
    items: [
      { href: "/skills", label: "Skills", icon: IconSkills },
      { href: "/trails", label: "Trails", icon: IconTrails },
      { href: "/platform", label: "Platform", icon: IconConsole },
    ],
  },
  {
    label: "Colony",
    items: [
      { href: "/directory", label: "Directory", icon: IconDirectory },
    ],
  },
];

// Only hash anchors participate in scroll-spy -- "/directory" is a real
// route with no matching element id on this page, so it's excluded here
// rather than passed to IntersectionObserver.observe(null).
const ALL_SECTION_IDS = NAV_GROUPS.flatMap((g) => g.items)
  .map((i) => i.href)
  .filter((href): href is `#${string}` => href.startsWith("#"))
  .map((href) => href.slice(1));

const SIDEBAR_COLLAPSE_KEY = "pmcro.sidebar.collapsed";

export default function Sidebar() {
  // ARCH-FRONTEND-SIDEBAR-004 (2026-07-15): persisted rather than plain
  // useState(false) -- collapse state resetting on every reload/hot-refresh
  // was the actual complaint (had to keep re-collapsing it). localStorage is
  // fine here: this is the real Next.js app running in the user's own
  // browser, not the sandboxed Claude-artifact environment where
  // localStorage is unavailable.
  const [collapsed, setCollapsed] = useState(false);
  useEffect(() => {
    const stored = window.localStorage.getItem(SIDEBAR_COLLAPSE_KEY);
    if (stored === "true") setCollapsed(true);
  }, []);
  useEffect(() => {
    window.localStorage.setItem(SIDEBAR_COLLAPSE_KEY, String(collapsed));
  }, [collapsed]);

  const pathname = usePathname();
  const [activeHref, setActiveHref] = useState(pathname || "/");

  useEffect(() => {
    setActiveHref(pathname || "/");
  }, [pathname]);

  const { agent } = useAgent({ agentId: "Orchestrator" });
  const state = agent.state as PmcroCycleState | undefined;
  const isLive = Boolean(state?.phase);

  return (
    <aside className="sidebar" data-collapsed={collapsed}>
      <div className="sidebar-brand">
        <span className="sidebar-mark" aria-hidden="true">
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 2.5l2.4 6.6L21 11.5l-6.6 2.4L12 20.5l-2.4-6.6L3 11.5l6.6-2.4L12 2.5z" />
          </svg>
        </span>
        <span className="sidebar-brand-text sidebar-label-text">
          PMCR-O
          <br />
          AI AGENT COMPANY
        </span>
        <button
          type="button"
          className="sidebar-toggle"
          aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          aria-expanded={!collapsed}
          onClick={() => setCollapsed((c) => !c)}
        >
          <IconChevron />
        </button>
      </div>

      {NAV_GROUPS.map((group) => (
        <nav className="sidebar-nav" key={group.label} aria-label={group.label}>
          <div className="sidebar-section-label sidebar-label-text">{group.label}</div>
          {group.items.map(({ href, label, icon: Icon }) => (
            <Link
              key={href}
              className="sidebar-link"
              aria-current={activeHref === href ? "page" : undefined}
              data-active={activeHref === href}
              href={href}
              title={collapsed ? label : undefined}
            >
              <Icon />
              <span className="sidebar-label-text">{label}</span>
            </Link>
          ))}
        </nav>
      ))}

      <div className="sidebar-spacer" />

      <div className="sidebar-divider" />
      <div className="sidebar-status" title={isLive ? "Live AG-UI connection" : "No cycle running yet"}>
        <span className="status-dot" data-live={isLive} aria-hidden="true" />
        <span className="sidebar-label-text sidebar-status-text">
          {isLive ? (
            <>
              Cycle <strong>{state?.cycle}</strong> · {state?.phase}
            </>
          ) : (
            "No cycle running yet"
          )}
        </span>
      </div>
    </aside>
  );
}
