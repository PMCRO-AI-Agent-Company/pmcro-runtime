# PMCR-O State Contract

State is execution truth for the Colony cycle. It is not durable knowledge.

## Cycle states

- `INTENT`: raw request received.
- `FRAME`: intent normalized with context, constraints, and acceptance conditions.
- `TRAIL`: governed sequence of intended work.
- `EXECUTE`: MAF workflow is running.
- `CHECK`: Checker validates the produced result.
- `REFLECT`: Reflector records improvements and follow-up opportunities.
- `EVIDENCE`: completion evidence is collected and linked.
- `SEAL`: cycle is complete and immutable as a historical record.
- `RETRY`: execution may repeat under an explicit retry policy.
- `ESCALATE`: human or higher-level governance is required.
- `HALT`: execution must stop.

## Rules

1. State transitions must be explicit.
2. Checkpoints belong to workflow execution and may be used to resume MAF execution.
3. A checkpoint does not replace a PMCR-O cycle record.
4. A cycle may enter `SEAL` only after a successful Checker gate and required Evidence.
5. Retry must preserve lineage to the originating Frame and Trail.
6. Memory writes occur only after the cycle reaches an allowed persistence point.
