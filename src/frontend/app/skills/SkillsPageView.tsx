"use client";

import { useState } from "react";
import SkillSelector from "../components/SkillSelector";
import type { SkillSummary } from "../lib/skills";

export default function SkillsPageView({ skills }: { skills: SkillSummary[] }) {
  const [selected, setSelected] = useState<string[]>([]);

  return (
    <main className="product-page" aria-labelledby="skills-title">
      <header className="product-page-header">
        <p className="workspace-section-kicker">Catalog · MAF Agent Skills</p>
        <h1 id="skills-title">Skills library</h1>
        <p>Browse the canonical marketplace catalog. Select skills to carry into your next governed run.</p>
      </header>
      <div className="product-page-meta"><strong>{skills.length}</strong> unique skills · native MAF staging remains the execution source</div>
      <SkillSelector skills={skills} value={selected} onChange={setSelected} />
    </main>
  );
}
