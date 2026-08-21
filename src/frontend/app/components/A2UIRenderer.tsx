// src/frontend/app/components/A2UIRenderer.tsx
//
// ARCH-VISUAL-BRIDGE-002 (2026-07-20): client-side renderer for A2UI tool calls.
// Maps catalog id "pmcro-visual-bridge" component names (TrailCard, AgentDomainCard)
// to the real React components in this codebase. Without this renderer, the LLM can
// call render_a2ui all it wants but nothing will appear in the CopilotKit surface.
//
// This component must be mounted inside the CopilotKit provider (ConsoleView is ideal since it's
// already mounted and has access to trailsByDomain if needed for future enhancements) and uses
// useRenderTool from @copilotkit/react-core/v2 to register handlers for specific
// component names. Each handler renders when the LLM calls the corresponding tool.
"";

import { z } from "zod";
import { useRenderTool, useDefaultRenderTool } from "@copilotkit/react-core/v2";
import AgentCard, { type AgentCardData } from "./AgentCard";
import TrailView, { type Trail } from "./TrailView";

// TrailCard renderer: displays a sealed/in-progress trail summary
// Schema defined in route.ts matches TrailView's Trail type fields
function TrailCardRenderer() {
  useRenderTool({
    name: "TrailCard",
    parameters: z.object({
      id: z.string(),
      domain: z.string(),
      trueIntent: z.string(),
      disposition: z.enum(["Accept", "Retry", "Halt"]).nullable(),
    }),
    render: ({ status, parameters }) => {
      // Build a minimal Trail object for TrailView
      // Note: TrailView expects full Trail shape; we provide a summary version
      if (status !== "complete") {
        return (
          <div style={{ padding: "12px 16px", color: "var(--colony-muted)", fontSize: "13px" }}>
            Loading trail...
          </div>
        );
      }

      const trail: Trail = {
        id: parameters.id,
        domain: parameters.domain,
        trueIntent: parameters.trueIntent,
        disposition: parameters.disposition,
        cycles: [],
      };

      return (
        <div style={{ maxWidth: "480px" }}>
          <TrailView trail={trail} />
        </div>
      );
    },
  });
}

// AgentDomainCard renderer: displays a C-Suite domain summary
// Schema defined in route.ts matches AgentCard's AgentCardData type fields
function AgentDomainCardRenderer() {
  useRenderTool({
    name: "AgentDomainCard",
    parameters: z.object({
      id: z.string(),
      abbr: z.string(),
      label: z.string(),
      color: z.string(),
      loopState: z
        .enum(["Planner", "Maker", "Checker", "Reflector", "Orchestrator"])
        .optional(),
    }),
    render: ({ status, parameters }) => {
      if (status !== "complete") {
        return (
          <div style={{ padding: "12px 16px", color: "var(--colony-muted)", fontSize: "13px" }}>
            Loading domain card...
          </div>
        );
      }

      const agentData: AgentCardData = {
        id: parameters.id,
        abbr: parameters.abbr,
        label: parameters.label,
        color: parameters.color,
        loopState: parameters.loopState,
        trailCount: undefined,
        statusTone: null,
      };

      return (
        <div style={{ maxWidth: "320px" }}>
          <AgentCard agent={agentData} />
        </div>
      );
    },
  });
}

// Default renderer for any other A2UI components (wildcard fallback)
// Useful for debugging and any future tools that don't have dedicated renderers
function DefaultA2UIRenderer() {
  useDefaultRenderTool({
    render: ({ name, parameters, status, result }) => {
      return (
        <div
          style={{
            padding: "12px 16px",
            background: "var(--colony-panel)",
            border: "1px solid var(--colony-border)",
            borderRadius: "10px",
            color: "var(--colony-text)",
            fontSize: "13px",
            maxWidth: "480px",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "8px", marginBottom: "8px" }}>
            <span style={{ color: status === "complete" ? "var(--colony-accent-2)" : "var(--colony-accent)" }}>
              {status === "complete" ? "✓" : "⏳"}
            </span>
            <strong>{name}</strong>
          </div>
          {status === "complete" && result && (
            <pre style={{ margin: 0, fontSize: "11px", color: "var(--colony-muted)" }}>
              {JSON.stringify(result, null, 2)}
            </pre>
          )}
        </div>
      );
    },
  });
}

// Main renderer component: mounts all A2UI handlers
// Mount this once inside the CopilotKit provider (ConsoleView is ideal since it's
// already mounted and has access to trailsByDomain if needed for future enhancements)
export default function A2UIRenderer() {
  // Register renderers for each known component
  TrailCardRenderer();
  AgentDomainCardRenderer();
  // Default catches any component not explicitly registered
  DefaultA2UIRenderer();

  // This component renders nothing itself -- the hooks handle rendering
  return null;
}