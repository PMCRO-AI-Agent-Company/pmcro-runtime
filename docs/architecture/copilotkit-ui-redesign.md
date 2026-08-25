# CopilotKit UI Redesign Contract

## Purpose

The PMCRO runtime UI is an LLM-native operating environment, not a developer dashboard with chat attached. The primary experience is Conversation → Orchestration → Activity → Evidence → Trail → Replay.

## Runtime boundary

CopilotKit is the presentation/application layer. AG-UI is the bidirectional agent-to-application transport. Microsoft Agent Framework owns agent/workflow execution. PMCRO owns governance, laws, acceptability, trail lineage, backward flow, and approval authority.

## UI surfaces

- Conversation: streaming messages, attachments, tool activity, regeneration, copy, and accessible keyboard interaction.
- Cycle rail: Orchestrator → Planner → Maker → Checker → Reflector with live state.
- Activity cards: render structured phase/tool state rather than raw JSON logs.
- Trail player: inspect, compare, replay, and navigate immutable cycle evidence.
- Approval center: present pending governed approvals and resume through the MAF/AG-UI approval path.
- Workspace: Trails, Agents, Skills, MCP, Evidence, Runtime, Settings.

## Generative UI policy

Prefer controlled application components and tool-call/state rendering for stable PMCRO surfaces. Use A2UI Fixed Schema for known structured surfaces where agent-provided data is appropriate. Use Dynamic Schema only where the product explicitly accepts model-generated layout variation. MCP Apps are sandboxed external UI and must remain subject to runtime authorization and trust boundaries.

## PMCRO visual language

Every phase surface uses first-person semantic framing such as `I AM: Orchestrator`, `I SELECT: Strategy`, `I DISPATCH: Planner`, `I CHECK: Acceptability`, and `I RECORD: Trail Evidence`. The UI must make phase identity, current cycle, disposition, and evidence provenance obvious without requiring users to read raw logs.

## Safety and authority

The UI must never imply that rendering an approval request grants permission. Approval is a governed transition. CopilotKit cannot override PMCRO Laws, Acceptability, authorization, or sealed Trail evidence.

## Implementation direction

Redesign the existing ConsoleView, ChatPanel, Sidebar, HarnessView, A2UI renderer, and Trail Player as one coherent application shell. Preserve the existing MAF/AG-UI transport and runtime behavior while replacing dashboard-first presentation with an LLM-style conversation and activity model.
