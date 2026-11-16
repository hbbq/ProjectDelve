const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const root=path.resolve(__dirname,'../ProjectDelve.Web/wwwroot');
const read=file=>JSON.parse(fs.readFileSync(path.join(root,file),'utf8'));
const assets=()=>read('presentation/cursors/catalog.json');
function harness({storage=new Map(),missing=new Set(),hotspots={},stalled=false,wrongSize=false}={}) {
  const images=[];
  const context=vm.createContext({localStorage:{getItem:key=>storage.get(key),setItem:(key,value)=>storage.set(key,value)}});
  for(const file of ['presentation-cursors.js','presentation-profiles.js','playtest-viewport.js'])
    vm.runInContext(fs.readFileSync(path.join(root,file),'utf8').replace(/^import .*\r?\n/gm,'').replace(/^export /gm,''),context);
  const api=vm.runInContext('({loadCursorAssets,validateCursorAssets,validateCursorHotspots,cursorSize,setCursorSize,onCursorPresentationChange,resolveCursorAsset,loadPresentationProfiles,resolvePresentationCursor,boardInteraction,boardCandidates})',context);
  const fetcher=async url=>({ok:true,json:async()=>url.endsWith('profiles.json')?read('presentation/profiles.json'):url.endsWith('hotspots.json')?{schemaVersion:1,hotspots}:assets()});
  const createImage=()=>{
    const image={};images.push(image);
    Object.defineProperty(image,'src',{set(url){image.url=url;const size=Number(url.match(/generated\/(\d+)\//)[1]);
      image.naturalWidth=wrongSize?1:size;image.naturalHeight=size;
      image.finish=()=>missing.has(url)?image.onerror():image.onload();
      if(!stalled)queueMicrotask(image.finish);
    }});return image;
  };
  return {api,images,storage,fetcher,createImage,async load(){await api.loadPresentationProfiles(fetcher);await api.loadCursorAssets(fetcher,createImage);}};
}
const custom=(id,size,fallback,x=size/2,y=x)=>`url("./presentation/cursors/generated/${size}/${id}.png") ${x} ${y}, ${fallback}`;

test('all profile asset references exist; choose/invalid remain interaction-only and originals supply all three sizes',()=>{
  const ids=new Set(assets().cursors.map(c=>c.id));
  assert.deepEqual([...ids].sort(),['attack','break','choose','collect','fireball','heal','invalid','move','open-door','operate','summon','swap']);
  for(const profile of read('presentation/profiles.json').profiles)if(typeof profile.cursor==='string'){
    assert.ok(ids.has(profile.cursor),profile.id);assert.ok(!['choose','invalid'].includes(profile.cursor));
  }
  for(const cursor of assets().cursors)for(const size of [32,40,48])assert.ok(fs.existsSync(path.join(root,cursor.images[size])));
});

test('shared assets resolve from actual profile IDs at32/40/48 with centered hotspots and persistent Client-only size',async()=>{
  const h=harness();await h.load();assert.equal(h.api.cursorSize(),40);
  for(const size of [32,40,48]){
    assert.equal(h.api.setCursorSize(size),true);
    for(const profile of ['standard-attack','clawAttack','targetUnitAttack','cleave'])
      assert.equal(h.api.resolvePresentationCursor(profile,'crosshair'),custom('attack',size,'crosshair'));
    assert.equal(h.api.resolvePresentationCursor('fireball','crosshair'),custom('fireball',size,'crosshair'));
    assert.equal(h.api.resolvePresentationCursor('adjacentUnitSummon','pointer'),custom('summon',size,'pointer'));
    assert.equal(h.storage.get('delve.cursor.size.v1'),String(size));
  }
  assert.equal(harness({storage:h.storage}).api.cursorSize(),48);
  assert.equal(h.api.setCursorSize(64),false);assert.equal(h.api.cursorSize(),48);
  assert.equal(harness({storage:new Map([['delve.cursor.size.v1','corrupt']])}).api.cursorSize(),40);
});

test('hotspot overrides scale in presentation while invalid override configuration retains centered defaults',async()=>{
  const h=harness({hotspots:{attack:[.25,.75]}});await h.load();
  for(const size of [32,40,48]){h.api.setCursorSize(size);assert.equal(h.api.resolveCursorAsset('attack','pointer'),custom('attack',size,'pointer',size/4,size*3/4));}
  for(const point of [[1,0],[-1,0],[0,NaN],[.5],{},[.5,.5,.5]])
    assert.throws(()=>h.api.validateCursorHotspots({schemaVersion:1,hotspots:{attack:point}}));
  const invalid=harness({hotspots:{attack:[2,0]}});await invalid.load();
  assert.equal(invalid.api.resolveCursorAsset('attack','pointer'),custom('attack',40,'pointer'));
});

test('unknown profiles and assets, failed images, incorrect dimensions and failed catalogs keep generic behavior',async()=>{
  const h=harness({missing:new Set(['./presentation/cursors/generated/40/fireball.png'])});await h.load();
  assert.equal(h.api.resolvePresentationCursor('fireball','crosshair'),'crosshair');
  assert.equal(h.api.resolvePresentationCursor('future.imported','pointer'),'pointer');
  assert.equal(h.api.resolveCursorAsset('../attack','pointer'),'pointer');
  assert.equal(h.api.resolvePresentationCursor('selfModifiers','pointer'),'pointer');
  h.api.setCursorSize(32);assert.equal(h.api.resolvePresentationCursor('fireball','pointer'),custom('fireball',32,'pointer'));
  const wrong=harness({wrongSize:true});await wrong.load();assert.equal(wrong.api.resolveCursorAsset('attack','pointer'),'pointer');
  const failed=harness();await failed.api.loadCursorAssets(async()=>{throw Error('offline');},failed.createImage);
  assert.equal(failed.api.resolveCursorAsset('choose','context-menu'),'context-menu');
  await failed.load();assert.equal(failed.api.resolveCursorAsset('choose','context-menu'),custom('choose',40,'context-menu'));
});

test('asset catalog rejects duplicates, unsafe or incorrect URLs and invalid IDs before constructing any image',()=>{
  const {api}=harness(),first=assets().cursors[0];
  for(const cursors of [[first,first],[{...first,id:'../attack'}],[{...first,id:'1attack'}],[{...first,images:{...first.images,40:'https://example.com/attack.png'}}],
    [{...first,images:{...first.images,40:'./presentation/cursors/generated/48/attack.png'}}],[{id:'attack'}]])
    assert.throws(()=>api.validateCursorAssets({schemaVersion:1,cursors}));
});

test('late image readiness and size switches notify feedback without blocking decisions or changing click classification',async()=>{
  const h=harness({stalled:true});await h.api.loadPresentationProfiles(h.fetcher);
  let changes=0;h.api.onCursorPresentationChange(()=>changes++);
  const loading=h.api.loadCursorAssets(h.fetcher,h.createImage);
  const choice={key:'opaque',presentationProfileId:'fireball',interaction:{kind:'Position'}};
  assert.equal(h.api.boardInteraction([choice],{targeting:true}).cursor,'crosshair');
  await new Promise(resolve=>setImmediate(resolve));
  assert.equal(h.images.length,36);
  for(const image of h.images)image.finish();await loading;
  assert.ok(changes>0);assert.equal(h.api.boardInteraction([choice]).choice,choice);
  assert.equal(h.api.boardInteraction([choice]).cursor,custom('fireball',40,'pointer'));
  h.api.setCursorSize(48);await Promise.resolve();assert.equal(h.api.boardInteraction([choice]).cursor,custom('fireball',48,'pointer'));
});

test('filtered source-aware choices determine asset; gesture/busy/chooser/invalid priority retains dispatch',async()=>{
  const h=harness();await h.load();
  const first={key:'native',accountingId:'unit:first',mechanicId:'fireball',presentationProfileId:'fireball',relevant:true,interaction:{kind:'Position',position:{x:1,y:1}}};
  const second={...first,key:'equipment',accountingId:'equipment:second',presentationProfileId:'adjacentUnitSummon',relevant:false};
  const state={physical:{figures:[]}},decision={candidates:[first,second]};
  const candidates=visible=>h.api.boardCandidates({cell:{x:1,y:1}},decision,state,{},visible);
  assert.equal(h.api.boardInteraction(candidates(()=>true)).cursor,custom('choose',40,'context-menu'));
  assert.equal(h.api.boardInteraction(candidates(c=>c.relevant)).cursor,custom('fireball',40,'pointer'));
  assert.equal(h.api.boardInteraction(candidates(c=>c.accountingId===second.accountingId)).cursor,custom('summon',40,'pointer'));
  assert.equal(h.api.boardInteraction([first,second],{dragging:true}).cursor,'grabbing');
  assert.equal(h.api.boardInteraction([first,second],{busy:true,dragging:true}).cursor,'default');
  assert.equal(h.api.boardInteraction([],{targeting:true}).cursor,custom('invalid',40,'not-allowed'));
  assert.equal(h.api.boardInteraction([],{targeting:true,hit:false}).cursor,'grab');
});
