# Agent Memory

Agent memory is role-scoped retained context and is distinct from shared Colony memory and workflow execution state.

Expected layout:

```text
agent-memory/
├── orchestrator/
├── planner/
├── maker/
├── checker/
└── reflector/
```

Memory entries should be evidence-backed, scoped, redactable, and safe to reuse. Secrets and raw credentials are never retained here.
