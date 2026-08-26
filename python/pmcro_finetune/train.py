"""Deterministic SFT admission validator; real training remains explicit opt-in."""
from __future__ import annotations
import argparse,hashlib,json,sys
from pathlib import Path
from pmcro_finetune.checker_quality import validate_checker_row
from pmcro_finetune.secret_quality import validate_row_secrets
ROLES={'orchestrator','planner','maker','checker','reflector'}; MSG=('system','user','assistant'); REQUIRED={'id','source_trail','role','messages','meta'}
def validate_row(row,line):
    e=[]; missing=REQUIRED-row.keys()
    if missing:return [f'line {line}: missing keys {sorted(missing)}']
    if not isinstance(row['id'],str) or not row['id']:e.append(f'line {line}: invalid id')
    if row['role'] not in ROLES:e.append(f'line {line}: unsupported role {row["role"]!r}')
    m=row['messages']
    if not isinstance(m,list) or len(m)!=3:e.append(f'line {line}: messages must contain exactly 3 entries')
    else:
        if tuple(x.get('role') if isinstance(x,dict) else None for x in m)!=MSG:e.append(f'line {line}: message roles must be {MSG}')
        for i,x in enumerate(m):
            if not isinstance(x,dict) or not isinstance(x.get('content'),str) or not x['content'].strip():e.append(f'line {line}: empty message {i}')
    meta=row['meta']
    if not isinstance(meta,dict) or meta.get('sealed') is not True or meta.get('quality')!='pass':e.append(f'line {line}: meta must be sealed=true, quality=pass')
    e += [f'line {line}: {x.code}: {x.message}' for x in validate_checker_row(row)]
    e += [f'line {line}: {x}' for x in validate_row_secrets(row)]
    return e
def digest(row):return hashlib.sha256(json.dumps(row,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode()).hexdigest()
def validate_jsonl(path):
    errors=[];ids={};hashes={};n=0
    for line_no,line in enumerate(path.read_text(encoding='utf-8').splitlines(),1):
        if not line.strip():continue
        n+=1
        try:row=json.loads(line)
        except json.JSONDecodeError as x:errors.append(f'line {line_no}: invalid JSON: {x}');continue
        errors+=validate_row(row,line_no)
        rid=row.get('id') if isinstance(row,dict) else None
        if rid in ids:errors.append(f'line {line_no}: DUPLICATE_ID {rid!r}; first {ids[rid]}')
        elif isinstance(rid,str):ids[rid]=line_no
        h=digest(row)
        if h in hashes:errors.append(f'line {line_no}: DUPLICATE_ROW; first {hashes[h]}')
        else:hashes[h]=line_no
    if errors:raise ValueError('\n'.join(errors))
    return n
def main(argv=None):
    p=argparse.ArgumentParser();p.add_argument('--data',type=Path,required=True);p.add_argument('--out',type=Path,default=Path('models/pmcro-sft-latest'));p.add_argument('--base-model',default='');p.add_argument('--max-steps',type=int,default=50);p.add_argument('--dry-run',action='store_true');p.add_argument('--execute',action='store_true');a=p.parse_args(argv)
    if not a.data.exists():print(f'FAIL: data missing: {a.data}',file=sys.stderr);return 2
    try:n=validate_jsonl(a.data)
    except ValueError as x:print(f'FAIL: training admission gate:\n{x}',file=sys.stderr);return 1
    a.out.mkdir(parents=True,exist_ok=True); marker=a.out/'TRAIN_JOB.json'; marker.write_text(json.dumps({'status':'dry_run_complete' if not a.execute else 'execute_not_implemented','rows':n,'data':str(a.data),'base_model':a.base_model or None,'max_steps':a.max_steps},indent=2)+'\n',encoding='utf-8');print(f'OK: validated {n} canonical SFT rows; wrote {marker}')
    return 3 if a.execute else 0
if __name__=='__main__':raise SystemExit(main())
