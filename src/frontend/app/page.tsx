// PMCRO LLM-native home workspace.
// The global AppHost/CopilotKit shell remains in layout.tsx; this route is
// deliberately conversation-first and uses the Orchestrator as its primary
// interaction surface. Trail data is still loaded server-side so the UI can
// surface recent execution evidence without inventing client state.
import { loadTrailsByDomain } from "./lib/trails";
import LlmHome from "./components/LlmHome";

export const dynamic = "force-dynamic";

export default async function Home() {
  const trailsByDomain = await loadTrailsByDomain();
  return <LlmHome trailsByDomain={trailsByDomain} />;
}
