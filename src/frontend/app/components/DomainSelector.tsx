// src/frontend/app/components/DomainSelector.tsx
//
// ARCH-DOMAIN-SELECT-001 (2026-07-20): lets a request be tagged with one of
// the AI Agent Company's 10 C-Suite domains (roster mirrors
// W:\pmcro-ai-company\catalog\skills.json exactly -- same 10 names, in the
// same order). This does NOT mean 10 separate backend AIAgents exist --
// per COLONY.md's own framing, a C-Suite "seat" is a documentation-only
// domain scope (a SKILL.md), not an executable identity. What's real today:
// FileTrailWriter.cs names a trail's directory from whatever string reaches
// it as subjectAgentName, independent of whether SubjectAgentRegistry can
// actually resolve that string to a live AIAgent (unresolvable names fall
// back to filesystem-agent for execution -- see Program.cs). So picking a
// domain here and sending a request already produces a real, correctly-
// tagged trail on disk (`.pmcro/trails/<domain>/<uuid>/`) even before any
// domain-specific skill-loading behavior is wired into the Orchestrator's
// tool call. That deeper wiring (making "cfo" actually load
// catalog/Tools/AI-Company/skills/cfo/SKILL.md into context instead of
// silently running as filesystem-agent) is a separate, later step -- a
// configuration/instruction change, not a UI blocker.
"use client";

import type { LoopRole } from "./AgentCard";

export type DomainCategory = "C-Suite" | "Staff" | "Domain";

export type Domain = {
  id: string;
  abbr: string;
  label: string;
  color: string;
  /** ARCH-AGENT-DIRECTORY-001 (2026-07-20): mirrors catalog/skills.json's
   * own `category` field 1:1 ("Executive" -> "C-Suite", "Staff" -> "Staff",
   * "Domain" -> "Domain") -- used for the Agent Directory's filter tabs. */
  category: DomainCategory;
  /** ARCH-AGENT-DIRECTORY-001: condensed from each domain's real SKILL.md
   * "USE FOR:" clause in catalog/skills.json -- not invented copy. */
  description: string;
  /** ARCH-AGENT-DIRECTORY-001: topic phrases lifted from the same real
   * "USE FOR:" clause (comma-separated scope items), not a fictional
   * skills/tasks mock -- there is no separate "skill pack" field in
   * catalog/skills.json today, so these are a direct, literal parse of the
   * one real description string each domain already has. */
  tags: string[];
  /** ARCH-DIRECTORY-VISUAL-001 (2026-07-20): each domain's real, stated
   * "Primary Loop Emphasis" -- grep'd directly from every domain's own
   * SKILL.md ("## Primary Loop Emphasis" heading), not guessed from the
   * abbreviation. This is a static, documented fact about the domain ("CTO
   * cycles tend to be Maker-shaped"), independent of whether any cycle is
   * currently running -- distinct from AgentCard's optional live
   * `loopState` override, which reflects an in-progress cycle when one
   * exists. */
  loop: LoopRole;
};

// Order and all fields below mirror catalog/skills.json exactly (10 domain
// entries, category/description condensed from each skill's real
// description field -- see catalog/skills.json for the verbatim source).
export const DOMAINS: Domain[] = [
  {
    id: "ceo", abbr: "CEO", label: "Chief Executive Officer", color: "#4A9EFF", category: "C-Suite",
    description: "Strategic direction, OKR management, compute/priority allocation, and approval of major cross-agent actions.",
    tags: ["Strategic Planning", "OKR Management", "Compute Allocation", "Cross-Agent Approval"],
    loop: "Orchestrator",
  },
  {
    id: "chief-of-staff", abbr: "CoS", label: "Chief of Staff", color: "#2DD4BF", category: "Staff",
    description: "Priority triage of the CEO's decision queue, cross-agent coordination, weekly brief writing, and filtering agent output before CEO review.",
    tags: ["Priority Triage", "Cross-Agent Coordination", "Brief Writing", "Output Filtering"],
    loop: "Planner",
  },
  {
    id: "cto", abbr: "CTO", label: "Chief Technology Officer", color: "#A78BFA", category: "C-Suite",
    description: "Technical architecture, PMCR-O loop/skill-pack design and validation, security posture, DevOps pipelines, and incident response.",
    tags: ["Architecture", "Skill-Pack Validation", "Security", "DevOps", "Incident Response"],
    loop: "Maker",
  },
  {
    id: "coo", abbr: "COO", label: "Chief Operating Officer", color: "#60A5FA", category: "C-Suite",
    description: "SOP creation, workflow automation, vendor management, compliance enforcement, resource allocation, and KPI dashboards.",
    tags: ["SOP Creation", "Workflow Automation", "Vendor Management", "Compliance", "KPI Dashboards"],
    loop: "Checker",
  },
  {
    id: "cfo", abbr: "CFO", label: "Chief Financial Officer", color: "#34D399", category: "C-Suite",
    description: "Budgeting, cash flow analysis, forecasting, cost optimization, financial reporting, and investor updates.",
    tags: ["Budgeting", "Forecasting", "Cost Optimization", "Financial Reporting"],
    loop: "Reflector",
  },
  {
    id: "cro", abbr: "CRO", label: "Chief Revenue Officer", color: "#F472B6", category: "C-Suite",
    description: "Lead generation, CRM automation, outreach, pipeline management, proposal writing, and deal closing.",
    tags: ["Lead Generation", "CRM Automation", "Pipeline Management", "Deal Closing"],
    loop: "Maker",
  },
  {
    id: "cmo", abbr: "CMO", label: "Chief Marketing Officer", color: "#FB923C", category: "C-Suite",
    description: "Content creation, social media, campaign management, SEO, and brand voice consistency.",
    tags: ["Content Creation", "Social Media", "Campaign Management", "SEO"],
    loop: "Maker",
  },
  {
    id: "clo", abbr: "CLO", label: "Chief Legal Officer", color: "#F59E0B", category: "C-Suite",
    description: "Contract review, policy enforcement, risk analysis, regulatory compliance, privacy reviews, and IP protection.",
    tags: ["Contract Review", "Risk Analysis", "Compliance", "IP Protection"],
    loop: "Checker",
  },
  {
    id: "chro", abbr: "CHRO", label: "Chief Human Resources Officer", color: "#22D3EE", category: "C-Suite",
    description: "Hiring pipelines, workforce planning, onboarding, performance reviews, culture documentation, and training design.",
    tags: ["Hiring Pipelines", "Workforce Planning", "Onboarding", "Training Design"],
    loop: "Planner",
  },
  {
    id: "domain-specialist", abbr: "K&S", label: "Knowledge & Synthesis", color: "#E879F9", category: "Domain",
    description: "Distilling raw chat/session exports into structured knowledge, extracting recurring patterns, and hydrating Colony memory from session history.",
    tags: ["Knowledge Distillation", "Pattern Extraction", "Memory Hydration"],
    loop: "Maker",
  },
];

export default function DomainSelector({
  value,
  onChange,
}: {
  value: string | null;
  onChange: (id: string | null) => void;
}) {
  return (
    <div className="context-control">
      <label htmlFor="domain-context">Routing context</label>
      <select
        id="domain-context"
        value={value ?? ""}
        onChange={(event) => onChange(event.target.value || null)}
      >
        <option value="">Untagged · default filesystem agent</option>
        {DOMAINS.map((domain) => (
          <option key={domain.id} value={domain.id}>
            {domain.label} · {domain.loop} emphasis
          </option>
        ))}
      </select>
    </div>
  );
}
