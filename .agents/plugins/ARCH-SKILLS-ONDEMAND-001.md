# ARCH-SKILLS-ONDEMAND-001 — On-demand skill loading scope note

Status: SCOPED, NOT VERIFIED. No sealed trail exists for any claim below.
Written 2026-08-21 in response to "configure all skills into project."

## The actual problem

marketplace.json now lists all 25 plugins (110+ individual SKILL.md files
across pmcro-skills, dotnet-skills, github-skills, figma-skills), but only
10 have `"stage": true`. The other 15 (the dotnet-* long tail) are visible
on the frontend /skills page but NOT materialized into StagingRoot, so
they are not advertised to the live Ollama-backed agent yet.

## Why we didn't just raise OLLAMA_CONTEXT_LENGTH

AppHost.cs already documents `BUG-OLLAMA-SIGSEGV-001`: a SIGSEGV crash on
this machine's RTX 4070 Laptop GPU at the *current* 16384 ctx with flash
attention on. Flash attention was disabled as mitigation, but that fix is
explicitly marked unverified (no sealed trail). Raising context length
further, on top of an unconfirmed mitigation, is a real crash risk and
was not something to do silently.

## The better lever: confirm progressive disclosure is actually wired

Per `/mnt/skills/user/maf-native-2026/SKILL.md`, MAF's `AgentSkillsProvider`
is supposed to do four-stage progressive disclosure natively:
advertise skill names -> load_skill (full instructions) -> read_skill_resource
-> run_skill_script. If this is genuinely wired correctly, the model's
context only needs to hold ~name+description for all 110+ skills
(a few thousand tokens) plus whichever single skill(s) get `load_skill`'d
during a given cycle — NOT all 110+ full SKILL.md bodies at once. That
would mean the current 16384 ctx is likely already enough for a much
larger catalog than the 10-plugin curated set, without touching the GPU
config at all.

**This is unverified.** `MarketplaceSkillsWatcherService.cs` already flags
this exact uncertainty as a standing BUILD-RISK: whether `AgentSkillsProvider`
re-reads StagingRoot per advertise()/load_skill() call, or only once at
agent construction, has not been confirmed against this repo's pinned
`Microsoft.Agents.AI` package version.

## MAF skill resource types already line up with what we materialize

Per `maf-native-2026`, `AgentSkillsProvider`'s progressive disclosure has
four stages, and the last two (`read_skill_resource`, `run_skill_script`)
map directly onto file kinds this repo already produces per skill:

- `load_skill` -> the skill's `SKILL.md` body (metadata + instructions).
- `read_skill_resource` -> reference docs and assets sitting alongside a
  skill (this repo's `references/` and `assets/` folders).
- `run_skill_script` -> executable helpers (this repo's `scripts/` folder),
  delegated to a runner MAF does NOT supply itself -- Colony still owns
  sandboxing/approval for these (per maf-native-2026 section 1).

`MarketplaceSkillsMaterializer.MirrorDirectory` already folds
`commands/references/scripts/assets/agents` from the plugin root into each
materialized skill folder specifically so these line up with the GA
convention (SKILL.md + references/ + assets/ + scripts/ all inside one
skill folder) -- see the existing comment in that file. So the plumbing
for reference/asset/script resources is already in place for the 10
currently-staged plugins; widening `stage: true` to the rest doesn't
require new plumbing for this part, only the load_skill verification
above. `run_skill_script` calls still require approval by default (MAF
GA behavior) -- confirm that gate is actually enforced here before
trusting it, same "unverified until sealed trail" rule as everything
else in this note.

## Static verification of Program.cs (2026-08-21, no runtime touched)

Read the full `ProjectName.OrchestratorService/Program.cs` wiring. Confirmed
from source (not a self-report):

- Only THREE agents actually construct an `AgentSkillsProvider` pointed at
  the full `MarketplaceSkillsMaterializer.StagingRoot`: the keyed
  `"Orchestrator"` agent, `codeact-agent` (via `domainSkillsProvider`), and
  `"HarnessAgent"` (via `harnessSkillsProvider`, explicitly commented as
  affording "progressive disclosure ... spread across turns").
- `filesystem-agent`, `terminal-agent`, `playwright-agent` do NOT use
  `AgentSkillsProvider` at all. They get a small hardcoded "Colony Laws"
  excerpt via `SkillManifestReader.ReadColonyLaws()` instead, and are
  explicitly capped at `MaximumIterationsPerRequest = 1` (comment: swapping
  in the full manifest "would bloat every cycle's prompt for a local 8b
  model"). No multi-turn skill loading is possible for these three
  regardless of how many plugins get staged.

**Implication:** widening `stage: true` to all 25 plugins only affects
context/GPU load for the Orchestrator, codeact-agent, and HarnessAgent —
not the whole system. That narrows the blast radius, but does NOT resolve
the open question below.

**Still unverified (this is a black box from source alone):**
`AgentSkillsProvider` itself is compiled MAF library code, not something
in this repo. Whether its "advertise" stage genuinely costs only
name+description tokens per skill, or more, cannot be confirmed by reading
this repo's source — it requires either MAF's own docs/source or an
actual run. I did not start the Ollama container or run a live cycle to
test this; that's a GPU-touching action sitting next to an unresolved
SIGSEGV bug and should be done by Shawn directly, watching GPU state, not
triggered blind through a file-editing tool.

## What would actually unblock widening `stage: true` to all 25

1. Run a real cycle that calls `load_skill` against a plugin NOT in the
   original curated 10 (e.g. `dotnet-test`) and confirm via trail evidence
   (not self-report) that: (a) only its name/description appeared in the
   initial context, (b) full content loaded only after `load_skill`, and
   (c) no SIGSEGV occurred.
2. If (a)-(c) hold, flip the remaining 15 `dotnet-*` entries to
   `"stage": true` — no context-length change needed.
3. If full content is being front-loaded regardless of `load_skill`
   staging (i.e. progressive disclosure is not actually happening),
   the real fix is on the MAF wiring in Program.cs, not the manifest.

## Deliberately not done here

- Did not touch `OLLAMA_CONTEXT_LENGTH` or `OLLAMA_FLASH_ATTENTION`.
- Did not flip any dotnet-* plugin to `stage: true`.
- Did not fabricate a sealed trail for this investigation — this file is
  a scope note, not a disposition. A real PMCR-O cycle (Plan -> Make ->
  Check -> Reflect) with phase JSONL is still required before anything
  here counts as verified.
