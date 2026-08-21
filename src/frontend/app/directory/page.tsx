// src/frontend/app/directory/page.tsx
//
// ARCH-AGENT-DIRECTORY-001/002 (2026-07-20): the real "All Agents" screen,
// built from PMCRO_UI's mockup Directory view but wired to real data --
// DomainSelector.tsx's DOMAINS roster (mirrors catalog/skills.json) and
// real sealed trails read from disk via lib/trails.ts, not PMCRO_UI's
// hand-authored agents/ceoTrails/ctoTrails mock arrays.
//
// Server Component: reads the filesystem once per request via
// loadTrailsByDomain() (server-only, node:fs) and passes plain data down to
// the "use client" AgentDirectory for interactivity. No API route needed --
// this is a real Next.js App Router Server Component doing a direct server-
// side read, not a client fetch.
import { DOMAINS } from "../components/DomainSelector";
import { loadTrailsByDomain } from "../lib/trails";
import AgentDirectory from "../components/AgentDirectory";

export const metadata = {
  title: "Directory · PMCR-O AI Agent Company",
};

// Revalidate on every request rather than caching statically -- trail data
// changes whenever a cycle seals, and this is a single-operator console,
// not a public page where request-time fs reads would be a scaling concern.
export const dynamic = "force-dynamic";

export default async function DirectoryPage() {
  const trailsByDomain = await loadTrailsByDomain();
  return <AgentDirectory domains={DOMAINS} trailsByDomain={trailsByDomain} />;
}
