"""Deterministic dataset manifest creation/validation for PMCR-O SFT artifacts."""
from __future__ import annotations
import hashlib,json
from pathlib import Path

REQUIRED=("manifest_version","dataset_id","source_refs","row_count","content_sha256","quality_gates","splits")
def content_sha256(path:Path)->str:
    return hashlib.sha256(path.read_bytes()).hexdigest()
def build_manifest(data:Path,dataset_id:str,source_refs:list[str],quality_gates:dict,splits:dict,chat_template:str|None=None,tokenizer_ref:str|None=None)->dict:
    rows=sum(1 for line in data.read_text(encoding='utf-8').splitlines() if line.strip())
    if rows<1: raise ValueError('dataset must contain at least one row')
    if sum(int(v) for v in splits.values())!=rows: raise ValueError('split counts must equal row_count')
    m={'manifest_version':'1.0','dataset_id':dataset_id,'source_refs':source_refs,'row_count':rows,'content_sha256':content_sha256(data),'quality_gates':quality_gates,'splits':splits}
    if chat_template is not None:m['chat_template']=chat_template
    if tokenizer_ref is not None:m['tokenizer_ref']=tokenizer_ref
    return m
def validate_manifest(manifest:dict,data:Path)->list[str]:
    errors=[f'missing {k}' for k in REQUIRED if k not in manifest]
    if errors:return errors
    if manifest['manifest_version']!='1.0':errors.append('unsupported manifest_version')
    if not isinstance(manifest['source_refs'],list) or not manifest['source_refs']:errors.append('source_refs must be non-empty')
    actual=sum(1 for line in data.read_text(encoding='utf-8').splitlines() if line.strip())
    if manifest['row_count']!=actual:errors.append(f'row_count mismatch: manifest={manifest["row_count"]} actual={actual}')
    digest=content_sha256(data)
    if manifest['content_sha256']!=digest:errors.append('content_sha256 mismatch')
    if not all(k in manifest['quality_gates'] for k in ('schema','checker','secret','disposition','dedup')):errors.append('quality_gates incomplete')
    splits=manifest['splits']
    if not all(k in splits for k in ('train','validation','test')):errors.append('splits incomplete')
    elif sum(int(v) for v in splits.values())!=actual:errors.append('split counts do not equal row_count')
    return errors
