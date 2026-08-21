"use client";

import { useMemo, useState } from "react";
import type { SkillSummary } from "../lib/skills";

type SkillGroup = { plugin: string; skills: SkillSummary[] };

export default function SkillSelector({ skills, value, onChange }: {
  skills: SkillSummary[];
  value: string[];
  onChange: (ids: string[]) => void;
}) {
  const [query, setQuery] = useState("");
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const groups = useMemo<SkillGroup[]>(() => {
    const needle = query.trim().toLowerCase();
    const grouped = new Map<string, SkillSummary[]>();
    for (const skill of skills) {
      if (needle && !`${skill.name} ${skill.plugin} ${skill.description}`.toLowerCase().includes(needle)) continue;
      grouped.set(skill.plugin, [...(grouped.get(skill.plugin) ?? []), skill]);
    }
    return [...grouped.entries()]
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([plugin, groupedSkills]) => ({
        plugin,
        skills: groupedSkills.sort((left, right) => left.name.localeCompare(right.name)),
      }));
  }, [query, skills]);

  const toggle = (id: string) => onChange(value.includes(id) ? value.filter((item) => item !== id) : [...value, id]);
  const toggleGroup = (plugin: string) => setExpanded((current) => ({ ...current, [plugin]: !current[plugin] }));

  return (
    <fieldset className="skill-selector">
      <legend className="skill-selector-legend"><span className="legend-icon">✦</span> Skill context <span className="legend-subtitle">optional expertise for this task</span></legend>
      <div className="skill-selector-toolbar">
        <input className="skill-selector-search" aria-label="Search plugin skills" placeholder="Search the MAF skill catalog…" value={query} onChange={(event) => setQuery(event.target.value)} />
        <span className="skill-selector-count">{value.length} active · {skills.length} available</span>
        {value.length > 0 && <button type="button" className="skill-selector-clear" onClick={() => onChange([])}>Clear</button>}
      </div>
      <p className="skill-selector-note">Choose optional context. MAF resolves dependencies from the marketplace and stages skills under <code>.pmcro/skills-staging</code>.</p>
      {value.length > 0 && <div className="skill-selection-summary" aria-label="Selected skills">{value.map((id) => <span key={id} className="skill-selection-chip">{id}</span>)}</div>}
      <div className="skill-selector-groups" aria-live="polite">
        {groups.map((group) => {
          const isOpen = Boolean(query) || expanded[group.plugin];
          const selectedCount = group.skills.filter((skill) => value.includes(skill.id)).length;
          return (
            <section className="skill-group" key={group.plugin}>
              <button type="button" className="skill-group-toggle" aria-expanded={isOpen} onClick={() => toggleGroup(group.plugin)}>
                <span><strong>{group.plugin}</strong><small>{group.skills.length} skills{selectedCount ? ` · ${selectedCount} selected` : ""}</small></span>
                <span aria-hidden="true">{isOpen ? "−" : "+"}</span>
              </button>
              {isOpen && <div className="skill-selector-list">
                {group.skills.map((skill) => <label className="skill-option" key={`${group.plugin}:${skill.id}`} data-selected={value.includes(skill.id)}>
                  <input type="checkbox" checked={value.includes(skill.id)} onChange={() => toggle(skill.id)} />
                  <span className="skill-option-copy"><strong>{skill.name}</strong><small>{skill.description}</small></span>
                </label>)}
              </div>}
            </section>
          );
        })}
        {groups.length === 0 && <p className="skill-selector-empty">No marketplace skills match this search.</p>}
      </div>
    </fieldset>
  );
}
