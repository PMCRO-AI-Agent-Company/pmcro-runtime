# Runtime State

State is operational and ephemeral/durable execution context, not knowledge memory.

- `schemas/` — state contracts
- `runs/` — execution instances
- `checkpoints/` — resumable workflow checkpoints
- `locks/` — concurrency/ownership metadata

Do not place long-term agent knowledge here.
