# Providers

Provider files declare authoritative implementations behind PMCRO capabilities.

PMCRO intentionally does not reimplement GitHub, Playwright, Docker, or terminal infrastructure. The registry points to the provider and policies determine whether the capability may be used.

For MCP providers, prefer the official provider and validate its current version, security model, and authentication mechanism before production use.
