# pmcro_finetune (runtime worker)

Export sealed `.pmcro/trails` to SFT JSONL; optional dry-run train marker.

Full implementation may be synced from `pmcro-skills/python/pmcro_finetune`.
Until then, minimal CLI:

```bash
pip install -r requirements.txt
python -m pmcro_finetune export --trails ../../.pmcro/trails --out ../../data/pmcro-sft.jsonl
python -m pmcro_finetune train --data ../../data/pmcro-sft.jsonl --out ../../models/pmcro-sft-latest --dry-run
```

Aspire: `AddPythonApp("pmcro-finetune", "../../python/pmcro_finetune")` from AppHost.
