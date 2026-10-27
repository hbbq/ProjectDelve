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
  const controls={};for(const id of ['root','mode','span','follow','min','max','margin','side','side-label','resume','reset','in','out','status','objectives','tilt','yaw','cutaway','projection','fov'])controls[id]=new Element();controls.root.append(controls.status);
  for(const id of ['projection','fov']){const label=new Element('label');label.append(controls[id]);controls.root.append(label);}
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

test('Tabletop handoff shares the Three instance and waits for the authoritative checkpoint',async()=>{
  const {createViewportRouter}=await moduleAt('playtest-viewport.js');let loads=0;const modes=[],overlay={dice:'retained'};
  const dom={mode:'enhanced',host:{append(){}},setActive(){}};
  const three={host:{append(node){assert.equal(node,overlay);}},setActive(){},setPresentation(mode){modes.push(mode);}};
  const router=createViewportRouter({dom,loadThree:async()=>{loads++;return three;},overlay});
  await router.request('three');router.beginEvent();await router.request('tabletop');assert.deepEqual(modes,['flat']);
  await router.checkpoint();assert.equal(router.active,three);assert.deepEqual(modes,['flat','tabletop']);
  await router.request('three');assert.equal(loads,1);assert.equal(router.active,three);assert.equal(overlay.dice,'retained');
});

test('Tabletop preserves Unit roots, identity and footprints while camera orientation is independent',async()=>{
  const f=fixture(),h=await harness(f);try{
    const scene=h.rendered.at(-1).scene,root=scene.getObjectByName('units').children[0],footprint=structuredClone(root.userData.footprint);
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();assert.deepEqual(h.failures,[]);
    assert.equal(h.viewport.inspect().presentation,'tabletop');assert.equal(scene.getObjectByName('units').children[0],root);
    assert.equal(root.userData.unitId,'hero');assert.deepEqual(root.userData.footprint,footprint);
    const body=root.getObjectByName('cardboard-body');assert.equal(body.userData.angle,Math.PI);assert.ok(body.getObjectByName('reverse'));
    h.viewport.camera.setOrientation({tilt:0,yaw:90},false);h.flush();assert.equal(root.getObjectByName('cardboard-body'),body);
    h.viewport.setPresentation('flat');h.viewport.settle();h.flush();assert.equal(scene.getObjectByName('units').children[0],root);assert.equal(root.getObjectByName('cardboard-body'),undefined);
    assert.equal(h.viewport.camera.current().tilt,0);assert.equal(h.viewport.camera.current().yaw,0);
  }finally{h.cleanup();}
});

test('Tabletop physical slab, both token sizes, raised components and all item fallbacks render',async()=>{
  const {boardVisuals}=await moduleAt('board-visuals.js');const f=fixture(16);
  f.state.physical.board.terrain=['SolidRock','Tree','FruitTree','StoneFloorWithTable','Rail'].map((kind,x)=>({position:{x,y:0},kind,direction:'East'}));
  f.state.physical.board.edges=['Wall','ClosedDoor','OpenDoor','WallWithWindow'].map((kind,x)=>({kind,a:{x,y:5},b:{x,y:6}}));
  f.state.resources=[...Object.keys(boardVisuals.resource).filter(k=>k!=='default'),'new-resource'].map((id,x)=>({position:{x,y:7},type:{id,name:id}}));
  f.state.cellMarkers=['banana-peel','new-marker'].map((id,x)=>({position:{x,y:8},type:{id,name:id}}));
  f.presentation.machines=[{id:'press',typeId:'diamond-press',name:'Press',position:{x:7,y:5},ownerSide:'blue',resourceStorage:[]}];
  const h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();assert.deepEqual(h.failures,[]);
    const scene=h.rendered.at(-1).scene,slab=scene.getObjectByName('board').children[0].children[0];
    assert.equal(slab.scale.x,16.24);assert.equal(slab.scale.y,.12);assert.ok(slab.position.y<0);
    const bodies=scene.getObjectByName('units').children.map(root=>root.getObjectByName('cardboard-body'));
    assert.equal(bodies[0].userData.bounds.height,.06);assert.equal(bodies[1].userData.bounds.height,.06);assert.equal(bodies[1].userData.radius,2*bodies[0].userData.radius);
    const doors=scene.getObjectByName('edges').children;assert.equal(doors.length,2);assert.equal(doors[0].getObjectByName('door-leaf').visible,true);assert.equal(doors[1].getObjectByName('door-leaf').visible,false);
    assert.ok(scene.getObjectByName('objects').children.every(n=>n.isInstancedMesh));
    for(const object of f.state.resources)assert.ok(scene.getObjectByName(`resource:${object.type.id}`));
    assert.ok(scene.getObjectByName('cellMarker:new-marker'));assert.ok(scene.getObjectByName('cellMarker:banana-peel'));
    assert.equal(h.viewport.pick(h.viewport.project({x:-.06,y:3})),null);
    const before=h.requests.length;for(let i=0;i<6;i++){h.viewport.setPresentation('flat');h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();}
    assert.equal(h.requests.length,before);assert.equal(h.frames.size,0);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

test('angled ray picking, cap proxies, open Doors, pan, zoom and cutaway use logical geometry',async()=>{
  const h=await harness(fixture(16));try{
    h.viewport.setPresentation('tabletop');h.viewport.camera.setMode('full');h.viewport.settle();
    for(const yaw of [0,47,90,180,275]){
      h.viewport.camera.setOrientation({tilt:55,yaw},false);h.viewport.settle();h.flush();
      for(const cell of [{x:0,y:0},{x:8,y:10},{x:5,y:4}])assert.deepEqual(h.viewport.pick(h.viewport.project({x:cell.x+.5,y:cell.y+.5})).cell,cell);
      const cap=h.viewport.project({x:2.5,y:2.5,height:.084});assert.equal(h.viewport.pick(cap).unitId,'hero');
      const door={a:{x:8,y:8},b:{x:9,y:8},kind:'OpenDoor'};h.viewport.showInteraction({candidates:[{key:'d',interaction:{kind:'Door',door}}]});
      assert.deepEqual(h.viewport.pick(h.viewport.project({x:9,y:8.5})).door,door);
    }
    const camera=h.viewport.camera;camera.setMode('fixed');camera.manual({...camera.current(),x:8,y:8,scale:100});
    const point={x:470,y:280},before=camera.ground(point);camera.zoom(1.2,point);assert.ok(Math.hypot(camera.ground(point).x-before.x,camera.ground(point).y-before.y)<1e-8);
    camera.pan({x:450,y:300},{x:490,y:325});assert.equal(camera.inspect().suspended,true);
    const orientation={tilt:camera.current().tilt,yaw:camera.current().yaw};camera.resume();h.viewport.settle();assert.deepEqual({tilt:camera.current().tilt,yaw:camera.current().yaw},orientation);
    h.controls.cutaway.checked=true;h.controls.cutaway.emit('change');h.flush();assert.equal(h.rendered.at(-1).scene.getObjectByName('objects').visible,false);
    assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

test('perspective picking, supplied Move/Attack choices and independent lens/zoom remain exact during transitions',async()=>{
  const h=await harness(fixture(16));try{
    h.viewport.setPresentation('tabletop');const c=h.viewport.camera;c.setMode('full');h.viewport.settle();h.flush();
    const focus=c.current();c.setProjection('perspective');assert.equal(c.inspect().transitionActive,true);
    h.flush(performance.now()+100);assert.equal(h.rendered.at(-1).camera.isPerspectiveCamera,true);
    assert.ok(c.current().projectionMix>0&&c.current().projectionMix<1);
    assert.ok(Math.hypot(c.current().x-focus.x,c.current().y-focus.y)<1e-8);
    assert.deepEqual(h.viewport.pick(h.viewport.project({x:8.5,y:8.5})).cell,{x:8,y:8});
    h.viewport.settle();
    for(const fov of [20,45,80])for(const yaw of [0,47,180,275]){
      c.setFov(fov,false);c.setOrientation({tilt:55,yaw},false);h.flush();
      for(const cell of [{x:0,y:0},{x:8,y:10},{x:5,y:4}])assert.deepEqual(h.viewport.pick(h.viewport.project({x:cell.x+.5,y:cell.y+.5})).cell,cell);
      assert.equal(h.viewport.pick(h.viewport.project({x:2.5,y:2.5,height:.084})).unitId,'hero');
      assert.equal(h.viewport.pick(h.viewport.project({x:5,y:4,height:.084})).unitId,'large');
      const door={a:{x:8,y:8},b:{x:9,y:8},kind:'OpenDoor'};
      h.viewport.showInteraction({candidates:[{key:'door',interaction:{kind:'Door',door}},{key:'move',interaction:{kind:'Position',position:{x:5,y:4}}},{key:'attack',interaction:{kind:'Unit',unitId:'large'}}]});
      assert.deepEqual(h.viewport.pick(h.viewport.project({x:9,y:8.5})).door,door);
      const closed={...door,kind:'ClosedDoor'};h.viewport.showInteraction({candidates:[{key:'closed',interaction:{kind:'Door',door:closed}}]});assert.deepEqual(h.viewport.pick(h.viewport.project({x:9,y:8.8,height:.55})).door,closed);
      assert.deepEqual(h.viewport.pick(h.viewport.project({x:5.5,y:4.5})).cell,{x:5,y:4});
    }
    c.setMode('fixed');c.manual({...c.current(),x:8,y:8,scale:80});
    assert.equal(h.controls.fov.disabled,false);h.controls.fov.value='45';h.controls.fov.emit('input');h.viewport.settle();assert.equal(c.current().fov,45);c.setFov(80,false);
    const initial=c.current(),point={x:470,y:310},anchor=c.ground(point);
    c.zoom(1.2,point);assert.equal(c.current().fov,80);assert.ok(Math.hypot(c.ground(point).x-anchor.x,c.ground(point).y-anchor.y)<1e-8);
    const scale=c.current().scale;for(let i=0;i<10;i++){c.setFov(20,false);c.setFov(80,false);}
    assert.equal(c.current().scale,scale);assert.equal(c.current().x,c.inspect().target.x);
    c.setFov(100,false);assert.equal(c.current().fov,80);c.setFov(0,false);assert.equal(c.current().fov,20);
    const before=c.current();c.pan({x:450,y:300},{x:490,y:325});assert.ok(c.current().x!==before.x||c.current().y!==before.y);
    const panFocus=c.current();c.setProjection('orthographic',false);assert.equal(c.current().x,panFocus.x);assert.equal(c.current().y,panFocus.y);h.flush();assert.equal(h.rendered.at(-1).camera.isOrthographicCamera,true);assert.equal(h.controls.fov.disabled,true);
    c.manual({...c.current(),x:0,y:0});c.setOrientation({tilt:0,yaw:0},false);c.setProjection('perspective',false);c.setProjection('orthographic',false);assert.equal(c.current().x,0);assert.equal(c.current().y,0);
    c.setProjection('perspective',false);h.flush();const root=h.rendered.at(-1).scene.getObjectByName('units').children[0];
    h.viewport.setPresentation('flat');h.viewport.settle();h.flush();assert.equal(h.rendered.at(-1).camera.isOrthographicCamera,true);assert.equal(h.rendered.at(-1).scene.getObjectByName('units').children[0],root);
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();assert.equal(h.rendered.at(-1).camera.isPerspectiveCamera,true);assert.equal(h.frames.size,0);
    assert.deepEqual(h.failures,[]);assert.ok(initial.scale>0);
  }finally{h.cleanup();}
});

test('perspective transitions retarget, settle for Skip, reduced motion, replacement and context recovery',async()=>{
  const f=fixture(16),h=await harness(f);try{
    const c=h.viewport.camera;h.viewport.setPresentation('tabletop');h.viewport.settle();
    c.setProjection('perspective');h.flush(performance.now()+90);const mid=c.current();
    c.setProjection('orthographic');assert.deepEqual(c.current(),mid);h.flush(performance.now()+50);assert.ok(c.current().projectionMix<mid.projectionMix);
    h.viewport.settle();assert.equal(c.current().projectionMix,0);assert.equal(c.inspect().transitionActive,false);
    globalThis.matchMedia=()=>({matches:true,addEventListener(){}});c.setProjection('perspective');assert.equal(c.inspect().transitionActive,false);assert.equal(c.current().projectionMix,1);
    globalThis.matchMedia=()=>({matches:false,addEventListener(){}});c.setFov(80);assert.equal(c.inspect().transitionActive,true);
    h.flush(performance.now()+70);const beforeZoom=c.current(),point={x:470,y:310},ground=c.ground(point);c.zoom(1.1,point);assert.equal(c.current().fov,beforeZoom.fov);assert.ok(Math.hypot(c.ground(point).x-ground.x,c.ground(point).y-ground.y)<1e-7);assert.equal(c.inspect().suspended,true);
    h.viewport.setActive(false);assert.equal(c.inspect().transitionActive,false);assert.equal(h.frames.size,0);h.viewport.setActive(true);h.flush();
    const canvas=h.host.children.find(n=>n.tagName==='canvas');canvas.emit('webglcontextlost');assert.equal(h.frames.size,0);canvas.emit('webglcontextrestored');h.flush();assert.equal(h.viewport.inspect().lost,false);
    const next=structuredClone(f);next.metadata.sessionId='replacement';h.viewport.renderFrame(next);h.flush();assert.equal(c.inspect().transitionActive,false);assert.equal(c.current().fov,80);assert.equal(h.frames.size,0);
  }finally{h.cleanup();}
});

test('perspective touch gestures interrupt camera follow without submitting a choice or leaking hidden camera events',async()=>{
  const h=await harness(fixture(50,'blue'));try{
    h.viewport.setPresentation('tabletop');const c=h.viewport.camera;c.setProjection('perspective',false);c.setMode('fixed');h.viewport.settle();
    const canvas=h.host.children.find(n=>n.tagName==='canvas');
    for(const fov of [20,45,80]){
      c.setFov(fov,false);c.resume();h.viewport.settle();const target=c.inspect().target;
      c.occurrence({kind:'MovementCompleted'},{role:'Movement',unitId:'hidden',movementSegments:[[{x:45,y:45},{x:46,y:45}]],movingFigure:{sideId:'red',cellSpan:1}});c.movementStep({x:46,y:45});assert.deepEqual(c.inspect().target,target);
      canvas.emit('pointerdown',{button:0,pointerId:1,pointerType:'touch',clientX:300,clientY:300});canvas.emit('pointerdown',{button:0,pointerId:2,pointerType:'touch',clientX:400,clientY:300});canvas.emit('pointermove',{pointerId:2,clientX:450,clientY:320});canvas.emit('pointerup',{pointerId:2,clientX:450,clientY:320});canvas.emit('pointerup',{pointerId:1,clientX:300,clientY:300});assert.equal(h.picked.length,0);assert.equal(c.inspect().suspended,true);assert.equal(c.current().fov,fov);
    }
    h.viewport.settle();h.flush();assert.equal(h.frames.size,0);
  }finally{h.cleanup();}
});

test('perspective Auto retains active footprints through narrow-view rotations and lens transitions',async()=>{
  const h=await harness(fixture(50));try{
    h.host.clientWidth=320;h.host.clientHeight=700;h.viewport.setPresentation('tabletop');const c=h.viewport.camera;c.setProjection('perspective',false);c.setMode('auto');
    c.stable({unitId:'large',candidates:[{boardVisual:{kind:'move'},placementCells:[{x:7,y:5},{x:8,y:5},{x:7,y:6},{x:8,y:6}]}]});h.viewport.settle();h.flush();
    for(const yaw of [0,45,135,270]){
      c.setOrientation({tilt:55,yaw});c.setFov(80);const start=performance.now();
      for(const ms of [30,100,200,300]){h.flush(start+ms);for(const x of [4,6])for(const y of [3,5]){const p=h.viewport.project({x,y});assert.ok(!p.behind&&p.x>=-.1&&p.x<=320.1&&p.y>=-.1&&p.y<=700.1,JSON.stringify({p,pose:c.current()}));}}
      c.setFov(20,false);h.flush();
    }
    assert.equal(h.frames.size,0);
  }finally{h.cleanup();}
});

test('observed Rock regions share coplanar tops, omit internal/unknown boundaries and cache resources; Tables have four legs',async()=>{
  const {rockBoundaryMask}=await moduleAt('three-tabletop.js');
  assert.equal(rockBoundaryMask(0,0,()=> 'Unknown'),0);assert.equal(rockBoundaryMask(0,0,()=> 'SolidRock'),0);assert.equal(rockBoundaryMask(0,0,()=> 'StoneFloor'),15);
  const f=fixture(16,'blue');f.state.exploredCells.blue=[{x:2,y:2},{x:3,y:2},{x:4,y:2}];
  f.state.physical.board.terrain=[{kind:'SolidRock',position:{x:2,y:2}},{kind:'SolidRock',position:{x:3,y:2}},{kind:'SolidRock',position:{x:15,y:15}},{kind:'StoneFloorWithTable',position:{x:4,y:2}}];
  const h=await harness(f),disposedGeometryCounts=new Map();try{
    const {observedTerrain}=await moduleAt('three-viewport.js'),data=observedTerrain(f);assert.equal(rockBoundaryMask(2,2,data.kindAt),0);assert.equal(rockBoundaryMask(3,2,data.kindAt),2);
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();const scene=h.rendered.at(-1).scene,objects=scene.getObjectByName('objects'),m=new h.THREE.Matrix4();
    const rockWalls=objects.children.filter(n=>n.geometry.getAttribute('position').count===12);assert.equal(rockWalls.reduce((n,b)=>n+b.count,0),1);
    let legs=0;for(const batch of objects.children)for(let i=0;i<batch.count;i++){batch.getMatrixAt(i,m);const size=new h.THREE.Vector3().setFromMatrixScale(m);if(Math.abs(size.y-.34)<1e-6&&Math.abs(size.x-.085)<1e-6)legs++;}assert.equal(legs,4);
    const previous=[...objects.children],count=h.requests.length;h.viewport.renderFrame(f);h.flush();assert.deepEqual(objects.children,previous);assert.equal(h.requests.length,count);
    for(let i=0;i<6;i++){h.viewport.renderFrame(f);h.flush();for(const batch of objects.children)assert.equal(batch.material.userData.fallbacks?.size??0,0);}
    const hiddenChanged=structuredClone(f);hiddenChanged.state.physical.board.terrain.at(-2).kind='Tree';h.viewport.renderFrame(hiddenChanged);h.flush();assert.deepEqual(objects.children,previous);assert.equal(h.requests.length,count);
    scene.traverse(node=>{if(node.geometry&&!disposedGeometryCounts.has(node.geometry)){disposedGeometryCounts.set(node.geometry,0);node.geometry.addEventListener('dispose',()=>disposedGeometryCounts.set(node.geometry,disposedGeometryCounts.get(node.geometry)+1));}});
    assert.equal(h.frames.size,0);
  }finally{h.cleanup();}
  assert.ok(disposedGeometryCounts.size>0);assert.ok([...disposedGeometryCounts.values()].every(count=>count===1));
});

test('observed Tabletop posture flips settle, cancel and leave logical placement and HTML information fixed',async()=>{
  const f=fixture(),h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();assert.equal(h.viewport.inspect().flips,0);
    const next=structuredClone(f);next.state.physical.figures[0].posture='Upright';h.viewport.renderFrame(next);assert.equal(h.viewport.inspect().flips,0);
    h.viewport.afterOccurrence({kind:'PostureChanged',unitId:'hero'});assert.equal(h.viewport.inspect().flips,1);
    const root=h.rendered.at(-1).scene.getObjectByName('units').children[0],position=root.position.clone();
    h.flush(performance.now()+150);assert.deepEqual(root.position,position);assert.ok(root.getObjectByName('cardboard-body').scale.y<=1);
    h.viewport.settle();h.flush();assert.equal(root.getObjectByName('cardboard-body').userData.angle,0);assert.equal(h.viewport.inspect().flips,0);
    h.viewport.renderFrame(f);h.viewport.afterOccurrence({kind:'PostureChanged',unitId:'hero'});h.viewport.cancel();h.flush();assert.equal(root.getObjectByName('cardboard-body').userData.angle,Math.PI);
    h.viewport.setActive(false);const renders=h.viewport.inspect().renderCount;h.flush();assert.equal(h.viewport.inspect().renderCount,renders);
  }finally{h.cleanup();}
});

test('Tabletop fog omits unknown raised shapes and preserves entire observable stable 2x2 Units',async()=>{
  const f=fixture(16,'blue');f.state.physical.board.terrain=[{kind:'SolidRock',position:{x:10,y:10}}];const h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();
    assert.ok(h.requests.every(r=>!r.url.includes('solid-rock')));
    const scene=h.rendered.at(-1).scene,large=scene.getObjectByName('units').children[1];
    assert.equal(large.visible,true);let clipped=0;large.traverse(n=>{if(n.material?.clippingPlanes)clipped++;});assert.equal(clipped,0);
    for(const projection of ['orthographic','perspective'])for(const fov of [20,45,80])for(const yaw of [0,80,180,270]){h.viewport.camera.setProjection(projection,false);h.viewport.camera.setFov(fov,false);h.viewport.camera.setOrientation({tilt:55,yaw},false);h.flush();assert.equal(large.visible,true);assert.equal(h.viewport.inspect().lost,false);assert.ok(h.requests.every(r=>!r.url.includes('solid-rock')));}
  }finally{h.cleanup();}
});

test('elevated Tabletop movement clips every child to displayed explored Cell columns at every angle',async()=>{
  const f=fixture(16,'blue'),h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();
    for(const projection of ['orthographic','perspective'])for(const fov of [20,45,80])for(const yaw of [0,90,225]){
      h.viewport.camera.setProjection(projection,false);h.viewport.camera.setFov(fov,false);
      h.viewport.camera.setOrientation({tilt:55,yaw},false);h.flush();
      const event={role:'Movement',unitId:'hero',movingFigure:{name:'Hero',sideId:'blue',cellSpan:1,posture:'Upright'},movementSegments:[[{x:1,y:2},{x:2,y:2}],[{x:5,y:2},{x:6,y:2}]]};
      const positions=[];await h.viewport.present(event,{}, {skipping:()=>false,pause:async()=>{
        h.flush(performance.now()+230);const root=h.rendered.at(-1).scene.getObjectByName('movement').children[0];positions.push(root.position.x);
        for(const copy of root.children.filter(c=>c.visible))copy.traverse(n=>{if(n.isMesh){assert.equal(n.material.clippingPlanes.length,4);const planes=n.material.clippingPlanes;assert.ok(planes[0].constant===-2&&planes[2].constant===-2);}});
        assert.ok(h.viewport.inspect().clippedCopies<=1);assert.equal(h.host.querySelector('.three-token-info').hidden,true);
      }});
      assert.deepEqual(positions,[2.5,6.5]);assert.equal(h.viewport.inspect().clippedCopies,0);assert.equal(h.viewport.inspect().transient,false);
    }
    assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

test('dense 50x50 Tabletop static components batch by shared geometry/material and settle to idle',async()=>{
  const f=fixture(50);f.state.physical.board.terrain=Array.from({length:2500},(_,i)=>({kind:i%3?'SolidRock':'Tree',position:{x:i%50,y:Math.floor(i/50)}}));
  const h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();const objects=h.rendered.at(-1).scene.getObjectByName('objects');
    assert.ok(objects.children.length<25);assert.ok(objects.children.every(n=>n.isInstancedMesh));assert.equal(h.frames.size,0);assert.deepEqual(h.failures,[]);
    h.viewport.renderFrame(f);h.flush();assert.ok(objects.children.length<25);assert.equal(h.frames.size,0);
  }finally{h.cleanup();}
});

test('observed Door changes animate only the leaf and Skip settles canonical state',async()=>{
  const f=fixture(16),door={kind:'ClosedDoor',a:{x:7,y:7},b:{x:8,y:7}};f.state.physical.board.edges=[door];
  const h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();assert.equal(h.viewport.inspect().doorMotions,0);
    const open=structuredClone(f);open.state.physical.board.edges[0].kind='OpenDoor';h.viewport.renderFrame(open);assert.equal(h.viewport.inspect().doorMotions,1);
    h.flush(performance.now()+120);let leaf=h.rendered.at(-1).scene.getObjectByName('door-leaf');assert.ok(leaf.position.y>0);assert.ok(leaf.scale.y<1);
    h.viewport.settle();h.flush();assert.equal(leaf.visible,false);assert.equal(h.viewport.inspect().doorMotions,0);
    h.viewport.renderFrame(f);h.viewport.cancel();h.flush();leaf=h.rendered.at(-1).scene.getObjectByName('door-leaf');assert.equal(leaf.visible,true);assert.equal(leaf.position.y,0);assert.equal(leaf.scale.y,1);assert.equal(h.frames.size,0);
  }finally{h.cleanup();}
});

test('Tabletop movement Skip removes transient pool, restores stable token, and cannot continue stale segments',async()=>{
  const h=await harness(fixture(16,'blue'));try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();
    let pauses=0;await h.viewport.present({role:'Movement',unitId:'hero',movingFigure:{name:'Hero',cellSpan:1,sideId:'blue',posture:'Upright'},movementSegments:[[{x:2,y:2},{x:3,y:2},{x:4,y:2}],[{x:4,y:3},{x:5,y:3}]]},{},{skipping:()=>false,pause:async()=>{pauses++;h.viewport.settle();}});
    h.flush();assert.equal(pauses,1);assert.equal(h.viewport.inspect().transient,false);assert.equal(h.viewport.inspect().clippedCopies,0);
    assert.equal(h.rendered.at(-1).scene.getObjectByName('units').children[0].visible,true);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

test('Tabletop context recovery and missing portraits reuse scene resources without stale bodies',async()=>{
  const f=fixture();f.state.units[0].typeId='barbarian-type';f.state.types.push({id:'barbarian-type',hp:5});const h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();
    const scene=h.rendered.at(-1).scene,root=scene.getObjectByName('units').children[0];
    for(const request of h.requests)request.error();h.flush();assert.ok(Object.values(h.viewport.inspect().textureStates).includes('missing'));
    const canvas=h.host.children.find(n=>n.tagName==='canvas');canvas.emit('webglcontextlost');assert.equal(h.frames.size,0);
    h.viewport.setActive(false);canvas.emit('webglcontextrestored');h.viewport.setActive(true);h.viewport.settle();h.flush();assert.equal(scene.getObjectByName('units').children[0],root);assert.equal(h.disposed,0);
    const replacement=structuredClone(f);replacement.metadata.sessionId='replacement';h.viewport.renderFrame(replacement);h.viewport.settle();h.flush();assert.notEqual(scene.getObjectByName('units').children[0],root);assert.equal(h.viewport.inspect().flips,0);
  }finally{h.cleanup();}
});

test('Tabletop raised proxy picks work at the board boundary without making its border playable',async()=>{
  const f=fixture(16);f.state.physical.figures[0].position={x:0,y:0};f.presentation.figures.hero.occupiedCells=[{x:0,y:0}];const h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.camera.setMode('full');h.viewport.camera.setOrientation({tilt:55,yaw:0},false);h.viewport.settle();h.flush();
    assert.equal(h.viewport.pick(h.viewport.project({x:.5,y:.05,height:.084})).unitId,'hero');
    assert.equal(h.viewport.pick(h.viewport.project({x:-.1,y:5})),null);
    const door={a:{x:3,y:0},b:{x:4,y:0},kind:'ClosedDoor'};
    h.viewport.showInteraction({candidates:[{key:'door',interaction:{kind:'Door',door}}]});
    assert.deepEqual(h.viewport.pick(h.viewport.project({x:4,y:.5,height:.55})).door,door);
    assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

test('representation transitions keep whole observable Unit roots above fog in both directions',async()=>{
  const h=await harness(fixture(16,'blue'));try{
    for(const mode of ['tabletop','flat','tabletop','flat']){
      h.viewport.setPresentation(mode);h.flush(performance.now()+90);
      const scene=h.rendered.at(-1).scene,units=scene.getObjectByName('units'),fog=scene.getObjectByName('fog');
      assert.equal(units.scale.y,1);const matrix=new h.THREE.Matrix4();fog.children[0].getMatrixAt(0,matrix);
      const root=units.children[1];assert.equal(root.visible,true);assert.ok(root.position.y>matrix.elements[13]);
      h.viewport.settle();h.flush();assert.equal(h.frames.size,0);
    }
  }finally{h.cleanup();}
});
