# PMCR-O Declarative Workflow

This directory contains the Microsoft Agent Framework declarative workflow used as the PMCR-O execution graph.

## Architecture

The MAF workflow owns deterministic orchestration:

`Planner -> Subject/MCP -> HIL -> Checker -> Reflector -> Gate/Retry`

The existing PMCR-O domain services remain responsible for governance, evidence/trail persistence, skills, HIL, and runtime integration. `PmcroLoop` is retained during migration as a compatibility path; it is not the target orchestration engine for the declarative path.

## Runtime requirements

- .NET 11
- Microsoft Agent Framework Workflows + Declarative
- Microsoft Agent Framework Declarative MCP integration
- MCP Filesystem / Terminal / Playwright services
- Existing PMCR-O trail/evidence services

## Evidence rule

MCP execution is not sufficient by itself. Every tool result must become a normalized execution artifact before Checker evaluation. The artifact must retain the tool name, arguments, success/error state, and returned evidence. Gate coverage must bind each success criterion to at least one concrete evidence item.

## Migration rule

Do not delete the hand-rolled workflow or `PmcroLoop` until the declarative path passes the same runtime validation suite and produces equivalent sealed trail artifacts. The declarative workflow is the strategic execution path; the hand-rolled path remains a migration fallback only.
