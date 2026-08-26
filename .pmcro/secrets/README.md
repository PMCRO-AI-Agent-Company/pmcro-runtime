# Secret references

This directory contains references and policy only. It must never contain secret values, tokens, passwords, private keys, certificates, connection-string credentials, `.env` files, or provider exports.

## Reference contract

| Concern | PMCRO record | Runtime owner |
| --- | --- | --- |
| GitHub authentication | `github-token` parameter reference and least-privilege scope | Official GitHub MCP authentication, user secrets, or CI secret store |
| Docker MCP credentials | provider/profile reference only | Docker MCP Toolkit and its configured credential flow |
| Azure production secrets | `azure-key-vault-uri` reference and policy | Azure Key Vault with managed identity |
| CI/CD secrets | Aspire parameter name | CI secret store injected as `Parameters__<parameter_name>` |

## Rotation and evidence

- Rotate values at the external secret provider; update only the reference or policy here when the contract changes.
- Never attach secret values to trails, frames, evidence, logs, telemetry, prompts, memory, or generated artifacts.
- Record only redacted provider identifiers and the outcome of configuration validation.
