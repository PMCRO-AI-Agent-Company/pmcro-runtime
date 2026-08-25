# PMCRO Orchestrator Approval

## Status

Authoritative runtime contract for `/pmcro-orchestrator:approve`.

## Rule

PMCRO owns the governance decision. Microsoft Agent Framework owns workflow suspension and resumption.

The runtime MUST NOT implement a second boolean-only approval engine.

## Flow

```text
/pmcro-orchestrator:approve
        |
        v
PMCRO command/control plane
        |
        v
Locate active MAF RequestInfoEvent / external request
        |
        +--> validate PMCRO Laws
        +--> validate Constraints
        +--> validate Acceptability
        +--> validate authorization
        |
        v
Create the typed MAF response
        |
        v
SendResponseAsync / resume workflow
        |
        v
record approval in immutable Trail
```

## No-argument semantics

When there is exactly one active approval request for the current governed run, `/pmcro-orchestrator:approve` targets that request. If there is no active request, or multiple ambiguous requests, the command MUST NOT guess.

A future explicit request identifier MAY be supplied:

```text
/pmcro-orchestrator:approve <requestId>
```

## Output contract

```text
PMCRO

I AM: Orchestrator
I RECEIVE: /pmcro-orchestrator:approve
I LOCATE: <request>
I VALIDATE: Laws / Constraints / Acceptability / Authorization
I AUTHORIZE: <transition>
I RESUME: MAF workflow
I RECORD: approval evidence in Trail
STATUS: APPROVED
```

If no unambiguous request exists:

```text
PMCRO

I AM: Orchestrator
I RECEIVE: /pmcro-orchestrator:approve
I LOCATE: no unambiguous pending approval
STATUS: NO_PENDING_APPROVAL
```

## MAF integration

MAF Workflows expose external requests through `RequestInfoEvent` and typed responses. Pending requests are checkpoint-aware and are re-emitted when a workflow is restored. PMCRO must integrate with this mechanism rather than duplicating workflow state.

## UI integration

AG-UI/CopilotKit is the presentation boundary. The UI may display and collect approval, but cannot bypass PMCRO authorization or directly mutate Trail state.

## Trail

Approval is an evidence event. It records who/what authorized the transition, request identifier, cycle, timestamp, and resulting workflow disposition. Approval does not imply task success; Checker and Reflector remain authoritative for verification and disposition.
