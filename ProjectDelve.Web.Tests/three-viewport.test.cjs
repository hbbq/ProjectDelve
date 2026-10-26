const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {pathToFileURL}=require('node:url');
const moduleAt=name=>import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot',name)).href);

test('viewport handoff waits for the authoritative event checkpoint and preserves overlay identity',async()=>{
  const {createViewportRouter}=await moduleAt('playtest-viewport.js');
  const overlay={dice:{faces:['Hit']},decision:{key:'opaque'},history:['once']},calls=[];
  const view=name=>({host:{append:node=>calls.push([name,'overlay',node])},mode:'enhanced',setActive:value=>calls.push([name,value]),dispose:()=>calls.push([name,'dispose'])});
  const dom=view('dom'),three=view('three');let loads=0;
  const router=createViewportRouter({dom,overlay,loadThree:async()=>{loads++;return three;}});
  router.beginEvent();await router.request('three');assert.equal(router.active,dom);assert.equal(loads,0);
  await router.checkpoint();assert.equal(router.active,three);assert.equal(calls[1][2],overlay);
  for(let i=0;i<10;i++){await router.request('classic');await router.request('three');}
  assert.equal(loads,1);assert.deepEqual(overlay.history,['once']);assert.deepEqual(overlay.dice.faces,['Hit']);assert.equal(overlay.decision.key,'opaque');
  router.dispose();assert.equal(calls.filter(c=>c[1]==='dispose').length,2);
});
test('failed initialization retains DOM and a superseding selection cannot expose the wrong renderer',async()=>{
  const {createViewportRouter}=await moduleAt('playtest-viewport.js');const errors=[];
  const dom={host:{append(){}},mode:'enhanced',setActive(){throw Error('DOM must stay active');}};
  const router=createViewportRouter({dom,overlay:{},loadThree:async()=>{throw Error('no WebGL');},onError:e=>errors.push(e.message)});
  await router.request('three');assert.equal(router.active,dom);assert.deepEqual(errors,['no WebGL']);
  let resolve;const pending=new Promise(r=>resolve=r);let activations=0;
  const second=createViewportRouter({dom,overlay:{},loadThree:()=>pending});
  const first=second.request('three');const back=second.request('enhanced');resolve({host:{append(){}},setActive(){activations++;}});await first;await back;
  assert.equal(second.active,dom);assert.equal(activations,0);
});
test('activation failure rolls back both the working viewport and shared overlay',async()=>{
  const{createViewportRouter}=await moduleAt('playtest-viewport.js');const overlay={},owners=[],states=[];
  const dom={mode:'enhanced',host:{append:()=>owners.push('dom')},setActive:value=>states.push(value)};
  const broken={host:{append:()=>owners.push('broken')},setActive:value=>{if(value)throw Error('lost');}};
  const errors=[];const router=createViewportRouter({dom,overlay,loadThree:async()=>broken,onError:e=>errors.push(e.message)});
  await router.request('three');assert.equal(router.active,dom);assert.deepEqual(owners,['broken','dom']);assert.deepEqual(states,[false,true]);assert.deepEqual(errors,['lost']);
});
test('an SSE event beginning during a lazy import postpones the handoff to its after-frame',async()=>{
  const{createViewportRouter}=await moduleAt('playtest-viewport.js');let resolve;
  const pending=new Promise(r=>resolve=r),dom={mode:'enhanced',host:{append(){}},setActive(){}},three={host:{append(){}},setActive(){}};
  const router=createViewportRouter({dom,overlay:{},loadThree:()=>pending});const loading=router.request('three');router.beginEvent();resolve(three);await loading;
  assert.equal(router.active,dom);assert.equal(router.inspect().pending,'three');await router.checkpoint();assert.equal(router.active,three);
});
test('candidate mapping preserves opaque order, None, Cell accuracy under large Units and open Door targets',async()=>{
  const{boardCandidates}=await moduleAt('playtest-viewport.js');
  const state={physical:{figures:[{id:'large',position:{x:1,y:1}}]}},geometry={large:{occupiedCells:[{x:1,y:1},{x:2,y:1},{x:1,y:2},{x:2,y:2}]}};
  const a={key:'z',interaction:{kind:'Position',position:{x:2,y:2}}},b={key:'a',interaction:{kind:'Unit',unitId:'large'}},none={key:null,interaction:{kind:'Unit',unitId:'large'}},decision={candidates:[a,b],noneChoice:none};
  assert.deepEqual(boardCandidates({cell:{x:2,y:2}},decision,state,geometry).map(c=>c.key),['z','a',null]);
  assert.deepEqual(boardCandidates({cell:{x:1,y:2}},decision,state,geometry).map(c=>c.key),['a',null]);
  assert.deepEqual(boardCandidates({cell:{x:2,y:2}},decision,state,geometry,c=>c.key!=='z').map(c=>c.key),['a',null]);
  const door={a:{x:0,y:0},b:{x:1,y:0},kind:'OpenDoor'},candidate={key:'door',interaction:{kind:'Door',door}};
  assert.deepEqual(boardCandidates({door:{...door,a:door.b,b:door.a}},{candidates:[candidate]},state,geometry),[candidate]);
});
test('LOD hysteresis and logarithmic camera interpolation retain readable stable levels',async()=>{
  const{tokenDetail,interpolatePose}=await moduleAt('playtest-viewport.js');
  for(const[d,l]of[[20,0],[28,1],[48,2],[80,3]])assert.equal(tokenDetail(d),l);
  assert.equal(tokenDetail(81,2),2);assert.equal(tokenDetail(84,2),3);assert.equal(tokenDetail(77,3),3);assert.equal(tokenDetail(75,3),2);
  const pose=interpolatePose({x:0,y:0,scale:10},{x:10,y:20,scale:100},.5);assert.equal(pose.x,8.75);assert.ok(pose.scale>10&&pose.scale<100);
  assert.ok(Math.abs(interpolatePose(pose,{x:1,y:2,scale:20},1).scale-20)<1e-10);
});

class Element {
  constructor(tag='div'){this.tagName=tag;this.children=[];this.style={setProperty(k,v){this[k]=v;}};this.dataset={};this.listeners={};this.attributes={};this.className='';this.hidden=false;this.clientWidth=900;this.clientHeight=600;this.width=300;this.height=150;this.value='';this.classList={add:()=>{},remove:()=>{}};}
  append(...nodes){for(const node of nodes){node.remove?.();node.parentElement=this;this.children.push(node);}}
  prepend(node){this.append(node);this.children.unshift(this.children.pop());}
  remove(){if(this.parentElement)this.parentElement.children=this.parentElement.children.filter(n=>n!==this);this.parentElement=null;}
  replaceChildren(...nodes){for(const child of this.children)child.parentElement=null;this.children=[];this.append(...nodes);}
  set innerHTML(value){this.replaceChildren(...[...value.matchAll(/<span class="([^"]+)">/g)].map(m=>{const n=new Element('span');n.className=m[1];return n;}));}
  setAttribute(k,v){this.attributes[k]=v;}
  querySelector(selector){return this.children.flatMap(n=>[n,n.querySelector(selector)]).find(n=>n&&(selector[0]==='.'?n.className.split(' ').includes(selector.slice(1)):n.id===selector.slice(1)));}
  getBoundingClientRect(){return{left:0,top:0,width:this.clientWidth,height:this.clientHeight};}
  addEventListener(type,fn,options={}){(this.listeners[type]??=[]).push(fn);options.signal?.addEventListener('abort',()=>{this.listeners[type]=this.listeners[type].filter(f=>f!==fn);},{once:true});}
  emit(type,event={}){for(const fn of this.listeners[type]??[])fn({type,target:this,preventDefault(){},...event});}
  getContext(){return{clearRect(){},strokeText(){},fillText(){}};}
  setPointerCapture(id){this.capture=id;}
  hasPointerCapture(id){return this.capture===id;}
  releasePointerCapture(){this.capture=null;}
  focus(){}
}
function fixture(size=8,side=null){
  const figures=[{id:'hero',position:{x:2,y:2},posture:'Lying'},{id:'large',position:{x:4,y:3},posture:'Upright'}];
  return{metadata:{sessionId:'one',round:1,assignments:[{sideId:'blue',controller:'Human'}]},visualSeed:'test',state:{round:1,currentUnitId:'hero',types:[{id:'test',hp:5}],units:figures.map(f=>({id:f.id,typeId:'test',sideId:'blue',currentHp:3})),physical:{board:{width:size,height:size,terrain:[],edges:[]},figures},exploredCells:side?{blue:[{x:2,y:2},{x:4,y:3}]}:undefined},presentation:{viewingSideId:side,cards:{hero:{displayName:'Hero'},large:{individualName:'An Authored Large Name',displayName:'Large'}},figures:{hero:{cellSpan:1,occupiedCells:[{x:2,y:2}]},large:{cellSpan:2,occupiedCells:[{x:4,y:3},{x:5,y:3},{x:4,y:4},{x:5,y:4}]}},machines:[],objectives:[]}};
}
async function harness(frame=fixture()){
  const frames=new Map(),requests=[],rendered=[],picked=[],hovered=[],failures=[];let handle=0,disposed=0;
  const saved={};for(const key of ['document','ResizeObserver','requestAnimationFrame','cancelAnimationFrame','addEventListener','matchMedia'])saved[key]=globalThis[key];
  const controls={};for(const id of ['root','mode','span','follow','min','max','margin','side','side-label','resume','reset','in','out','status','objectives'])controls[id]=new Element();controls.root.append(controls.status);
  const host=new Element();
  globalThis.document={createElement:tag=>new Element(tag),getElementById:()=>null};globalThis.ResizeObserver=class{observe(){}disconnect(){}};
  globalThis.requestAnimationFrame=fn=>{frames.set(++handle,fn);return handle;};globalThis.cancelAnimationFrame=id=>frames.delete(id);globalThis.addEventListener=()=>{};
  globalThis.matchMedia=()=>({matches:false,addEventListener(){}});
  const THREE=await moduleAt('vendor/three/0.180.0/build/three.module.js'),{createThreeViewport}=await moduleAt('three-viewport.js');
  const renderer={shadowMap:{},info:{render:{calls:0,triangles:0},memory:{textures:0,geometries:0}},setClearColor(){},setSize(w,h){host.children.find(n=>n.tagName==='canvas').width=w;host.children.find(n=>n.tagName==='canvas').height=h;},render(scene,camera){rendered.push({scene,camera});let calls=0;scene.traverse(n=>{if(n.isMesh||n.isLine)calls++;});this.info.render.calls=calls;},dispose(){disposed++;}};
  const viewport=createThreeViewport({host,controls,animate:()=>true,onPick:hit=>picked.push(hit),onHover:id=>hovered.push(id),onFailure:error=>failures.push(error.message),rendererFactory:()=>renderer,textureLoader:{load(url,ready,progress,error){requests.push({url,ready,error});}}});
  viewport.setActive(true);viewport.renderFrame(frame);
  const flush=(time=performance.now())=>{const todo=[...frames.values()];frames.clear();for(const fn of todo)fn(time);};flush();
  return{viewport,host,controls,frames,requests,rendered,picked,hovered,failures,THREE,flush,renderer,get disposed(){return disposed;},cleanup(){viewport.dispose();for(const[key,value]of Object.entries(saved))if(value===undefined)delete globalThis[key];else globalThis[key]=value;}};
}
test('real scene maps Cells/Edges, reuses keyed geometry, preserves fully observable large Units over fog',async()=>{
  const{cellCenter,edgePlacement,observedTerrain}=await moduleAt('three-viewport.js');assert.deepEqual(cellCenter({x:3,y:4},2),{x:4,z:5});
  assert.deepEqual(edgePlacement({a:{x:1,y:1},b:{x:2,y:1}}),{x:2,z:1.5,vertical:true});
  const f=fixture(8,'blue');assert.equal(observedTerrain(f).kindAt(0,0),'Unknown');assert.equal(observedTerrain(f).kindAt(2,2),'StoneFloor');
  const h=await harness(f);try{
    const scene=h.rendered.at(-1).scene,terrain=scene.getObjectByName('terrain'),fog=scene.getObjectByName('fog'),units=scene.getObjectByName('units');
    assert.equal(terrain.children.reduce((n,b)=>n+b.count,0),2);assert.equal(fog.children[0].count,62);assert.equal(units.children.length,2);assert.equal(units.children[1].position.x,5);
    const fogPose=new h.THREE.Matrix4();fog.children[0].getMatrixAt(0,fogPose);assert.ok(units.children[1].position.y>fogPose.elements[13]);
    assert.equal(h.host.querySelector('.three-token-info').title.includes('Lying'),true);
    assert.ok(h.requests.every(r=>!r.url.includes('solid-rock')));const first=terrain.children[0];h.viewport.renderFrame(f);h.flush();assert.equal(terrain.children[0],first);
    const count=h.viewport.inspect().renderCount;h.flush();assert.equal(h.viewport.inspect().renderCount,count);h.viewport.setActive(false);h.viewport.renderFrame(f);assert.equal(h.frames.size,0);
  }finally{h.cleanup();}assert.equal(h.disposed,1);
});
test('all catalog terrain and objects render deterministically; cached textures fail safely and load once',async()=>{
  const{boardVisuals}=await moduleAt('board-visuals.js');const f=fixture(12);
  f.state.physical.board.terrain=Object.keys(boardVisuals.terrain).filter(id=>id!=='default'&&id!=='Unknown').map((kind,i)=>({kind,position:{x:i%12,y:Math.floor(i/12)},direction:'East'}));
  f.state.resources=[{position:{x:0,y:2},type:{id:'diamond',name:'Diamond'}}];f.state.cellMarkers=[{position:{x:1,y:2},type:{id:'banana-peel',name:'Peel'}}];f.presentation.machines=[{id:'press',typeId:'diamond-press',name:'Press',position:{x:2,y:2},resourceStorage:[]}];
  f.state.physical.board.edges=['Wall','ClosedDoor','OpenDoor'].map((kind,i)=>({kind,a:{x:i,y:3},b:{x:i,y:4}}));
  const h=await harness(f);try{
    assert.ok(h.requests.some(r=>r.url.includes('solid-rock')));assert.ok(h.requests.some(r=>r.url.endsWith('.svg')));assert.ok(h.requests.some(r=>r.url.includes('fruit-tree')));
    assert.equal(new Set(h.requests.map(r=>r.url)).size,h.requests.length);const count=h.requests.length;h.viewport.renderFrame(f);assert.equal(h.requests.length,count);
    h.requests[0].error();h.requests[1].ready(new h.THREE.Texture());h.flush();assert.ok(Object.values(h.viewport.inspect().textureStates).includes('missing'));
    assert.equal(h.rendered.at(-1).scene.getObjectByName('edges').children.length,3);assert.ok(h.rendered.at(-1).scene.getObjectByName('items').children.length>=3);
  }finally{h.cleanup();}
});
test('ray picking uses the current camera, exact Cells, 2x2 footprints and transparent open Door proxies',async()=>{
  const f=fixture(),h=await harness(f);try{
    h.viewport.camera.setMode('full');h.viewport.settle();h.flush();
    const p=h.viewport.project({x:5.5,y:4.5});assert.equal(h.viewport.pick(p).unitId,'large');assert.deepEqual(h.viewport.pick(p).cell,{x:5,y:4});
    const door={a:{x:2,y:2},b:{x:3,y:2},kind:'OpenDoor'};h.viewport.showInteraction({candidates:[{key:'open',interaction:{kind:'Door',door}}]});h.flush();assert.deepEqual(h.viewport.pick(h.viewport.project({x:3,y:2.5})).door,door);
    h.viewport.camera.manual({x:4,y:4,scale:90},true);h.flush(performance.now()+100);const mid=h.viewport.project({x:1.5,y:1.5});assert.deepEqual(h.viewport.pick(mid).cell,{x:1,y:1});
    const canvas=h.host.children.find(n=>n.tagName==='canvas');canvas.emit('pointerdown',{button:0,pointerId:1,clientX:450,clientY:300});canvas.emit('pointermove',{pointerId:1,clientX:500,clientY:300});canvas.emit('pointerup',{pointerId:1,clientX:500,clientY:300});assert.equal(h.picked.length,0);
    canvas.emit('pointerdown',{button:0,pointerId:2,clientX:450,clientY:300});canvas.emit('pointerup',{pointerId:2,clientX:450,clientY:300});assert.equal(h.picked.length,1);
  }finally{h.cleanup();}
});
test('observed segment playback masks transient tokens and never joins hidden gaps; cancellation clears motion',async()=>{
  const f=fixture(8,'blue'),h=await harness(f);try{
    const shown=[];const event={role:'Movement',unitId:'hero',movingFigure:{typeId:'test',sideId:'blue',name:'Hero',cellSpan:1,posture:'Upright'},movementSegments:[[{x:1,y:2},{x:2,y:2}],[{x:5,y:2},{x:6,y:2}]]};
    await h.viewport.present(event,{kind:'MovementObserved'},{skipping:()=>false,pause:async()=>{h.flush(performance.now()+230);const scene=h.rendered.at(-1).scene;shown.push(scene.getObjectByName('movement').children[0].position.x);assert.equal(scene.getObjectByName('units').children[0].visible,false);const fogPose=new h.THREE.Matrix4();scene.getObjectByName('fog').children[0].getMatrixAt(0,fogPose);assert.ok(scene.getObjectByName('movement').children[0].position.y<fogPose.elements[13]);}});
    assert.deepEqual(shown,[2.5,6.5]);assert.equal(h.viewport.inspect().transient,false);assert.equal(h.rendered.at(-1).scene.getObjectByName('movement').children.length,0);
    let pauses=0;await h.viewport.present(event,{}, {skipping:()=>false,pause:async()=>{pauses++;h.viewport.cancel();}});assert.equal(pauses,1);assert.equal(h.viewport.inspect().transient,false);
  }finally{h.cleanup();}
});
test('camera modes, overflow, manual suspension and safe follow use observed policy',async()=>{
  const f=fixture(50,'blue'),h=await harness(f);try{
    const c=h.viewport.camera;c.setMode('full');h.viewport.settle();assert.equal(c.inspect().target.scale,12);
    c.setMode('auto');c.stable({unitId:'hero',candidates:[{boardVisual:{kind:'move'},placementCells:[{x:49,y:49}]}]});assert.equal(c.inspect().overflow,true);assert.ok(c.inspect().fittedSpan<=15);
    c.zoom(1.2);assert.equal(c.inspect().suspended,true);c.stable({unitId:'hero',candidates:[]});assert.equal(c.inspect().suspended,true);c.resume();assert.equal(c.inspect().suspended,false);
    const target=c.inspect().target;c.occurrence({kind:'MovementCompleted'},{role:'Movement',unitId:'hidden',movementSegments:[[{x:45,y:45},{x:46,y:45}]],movingFigure:{sideId:'red',cellSpan:1}});c.movementStep({x:46,y:45});assert.deepEqual(c.inspect().target,target);
    c.resize(400,700);h.viewport.settle();assert.ok(Number.isFinite(c.inspect().current.scale));
  }finally{h.cleanup();}
});
test('context loss stops frames, refuses unsafe reactivation, and restoration reuses the scene',async()=>{
  const h=await harness();try{const canvas=h.host.children.find(n=>n.tagName==='canvas');canvas.emit('webglcontextlost');assert.equal(h.viewport.inspect().lost,true);assert.equal(h.frames.size,0);assert.equal(h.failures.length,1);
    h.viewport.setActive(false);assert.throws(()=>h.viewport.setActive(true),/WebGL is unavailable/);canvas.emit('webglcontextrestored');h.viewport.setActive(true);h.flush();assert.equal(h.viewport.inspect().lost,false);assert.equal(h.disposed,0);
    h.viewport.dispose();assert.equal(h.disposed,1);assert.equal(canvas.listeners.pointerdown.length,0);h.viewport.dispose();assert.equal(h.disposed,1);
  }finally{h.cleanup();}
});
test('touch pinch suppresses tap, keyboard focuses exact Cells, and stable labels obey LOD/HP/culling',async()=>{
  const f=fixture();f.state.types[0].hp=1;const h=await harness(f);try{const canvas=h.host.children.find(n=>n.tagName==='canvas');
    canvas.emit('pointerdown',{button:0,pointerId:1,pointerType:'touch',clientX:300,clientY:300});canvas.emit('pointerdown',{button:0,pointerId:2,pointerType:'touch',clientX:400,clientY:300});canvas.emit('pointermove',{pointerId:2,clientX:450,clientY:300});canvas.emit('pointerup',{pointerId:2,clientX:450,clientY:300});canvas.emit('pointerup',{pointerId:1,clientX:300,clientY:300});assert.equal(h.picked.length,0);assert.equal(h.viewport.camera.inspect().suspended,true);
    h.host.emit('keydown',{key:'ArrowRight'});h.host.emit('keydown',{key:'Enter'});assert.deepEqual(h.picked[0].cell,{x:3,y:2});
    h.viewport.camera.manual({x:2.5,y:2.5,scale:110});h.flush();const info=h.host.querySelector('.three-token-info');assert.equal(info.children[0].textContent,'');assert.equal(info.dataset.detail,'3');assert.equal(info.title.includes('Lying'),true);
    h.viewport.camera.manual({x:7,y:7,scale:300});h.flush();assert.equal(info.hidden,true);
  }finally{h.cleanup();}
});
test('50x50 terrain and unknown covers use bounded material batches and remain idle after settling',async()=>{
  const f=fixture(50);const h=await harness(f);try{const scene=h.rendered.at(-1).scene,terrain=scene.getObjectByName('terrain');assert.equal(terrain.children.reduce((n,b)=>n+b.count,0),2500);assert.ok(terrain.children.length<=3);
    for(let i=0;i<12;i++){h.viewport.setActive(false);h.viewport.setActive(true);h.viewport.renderFrame(f);h.viewport.settle();h.flush();}assert.equal(terrain.children.length,3);assert.equal(h.frames.size,0);
    const next=fixture(50,'blue');next.metadata.sessionId='replacement';h.viewport.renderFrame(next);h.flush();assert.equal(h.viewport.inspect().transient,false);assert.equal(scene.getObjectByName('fog').children[0].count,2498);
  }finally{h.cleanup();}
});
test('reconciling a moved portrait releases old fallback references and hidden hosts do not render',async()=>{
  const f=fixture();f.state.units[0].typeId='barbarian-type';f.state.types.push({id:'barbarian-type',hp:5});const h=await harness(f);try{
    const scene=h.rendered.at(-1).scene,findPortrait=()=>scene.getObjectByName('units').children[0].children.find(n=>n.material?.userData.fallbacks);
    const portrait=findPortrait(),shared=portrait.material;assert.equal(shared.userData.fallbacks.size,1);
    for(let i=0;i<20;i++){const next=structuredClone(f);next.state.physical.figures[0].position.x=i%2;h.viewport.renderFrame(next);}
    assert.equal(shared.userData.fallbacks.size,1);h.host.clientWidth=0;h.viewport.settle();const renders=h.viewport.inspect().renderCount;h.flush();assert.equal(h.viewport.inspect().renderCount,renders);assert.equal(h.frames.size,0);
  }finally{h.cleanup();}
});
test('diagonal combat relationship joins token centers, including a captured large footprint',async()=>{
  const h=await harness();try{
    h.viewport.showInteraction({relationship:{attackerId:'hero',targets:[{targetId:'large'}]},combatPositions:new Map([['hero',{x:2,y:2,cellSpan:1}],['large',{x:4,y:3,cellSpan:2}]])});h.flush();
    const feedback=h.rendered.at(-1).scene.getObjectByName('feedback');feedback.updateMatrixWorld(true);const line=feedback.children.find(n=>n.isMesh);
    const ends=[-.5,.5].map(x=>new h.THREE.Vector3(x,0,0).applyMatrix4(line.matrixWorld)).sort((a,b)=>a.x-b.x);
    assert.ok(Math.abs(ends[0].x-2.5)<1e-6&&Math.abs(ends[0].z-2.5)<1e-6);
    assert.ok(Math.abs(ends[1].x-5)<1e-6&&Math.abs(ends[1].z-4)<1e-6);
  }finally{h.cleanup();}
});
