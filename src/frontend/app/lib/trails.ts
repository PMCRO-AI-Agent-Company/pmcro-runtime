// src/frontend/app/lib/trails.ts
//
// ARCH-AGENT-DIRECTORY-002 (2026-07-20): server-only reader for real sealed
// (and in-progress) trails under .pmcro/trails/<domain>/<uuid>/.
//
// FIX (2026-08-22): this reader was written against a documented
// trail-schema.md shape (snake_case frame fields, {seq,content} jsonl
// lines, lowercase disposition fields) that never actually matched what
// ProjectName.OrchestratorService's real FileTrailWriter emits. Confirmed
// by reading a live trail on disk end to end
// (.pmcro/trails/filesystem-agent/3fe6658e-.../):
//   - 00-frame.json is snake_case but minimal: {trail_id, seed_intent,
//     started_utc} -- no true_intent/created_at/domain/requested_by keys.
//   - NN-{plan,make,check,reflect}.jsonl are PascalCase C#-record dumps
//     (Steps/StepResults/CheckItems/RawPlan/RawVerdict/RawReflection),
//     not {seq,content} lines -- the old readJsonlEntries's `obj.content`
//     check never matched a single field in these files, so every cycle
//     silently rendered empty ("No plan entries for this cycle") even
//     though real, populated trail data existed on disk. Confirmed live
//     in the Console UI: the Trail Player showed "Untitled request" / "NO
//     DISPOSITION" / "No plan entries" for a trail whose disposition.json
//     on disk actually reads Disposition: "Accept".
//   - disposition.json is PascalCase (Disposition, FinalOutput,
//     RetryContext, HaltReason, EarnedConstraints, CycleNumber,
//     NextSeedIntent), not the lowercase {disposition, reason, sealed_at,
//     final_cycle} the old reader looked for.
// This rewrite parses the real on-disk shapes and converts each role's
// rich record into the {seq, content, result?} shape TrailView.tsx
// already renders -- that component's own contract did not need to
// change, only what feeds it.
//
// SERVER-ONLY: uses node:fs/promises. Must only be imported from a file
// with no "use client" directive -- importing this from a client
// component would try to bundle Node's fs module into browser JS and
// fail the build.
import { readdir, readFile, stat } from "node:fs/promises";
import path from "node:path";
import type { Trail, TrailCycle, TrailRoleEntry, TrailDisposition } from "../components/TrailView";

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

// RawPlan/RawVerdict/RawReflection/StepResults[].Output are themselves
// JSON, encoded as a C# string when embedded -- parsed best-effort, never
// thrown on.
function tryParseJson<T>(raw: string | null | undefined): T | null {
  if (!raw) return null;
  try {
    return JSON.parse(raw) as T;
  } catch {
    return null;
  }
}

// ── Real on-disk shapes (PascalCase, straight from the .NET FileTrailWriter) ──
type RawFrame = { trail_id?: string; seed_intent?: string; started_utc?: string };

type RawDisposition = {
  Disposition?: string;
  FinalOutput?: string;
  RetryContext?: string | null;
  HaltReason?: string | null;
  CycleNumber?: number;
  NextSeedIntent?: string | null;
};

type RawStep = { Index: number; Action: string; SubjectAgent: string; ActionType: string };
type RawPlanRecord = { Steps?: RawStep[]; SuccessCriteria?: string };

type RawStepResult = { StepIndex: number; Action: string; Output?: string; Ok?: boolean };
type RawMakeRecord = { StepResults?: RawStepResult[] };

type RawCheckItem = { StepIndex: number; Passed: boolean; FailureEvidence?: string | null; Criterion?: string | null };
type RawCheckRecord = { CheckItems?: RawCheckItem[]; RawVerdict?: string };

type RawReflectRecord = {
  Disposition?: string;
  FinalOutput?: string;
  RawReflection?: string;
  HaltReason?: string | null;
  RetryContext?: string | null;
};

// Every NN-{role}.jsonl line is one full phase record -- "JSON Lines" here
// means a retried phase appends another whole line, not one small
// {seq,content} entry per line -- so every line is read, not just the
// first, in case a cycle retried a phase.
async function readPhaseLines<T>(filePath: string): Promise<T[]> {
  let raw: string;
  try {
    raw = await readFile(filePath, "utf-8");
  } catch {
    return [];
  }
  const records: T[] = [];
  for (const line of raw.split("\n")) {
    const trimmed = line.trim();
    if (!trimmed) continue;
    try {
      records.push(JSON.parse(trimmed) as T);
    } catch {
      // malformed line -- skip rather than crash the whole trail read
    }
  }
  return records;
}

function planEntries(records: RawPlanRecord[]): TrailRoleEntry[] {
  const entries: TrailRoleEntry[] = [];
  records.forEach((record, recordIndex) => {
    const steps = record.Steps ?? [];
    if (steps.length === 0) {
      if (record.SuccessCriteria) entries.push({ seq: recordIndex + 1, content: record.SuccessCriteria });
      return;
    }
    for (const step of steps) {
      entries.push({ seq: step.Index, content: `${step.Action} via ${step.SubjectAgent} (${step.ActionType})` });
    }
  });
  return entries;
}

function makeEntries(records: RawMakeRecord[]): TrailRoleEntry[] {
  const entries: TrailRoleEntry[] = [];
  records.forEach((record) => {
    for (const result of record.StepResults ?? []) {
      const parsedOutput = tryParseJson<{ data?: { entries?: { name: string; type: string }[] } }>(result.Output);
      const summary = parsedOutput?.data?.entries
        ? parsedOutput.data.entries.map((e) => `${e.name} (${e.type})`).join(", ")
        : (result.Output ?? "").slice(0, 200);
      entries.push({ seq: result.StepIndex, content: `${result.Action}: ${summary}`, result: result.Ok ? "pass" : "fail" });
    }
  });
  return entries;
}

function checkEntries(records: RawCheckRecord[]): TrailRoleEntry[] {
  const entries: TrailRoleEntry[] = [];
  records.forEach((record) => {
    const items = record.CheckItems ?? [];
    if (items.length === 0) {
      const verdict = tryParseJson<{ criteria_results?: { criterion: string; result: string }[] }>(record.RawVerdict);
      verdict?.criteria_results?.forEach((c, i) => {
        entries.push({ seq: i + 1, content: c.criterion, result: c.result === "PASS" ? "pass" : "fail" });
      });
      return;
    }
    items.forEach((item) => {
      entries.push({
        seq: item.StepIndex,
        content: item.Criterion ?? (item.Passed ? "Check passed" : item.FailureEvidence ?? "Check failed"),
        result: item.Passed ? "pass" : "fail",
      });
    });
  });
  return entries;
}

function reflectEntries(records: RawReflectRecord[]): TrailRoleEntry[] {
  return records.map((record, i) => {
    const parsed = tryParseJson<{ cycle_summary?: string }>(record.RawReflection ?? record.FinalOutput);
    const content = parsed?.cycle_summary ?? record.HaltReason ?? record.RetryContext ?? record.Disposition ?? "Reflection recorded";
    const result: TrailRoleEntry["result"] =
      record.Disposition === "Accept" ? "pass" : record.Disposition === "Halt" ? "fail" : record.Disposition === "Retry" ? "note" : undefined;
    return { seq: i + 1, content, result };
  });
}

async function readOneTrail(domain: string, uuid: string, trailDir: string): Promise<Trail | null> {
  const frame = await readJsonSafe<RawFrame>(path.join(trailDir, "00-frame.json"));
  // No frame -- either not a real trail yet, or a directory that failed
  // Article 1 (tool actions with no open trail). Either way, not renderable.
  if (!frame) return null;

  const disposition = await readJsonSafe<RawDisposition>(path.join(trailDir, "disposition.json"));

  // EC-009 caps a trail at 3 cycles -- check for 01/02/03 rather than
  // globbing, so an unrelated file never gets misread as a cycle.
  const cycles: TrailCycle[] = [];
  for (const n of ["01", "02", "03"]) {
    const planPath = path.join(trailDir, `${n}-plan.jsonl`);
    if (!(await pathExists(planPath))) continue;
    const [planRecords, makeRecords, checkRecords, reflectRecords] = await Promise.all([
      readPhaseLines<RawPlanRecord>(planPath),
      readPhaseLines<RawMakeRecord>(path.join(trailDir, `${n}-make.jsonl`)),
      readPhaseLines<RawCheckRecord>(path.join(trailDir, `${n}-check.jsonl`)),
      readPhaseLines<RawReflectRecord>(path.join(trailDir, `${n}-reflect.jsonl`)),
    ]);
    cycles.push({
      number: n,
      plan: planEntries(planRecords),
      make: makeEntries(makeRecords),
      check: checkEntries(checkRecords),
      reflect: reflectEntries(reflectRecords),
    });
  }

  return {
    id: frame.trail_id ?? uuid,
    domain,
    trueIntent: frame.seed_intent ?? "",
    requestedBy: undefined,
    createdAt: frame.started_utc,
    disposition: (disposition?.Disposition ?? null) as TrailDisposition,
    reason: disposition?.HaltReason ?? disposition?.RetryContext ?? undefined,
    cycles,
  };
}

/**
 * Reads every real trail under .pmcro/trails/, grouped by domain, most
 * recently created first. Returns {} if .pmcro/trails doesn't exist yet
 * (e.g. a fresh checkout before any cycle has run) rather than throwing --
 * an empty directory is a valid state, not an error.
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
