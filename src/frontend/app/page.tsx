// src/frontend/app/page.tsx
//
// ARCH-CONSOLE-TRAILPLAYER-001 (2026-07-20): converted from a "use client"
// component to a Server Component, mirroring app/directory/page.tsx's
// pattern -- reads real trails once per request via loadTrailsByDomain()
// (server-only, node:fs) and hands them down as a plain prop to the
// client-side ConsoleView, which owns all the interactive state (hero
// prompt, domain tag, live PhaseRail, etc.). This is what lets the Trail
// player section at the bottom of the Console show real cycle data instead
// of the old hardcoded `trail={null}`.
import { loadTrailsByDomain } from "./lib/trails";
import { loadSkillCatalog } from "./lib/skills";
import ConsoleView from "./components/ConsoleView";

// Same reasoning as app/directory/page.tsx: trail data changes whenever a
// cycle seals, and this is a single-operator console, not a public page
// where per-request fs reads would be a scaling concern.
export const dynamic = "force-dynamic";

export default async function Home() {
  const [trailsByDomain, skills] = await Promise.all([
    loadTrailsByDomain(),
    loadSkillCatalog(),
  ]);
  return <ConsoleView trailsByDomain={trailsByDomain} skills={skills} />;
}
