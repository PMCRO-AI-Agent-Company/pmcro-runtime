// src/frontend/app/api/copilotkit/route.ts
//
// ARCH-AGUI-001 / ARCH-COPILOTKIT-001 (2026-07-11): the CopilotKit runtime lives
// here, server-side inside this Next.js process. It bridges the browser (which
// only ever talks to /api/copilotkit) to ProjectName.OrchestratorService's real
// AG-UI endpoint at {AGUI_SERVER_URL} (see ARCH-AGUI-001 in
// src/services/ProjectName.OrchestratorService/Program.cs, which maps
// app.MapAGUI("/agui", ...) over the keyed "Orchestrator" AIAgent).
//
// AGUI_SERVER_URL is injected by Aspire (see AppHost.cs: WithEnvironment(
// "AGUI_SERVER_URL", $"{orchestratorService.GetEndpoint("http")}/agui")) and is
// read here via process.env — safe, since this file only ever runs server-side
// and is never bundled into client JS (unlike NEXT_PUBLIC_* vars).
//
// ExperimentalEmptyAdapter is correct here (not a placeholder to fill in later):
// the .NET backend's own AIAgent (Ollama-backed) does all LLM reasoning and tool
// orchestration. The runtime's job is purely to speak AG-UI on both sides, not
// to run its own model.
import { HttpAgent } from "@ag-ui/client";
import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  copilotRuntimeNextJSAppRouterEndpoint,
} from "@copilotkit/runtime";
import { NextRequest } from "next/server";
import { Agent as UndiciAgent, fetch as undiciFetch } from "undici";

const AGUI_SERVER_URL = process.env.AGUI_SERVER_URL ?? "http://localhost:5100/agui";

// ARCH-HARNESS-003 (2026-07-22): second AG-UI endpoint for the parallel
// HarnessAgent surface (see ARCH-HARNESS-001/002 in
// OrchestratorService/Program.cs). Injected by Aspire the same way
// AGUI_SERVER_URL is (see AppHost.cs). Registered below under the "Harness"
// key -- per CopilotKit's documented multi-agent pattern, this is NOT
// auto-used by prebuilt components (CopilotSidebar/CopilotChat/CopilotPopup
// still default to "Orchestrator", the only entry those docs describe as
// auto-selected); selecting it requires useAgent({ agentId: "Harness" }) on
// the frontend, which is not yet wired into any component -- this only
// registers the agent with the runtime so it CAN be selected.
const AGUI_HARNESS_SERVER_URL = process.env.AGUI_HARNESS_SERVER_URL ?? "http://localhost:5100/agui/harness";

// EC-AGUI-TIMEOUT-001 (2026-07-11): Node's global fetch (undici under the hood)
// defaults to a 300s (5min) headersTimeout/bodyTimeout. A full PMCR-O cycle —
// Plan -> Make -> Check -> Reflect, up to Orchestrator__MaxLoops repeats, each
// phase a separate qwen3:8b call on a single 8GB laptop GPU that can evict/reload
// between calls — routinely exceeds 5 minutes for anything non-trivial. Since
// /agui doesn't flush SSE headers until the .NET side actually starts writing,
// the client-side headers timeout fires first (HeadersTimeoutError /
// INCOMPLETE_STREAM), even though the backend is still working. This dispatcher
// raises both timeouts well past any realistic cycle duration; the real fix if
// this still isn't enough is to reduce Orchestrator__MaxLoops or raise Ollama's
// OLLAMA_KEEP_ALIVE so the model doesn't unload between phases.
const longRunningDispatcher = new UndiciAgent({
  headersTimeout: 30 * 60 * 1000, // 30 min
  bodyTimeout: 30 * 60 * 1000,
});

const longRunningFetch = ((url: string, requestInit: RequestInit) =>
  undiciFetch(url as any, { ...(requestInit as any), dispatcher: longRunningDispatcher } as any) as unknown as Promise<Response>);

// Registered under "Orchestrator" to match the keyed AIAgent name on the .NET
// side (see AddKeyedSingleton<AIAgent>("Orchestrator", ...) in Program.cs).
// ARCH-HARNESS-003 (2026-07-22): CopilotKit's own docs (docs.copilotkit.ai/
// backend/copilot-runtime) state prebuilt components auto-use an agent
// registered under the literal key "default", not just "whichever key exists
// alone" -- that's a discrepancy from what this comment previously claimed
// for the single-agent case, now UNVERIFIED against this repo's installed
// CopilotKit version rather than corrected outright (don't want to guess a
// behavior change without checking node_modules like ARCH-A2UI-001 did).
// Selecting either agent explicitly is unambiguous either way:
// useAgent({ agentId: "Orchestrator" }) or useAgent({ agentId: "Harness" }).
// Neither is wired into any component yet — this file only registers both
// agents with the runtime so the frontend CAN address them.
const orchestratorAgent = new HttpAgent({ url: AGUI_SERVER_URL, fetch: longRunningFetch });

// ARCH-HARNESS-003: same long-running dispatcher as the Orchestrator agent --
// the harness's own loop (now re-invoking via CompletionMarkerLoopEvaluator,
// ARCH-HARNESS-002) can run just as long per turn.
const harnessAgent = new HttpAgent({ url: AGUI_HARNESS_SERVER_URL, fetch: longRunningFetch });

const runtime = new CopilotRuntime({
  agents: {
    Orchestrator: orchestratorAgent,
    Harness: harnessAgent,
  },
  // ARCH-A2UI-001 (2026-07-15): a2ui here is A2UIMiddlewareConfig, verified
  // against the installed node_modules/@ag-ui/a2ui-middleware/dist/index.d.ts
  // and node_modules/@copilotkit/runtime/dist/v2/runtime/core/runtime.d.mts --
  // NOT the secondary-LLM "Dynamic Schema" mechanism the public CopilotKit
  // docs describe for other SDK generations. injectA2UITool:true injects a
  // structured `render_a2ui` tool into the Orchestrator agent's own tool list
  // (visible over the existing AG-UI stream from OrchestratorService); qwen3:8b
  // itself must call it with { surfaceId, components, data }. No second model,
  // no Ollama OpenAI-compat routing, no extra Aspire wiring required.
  //
  // ARCH-VISUAL-BRIDGE-001 (2026-07-20): `schema` (A2UIMiddlewareConfig.schema,
  // an A2UIInlineCatalogSchema) declares two named components beyond the
  // built-in basic_catalog vocabulary (Text/Card/Column/etc.), shaped to match
  // the props AgentCard.tsx and TrailView.tsx actually accept -- so the
  // Orchestrator can ask for "a trail card" or "an agent domain card" as a
  // semantic unit instead of hand-assembling one from Row/Column/Text every
  // time. UPDATE (2026-08-22): the client-side renderer now exists --
  // app/components/A2UIRenderer.tsx maps catalogId "pmcro-visual-bridge"
  // TrailCard/AgentDomainCard to real TrailView/AgentCard components via
  // useRenderTool, and is mounted in ConsoleView.tsx. A render_a2ui call
  // naming these components WILL paint in the CopilotKit surface. What's
  // still unverified: no observed live cycle has actually had qwen3:8b call
  // render_a2ui with this schema -- the wiring is confirmed on disk, not
  // confirmed to fire end-to-end.
  a2ui: {
    injectA2UITool: true,
    schema: {
      catalogId: "pmcro-visual-bridge",
      components: {
        TrailCard: {
          type: "object",
          description:
            "Summary card for one sealed or in-progress PMCR-O trail. Mirrors TrailView.tsx's Trail type minus the cycle detail (plan/make/check/reflect entries aren't summarized here -- link to the full trail instead of inlining them).",
          required: ["id", "domain", "trueIntent", "disposition"],
          properties: {
            id: { type: "string", description: "Trail UUID." },
            domain: { type: "string", description: "C-Suite domain id, e.g. 'cto'." },
            trueIntent: { type: "string", description: "The cycle's true_intent, one sentence." },
            disposition: {
              type: "string",
              enum: ["Accept", "Retry", "Halt"],
              description: "Real LoopDisposition value from LoopFrame.cs -- not a fictional 4-value schema.",
            },
          },
        },
        AgentDomainCard: {
          type: "object",
          description:
            "Summary card for one C-Suite domain. Mirrors AgentCard.tsx's AgentCardData type.",
          required: ["id", "abbr", "label", "color"],
          properties: {
            id: { type: "string", description: "Domain id, e.g. 'cfo'. Must match catalog/skills.json." },
            abbr: { type: "string", description: "Short badge text, e.g. 'CFO'." },
            label: { type: "string", description: "Full display name, e.g. 'CFO'." },
            color: { type: "string", description: "Hex accent color, matching DomainSelector.tsx's DOMAINS entry." },
            loopState: {
              type: "string",
              enum: ["Planner", "Maker", "Checker", "Reflector", "Orchestrator"],
              description: "Optional -- current PMCR-O role if a live cycle is running for this domain.",
            },
          },
        },
      },
    },
  },
});

const serviceAdapter = new ExperimentalEmptyAdapter();

export const POST = async (req: NextRequest) => {
  const { handleRequest } = copilotRuntimeNextJSAppRouterEndpoint({
    runtime,
    serviceAdapter,
    endpoint: "/api/copilotkit",
  });
  return handleRequest(req);
};
