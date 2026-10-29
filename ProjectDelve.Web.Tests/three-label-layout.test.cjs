const {test}=require('node:test');
const assert=require('node:assert/strict');
const {pathToFileURL}=require('node:url');
const path=require('node:path');
const moduleAt=()=>import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot/three-label-layout.js')).href);
const overlaps=(a,b)=>a.x<b.x+b.width&&a.x+a.width>b.x&&a.y<b.y+b.height&&a.y+a.height>b.y;
const visible=layout=>[...layout.values()].filter(r=>!r.hidden);
const view={width:500,height:360};
const label=(id,x=250,y=180,priority=0)=>({id,x,y,width:100,height:22,compactWidth:48,priority});

test('isolated labels sit above the head at their supplied screen size without mutating inputs',async()=>{
  const {layoutTabletopLabels}=await moduleAt(),c=label('a'),before=structuredClone(c);
  const p=layoutTabletopLabels([c],view).get('a');
  assert.equal(p.hidden,false);assert.equal(p.compact,false);assert.equal(p.x+p.width/2,c.x);assert.ok(p.y+p.height<c.y);assert.equal(p.height,22);
  assert.deepEqual(c,before);
});

test('dense labels retain the primary first and never overlap one another',async()=>{
  const {layoutTabletopLabels}=await moduleAt(),candidates=Array.from({length:24},(_,i)=>label(`unit-${i}`,250,180,i===19?4:0));
  const output=layoutTabletopLabels(candidates,view),primary=output.get('unit-19'),shown=visible(output);
  assert.equal(primary.hidden,false);assert.equal(primary.compact,false);assert.ok(shown.length<candidates.length);
  for(let i=0;i<shown.length;i++)for(let j=i+1;j<shown.length;j++)assert.equal(overlaps(shown[i],shown[j]),false);
  assert.ok(shown.every(r=>Math.abs(r.x+r.width/2-250)<=110&&Math.abs(r.y+r.height/2-180)<=100));
});

test('identity breaks equal-priority ties deterministically, independent of input order',async()=>{
  const {layoutTabletopLabels}=await moduleAt(),candidates=['z','a','m','b','n'].map(id=>label(id));
  const a=layoutTabletopLabels(candidates,view),b=layoutTabletopLabels([...candidates].reverse(),view);
  assert.deepEqual([...a],[...b]);assert.deepEqual([...a], [...layoutTabletopLabels(candidates,view)]);
});

test('a compact secondary plate fits a crowded narrow row before being hidden',async()=>{
  const {layoutTabletopLabels}=await moduleAt(),candidates=[label('primary',90,36,4),{...label('secondary',150,36),compactWidth:40}];
  const output=layoutTabletopLabels(candidates,{width:240,height:40}),a=output.get('primary'),b=output.get('secondary');
  assert.equal(a.hidden,false);assert.equal(a.compact,false);assert.equal(b.hidden,false);assert.equal(b.compact,true);assert.equal(overlaps(a,b),false);
});

test('narrow layouts use compact width without clipping or reducing text height',async()=>{
  const {layoutTabletopLabels}=await moduleAt(),c=label('unit',43,120);
  const p=layoutTabletopLabels([c],{width:86,height:180}).get('unit');
  assert.equal(p.hidden,false);assert.equal(p.compact,true);assert.equal(p.height,c.height);assert.ok(p.x>=0&&p.x+p.width<=86);
});

test('panels and other miniature bodies reserve space; own body does not displace its head label',async()=>{
  const {layoutTabletopLabels}=await moduleAt(),c=label('primary',250,180,4),body={x:200,y:140,width:100,height:70};
  const other={...label('other',440,300),bodyRect:body};
  const output=layoutTabletopLabels([c,other],view),p=output.get('primary');
  assert.equal(p.hidden,false);assert.equal(overlaps(p,body),false);
  const self=layoutTabletopLabels([{...c,bodyRect:body}],view).get('primary');assert.ok(self.y>p.y);
  const blocked={x:180,y:80,w:140,h:120},underPanel=layoutTabletopLabels([c],view,[blocked]).get('primary');assert.equal(underPanel.hidden,true);
});

test('distant offscreen anchors and fully occupied areas hide labels instead of jumping or covering information',async()=>{
  const {layoutTabletopLabels}=await moduleAt(),candidates=[label('top',250,-200,4),label('left',-200,180),label('bottom',250,1000)];
  assert.ok([...layoutTabletopLabels(candidates,view).values()].every(r=>r.hidden));
  assert.equal(layoutTabletopLabels([label('a')],view,[{x:0,y:0,width:500,height:360}]).get('a').hidden,true);
});

test('near-top labels stay inside the viewport and use a side slot rather than cover their own body',async()=>{
  const {layoutTabletopLabels}=await moduleAt(),c=label('primary',250,5,4);
  const clamped=layoutTabletopLabels([c],view).get('primary');assert.equal(clamped.hidden,false);assert.ok(clamped.y>=0);
  const body={x:230,y:0,width:40,height:100},beside=layoutTabletopLabels([{...c,bodyRect:body,compactWidth:undefined}],view).get('primary');
  assert.equal(beside.hidden,false);assert.equal(overlaps(beside,body),false);assert.ok(beside.x>=0&&beside.x+beside.width<=view.width);
});

test('a foreground label can sit beside its body when all three above-head rows contain other miniatures',async()=>{
  const {layoutTabletopLabels}=await moduleAt(),body={x:230,y:170,width:40,height:90},back={x:180,y:70,width:140,height:108};
  const obstacle={...label('rear'),width:0,bodyRect:back};
  for(const priority of [0,4]){
    const c={...label('front',250,180,priority),bodyRect:body,compactWidth:priority?undefined:40};
    const p=layoutTabletopLabels([obstacle,c],view).get('front');assert.equal(p.hidden,false);assert.equal(p.compact,!priority);
    assert.equal(overlaps(p,body),false);assert.equal(overlaps(p,back),false);assert.ok(Math.abs(p.x+p.width/2-c.x)<=110);
  }
});

test('lateral fallback remains blocked by panels and cannot jump across oversized body bounds',async()=>{
  const {layoutTabletopLabels}=await moduleAt(),c={...label('large',250,180,4),compactWidth:undefined,bodyRect:{x:40,y:170,width:420,height:100}};
  const blocked=[{x:0,y:0,width:500,height:180},{x:0,y:275,width:500,height:85}];
  assert.equal(layoutTabletopLabels([c],view,blocked).get('large').hidden,true);
});

test('a crowded foreground figure gets one full nameplate below its base when upper and lateral slots are blocked',async()=>{
  const {layoutTabletopLabels}=await moduleAt(),body={x:230,y:170,width:40,height:90},neighbors={x:100,y:60,width:300,height:190};
  const c={...label('front',250,180,4),bodyRect:body},obstacle={...label('neighbors'),width:0,bodyRect:neighbors};
  const p=layoutTabletopLabels([c,obstacle],view).get('front');
  assert.equal(p.hidden,false);assert.equal(p.compact,false);assert.equal(p.x+p.width/2,c.x);assert.ok(p.y>body.y+body.height);
  assert.equal(overlaps(p,body),false);assert.equal(overlaps(p,neighbors),false);
  assert.equal(layoutTabletopLabels([c,obstacle],view,[{x:200,y:260,width:100,height:50}]).get('front').hidden,true);
  assert.equal(layoutTabletopLabels([c,obstacle],{width:500,height:280}).get('front').hidden,true);
});

test('invalid and hidden layouts return explicit hidden records without unbounded offscreen indexing',async()=>{
  const {layoutTabletopLabels}=await moduleAt(),c=label('a');
  assert.equal(layoutTabletopLabels([c],{width:0,height:0}).get('a').hidden,true);
  assert.equal(layoutTabletopLabels([{...c,x:75,y:10}],{width:150,height:20}).get('a').hidden,true);
  assert.equal(layoutTabletopLabels([{...c,x:NaN}],view).get('a').hidden,true);
  const p=layoutTabletopLabels([c],view,[{x:-1e12,y:-1e12,width:1,height:1}]).get('a');assert.equal(p.hidden,false);
});
