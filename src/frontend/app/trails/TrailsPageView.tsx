"use client";

import { useMemo, useState } from "react";
import TrailView, { type Trail } from "../components/TrailView";

export default function TrailsPageView({ trailsByDomain }: { trailsByDomain: Record<string, Trail[]> }) {
  const [selected, setSelected] = useState<Trail | null>(null);
  const trails = useMemo(() => Object.values(trailsByDomain).flat().sort((a, b) => (b.createdAt ?? "").localeCompare(a.createdAt ?? "")), [trailsByDomain]);

  return (
    <main className="product-page" aria-labelledby="trails-title">
      <header className="product-page-header">
        <p className="workspace-section-kicker">Evidence · PMCR-O Trails</p>
        <h1 id="trails-title">Trail history</h1>
        <p>Review sealed and in-progress cycles, their dispositions, and the evidence produced by each role.</p>
      </header>
      {selected ? (
        <div><button type="button" className="directory-back" onClick={() => setSelected(null)}>← All trails</button><TrailView trail={selected} /></div>
      ) : (
        <div className="trail-index-list">{trails.length === 0 ? <p className="phase-rail-idle">No trails on record yet.</p> : trails.map((trail) => (
          <button type="button" className="trail-index-card" key={trail.id} onClick={() => setSelected(trail)}>
            <span className="trail-view-domain">{trail.domain}</span><strong>{trail.trueIntent}</strong><small>{trail.id.slice(0, 8)} · {trail.createdAt ?? "undated"}</small>
          </button>
        ))}</div>
      )}
    </main>
  );
}
