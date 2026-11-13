"""Summarize paired Session B dungeon runs without hiding failed generation or losses."""
import argparse
import json
from pathlib import Path

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('before'); p.add_argument('after'); p.add_argument('--output',required=True)
p.add_argument('--evidence-output',required=True)
a=p.parse_args()
def load(path):
    rows=[json.loads(l) for l in Path(path).read_text(encoding='utf-8').splitlines()]
    assert len(rows)==156
    return {r['code']:r for r in rows}
before,after=load(a.before),load(a.after)
assert before.keys()==after.keys()
def compact(r):
    if 'error' in r:return dict(error=r['error'])
    return dict(fingerprint=r['fingerprint'],deterministic=r['deterministic'],engineValid=r['engineValid'],
        earlyRockEdits=r['earlyRockEdits'],dungeons=r['dungeons'],stats=r['civilizationStats'],
        eligibleMasses=sum(s['eligible'] for s in r['survey']),packedRooms=sum(s['capacity'] for s in r['survey']),
        failureReasons=[f['code'] for f in r['planningFailures']],
        elapsedMs=r['elapsedMs'],allocatedBytes=r['allocatedBytes'])
def summarize(rows):
    good=[r for r in rows if 'error' not in r]
    ds=[d for r in good for d in r['dungeons']]
    return dict(cases=len(rows),errors=len(rows)-len(good),placements=len(ds),rooms=sum(d['rooms'] for d in ds),
        usable=sum(d['usable'] for d in ds),reusedFloor=sum(d['reused'] for d in ds),absorbingDungeons=sum(d['reused']>0 for d in ds),
        eligibleMasses=sum(s['eligible'] for r in good for s in r['survey']),packedRooms=sum(s['capacity'] for r in good for s in r['survey']),
        shellIntact=all(d['shellIntact'] for d in ds),stableRootAccess=all(d['stableRootAccess'] for d in ds),
        totalMs=sum(r['elapsedMs'] for r in good),allocatedBytes=sum(r['allocatedBytes'] for r in good),
        maxWork=max((r['civilizationStats']['work'] for r in good),default=0),
        maxAttempts=max((r['civilizationStats']['attempts'] for r in good),default=0))
groups={'all':list(before)}
for mode,bits in [('off','0000'),('forest','1000'),('rock','0100'),('both','1100')]:
    for routes in [False,True]:
        groups[f'{mode}-routes-{routes}']=[k for k,r in before.items() if r['group']=='session-b' and f'-B{bits}-' in k and ('-Q1-' in k)==routes]
result={'groups':{g:{'before':summarize([before[k] for k in keys]),'after':summarize([after[k] for k in keys])} for g,keys in groups.items()}}
result['losses']=[dict(code=k,before=len(before[k].get('dungeons',[])),after=len(after[k].get('dungeons',[]))) for k in before if len(after[k].get('dungeons',[]))<len(before[k].get('dungeons',[]))]
result['gains']=[dict(code=k,before=len(before[k].get('dungeons',[])),after=len(after[k].get('dungeons',[]))) for k in before if len(after[k].get('dungeons',[]))>len(before[k].get('dungeons',[]))]
result['opportunities']=[dict(code=k,before=compact(before[k]),after=compact(after[k])) for k in before if before[k]['group']=='opportunity']
result['historicalAccessFailures']=[dict(code=k,before=compact(before[k]),after=compact(after[k])) for k in before if '-Q1-' not in k and before[k]['group']=='session-b' and (k.startswith('WLD1-30x24') and k.endswith('0000000000003039') and ('-B0000-' in k or '-B1000-' in k) or k.startswith('WLD1-60x50') and k.endswith('0000000000000003'))]
assert len(result['historicalAccessFailures'])==6
Path(a.output).write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
Path(a.evidence_output).write_text(''.join(json.dumps(dict(code=k,before=compact(before[k]),after=compact(after[k])),separators=(',',':'))+'\n' for k in before),encoding='utf-8')
print(json.dumps(result['groups'],indent=2));print('Losses',len(result['losses']),'gains',len(result['gains']))
