# Shared Colony Memory

This directory stores durable knowledge available across PMCR-O agents.

- `semantic/` — stable facts and architecture knowledge
- `episodic/` — significant completed events
- `procedural/` — proven procedures and patterns
- `organizational/` — decisions, policies, and institutional knowledge
- `indexes/` — retrieval metadata

Agent-specific retained context belongs under `.pmcro/agents/<agent>/memory/` and must not be mixed into shared memory.

Workflow execution state belongs under `.pmcro/state/`, not memory.
