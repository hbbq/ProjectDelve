"""Render original/baseline/current Rock maps from public generation output. Requires Pillow."""
import argparse,json
from collections import Counter
from pathlib import Path
from PIL import Image,ImageDraw
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('before');p.add_argument('after');p.add_argument('output');a=p.parse_args()
load=lambda path:{r['key']:r for r in map(json.loads,Path(path).read_text(encoding='utf-8').splitlines())}
before,after=load(a.before),load(a.after)
evidence={r['key']:r for r in map(json.loads,Path(__file__).with_name('evidence.jsonl').read_text(encoding='utf-8').splitlines()) if r['intensity']==3}
assert before.keys()==after.keys()
palette={'tree':'#234f3c','grass':'#a8bd87','dirt':'#b09163','solid-rock':'#555964','water':'#457fa7','wet-grass':'#7ca5a2','rocky-water':'#96b8bd','stepping-stones':'#adc2bc','stone-floor':'#ded5bf'}
size=360
image=Image.new('RGB',(1160,len(after)*420+70),'#faf8f0');draw=ImageDraw.Draw(image)
draw.text((15,12),'Rock only: Original / Previous Rock / Improved Rock',fill='black')
draw.text((15,32),'Gray Rock; green Trees; blue Water; yellow new floor reaching retained root; orange new floor outside it',fill='black')
for row,(key,current) in enumerate(after.items()):
    old=before[key];assert old['original']==current['original']
    for side,r in [('before',old),('after',current)]:assert r['fingerprint']==evidence[key][side]['fingerprint']
    original=current['original'];labels=current['originalLabels'];counts=Counter(i for i in labels if i>=0)
    root_label=min(counts,key=lambda i:(-counts[i],i)) if counts else -2
    anchor=labels.index(root_label) if counts else -1
    y0=70+row*420
    draw.text((15,y0),key,fill='black')
    for col,(title,terrain,region) in enumerate([('Original',original,labels),('Previous',old['terrain'],old['currentLabels']),('Improved',current['terrain'],current['currentLabels'])]):
        root=region[anchor] if anchor>=0 else -2;edits=0;reachable=0
        for i,t in enumerate(terrain):
            color=palette.get(t,'#dddddd')
            if original[i]=='solid-rock' and t!='solid-rock':
                accessible=root>=0 and region[i]==root;edits+=1;reachable+=accessible;color='#ffe270' if accessible else '#eb894e'
            x=15+col*385+(i%120)*3;y=y0+24+(i//120)*3
            draw.rectangle((x,y,x+2,y+2),fill=color)
        draw.text((15+col*385,y0+389),f'{title}: {edits} new floor, {reachable} root-connected',fill='black')
image.save(a.output)
