# /pmcro-orchestrator:approve

## Purpose

Approve the currently pending Human-in-the-Loop governed transition owned by the Orchestrator.

## Invocation

```text
/pmcro-orchestrator:approve
```

Optional explicit request targeting:

```text
/pmcro-orchestrator:approve <requestId>
```

## Semantics

This is a **control-plane command**, not a new Seed Intent and not a success verdict.

The runtime resolves the latest pending TYPE1 HIL request when no request ID is supplied. The approved action then resumes its governed cycle. Checker and Reflector still determine whether the work actually succeeds.

## PMCRO output

```text
PMCRO
I AM: The Orchestrator
I RECEIVE: /pmcro-orchestrator:approve
I CHECK: Pending governed approval
I APPROVE: <target>
I RECORD: Approval control event
I RESUME: Governed cycle
STATUS: APPROVED
```

## Safety

Approval never bypasses TYPE1 policy, Checker verification, Reflector disposition, or trail evidence requirements. If there is no pending approval, the runtime returns `NO_PENDING_APPROVAL` rather than fabricating an approval.
