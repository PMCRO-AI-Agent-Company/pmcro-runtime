# Configuration

`.pmcro/config/` contains environment-neutral configuration contracts. It is a contract layer, not a secret store.

- `parameters.yaml` declares external parameters and whether they are secret.
- `environments.yaml` maps environments to provider/profile policy.
- `profiles.yaml` describes desired MCP profile composition.

Actual secret values are supplied by Aspire/.NET configuration, CI secret stores, Docker MCP credential handling, or the production platform secret provider.

## Aspire boundary

The AppHost owns the runtime value boundary. Declare secret parameters with `secret: true`, then pass the parameter resource to a dependent project as an environment variable. `.pmcro` records the parameter name, intended provider, scope, and policy; it never stores the value.

For local development, resolve values from .NET user secrets or another local configuration provider. CI/CD injects `Parameters__<parameter_name>` variables, converting parameter dashes to underscores. Production resolves secret values from the platform secret provider; Azure deployments should use Azure Key Vault and managed identity.

`azure-key-vault-uri` is a non-secret endpoint reference. It must not be used to encode a secret, connection-string credential, or access token.
