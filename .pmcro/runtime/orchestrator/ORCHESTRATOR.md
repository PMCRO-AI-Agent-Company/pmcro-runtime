# PMCR-O Orchestrator Runtime Contract

The Orchestrator is the governance boundary between user intent, PMCR-O state, and workflow execution.

## Responsibilities

1. Normalize incoming intent into a Frame.
2. Create or select a Trail that describes the intended cycle.
3. Select a compatible Microsoft Agent Framework workflow.
4. Pass execution to the workflow; do not reimplement workflow execution in PMCR-O.
5. Enforce lifecycle gates around state transitions.
6. Require Evidence before completion can be sealed.
7. Invoke Checker gates before accepting Maker output.
8. Record Reflector improvements after accepted or rejected cycles.
9. Persist durable knowledge only through the Memory boundary.
10. Halt or request human input when governance conditions cannot be satisfied.

## Authority model

- PMCR-O owns intent normalization, governance, lifecycle, evidence policy, memory policy, and routing policy.
- Microsoft Agent Framework owns workflow execution, executor coordination, workflow checkpoints, and workflow events.
- MCP exposes capabilities to workflows and agents; MCP does not own PMCR-O state.

## Lifecycle

`INTENT -> FRAME -> TRAIL -> EXECUTE -> CHECK -> REFLECT -> EVIDENCE -> SEAL`

Failure transitions to `RETRY`, `ESCALATE`, or `HALT` according to governance policy.

## Invariants

- A cycle cannot execute without a Frame and Trail.
- A cycle cannot be sealed without Evidence.
- Checker failure cannot be silently converted to success.
- Workflow checkpoints are execution state, not Colony memory.
- Agent memory is scoped to a stable logical role and is never treated as shared Colony memory.
- Stable workflow/agent identities must be preserved when resuming checkpoints.
- Declarative workflows are preferred when the execution graph fits the declarative contract.

## Output contract

The Orchestrator emits a runtime decision containing:

- `frame_id`
- `trail_id`
- `workflow_id`
- `action`
- `state_transition`
- `required_evidence`
- `next_gate`
- `halt_reason` when applicable
