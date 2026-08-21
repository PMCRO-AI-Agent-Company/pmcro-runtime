// src/frontend/app/components/AgentDirectory.tsx
//
// ARCH-AGENT-DIRECTORY-001/002 (2026-07-20): the real "All Agents" screen --
// built from PMCRO_UI's mockup Directory/DetailView/TrailsPanel, but wired
// to real data end to end:
//   - Cards come from DomainSelector.tsx's DOMAINS (mirrors
//     catalog/skills.json), via the already-ported AgentCard.tsx -- not
//     PMCRO_UI's fictional agents[] array (skills/tasks/outputs/PMCR-O role
//     descriptions it hand-authored per agent).
//   - Trails come from real sealed (or in-progress) trail data read off disk
//     by lib/trails.ts and passed down from the Server Component at
//     app/directory/page.tsx -- not PMCRO_UI's hand-transcribed
//     ceoTrails/ctoTrails arrays.
//   - Disposition badges use TrailView.tsx's real dispositionTone/
//     dispositionLabel, which understand both real trail-producer schemas
//     (see ARCH-VISUAL-BRIDGE-002 in that file) -- not PMCRO_UI's own
//     4-value color set, which existed independently of this file.
//
// Deliberately NOT ported from PMCRO_UI's App.tsx: the Run Console (RunConsole,
// LoopViz, CycleCard, makeCycle()) and the Memory Bank view. Both were
// PMCRO_UI's own simulated/fabricated state (a setInterval fake-progress
// loop, invented memoryBank content) with no real backend to drive them --
// building a fake "Run Agent" button here would contradict this repo's own
// EC-VERIFY-FIRST-001. Wiring a real run trigger is a separate, later step
// once there's a real endpoint to call.
"use client";

import { useMemo, useState } from "react";
import type { Domain, DomainCategory } from "./DomainSelector";
import AgentCard, { LOOP_ROLE_COLOR, type AgentCardData } from "./AgentCard";
import TrailView, { dispositionLabel, dispositionTone, type Trail } from "./TrailView";

type NavFilter = "all" | DomainCategory;

const NAV_ITEMS: { id: NavFilter; label: string }[] = [
  { id: "all", label: "All Agents" },
  { id: "C-Suite", label: "C-Suite" },
  { id: "Staff", label: "Staff" },
  { id: "Domain", label: "Domain" },
];

function IconSearch() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="11" cy="11" r="7" />
      <path d="M21 21l-4.3-4.3" />
    </svg>
  );
}

function IconArrowLeft() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <path d="M19 12H5M11 18l-6-6 6-6" />
    </svg>
  );
}

function IconChevronRight() {
  return (
    <svg viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path d="M6 3.5L10.5 8L6 12.5" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

function TrailSummaryRow({ trail, onOpen }: { trail: Trail; onOpen: () => void }) {
  const tone = dispositionTone(trail.disposition);
  return (
    <button type="button" className="trail-summary-row" onClick={onOpen}>
      <div className="trail-summary-row-head">
        {tone && (
          <span className="phase-rail-badge" data-tone={tone}>
            {dispositionLabel(trail.disposition as NonNullable<Trail["disposition"]>)}
          </span>
        )}
        <span className="trail-summary-id">{trail.id.slice(0, 8)}</span>
        {trail.requestedBy && <span className="trail-summary-muted">{trail.requestedBy}</span>}
        {trail.createdAt && <span className="trail-summary-muted">{trail.createdAt}</span>}
        <span className="trail-summary-chevron" aria-hidden="true">
          <IconChevronRight />
        </span>
      </div>
      <p className="trail-summary-intent">{trail.trueIntent}</p>
    </button>
  );
}

function DomainDetail({
  domain,
  trails,
  onBack,
  onOpenTrail,
}: {
  domain: Domain;
  trails: Trail[];
  onBack: () => void;
  onOpenTrail: (t: Trail) => void;
}) {
  return (
    <div className="directory-detail">
      <button type="button" className="directory-back" onClick={onBack}>
        <IconArrowLeft /> Directory
      </button>

      <div className="directory-detail-head">
        <span className="agent-card-badge" style={{ background: domain.color + "20", color: domain.color }}>
          {domain.abbr}
        </span>
        <div>
          <h2 className="directory-detail-title">{domain.label}</h2>
          <p className="directory-detail-category">{domain.category}</p>
        </div>
      </div>

      {/* ARCH-DIRECTORY-VISUAL-001 (2026-07-20): 4-up stats strip, styled
          after the reference screenshots' Manages/Loop/Tasks/Status bar --
          but every value here traces to a real field (Domain.loop from each
          SKILL.md's own Primary Loop Emphasis, trails.length, the latest
          trail's real disposition) rather than an invented headcount or a
          fabricated Active/Idle/Busy status. */}
      <div className="directory-detail-stats">
        <div className="directory-detail-stat">
          <span className="directory-detail-stat-value" style={{ color: LOOP_ROLE_COLOR[domain.loop] }}>
            {domain.loop}
          </span>
          <span className="directory-detail-stat-label">Primary loop</span>
        </div>
        <div className="directory-detail-stat">
          <span className="directory-detail-stat-value">{trails.length}</span>
          <span className="directory-detail-stat-label">Trail{trails.length === 1 ? "" : "s"}</span>
        </div>
        <div className="directory-detail-stat">
          <span className="directory-detail-stat-value">{domain.category}</span>
          <span className="directory-detail-stat-label">Tier</span>
        </div>
        <div className="directory-detail-stat">
          {trails[0] ? (
            <span
              className="directory-detail-stat-value"
              style={{
                color:
                  dispositionTone(trails[0].disposition) === "pass"
                    ? "var(--colony-pass)"
                    : dispositionTone(trails[0].disposition) === "retry"
                      ? "var(--colony-retry)"
                      : dispositionTone(trails[0].disposition) === "halt"
                        ? "var(--colony-halt)"
                        : "var(--colony-muted)",
              }}
            >
              {dispositionLabel(trails[0].disposition as NonNullable<Trail["disposition"]>)}
            </span>
          ) : (
            <span className="directory-detail-stat-value" style={{ color: "var(--colony-muted)" }}>
              —
            </span>
          )}
          <span className="directory-detail-stat-label">Latest disposition</span>
        </div>
      </div>

      <p className="directory-detail-description">{domain.description}</p>

      {domain.tags.length > 0 && (
        <div className="agent-card-tags" style={{ marginTop: 4 }}>
          {domain.tags.map((t) => (
            <span className="agent-card-tag" key={t}>{t}</span>
          ))}
        </div>
      )}

      <div className="directory-detail-trails">
        <h3 className="colony-section-title" style={{ marginTop: 32 }}>
          Trails
          {trails.length > 0 && <span className="directory-trail-count-pill">{trails.length}</span>}
        </h3>
        {trails.length === 0 ? (
          <p className="phase-rail-idle">No trails on record for this domain yet.</p>
        ) : (
          <div className="trail-summary-list">
            {trails.map((t) => (
              <TrailSummaryRow key={t.id} trail={t} onOpen={() => onOpenTrail(t)} />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export default function AgentDirectory({
  domains,
  trailsByDomain,
}: {
  domains: Domain[];
  trailsByDomain: Record<string, Trail[]>;
}) {
  const [navFilter, setNavFilter] = useState<NavFilter>("all");
  const [search, setSearch] = useState("");
  const [selectedDomainId, setSelectedDomainId] = useState<string | null>(null);
  const [selectedTrail, setSelectedTrail] = useState<Trail | null>(null);

  const counts = useMemo(() => {
    const c: Record<NavFilter, number> = { all: domains.length, "C-Suite": 0, Staff: 0, Domain: 0 };
    for (const d of domains) c[d.category] += 1;
    return c;
  }, [domains]);

  const totalTrails = useMemo(
    () => Object.values(trailsByDomain).reduce((n, t) => n + t.length, 0),
    [trailsByDomain],
  );

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return domains.filter((d) => {
      const matchNav = navFilter === "all" || d.category === navFilter;
      const matchSearch =
        !q ||
        d.label.toLowerCase().includes(q) ||
        d.abbr.toLowerCase().includes(q) ||
        d.description.toLowerCase().includes(q);
      return matchNav && matchSearch;
    });
  }, [domains, navFilter, search]);

  const selectedDomain = selectedDomainId ? domains.find((d) => d.id === selectedDomainId) ?? null : null;

  // Trail view takes priority over the detail panel it was opened from.
  if (selectedTrail) {
    return (
      <div className="directory-shell">
        <button type="button" className="directory-back" onClick={() => setSelectedTrail(null)}>
          <IconArrowLeft /> {selectedDomain ? selectedDomain.label : selectedTrail.domain} · Trails
        </button>
        <TrailView trail={selectedTrail} />
      </div>
    );
  }

  if (selectedDomain) {
    return (
      <div className="directory-shell">
        <DomainDetail
          domain={selectedDomain}
          trails={trailsByDomain[selectedDomain.id] ?? []}
          onBack={() => setSelectedDomainId(null)}
          onOpenTrail={setSelectedTrail}
        />
      </div>
    );
  }

  return (
    <div className="directory-shell">
      <div className="directory-toolbar">
        <div className="directory-search">
          <span className="directory-search-icon" aria-hidden="true">
            <IconSearch />
          </span>
          <input
            className="directory-search-input"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search agents…"
          />
        </div>
        <span className="directory-trail-total">{totalTrails} trail{totalTrails === 1 ? "" : "s"} on record</span>
      </div>

      <div className="directory-nav">
        {NAV_ITEMS.map(({ id, label }) => (
          <button
            key={id}
            type="button"
            className="directory-nav-tab"
            role="tab"
            aria-selected={navFilter === id}
            data-active={navFilter === id}
            onClick={() => setNavFilter(id)}
          >
            {label}
            <span className="directory-nav-count">{counts[id]}</span>
          </button>
        ))}
      </div>

      {filtered.length === 0 ? (
        <p className="phase-rail-idle" style={{ marginTop: 24 }}>No agents match &ldquo;{search}&rdquo;.</p>
      ) : (
        <div className="colony-grid" style={{ marginTop: 20 }}>
          {filtered.map((d) => {
            const trails = trailsByDomain[d.id] ?? [];
            const card: AgentCardData = {
              id: d.id,
              abbr: d.abbr,
              label: d.label,
              color: d.color,
              description: d.description,
              tags: d.tags,
              trailCount: trails.length,
              loopState: d.loop,
              statusTone: trails[0] ? dispositionTone(trails[0].disposition) : null,
            };
            return <AgentCard key={d.id} agent={card} onClick={() => setSelectedDomainId(d.id)} />;
          })}
        </div>
      )}
    </div>
  );
}
