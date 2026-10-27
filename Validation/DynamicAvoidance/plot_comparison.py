from pathlib import Path
import json
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

ROOT=Path(__file__).resolve().parent
data={tag:json.loads((ROOT/(tag+'Main')/'report.json').read_text()) for tag in ['Baseline','Candidate']}
assert all(r['completed'] for r in data.values())
plt.rcParams.update({'font.family':'DejaVu Sans','font.size':10,'axes.spines.top':False,'axes.spines.right':False})
fig,axes=plt.subplots(2,2,figsize=(12,6.8),sharex='col',layout='constrained')
colors={'Baseline':'#9a4c37','Candidate':'#007f86'}
for col,(key,title) in enumerate([('cut_in-40-clear-v0','Cut-in / 40 km/h / clear'),('crossing-40-clear-v0','Crossing / 40 km/h / clear')]):
    for tag,report in data.items():
        case=next(c for c in report['cases'] if c['spec']['condition']==key)
        rows=case['telemetry'];trigger=case['triggerSeconds']
        ts=[r['seconds']-trigger for r in rows]
        label=('Previous' if tag=='Baseline' else 'Updated')+' — '+case['outcome']
        axes[0,col].plot(ts,[r['speed'] for r in rows],color=colors[tag],lw=2,label=label)
        axes[1,col].plot(ts,[r['offset'] for r in rows],color=colors[tag],lw=2)
        if case['collision']:
            t=case['elapsedSeconds']-trigger;v=case['impactSpeedKph']
            axes[0,col].scatter([t],[v],marker='x',s=70,lw=2,color=colors[tag],zorder=4)
            axes[0,col].annotate(f'Collision: {v:.1f} km/h',(t,v),xytext=(9,-20),textcoords='offset points',color=colors[tag],fontsize=9)
    axes[0,col].set_title(title,loc='left',fontweight='bold')
    axes[0,col].legend(loc='upper right',frameon=False,fontsize=8)
    axes[0,col].set_ylim(0,45)
    axes[1,col].set_ylim(0,4.8)
    axes[1,col].set_xlabel('Seconds after hazard trigger')
    for ax in axes[:,col]:
        ax.set_xlim(-.5,6);ax.axvline(0,color='#89939c',ls='--',lw=1);ax.grid(alpha=.16)
axes[0,0].set_ylabel('Rider speed (km/h)');axes[1,0].set_ylabel('Offset from route center (m)')
fig.suptitle('v2 detector held fixed · same physics-step hazard triggers',fontsize=15,fontweight='bold')
fig.supxlabel('Two selected main-suite conditions; full counts and remaining failures are in RESULTS.md.\nSimplified Unity vehicle model; these trajectories do not establish real motorcycle stability.',fontsize=9,color='#4a5660')
fig.savefig(ROOT/'dynamic-trajectories.png',dpi=160)
plt.close(fig)
print(ROOT/'dynamic-trajectories.png')
