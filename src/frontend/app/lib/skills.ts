// Server-only reader for the MAF marketplace registry.
import { readdir, readFile, stat } from "node:fs/promises";
import path from "node:path";

export type SkillSummary = {
  id: string;
  name: string;
  description: string;
  plugin: string;
  category: string;
};

type MarketplacePlugin = { name: string; source: string };

const REPO_ROOT = path.resolve(process.cwd(), "..", "..");
const MARKETPLACE_PATH = path.join(REPO_ROOT, ".agents", "plugins", "marketplace.json");

function frontmatterValue(source: string, key: string): string {
  const match = source.match(new RegExp(`^${key}:\\s*["']?([^"'\\n]+)`, "m"));
  return match?.[1]?.trim() ?? "";
}

async function readSkill(plugin: MarketplacePlugin, pluginRoot: string, folderName: string): Promise<SkillSummary | null> {
  try {
    const source = await readFile(path.join(pluginRoot, "skills", folderName, "SKILL.md"), "utf8");
    const name = frontmatterValue(source, "name") || folderName;
    return {
      id: name,
      name,
      description: frontmatterValue(source, "description").replace(/^USE FOR:\s*/i, "").replace(/\s+/g, " ") || "Repository skill",
      plugin: plugin.name,
      category: plugin.name.replace(/^pmcro-/, "").replace(/-/g, " "),
    };
  } catch {
    return null;
  }
}
export async function loadSkillCatalog(): Promise<SkillSummary[]> {
  try {
    const marketplace = JSON.parse(await readFile(MARKETPLACE_PATH, "utf8")) as { plugins?: MarketplacePlugin[] };
    const skillsByName = new Map<string, SkillSummary>();
    for (const plugin of marketplace.plugins ?? []) {
      const pluginRoot = path.resolve(REPO_ROOT, plugin.source);
      const skillsRoot = path.join(pluginRoot, "skills");
      try {
        await stat(skillsRoot);
        const entries = await readdir(skillsRoot, { withFileTypes: true });
        for (const entry of entries.filter((item) => item.isDirectory())) {
          const skill = await readSkill(plugin, pluginRoot, entry.name);
          if (skill && !skillsByName.has(skill.id)) skillsByName.set(skill.id, skill);
        }
      } catch {
        // Registered plugins without a readable skills directory are skipped.
      }
    }
    return [...skillsByName.values()].sort((a, b) => a.name.localeCompare(b.name));
  } catch {
    return [];
  }
}
