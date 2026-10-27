import csv, hashlib, json, math, statistics
from collections import Counter
from pathlib import Path

ROOT=Path(__file__).resolve().parent
def read_run(name):
    p=ROOT/name/'report.json'
    if not p.exists(): return None
    r=json.loads(p.read_text(encoding='utf-8-sig'))
    if not r['completed']: return None
    assert r['error'] in ('',None) and all(c['valid'] for c in r['cases'])
    return r
def summarize(r):
    cs=r['cases']; collisions=[c for c in cs if c['collision']]
    negatives=[c for c in cs if c['spec']['scenario'] in ('clear','roadside','clear_road')]
    nframes=sum(c['inferenceFrames'] for c in cs)
    return dict(n=len(cs),outcomes=dict(Counter(c['outcome'] for c in cs)),
        scenarios={s:dict(Counter(c['outcome'] for c in cs if c['spec']['scenario']==s)) for s in sorted({c['spec']['scenario'] for c in cs})},
        negativeCases=len(negatives),falseInterventions=sum(c['interventions']>0 for c in negatives),
        collisionMeanImpactKph=statistics.mean(c['impactSpeedKph'] for c in collisions) if collisions else None,
        collisionMeanRelativeKph=statistics.mean(c['impactRelativeSpeedKph'] for c in collisions) if collisions else None,
        meanLatencyMs=sum(c['meanLatencyMs']*c['inferenceFrames'] for c in cs)/nframes if nframes else None,
        medianInferenceHz=statistics.median(c['inferenceFrames']/c['wallSeconds'] for c in cs),
        medianRenderFps=statistics.median(c['renderedFrames']/c['wallSeconds'] for c in cs),
        inferenceFrames=nframes,staleFrames=sum(c['staleInferenceFrames'] for c in cs),
        freshMatchedFrames=sum(c['freshMatchedFrames'] for c in cs),
        maxLateralAcceleration=max(c['maxAbsLateralAcceleration'] for c in cs),
        maxSuccessfulDynamicLateralAcceleration=max((c['maxAbsLateralAcceleration'] for c in cs if c['completed'] and c['spec']['scenario'] in ('cut_in','crossing')),default=0),
        visionExtendedInterventions=sum(c['visionAssistedInterventions'] for c in cs),
        visionExtendedSteps=sum(c['extendedRiskSteps'] for c in cs))
def compare(a,b):
    left={c['spec']['condition']:c for c in a['cases']}
    right={c['spec']['condition']:c for c in b['cases']}
    assert set(left)==set(right)
    pairs=[]
    for k in left:
        x,y=left[k],right[k]
        assert x['spec']==y['spec']
        position_error=math.dist(x['triggerRiderPosition'].values(),y['triggerRiderPosition'].values())
        pairs.append(dict(condition=k,scenario=x['spec']['scenario'],baseline=x['outcome'],candidate=y['outcome'],
            baselineImpactKph=x['impactSpeedKph'],candidateImpactKph=y['impactSpeedKph'],
            baselineRelativeImpactKph=x['impactRelativeSpeedKph'],candidateRelativeImpactKph=y['impactRelativeSpeedKph'],
            baselineTriggerTick=x['triggerTick'],candidateTriggerTick=y['triggerTick'],triggerPositionErrorM=position_error,
            baselineFirstIntervention=x['firstIntervention'],candidateFirstIntervention=y['firstIntervention'],
            baselineMaxLateralAcceleration=x['maxAbsLateralAcceleration'],candidateMaxLateralAcceleration=y['maxAbsLateralAcceleration']))
    return dict(changes=[p for p in pairs if p['baseline']!=p['candidate']],
        triggerTickMismatches=sum(p['baselineTriggerTick']!=p['candidateTriggerTick'] for p in pairs),
        maxTriggerPositionErrorM=max(p['triggerPositionErrorM'] for p in pairs),pairs=pairs)

if __name__=='__main__':
    names=['BaselinePilot']+[f'Candidate{i:02}Pilot' for i in range(1,6)]+['FinalPilot','BaselineMain','CandidateMain','BaselineRepeat','CandidateRepeat','BaselineHold','CandidateHold','BaselineHeldout','CandidateHeldout']
    reports={n:r for n in names if (r:=read_run(n))}
    result={'runs':{n:summarize(r) for n,r in reports.items()},'comparisons':{}}
    for a,b in [('BaselinePilot','FinalPilot'),('BaselineMain','CandidateMain'),('BaselineRepeat','CandidateRepeat'),('BaselineHold','CandidateHold'),('BaselineHeldout','CandidateHeldout')]:
        if a in reports and b in reports:
            comp=compare(reports[a],reports[b]);result['comparisons'][b]=comp
            with (ROOT/(b+'-paired.csv')).open('w',encoding='utf-8-sig',newline='') as f:
                w=csv.DictWriter(f,fieldnames=comp['pairs'][0].keys());w.writeheader();w.writerows(comp['pairs'])
    result['repeatChecks']={}
    for tag in ['Baseline','Candidate']:
        if tag+'Main' in reports and tag+'Repeat' in reports:
            main=reports[tag+'Main'].copy();main['cases']=[c for c in main['cases'] if c['spec']['scenario'] in ('cut_in','crossing')]
            result['repeatChecks'][tag]=compare(main,reports[tag+'Repeat'])
    (ROOT/'summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({n:{k:v for k,v in r.items() if k in ['n','outcomes','negativeCases','falseInterventions','scenarios']} for n,r in result['runs'].items()},ensure_ascii=False,indent=2))
