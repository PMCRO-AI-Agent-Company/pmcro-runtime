"use client";

import { useState } from "react";

export type TrailRoleEntry = {
  seq: number | string;
  content: string;
  result?: "pass" | "fail" | "n/a" | "note" | "pass-with-finding";
};

export type TrailCycle = {
  number: string;
  plan: TrailRoleEntry[];
  make: TrailRoleEntry[];
  check: TrailRoleEntry[];
  reflect: TrailRoleEntry[];
};

export type TrailDisposition =
  | "Accept" | "Retry" | "Halt"
  | "accept" | "needs-approval" | "reject" | "needs-revision"
  | null;

export type Trail = {
  id: string;
  domain: string;
  trueIntent: string;
  requestedBy?: string;
  createdAt?: string;
  disposition: TrailDisposition;
  reason?: string;
  cycles: TrailCycle[];
};
export function dispositionTone(d: TrailDisposition): "pass" | "retry" | "halt" | null {
  switch (d) {
    case "Accept": case "accept": return "pass";
    case "Retry": case "needs-approval": case "needs-revision": return "retry";
    case "Halt": case "reject": return "halt";
    default: return null;
  }
}

export function dispositionLabel(d: TrailDisposition): string {
  if (!d) return "No disposition";
  return d.split("-").map((word) => word[0].toUpperCase() + word.slice(1)).join(" ");
}

const ROLE_TABS = ["plan", "make", "check", "reflect"] as const;
type RoleTab = (typeof ROLE_TABS)[number];

function ResultTag({ result }: { result?: TrailRoleEntry["result"] }) {
  if (!result) return null;
  const tone = result === "pass" ? "pass" : result === "fail" ? "halt" : result === "note" || result === "pass-with-finding" ? "retry" : "neutral";
  return <span className="trail-entry-result" data-tone={tone}>{result}</span>;
}

function TrailEntryRow({ entry }: { entry: TrailRoleEntry }) {
  return <div className="trail-entry-row"><span className="trail-entry-seq">{entry.seq}</span><p className="trail-entry-content">{entry.content}</p><ResultTag result={entry.result} /></div>;
}
export default function TrailView({ trail }: { trail: Trail | null }) {
  const [activeCycle, setActiveCycle] = useState(0);
  const [activeRole, setActiveRole] = useState<RoleTab>("plan");

  if (!trail) {
    return <div className="trail-view trail-view--empty"><p className="phase-rail-idle">No trail selected yet.</p></div>;
  }

  const tone = dispositionTone(trail.disposition);
  const cycle = trail.cycles[activeCycle];
  const entries: Record<RoleTab, TrailRoleEntry[]> = {
    plan: cycle?.plan ?? [],
    make: cycle?.make ?? [],
    check: cycle?.check ?? [],
    reflect: cycle?.reflect ?? [],
  };

  return (
    <div className="trail-view">
      <div className="trail-view-header">
        <div className="trail-view-header-line">
          <span className="trail-view-domain">{trail.domain}</span>
          <span className="trail-view-id">{trail.id.slice(0, 8)}…</span>
          <span className="phase-rail-badge" data-tone={tone ?? "neutral"}>{dispositionLabel(trail.disposition)}</span>
        </div>
        <p className="trail-view-intent">{trail.trueIntent || "Untitled request"}</p>
        {(trail.requestedBy || trail.createdAt) && <div className="trail-view-header-line trail-view-header-line--muted"><span>{trail.requestedBy}</span><span>{trail.createdAt}</span></div>}
      </div>
      {trail.reason && <div className="trail-view-reason"><p className="trail-view-reason-label">Disposition reason</p><p className="trail-view-reason-text">{trail.reason}</p></div>}
      <div className="trail-view-tabs">
        <div className="trail-view-cycle-tabs">{trail.cycles.map((cycleItem, index) => <button key={cycleItem.number} type="button" className="trail-view-tab" role="tab" aria-selected={activeCycle === index} data-active={activeCycle === index} onClick={() => setActiveCycle(index)}>Cycle {cycleItem.number}</button>)}</div>
        <div className="trail-view-role-tabs">{ROLE_TABS.map((role) => <button key={role} type="button" className="trail-view-tab trail-view-tab--role" role="tab" aria-selected={activeRole === role} data-active={activeRole === role} onClick={() => setActiveRole(role)}>{role}</button>)}</div>
      </div>
      <div className="trail-view-entries">
        {entries[activeRole].length === 0 ? <p className="phase-rail-idle">No {activeRole} entries for this cycle.</p> : entries[activeRole].map((entry, index) => <TrailEntryRow key={`${entry.seq}-${index}`} entry={entry} />)}
      </div>
    </div>
  );
}
