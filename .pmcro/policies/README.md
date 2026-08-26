# Policies

Policies are governance contracts evaluated by the Orchestrator and runtime before capability execution.

`execution.yaml` controls capability risk, approval, and evidence requirements.

`security.yaml` controls trust, secrets, network, provenance, and approval boundaries.

`permissions.yaml` maps roles and capability operations to the minimum authority needed for a governed cycle.

`network.yaml` defines egress and browser-network constraints independently from provider configuration.

Policies are intentionally separate from provider implementation and from workflow state.
