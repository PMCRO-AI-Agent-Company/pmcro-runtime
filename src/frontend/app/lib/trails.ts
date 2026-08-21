// src/frontend/app/lib/trails.ts
//
// ARCH-AGENT-DIRECTORY-002 (2026-07-20): server-only reader for real sealed
// (and in-progress) trails under .pmcro/trails/<domain>/<uuid>/, per
// catalog/Platform/PMCR-O/skills/orchestrator/references/trail-schema.md.
// This is the "real read endpoint" TrailView.tsx's own header comment has
// been waiting on since 2026-07-13 -- it did not exist before this file.
//
// SERVER-ONLY: uses node:fs/promises. Must only be imported from a file
// with no "use client" directive (a Server Component or another
// server-only module) -- importing this from a client component would try
// to bundle Node's fs module into browser JS and fail the build.
//
// Deliberately tolerant of missing/partial trail directories: a trail with
// 00-frame.json but no disposition.json yet is a real, still-open trail
// (Article 1 of .clinerules only requires 00-deps.json/00-frame.json to
// exist before a Make-phase tool call -- it does not require the trail to
// already be sealed), not an error. A directory with neither file is not a
// trail at all and is silently skipped.
import { readdir, readFile, stat } from "node:fs/promises";
import path from "node:path";
import type { Trail, TrailCycle, TrailRoleEntry, TrailDisposition } from "../components/TrailView";

// .pmcro/ lives at the repo root, two levels up from src/frontend (this
// Next.js app's own process.cwd() when run via `next dev`/`next build`
// from src/frontend, matching this repo's actual directory layout:
// W:\pmcro-ai-company\{.pmcro, src\frontend}).
const TRAILS_ROOT = path.resolve(process.cwd(), "..", "..", ".pmcro", "trails");

async function pathExists(p: string): Promise<boolean> {
  try {
    await stat(p);
    return true;
  } catch {
    return false;
  }
}

async function readJsonSafe<T>(filePath: string): Promise<T | null> {
  try {
    return JSON.parse(await readFile(filePath, "utf-8")) as T;
  } catch {
    return null;
  }
}

// Parses one NN-{role}.jsonl file into TrailRoleEntry[]. Lines without a
// string `content` field (e.g. check.jsonl's final disposition-signal line,
// per trail-schema.md's own documented shape:
// {"cycle":"NN","seq":"final","role":"check","disposition":"pass|fail",...})
// are intentionally excluded from the rendered entry list -- TrailView.tsx
// renders entries as {seq, content, result?}, and that final line carries no
// content to show. The disposition signal itself is read separately from
// disposition.json at the trail root, not reconstructed from this line.
async function readJsonlEntries(filePath: string): Promise<TrailRoleEntry[]> {
  let raw: string;
  try {
    raw = await readFile(filePath, "utf-8");
  } catch {
    return [];
  }
  const entries: TrailRoleEntry[] = [];
  for (const line of raw.split("\n")) {
    const trimmed = line.trim();
    if (!trimmed) continue;
    let obj: Record<string, unknown>;
    try {
      obj = JSON.parse(trimmed);
    } catch {
      continue; // malformed line -- skip rather than crash the whole trail read
    }
    if (typeof obj.content !== "string") continue;
    entries.push({
      seq: (obj.seq as number | string) ?? entries.length + 1,
      content: obj.content,
      result: obj.result as TrailRoleEntry["result"] | undefined,
    });
  }
  return entries;
}

async function readOneTrail(domain: string, uuid: string, trailDir: string): Promise<Trail | null> {
  const frame = await readJsonSafe<{
    trail_id?: string;
    domain?: string;
    true_intent?: string;
    created_at?: string;
    requested_by?: string;
  }>(path.join(trailDir, "00-frame.json"));
  // No frame -- either not a real trail yet, or a directory that failed
  // Article 1 (tool actions with no open trail). Either way, not renderable.
  if (!frame) return null;

  const disposition = await readJsonSafe<{
    sealed_at?: string;
    final_cycle?: string;
    disposition?: string;
    reason?: string;
  }>(path.join(trailDir, "disposition.json"));

  // EC-009 caps a trail at 3 cycles -- check for 01/02/03 rather than
  // globbing, so an unrelated file never gets misread as a cycle.
  const cycles: TrailCycle[] = [];
  for (const n of ["01", "02", "03"]) {
    const planPath = path.join(trailDir, `${n}-plan.jsonl`);
    if (!(await pathExists(planPath))) continue;
    const [plan, make, check, reflect] = await Promise.all([
      readJsonlEntries(planPath),
      readJsonlEntries(path.join(trailDir, `${n}-make.jsonl`)),
      readJsonlEntries(path.join(trailDir, `${n}-check.jsonl`)),
      readJsonlEntries(path.join(trailDir, `${n}-reflect.jsonl`)),
    ]);
    cycles.push({ number: n, plan, make, check, reflect });
  }

  return {
    id: frame.trail_id ?? uuid,
    domain: frame.domain ?? domain,
    trueIntent: frame.true_intent ?? "",
    requestedBy: frame.requested_by,
    createdAt: frame.created_at,
    disposition: (disposition?.disposition ?? null) as TrailDisposition,
    reason: disposition?.reason,
    cycles,
  };
}

/**
 * Reads every real trail under .pmcro/trails/, grouped by domain, most
 * recently created first. Returns {} if .pmcro/trails doesn't exist yet
 * (e.g. a fresh checkout before any cycle has run) rather than throwing --
 * an empty Directory is a valid state, not an error.
 */
export async function loadTrailsByDomain(): Promise<Record<string, Trail[]>> {
  const result: Record<string, Trail[]> = {};

  let domainEntries;
  try {
    domainEntries = await readdir(TRAILS_ROOT, { withFileTypes: true });
  } catch {
    return result;
  }

  const domainDirs = domainEntries.filter((d) => d.isDirectory()).map((d) => d.name);

  for (const domain of domainDirs) {
    const domainPath = path.join(TRAILS_ROOT, domain);
    let trailEntries;
    try {
      trailEntries = await readdir(domainPath, { withFileTypes: true });
    } catch {
      continue;
    }
    const uuids = trailEntries.filter((d) => d.isDirectory()).map((d) => d.name);

    const trails: Trail[] = [];
    for (const uuid of uuids) {
      const trail = await readOneTrail(domain, uuid, path.join(domainPath, uuid));
      if (trail) trails.push(trail);
    }

    trails.sort((a, b) => (b.createdAt ?? "").localeCompare(a.createdAt ?? ""));
    result[domain] = trails;
  }

  return result;
}
