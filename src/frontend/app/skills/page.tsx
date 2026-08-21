import { loadSkillCatalog } from "../lib/skills";
import SkillsPageView from "./SkillsPageView";

export const dynamic = "force-dynamic";
export const metadata = { title: "Skills · PMCR-O" };

export default async function SkillsPage() {
  return <SkillsPageView skills={await loadSkillCatalog()} />;
}
