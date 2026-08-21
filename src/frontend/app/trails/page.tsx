import { loadTrailsByDomain } from "../lib/trails";
import TrailsPageView from "./TrailsPageView";

export const dynamic = "force-dynamic";
export const metadata = { title: "Trails · PMCR-O" };

export default async function TrailsPage() {
  return <TrailsPageView trailsByDomain={await loadTrailsByDomain()} />;
}
