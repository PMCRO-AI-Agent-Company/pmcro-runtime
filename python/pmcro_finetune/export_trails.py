"""Export sealed PMCR-O trail frames to canonical SFT JSONL."""
from __future__ import annotations
import argparse,json,sys
from collections import defaultdict
from pathlib import Path
from pmcro_finetune.checker_quality import validate_checker_row
ORDER=("orchestrator","planner","maker","checker","reflector")
SYSTEM={"orchestrator":"I Am the Orchestrator. Route Plan→Make→Check→Reflect; do not implement domain work.","planner":"I Am the Planner. Produce the minimum plan and success criteria.","maker":"I Am the Maker. Execute under an open Trail and emit re-readable evidence.","checker":"I Am the Checker. Independently re-read artifacts; never execute; return PASS or FAIL with evidence.","reflector":"I Am the Reflector. Return ACCEPT, RETRY, ESCALATE, or HALT plus NextSeedIntent."}
class ExportError(RuntimeError): pass

def read_frames(path:Path):
    if path.exists():
        if path.suffix.lower()=='.jsonl': return [json.loads(x) for x in path.read_text(encoding='utf-8').splitlines() if x.strip()]
        return [json.loads(path.read_text(encoding='utf-8'))]
    alt=path.with_suffix('.jsonl')
    if alt.exists(): return read_frames(alt)
    bundle=path.parents[2]/'frames-bundle.jsonl'
    if not bundle.exists(): return []
    repo_root=bundle.parent.parent.parent
    targets={path.relative_to(repo_root).as_posix(),alt.relative_to(repo_root).as_posix()}
    result=[]
    for line in bundle.read_text(encoding='utf-8').splitlines():
        if not line.strip() or line.startswith('NOTE:'): continue
        try:item=json.loads(line)
        except json.JSONDecodeError:continue
        if item.get('path') in targets and isinstance(item.get('content'),dict): result.append(item['content'])
    return result

def cycles(trail:Path):
    result=defaultdict(dict)
    for role in ORDER:
        for frame in read_frames(trail/'frames'/f'{role}-frame.json'):
            c=int(frame.get('cycleNumber',1))
            if role in result[c]: raise ExportError(f'duplicate {role} frame cycle {c}')
            result[c][role]=frame
    return dict(result)

def dispositions(trail:Path):
    p=trail/'trail.json'
    if not p.exists(): return {}
    return {int(x['cycleNumber']):str(x.get('disposition','')).upper() for x in json.loads(p.read_text(encoding='utf-8')).get('cycles',[]) if 'cycleNumber' in x}

def sealed(trail:Path):
    p=trail/'trail.json'
    return p.exists() and json.loads(p.read_text(encoding='utf-8')).get('status')=='sealed'

def admitted(frames,disp):
    return bool(disp=='COMPLETE' and len(frames)==5 and frames.get('checker',{}).get('status','').upper()=='PASS' and frames.get('reflector',{}).get('status','').upper()=='ACCEPT')

def rows(trail:Path):
    tid=json.loads((trail/'trail.json').read_text(encoding='utf-8')).get('trailId',trail.name); cs=cycles(trail); ds=dispositions(trail); out=[]
    for c in sorted(cs):
        f=cs[c]; quality='pass' if admitted(f,ds.get(c)) else 'fail'
        for role in ORDER:
            if role not in f: continue
            frame=f[role]; assistant={k:frame.get(k) for k in ('action','evidence','status')}
            if frame.get('disposition') is not None: assistant['disposition']=frame['disposition']
            out.append({'id':f'{tid}:{role}:{c}','source_trail':tid,'role':role,'messages':[{'role':'system','content':SYSTEM[role]},{'role':'user','content':frame.get('received') or f'Continue as {role} for this cycle.'},{'role':'assistant','content':json.dumps(assistant,ensure_ascii=False)}],'meta':{'disposition':ds.get(c,frame.get('status')),'sealed':True,'quality':quality}})
    return out

def export(trails:Path,out:Path,pass_only=True):
    if not trails.exists(): raise ExportError(f'trails dir missing: {trails}')
    out.parent.mkdir(parents=True,exist_ok=True); count=0
    with out.open('w',encoding='utf-8') as fh:
        for trail in sorted(trails.iterdir()):
            if not trail.is_dir() or not sealed(trail): continue
            cs=cycles(trail); ds=dispositions(trail)
            if pass_only and (not cs or not all(admitted(f,ds.get(c)) for c,f in cs.items())): continue
            if any(len(f)!=5 for f in cs.values()): raise ExportError(f'sealed trail {trail.name} has incomplete role frames')
            rs=rows(trail); findings=[f'{r["id"]}:{x.code}' for r in rs for x in validate_checker_row(r)]
            if findings: print('SKIP semantic Checker findings: '+', '.join(findings),file=sys.stderr); continue
            for r in rs: fh.write(json.dumps(r,ensure_ascii=False)+'\n'); count+=1
    return count

def main(argv=None):
    p=argparse.ArgumentParser();p.add_argument('--trails',type=Path,default=Path('.pmcro/trails'));p.add_argument('--out',type=Path,default=Path('data/pmcro-sft.jsonl'));p.add_argument('--no-pass-only',action='store_true');a=p.parse_args(argv)
    try:n=export(a.trails,a.out,not a.no_pass_only)
    except ExportError as e:print(f'FAIL: {e}',file=sys.stderr);return 4
    print(f'OK: wrote {n} rows → {a.out}');return 0
if __name__=='__main__':raise SystemExit(main())
