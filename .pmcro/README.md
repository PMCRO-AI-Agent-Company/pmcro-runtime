# `.pmcro` Colony Runtime

`.pmcro/` is the runtime control-plane contract carried with the PMCRO skill ecosystem. It is deliberately provider-neutral and MAF-native.

## Runtime map

```text
Intent
  -> Frame
  -> Trail
  -> MAF Workflow
  -> Planner
  -> Maker
  -> Checker
  -> Reflector
  -> Evidence
  -> Seal

Capabilities -> Providers -> MCP/native tools
Memory      -> shared + agent-scoped
State       -> execution truth/checkpoints
Policies    -> permissions/approvals/security
Config      -> Aspire parameter seam + environment/profile references
```

## Provider rule

Use authoritative providers when they already exist:

- GitHub MCP for GitHub.
- Microsoft Playwright MCP for browser automation.
- Docker MCP Toolkit for MCP catalogs, profiles, gateway and containerized MCP server lifecycle.
- Native MAF/runtime tools for local capabilities where appropriate.

PMCRO adds governance around these providers; it does not wrap them merely to rename them.

## Docker and terminal

Docker MCP Toolkit is the preferred infrastructure boundary for MCP server catalogs and profiles. Terminal remains a separate high-risk host-command capability. Creating or switching Docker MCP profiles does not inherently require a terminal capability because Docker exposes dedicated MCP CLI/gateway operations.

## Secrets

`.pmcro/` contains secret references and policy only. Aspire parameters are the application's configuration seam; secret values come from user secrets, CI secret stores, Docker MCP credential handling, or production secret providers such as Azure Key Vault.
