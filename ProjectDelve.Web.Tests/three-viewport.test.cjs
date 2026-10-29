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
async function harness(frame=fixture(),options={}){
  const frames=new Map(),requests=[],rendered=[],picked=[],hovered=[],failures=[];let handle=0,disposed=0;
  const saved={};for(const key of ['document','ResizeObserver','requestAnimationFrame','cancelAnimationFrame','addEventListener','matchMedia'])saved[key]=globalThis[key];
  const controls={};for(const id of ['root','mode','span','follow','min','max','margin','side','side-label','resume','reset','in','out','status','objectives','tilt','yaw','cutaway','projection','fov','turn-left','turn-right','overhead','angle'])controls[id]=new Element();controls.root.append(controls.status);
  for(const id of ['projection','fov']){const label=new Element('label');label.append(controls[id]);controls.root.append(label);}
  const host=new Element();
  globalThis.document={createElement:tag=>new Element(tag),getElementById:()=>null};globalThis.ResizeObserver=class{observe(){}disconnect(){}};
  globalThis.requestAnimationFrame=fn=>{frames.set(++handle,fn);return handle;};globalThis.cancelAnimationFrame=id=>frames.delete(id);globalThis.addEventListener=()=>{};
  globalThis.matchMedia=()=>({matches:false,addEventListener(){}});
  const THREE=await moduleAt('vendor/three/0.180.0/build/three.module.js'),{createThreeViewport}=await moduleAt('three-viewport.js');
  const renderer={shadowMap:{},info:{render:{calls:0,triangles:0},memory:{textures:0,geometries:0}},setClearColor(){},setSize(w,h){host.children.find(n=>n.tagName==='canvas').width=w;host.children.find(n=>n.tagName==='canvas').height=h;},render(scene,camera){rendered.push({scene,camera,shadow:{...this.shadowMap}});let calls=0;scene.traverse(n=>{if(n.isMesh||n.isLine)calls++;});this.info.render.calls=calls;},dispose(){disposed++;}};
  renderer.capabilities={getMaxAnisotropy:()=>options.maxAnisotropy??16};
  const viewport=createThreeViewport({host,controls,animate:()=>true,onPick:hit=>picked.push(hit),onHover:id=>hovered.push(id),onFailure:error=>failures.push(error.message),rendererFactory:()=>renderer,textureLoader:{load(url,ready,progress,error){requests.push({url,ready,error});}},...options});
  viewport.setActive(true);viewport.renderFrame(frame);
  const flush=(time=performance.now())=>{const todo=[...frames.values()];frames.clear();for(const fn of todo)fn(time);};flush();
  return{viewport,host,controls,frames,requests,rendered,picked,hovered,failures,THREE,flush,renderer,get disposed(){return disposed;},cleanup(){viewport.dispose();for(const[key,value]of Object.entries(saved))if(value===undefined)delete globalThis[key];else globalThis[key]=value;}};
}

test('printed texture filtering respects hardware limits and caps sampling at four',async()=>{
  for(const [maximum,expected]of [[0,1],[2,2],[16,4]]){
    const h=await harness(fixture(),{maxAnisotropy:maximum});try{
      const texture=new h.THREE.Texture();let disposed=0;texture.addEventListener('dispose',()=>disposed++);
      h.requests[0].ready(texture);h.flush();assert.equal(texture.anisotropy,expected);
      h.viewport.dispose();assert.equal(disposed,1);
    }finally{h.cleanup();}
  }
});

test('Tabletop lamp initializes once, is world fixed, clamps preferences and persists without observation input',async()=>{
  const {createTabletopLighting,lightingDefaults,lightingPreferences}=await moduleAt('three-lighting.js');
  const THREE=await moduleAt('vendor/three/0.180.0/build/three.module.js');
  assert.deepEqual(lightingPreferences(null),lightingDefaults);
  assert.deepEqual(lightingPreferences({enabled:'off',key:Infinity,fill:-1,azimuth:900,elevation:0}),{...lightingDefaults,fill:0,azimuth:360,elevation:15});
  let saved='broken',invalidations=0;const scene=new THREE.Scene(),root=new Element(),storage={getItem:()=>saved,setItem:(key,value)=>{assert.equal(key,'delve.three.lighting.v1');saved=value;}};
  const environment={document:{createElement:t=>new Element(t)},localStorage:storage};
  const lamp=createTabletopLighting({scene,root,invalidate:()=>invalidations++,environment});
  const key=scene.getObjectByName('tabletop-key'),fill=scene.getObjectByName('tabletop-fill');
  assert.ok(key.isDirectionalLight&&fill.isHemisphereLight);assert.equal(key.castShadow,true);assert.equal(lamp.inspect().active,false);
  assert.ok(fill.color.b>fill.color.r&&fill.groundColor.r>fill.groundColor.b);
  lamp.setPresentation('tabletop');assert.equal(lamp.inspect().active,true);assert.ok(Math.abs(key.position.length()-1)<1e-12);
  const direction=key.position.clone();assert.ok(direction.y>0&&direction.x<0&&direction.z<0);
  lamp.controls.enabled.value='off';lamp.controls.enabled.emit('change');assert.equal(invalidations,1);assert.equal(lamp.controls.key.disabled,true);
  lamp.set({key:1.2,fill:.4,azimuth:90,elevation:35});assert.equal(key.intensity,1.2);assert.equal(fill.intensity,.4);assert.ok(key.position.x>0&&Math.abs(key.position.z)<1e-12);
  lamp.dispose();assert.equal(scene.children.length,0);assert.equal(root.children.length,0);
  const restored=createTabletopLighting({scene,root,invalidate:()=>{},environment});
  assert.deepEqual(restored.inspect(),{enabled:false,key:1.2,fill:.4,azimuth:90,elevation:35,active:false,materials:0,local:{sources:0,buildMs:0,width:1,height:1,enabled:false,textureBytes:4},firstFrameMs:null});restored.dispose();
  const denied=createTabletopLighting({scene,root,invalidate:()=>{},environment:{...environment,localStorage:{getItem(){throw Error('denied');},setItem(){throw Error('denied');}}}});
  denied.set({enabled:false});denied.dispose();
});

test('catalog allows tall Types to share one source; camera envelope stays fixed through loading and switching',async()=>{
  const f=fixture();f.state.physical.figures[0].posture='Upright';f.state.units[1].typeId='large-type';
  const descriptor={...miniatureDescriptor},options=miniatureOptions();delete descriptor.cellSpan;options.modelDescriptors={test:descriptor,'large-type':descriptor};
  options.visualCatalog={units:{test:{typeId:'test',targetHeight:1.2,maximumRadius:.38},'large-type':{typeId:'large-type',targetHeight:2.4,maximumRadius:.76}},models:{}};
  const h=await harness(f,options);try{
    h.viewport.setPresentation('tabletop');h.viewport.camera.setMode('full');h.viewport.settle();h.flush();const pose=h.viewport.camera.current();
    h.viewport.setRepresentation('miniatures');await h.viewport.models.request(descriptor).promise;h.viewport.checkpoint();h.viewport.settle();h.flush();
    const scene=h.rendered.at(-1).scene,small=scene.getObjectByName('unit:hero'),large=scene.getObjectByName('unit:large');
    assert.equal(small.userData.body.visualBounds.height,1.26);assert.equal(large.userData.body.visualBounds.height,2.46);
    assert.equal(small.getObjectByName('body').geometry,large.getObjectByName('body').geometry);assert.deepEqual(h.viewport.camera.current(),pose);
    h.viewport.setRepresentation('cardboard');h.viewport.setRepresentation('miniatures');h.viewport.settle();h.flush();assert.deepEqual(h.viewport.camera.current(),pose);assert.equal(h.viewport.models.inspect().length,1);
    h.viewport.camera.setMode('fixed');assert.equal(h.controls.span.hidden,false);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

const zombieAssetPath=path.join(__dirname,'../ProjectDelve.Web/wwwroot/art/models/units/zombie/v1/zombie.glb');
// CPU-only geometry tests keep the original binary/geometry and strip image
// bindings in memory. Edge separately verifies all original embedded textures.
async function parseGeometryOnly(b){
  const length=new DataView(b).getUint32(12,true),json=JSON.parse(new TextDecoder().decode(new Uint8Array(b,20,length)));
  delete json.images;delete json.textures;delete json.samplers;for(const m of json.materials){delete m.normalTexture;delete m.pbrMetallicRoughness.baseColorTexture;delete m.pbrMetallicRoughness.metallicRoughnessTexture;}
  let j=Buffer.from(JSON.stringify(json));j=Buffer.concat([j,Buffer.alloc((4-j.length%4)%4,32)]);const rest=Buffer.from(b,20+length),out=Buffer.alloc(20+j.length+rest.length);out.writeUInt32LE(0x46546c67);out.writeUInt32LE(2,4);out.writeUInt32LE(out.length,8);out.writeUInt32LE(j.length,12);out.writeUInt32LE(0x4e4f534a,16);j.copy(out,20);rest.copy(out,20+j.length);
  const {GLTFLoader}=await moduleAt('vendor/three/0.180.0/examples/jsm/loaders/GLTFLoader.js');return new GLTFLoader().parseAsync(out.buffer.slice(out.byteOffset,out.byteOffset+out.byteLength),'');
}
test('actual oversized Troll preserves scale, base picking, posture, facing and nine-column FoW entry/exit',async()=>{
  const raw=fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot/art/models/units/troll/v1/troll.glb')),bytes=raw.buffer.slice(raw.byteOffset,raw.byteOffset+raw.byteLength);
  const descriptor={approved:true,url:'./art/models/units/troll/v1/troll.glb',version:'tripo-v1',height:1.9,radius:.856,maxTextureDimension:2048};
  const f=fixture(16,'blue');f.state.physical.figures[0].posture='Upright';
  const h=await harness(f,{modelDescriptors:{test:descriptor},modelCacheOptions:{fetchBytes:async()=>bytes,load:parseGeometryOnly}});try{
    h.viewport.setPresentation('tabletop');h.viewport.setRepresentation('miniatures');await h.viewport.models.request(descriptor).promise;h.viewport.checkpoint();h.viewport.settle();h.flush();
    const scene=h.rendered.at(-1).scene,root=scene.getObjectByName('unit:hero'),body=root.userData.body;root.updateMatrixWorld(true);
    const bounds=new h.THREE.Box3().setFromObject(root.getObjectByName('normalized-model'));
    assert.ok(Math.abs(bounds.min.y-.072)<1e-6);assert.ok(Math.abs(bounds.max.y-1.972)<1e-6);assert.ok(body.visualBounds.radius>.855&&body.visualBounds.radius<=.856);assert.equal(body.pickRadius,.46);assert.equal(root.userData.footprint.length,1);
    assert.equal(h.viewport.camera.current().visualOverhang,.356);assert.equal(h.viewport.camera.current().visualHeight,2.05);
    const yaw=body.facingNode.rotation.y;
    for(const projection of ['perspective','orthographic']){
      h.viewport.camera.setProjection(projection,false);h.viewport.camera.setOrientation({tilt:0,yaw:0},false);h.viewport.settle();h.flush();
      assert.equal(h.viewport.pick(h.viewport.project({x:2.5,y:2.5,height:.9})).unitId,'hero');
      assert.notEqual(h.viewport.pick(h.viewport.project({x:3.2,y:2.5,height:0}))?.unitId,'hero');
      h.viewport.showInteraction({candidates:[{key:'move',interaction:{kind:'Position',position:{x:3,y:2}}}]});assert.deepEqual(h.viewport.pick(h.viewport.project({x:3.2,y:2.5})).cell,{x:3,y:2});
    }
    const lying=structuredClone(f);lying.state.physical.figures[0].posture='Lying';h.viewport.renderFrame(lying);h.viewport.settle();h.flush();assert.ok(root.getObjectByName('cardboard-body'));
    h.viewport.renderFrame(f);h.viewport.settle();h.flush();assert.equal(root.userData.body.facingNode.rotation.y,yaw);
    const {createMovementClipPool}=await moduleAt('three-tabletop.js'),all=new Set();for(let y=1;y<=3;y++)for(let x=1;x<=3;x++)all.add(`${x},${y}`);
    const pool=createMovementClipPool(root,all,h.THREE,h.viewport.lighting.clippedVariants);
    try{
      pool.update(2.5,2.5,1,0);assert.equal(pool.copies.filter(c=>c.body.visible).length,9);
      all.clear();for(let y=1;y<=3;y++)for(let x=2;x<=3;x++)all.add(`${x},${y}`);
      const owned=new Map();
      for(const enabled of [false,true])for(const heading of [0,.5,Math.PI/2,Math.PI])for(const x of [1.1,1.5,2,2.5,3.5,4,4.5,4.9]){
        h.viewport.lighting.set({enabled});pool.update(x,2.5,1,heading);pool.root.updateMatrixWorld(true);
        const expected=[];for(let cy=Math.floor(2.5-body.visualBounds.radius);cy<=Math.floor(2.5+body.visualBounds.radius);cy++)for(let cx=Math.floor(x-body.visualBounds.radius);cx<=Math.floor(x+body.visualBounds.radius);cx++)if(all.has(`${cx},${cy}`))expected.push(`${cx},${cy}`);
        const actual=[];
        for(const {body:copy,planes}of pool.copies){copy.traverse(n=>{for(const m of (Array.isArray(n.material)?n.material:[n.material]).filter(Boolean)){assert.equal(m.clippingPlanes.length,4);if(!owned.has(m)){owned.set(m,0);m.addEventListener('dispose',()=>owned.set(m,owned.get(m)+1));}}});if(!copy.visible)continue;
          const key=`${-planes[0].constant},${-planes[2].constant}`;actual.push(key);assert.ok(all.has(key));assert.equal(copy.getObjectByName('miniature-facing').rotation.y,heading);
          // Every fragment admitted by this copy lies in its explored column.
          for(const px of [x-.855,x,x+.855])for(const pz of [1.645,2.5,3.355]){const point=new h.THREE.Vector3(px,1,pz);if(planes.every(p=>p.distanceToPoint(point)>1e-8))assert.equal(`${Math.floor(px)},${Math.floor(pz)}`,key);}
        }
        assert.deepEqual(actual.sort(),expected.sort());assert.ok(actual.length<=9);
      }
      assert.equal(pool.copies.filter(c=>c.body.visible).length,0);pool.dispose();assert.ok([...owned.values()].every(n=>n===1));
    }finally{pool.dispose();}
    await h.viewport.present({role:'Movement',unitId:'hero',movingFigure:{typeId:'test',name:'Troll',sideId:'blue',cellSpan:1,posture:'Upright'},movementSegments:[[{x:1,y:2},{x:2,y:2}],[{x:5,y:2},{x:6,y:2}]]},{},{skipping:()=>false,pause:async()=>{
      h.flush(performance.now()+230);const clipped=scene.getObjectByName('clipped-movement');assert.ok(clipped);assert.equal(clipped.userData.span,1);assert.equal(h.host.querySelector('.three-token-info').hidden,true);
      for(const copy of clipped.children.filter(c=>c.visible))copy.traverse(n=>{for(const m of (Array.isArray(n.material)?n.material:[n.material]).filter(Boolean)){assert.equal(m.clippingPlanes[0].constant,-2);assert.equal(m.clippingPlanes[2].constant,-2);}});
    }});
    assert.equal(h.viewport.inspect().transient,false);assert.equal(h.viewport.inspect().clippedCopies,0);assert.equal(root.userData.body.facingNode.rotation.y,Math.PI/2+h.viewport.facing.get('hero').offset);
    h.viewport.camera.setOrientation({tilt:0,yaw:0},false);h.viewport.settle();h.viewport.setPresentation('flat');h.viewport.settle();h.flush();
    assert.equal(h.viewport.camera.current().visualOverhang,0);assert.equal(h.viewport.camera.current().visualHeight,0);
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();assert.equal(h.viewport.camera.current().visualOverhang,.356);
    const hidden=structuredClone(f);hidden.state.physical.figures=[];hidden.state.units=[];h.viewport.renderFrame(hidden);h.viewport.settle();h.flush();assert.equal(scene.getObjectByName('unit:hero'),undefined);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});
test('production Zombie actual geometry grounds, picks and clips throughout observed movement in both lighting modes',async()=>{
  const raw=fs.readFileSync(zombieAssetPath),bytes=raw.buffer.slice(raw.byteOffset,raw.byteOffset+raw.byteLength);
  const descriptor={approved:true,url:'./art/models/units/zombie/v1/zombie.glb',version:'h3.1-v1',height:1.2,radius:.38,maxTextureDimension:2048};
  const f=fixture(16,'blue');f.state.physical.figures[0].posture='Upright';
  // Node has no image decoder. Only this CPU geometry test removes texture
  // bindings from an in-memory CPU test copy; actual textures are verified in Edge.
  const load=async b=>{const view=new DataView(b),length=view.getUint32(12,true),json=JSON.parse(new TextDecoder().decode(new Uint8Array(b,20,length)));
    delete json.images;delete json.textures;delete json.samplers;for(const m of json.materials){delete m.normalTexture;delete m.pbrMetallicRoughness.baseColorTexture;delete m.pbrMetallicRoughness.metallicRoughnessTexture;}
    let j=Buffer.from(JSON.stringify(json));j=Buffer.concat([j,Buffer.alloc((4-j.length%4)%4,32)]);const rest=Buffer.from(b,20+length),out=Buffer.alloc(20+j.length+rest.length);out.writeUInt32LE(0x46546c67);out.writeUInt32LE(2,4);out.writeUInt32LE(out.length,8);out.writeUInt32LE(j.length,12);out.writeUInt32LE(0x4e4f534a,16);j.copy(out,20);rest.copy(out,20+j.length);
    const {GLTFLoader}=await moduleAt('vendor/three/0.180.0/examples/jsm/loaders/GLTFLoader.js');return new GLTFLoader().parseAsync(out.buffer.slice(out.byteOffset,out.byteOffset+out.byteLength),'');};
  const h=await harness(f,{modelDescriptors:{test:descriptor},modelCacheOptions:{fetchBytes:async()=>bytes,load}});try{
    h.viewport.setPresentation('tabletop');h.viewport.setRepresentation('miniatures');await h.viewport.models.request(descriptor).promise;h.viewport.checkpoint();h.viewport.settle();h.flush();
    const scene=h.rendered.at(-1).scene,root=scene.getObjectByName('unit:hero');root.updateMatrixWorld(true);const box=new h.THREE.Box3().setFromObject(root.getObjectByName('normalized-model'));
    assert.ok(Math.abs(box.min.y-.072)<1e-6);assert.ok(Math.abs(box.max.y-1.272)<1e-6);assert.equal(h.viewport.models.inspect()[0].triangles,9674);
    for(const projection of ['perspective','orthographic']){h.viewport.camera.setProjection(projection,false);h.viewport.settle();h.flush();assert.equal(h.viewport.pick(h.viewport.project({x:2.5,y:2.5,height:.8})).unitId,'hero');}
    await h.viewport.present({role:'Movement',unitId:'hero',movingFigure:{typeId:'test',name:'Zombie',sideId:'blue',cellSpan:1,posture:'Upright'},movementSegments:[[{x:1,y:2},{x:2,y:2}],[{x:5,y:2},{x:6,y:2}]]},{},{skipping:()=>false,pause:async()=>{
      h.flush(performance.now()+230);const pool=scene.getObjectByName('clipped-movement');assert.ok(pool);
      for(const enabled of [true,false]){h.viewport.lighting.set({enabled});h.flush();let meshes=0;pool.traverse(n=>{if(n.isMesh){meshes++;for(const m of Array.isArray(n.material)?n.material:[n.material])assert.equal(m.clippingPlanes.length,4);}});assert.ok(meshes>=3);}
      assert.equal(h.host.querySelector('.three-token-info').hidden,true);
    }});assert.deepEqual(h.failures,[]);h.viewport.settle();h.flush();assert.equal(h.frames.size,0);
  }finally{h.cleanup();}
});

const miniatureDescriptor={approved:true,url:'./__qa/miniatures/static-figure.glb',version:'fixture-v1',cellSpan:1};
function miniatureOptions(extra={}){return {modelDescriptors:{test:miniatureDescriptor},modelCacheOptions:{fetchBytes:async()=>{const b=fs.readFileSync(path.join(__dirname,'fixtures/miniatures/static-figure.glb'));return b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength);},...extra}};}
async function readyMiniature(h){h.viewport.setPresentation('tabletop');h.viewport.setRepresentation('miniatures');await h.viewport.models.request(miniatureDescriptor).promise;h.viewport.checkpoint();h.viewport.settle();h.flush();}
test('current Zombie and Troll bases preserve model approval, facing, picking, camera and posture despite contact warnings',async()=>{
  const disk=JSON.parse(fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot/art/models/unit-visuals.json')));
  const {validateUnitVisualCatalog,resolveUnitModel}=await moduleAt('unit-visuals.js');
  const catalog=validateUnitVisualCatalog(disk,disk.units.map(u=>({unitTypeId:u.typeId,cellSpan:u.typeId==='red-dragon-type'?2:1})));
  const f=fixture();f.state.units[0].typeId='zombie-type';f.state.units[1].typeId='troll-type';f.state.physical.figures[0].posture='Upright';
  f.presentation.figures.large={cellSpan:1,occupiedCells:[{x:4,y:3}]};
  const h=await harness(f,{visualCatalog:catalog,modelCacheOptions:{fetchBytes:async url=>{const b=fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot',new URL(url).pathname.split('/wwwroot/')[1]));return b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength);},load:parseGeometryOnly}});
  try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();const pose=h.viewport.camera.current();h.viewport.setRepresentation('miniatures');
    for(const id of ['zombie-type','troll-type'])await h.viewport.models.request(resolveUnitModel(catalog,id,1)).promise;
    h.viewport.checkpoint();h.viewport.settle();h.flush();const scene=h.rendered.at(-1).scene;
    const zombie=scene.getObjectByName('unit:hero'),troll=scene.getObjectByName('unit:large');
    for(const [root,radius,height]of [[zombie,.25,.75],[troll,.44,1]]){
      const base=root.getObjectByName('miniature-base'),model=root.getObjectByName('normalized-model');assert.ok(base&&model);
      assert.ok(Math.abs(new h.THREE.Box3().setFromObject(base).getSize(new h.THREE.Vector3()).x-2*radius)<1e-6);assert.equal(root.userData.body.pickRadius,.46);assert.equal(root.userData.footprint.length,1);
      root.updateMatrixWorld(true);const box=new h.THREE.Box3().setFromObject(model);assert.ok(Math.abs(box.max.y-box.min.y-height)<1e-6);assert.ok(Math.abs(box.min.y-.072)<1e-6);
      const matrix=base.matrixWorld.clone();for(const yaw of [0,.7,Math.PI/2,Math.PI,-Math.PI/2]){h.viewport.facing.get(root.userData.unitId).yaw=yaw;h.viewport.syncFacing(true);root.updateMatrixWorld(true);assert.deepEqual(base.matrixWorld.elements,matrix.elements);assert.equal(base.rotation.y,0);assert.ok(Math.abs(new h.THREE.Box3().setFromObject(base).getSize(new h.THREE.Vector3()).x-2*radius)<1e-6);}
    }
    assert.equal(zombie.userData.base.fitsContact,true);assert.ok(zombie.userData.base.margin>.046);
    assert.equal(troll.userData.base.fitsContact,false);assert.ok(Math.abs(troll.userData.base.shortfall-.0500286923)<1e-8);
    assert.ok(h.viewport.inspect().bases.find(b=>b.typeId==='troll-type').warning);assert.ok(h.viewport.models.inspect().every(e=>e.state==='ready'));
    for(const projection of ['perspective','orthographic']){
      h.viewport.camera.setProjection(projection,false);h.viewport.camera.setOrientation({tilt:0,yaw:0},false);h.viewport.settle();h.flush();
      // Outside the .25 physical radius, still within the authoritative Cell/proxy.
      assert.equal(h.viewport.pick(h.viewport.project({x:2.90,y:2.5,height:0})).unitId,'hero');
    }
    h.viewport.camera.setProjection(pose.projection,false);h.viewport.settle();h.flush();assert.equal(h.viewport.camera.current().visualHeight,pose.visualHeight);assert.equal(h.viewport.camera.current().visualOverhang,pose.visualOverhang);
    for(const enabled of [false,true]){h.viewport.lighting.set({enabled});h.flush();assert.ok(troll.getObjectByName('normalized-model'));}
    const lying=structuredClone(f);lying.state.physical.figures[1].posture='Lying';h.viewport.renderFrame(lying);h.viewport.settle();h.flush();assert.ok(troll.getObjectByName('cardboard-body'));assert.equal(troll.getObjectByName('miniature-base'),undefined);
    h.viewport.renderFrame(f);h.viewport.settle();h.flush();assert.ok(troll.getObjectByName('normalized-model'));assert.equal(troll.userData.base.radius,.44);
    const disposed=new Map();scene.traverse(n=>{if(n.isMesh&&n.geometry&&!disposed.has(n.geometry)){disposed.set(n.geometry,0);n.geometry.addEventListener('dispose',()=>disposed.set(n.geometry,disposed.get(n.geometry)+1));}});
    h.viewport.dispose();assert.ok([...disposed.values()].every(n=>n===1));assert.equal(h.frames.size,0);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

for(const slug of ['goblin','barbarian','cleric','hobby-knight','worker',...Object.keys(require('./fixtures/miniatures/remaining-batch.json')),...Object.keys(require('./fixtures/miniatures/final-batch.json'))])test(`production ${slug} geometry shares resources, grounds, faces, clips, picks and restores upright`,async()=>{
  const disk=JSON.parse(fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot/art/models/unit-visuals.json'))),{validateUnitVisualCatalog,resolveUnitModel}=await moduleAt('unit-visuals.js');
  const catalog=validateUnitVisualCatalog(disk,disk.units.map(u=>({unitTypeId:u.typeId,cellSpan:u.typeId==='red-dragon-type'?2:1}))),id=slug+'-type',descriptor=resolveUnitModel(catalog,id,1);
  const f=fixture(16,'blue');f.state.units.forEach(u=>u.typeId=id);f.state.physical.figures[0].posture='Upright';f.presentation.figures.large={cellSpan:1,occupiedCells:[{x:4,y:3}]};
  let downloads=0;const h=await harness(f,{visualCatalog:catalog,modelCacheOptions:{fetchBytes:async()=>{downloads++;const b=fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot',descriptor.url));return b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength);},load:parseGeometryOnly}});
  try{
    h.viewport.setPresentation('tabletop');h.viewport.setRepresentation('miniatures');await h.viewport.models.request(descriptor).promise;h.viewport.checkpoint();h.viewport.settle();h.flush();
    const scene=h.rendered.at(-1).scene,root=scene.getObjectByName('unit:hero'),other=scene.getObjectByName('unit:large'),model=root.getObjectByName('normalized-model'),base=root.getObjectByName('miniature-base');
    root.updateMatrixWorld(true);const box=new h.THREE.Box3().setFromObject(model);assert.ok(Math.abs(box.min.y-.072)<1e-6);assert.ok(Math.abs(box.max.y-box.min.y-descriptor.height)<1e-6);assert.equal(root.userData.footprint.length,1);assert.equal(root.userData.base.fitsContact,!['royal-king','locomotive','ghost','diamond-kart'].includes(slug));
    const meshes=[];model.traverse(n=>{if(n.isMesh)meshes.push(n);});assert.equal(meshes[0].geometry,other.getObjectByName(meshes[0].name).geometry);assert.equal(meshes[0].material,other.getObjectByName(meshes[0].name).material);assert.equal(downloads,1);
    const baseMatrix=base.matrixWorld.clone();for(const yaw of [0,Math.PI/2,Math.PI,-Math.PI/2]){h.viewport.facing.get('hero').yaw=yaw;h.viewport.syncFacing(true);root.updateMatrixWorld(true);assert.deepEqual(base.matrixWorld.elements,baseMatrix.elements);assert.equal(root.userData.body.facingNode.rotation.y,yaw);}
    for(const projection of ['perspective','orthographic']){h.viewport.camera.setProjection(projection,false);h.viewport.camera.setOrientation({tilt:0,yaw:0},false);h.viewport.settle();h.flush();assert.equal(h.viewport.pick(h.viewport.project({x:2.5,y:2.5,height:0})).unitId,'hero');h.viewport.showInteraction({candidates:[{key:'move',interaction:{kind:'Position',position:{x:3,y:2}}}]});assert.deepEqual(h.viewport.pick(h.viewport.project({x:3.2,y:2.5})).cell,{x:3,y:2});}
    const lying=structuredClone(f);lying.state.physical.figures[0].posture='Lying';h.viewport.renderFrame(lying);h.viewport.settle();h.flush();assert.ok(root.getObjectByName('cardboard-body'));assert.equal(root.getObjectByName('normalized-model'),undefined);
    h.viewport.renderFrame(f);h.viewport.settle();h.flush();assert.ok(root.getObjectByName('normalized-model'));assert.equal(root.userData.body.facingNode.rotation.y,-Math.PI/2);
    await h.viewport.present({role:'Movement',unitId:'hero',movingFigure:{typeId:id,name:slug,sideId:'blue',cellSpan:1,posture:'Upright'},movementSegments:[[{x:1,y:2},{x:2,y:2}],[{x:5,y:2},{x:6,y:2}]]},{},{skipping:()=>false,pause:async()=>{
      h.flush(performance.now()+230);const pool=scene.getObjectByName('clipped-movement');assert.ok(pool);assert.equal(h.host.querySelector('.three-token-info').hidden,true);
      for(const enabled of [false,true]){h.viewport.lighting.set({enabled});h.flush();for(const copy of pool.children.filter(c=>c.visible))copy.traverse(n=>{for(const m of (Array.isArray(n.material)?n.material:[n.material]).filter(Boolean)){assert.equal(m.clippingPlanes[0].constant,-2);assert.equal(m.clippingPlanes[2].constant,-2);}});}
    }});
    assert.equal(h.viewport.inspect().transient,false);assert.equal(h.viewport.inspect().clippedCopies,0);
    h.viewport.setRepresentation('cardboard');h.viewport.setRepresentation('miniatures');h.viewport.setPresentation('flat');h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();assert.ok(root.getObjectByName('normalized-model'));assert.equal(downloads,1);
    const disposed=new Map();scene.traverse(n=>{if(n.geometry&&!disposed.has(n.geometry)){disposed.set(n.geometry,0);n.geometry.addEventListener('dispose',()=>disposed.set(n.geometry,disposed.get(n.geometry)+1));}});
    const hidden=structuredClone(f);hidden.state.physical.figures=[];hidden.state.units=[];h.viewport.renderFrame(hidden);h.viewport.settle();h.flush();assert.equal(scene.getObjectByName('unit:hero'),undefined);
    h.viewport.dispose();assert.ok([...disposed.values()].every(n=>n===1));assert.equal(h.frames.size,0);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

test('a future 2x1 presentation base uses actual occupied dimensions and remains centered',async()=>{
  const f=fixture();f.state.units[1].typeId='rectangle-fixture';f.presentation.figures.large.occupiedCells=[{x:4,y:3},{x:5,y:3}];
  const d={...miniatureDescriptor,cellSpan:2,radius:.76,baseRadius:.8},options=miniatureOptions();options.modelDescriptors={'rectangle-fixture':d};
  const h=await harness(f,options);try{
    h.viewport.setPresentation('tabletop');h.viewport.setRepresentation('miniatures');await h.viewport.models.request(d).promise;h.viewport.checkpoint();h.viewport.settle();h.flush();
    const root=h.rendered.at(-1).scene.getObjectByName('unit:large');assert.equal(root.position.x,5);assert.equal(root.position.z,3.5);assert.equal(root.userData.base.radius,.45);assert.ok(root.getObjectByName('normalized-model'));assert.equal(root.userData.footprint.length,2);
  }finally{h.cleanup();}
});
test('miniature loading keeps cardboard, replaces only body at checkpoints, and retains roots, footprint, camera and Flat',async()=>{
  const f=fixture();f.state.physical.figures[0].posture='Upright';let release;
  const options=miniatureOptions({fetchBytes:()=>new Promise(r=>release=r)}),h=await harness(f,options);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();const root=h.rendered.at(-1).scene.getObjectByName('unit:hero'),footprint=structuredClone(root.userData.footprint),camera=h.viewport.camera.current();
    h.viewport.beginEvent();h.viewport.setRepresentation('miniatures');assert.equal(h.viewport.inspect().representation,'cardboard');h.viewport.checkpoint();assert.ok(root.getObjectByName('cardboard-body'));
    h.viewport.beginEvent();const b=fs.readFileSync(path.join(__dirname,'fixtures/miniatures/static-figure.glb'));release(b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength));await h.viewport.models.request(miniatureDescriptor).promise;h.flush();assert.ok(root.getObjectByName('cardboard-body'));
    h.viewport.checkpoint();h.flush();assert.ok(root.getObjectByName('miniature-body'));assert.equal(root.userData.body.facingNode,root.getObjectByName('miniature-facing'));assert.deepEqual(root.userData.footprint,footprint);assert.deepEqual(h.viewport.camera.current(),camera);
    assert.ok(h.rendered.at(-1).scene.getObjectByName('unit:large').getObjectByName('cardboard-body'));const yaw=root.userData.body.facingNode.rotation.y;
    h.viewport.setRepresentation('cardboard');assert.ok(root.getObjectByName('cardboard-body'));assert.equal(root.rotation.y,0);h.viewport.setRepresentation('miniatures');assert.equal(root.userData.body.facingNode.rotation.y,yaw);
    h.viewport.setPresentation('flat');h.viewport.settle();h.flush();assert.equal(h.rendered.at(-1).scene.getObjectByName('unit:hero'),root);assert.equal(root.getObjectByName('miniature-body'),undefined);assert.equal(root.userData.body,undefined);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});
test('miniatures use shared PBR/Basic pairs, procedural Side base, ground feet and model label anchors',async()=>{
  const f=fixture();f.state.physical.figures[0].posture='Upright';const h=await harness(f,miniatureOptions());try{
    h.viewport.lighting.set({enabled:false});await readyMiniature(h);const root=h.rendered.at(-1).scene.getObjectByName('unit:hero'),mesh=root.getObjectByName('body');
    assert.equal(mesh.material.isMeshBasicMaterial,true);assert.ok(root.getObjectByName('miniature-base'));root.updateMatrixWorld(true);const box=new h.THREE.Box3().setFromObject(root.getObjectByName('normalized-model'));assert.ok(Math.abs(box.min.y-(.012+.06))<1e-6);assert.ok(root.userData.infoAnchor.y>1.2);
    const off=mesh.material;h.viewport.lighting.set({enabled:true});assert.equal(mesh.material.isMeshStandardMaterial,true);h.viewport.lighting.set({enabled:false});assert.equal(mesh.material,off);
    h.viewport.settle();h.flush();assert.equal(h.frames.size,0);const before=h.viewport.inspect().renderCount;h.flush();assert.equal(h.viewport.inspect().renderCount,before);
  }finally{h.cleanup();}
});
test('Ghost appearance shares cached resources and survives lighting, facing, FoW movement and renderer handoffs',async()=>{
  const descriptor={...miniatureDescriptor,appearance:'ghost'},options=miniatureOptions();options.modelDescriptors={test:descriptor};
  let downloads=0;const fetchBytes=options.modelCacheOptions.fetchBytes;options.modelCacheOptions.fetchBytes=async()=>{downloads++;return fetchBytes();};
  const f=fixture(16,'blue');f.state.physical.figures[0].posture='Upright';f.presentation.figures.large={cellSpan:1,occupiedCells:[{x:4,y:3}]};
  const unchanged=structuredClone(f),h=await harness(f,options),cached=new Map(),clipped=new Map();
  const track=(map,resource)=>{if(!map.has(resource)){map.set(resource,0);resource.addEventListener('dispose',()=>map.set(resource,map.get(resource)+1));}};
  try{
    h.viewport.setPresentation('tabletop');h.viewport.setRepresentation('miniatures');const entry=h.viewport.models.request(descriptor);await entry.promise;
    h.viewport.checkpoint();h.viewport.settle();h.flush();
    const scene=h.rendered.at(-1).scene,root=scene.getObjectByName('unit:hero'),other=scene.getObjectByName('unit:large');
    for(const resource of [...entry.resources.materials,...entry.resources.geometries,...entry.resources.textures])track(cached,resource);
    const lightCount=()=>{let count=0;scene.traverse(n=>{if(n.isLight)count++;});return count;},lights=lightCount();
    const stableVariants=new Map();
    for(const enabled of [false,true,false,true]){
      h.viewport.lighting.set({enabled});h.viewport.settle();h.flush();
      const mesh=root.getObjectByName('body'),material=mesh.material;
      assert.equal(material.transparent,true);assert.equal(material.depthWrite,false);assert.equal(material.depthTest,true);
      assert.equal(mesh.castShadow,false);assert.equal(mesh.receiveShadow,false);
      assert.notEqual(material.onBeforeCompile,h.THREE.Material.prototype.onBeforeCompile);
      assert.equal(!!material.isMeshStandardMaterial,enabled);assert.equal(!!material.isMeshBasicMaterial,!enabled);
      const native=h.THREE.ShaderLib[enabled?'standard':'basic'],shader={uniforms:{},vertexShader:native.vertexShader,fragmentShader:native.fragmentShader};material.onBeforeCompile(shader);
      for(const stage of ['vertexShader','fragmentShader'])for(const chunk of native[stage].match(/#include <[^>]+>/g))assert.ok(shader[stage].includes(chunk),`Ghost must retain ${chunk}`);
      assert.notEqual(shader.fragmentShader,native.fragmentShader);assert.notEqual(shader.vertexShader,native.vertexShader);
      assert.equal(mesh.geometry,other.getObjectByName('body').geometry);assert.equal(material,other.getObjectByName('body').material);
      assert.equal(material.clippingPlanes,null);assert.equal(lightCount(),lights);
      if(stableVariants.has(enabled))assert.equal(material,stableVariants.get(enabled));else stableVariants.set(enabled,material);
      root.getObjectByName('miniature-base').traverse(n=>{if(n.isMesh)assert.notEqual(n.material.onBeforeCompile,material.onBeforeCompile);});
    }
    h.viewport.facing.get('hero').yaw=1.2;h.viewport.syncFacing();h.viewport.settle();h.flush();
    assert.equal(root.getObjectByName('miniature-facing').rotation.y,1.2);assert.notEqual(other.getObjectByName('miniature-facing').rotation.y,1.2);
    await h.viewport.present({role:'Movement',unitId:'hero',movingFigure:{typeId:'test',name:'Ghost fixture',sideId:'blue',cellSpan:1,posture:'Upright'},movementSegments:[[{x:1,y:2},{x:2,y:2}],[{x:5,y:2},{x:6,y:2}]]},{},{skipping:()=>false,pause:async()=>{
      h.flush(performance.now()+230);const pool=scene.getObjectByName('clipped-movement');assert.ok(pool);assert.equal(root.visible,false);
      for(const enabled of [false,true]){
        h.viewport.lighting.set({enabled});h.flush(performance.now()+240);
        const source=stableVariants.get(enabled),yaw=pool.children[0].getObjectByName('miniature-facing').rotation.y;
        for(const copy of pool.children){
          assert.equal(copy.getObjectByName('miniature-facing').rotation.y,yaw);
          copy.traverse(n=>{for(const m of (Array.isArray(n.material)?n.material:[n.material]).filter(Boolean))track(clipped,m);});
          const material=copy.getObjectByName('body').material;
          assert.equal(copy.getObjectByName('body').castShadow,false);assert.equal(copy.getObjectByName('body').receiveShadow,false);
          assert.notEqual(material,source);assert.equal(material.onBeforeCompile,source.onBeforeCompile);
          assert.equal(material.customProgramCacheKey(),source.customProgramCacheKey());
          assert.equal(material.transparent,true);assert.equal(material.depthWrite,false);assert.equal(material.depthTest,true);
          assert.equal(material.clippingPlanes.length,4);assert.equal(material.clipIntersection,false);assert.equal(material.clipShadows,true);
          if(copy.visible){assert.equal(material.clippingPlanes[0].constant,-2);assert.equal(material.clippingPlanes[2].constant,-2);}
        }
        assert.equal(source.clippingPlanes,null);assert.ok(h.viewport.inspect().clippedCopies<=1);assert.equal(lightCount(),lights);
      }
    }});
    assert.ok(clipped.size>0);assert.ok([...clipped.values()].every(n=>n===1));assert.ok([...cached.values()].every(n=>n===0));
    assert.equal(h.viewport.inspect().transient,false);assert.equal(root.visible,true);const yaw=root.getObjectByName('miniature-facing').rotation.y;
    for(let i=0;i<2;i++){
      h.viewport.setActive(false);assert.equal(h.frames.size,0);const renders=h.viewport.inspect().renderCount;h.flush();assert.equal(h.viewport.inspect().renderCount,renders);
      h.viewport.setActive(true);h.viewport.setRepresentation('cardboard');h.viewport.setRepresentation('miniatures');h.viewport.setPresentation('flat');h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();
      assert.equal(root.getObjectByName('miniature-facing').rotation.y,yaw);assert.equal(root.getObjectByName('body').material,stableVariants.get(true));
      assert.equal(h.frames.size,0);
    }
    const hidden=structuredClone(f);hidden.state.physical.figures=[];hidden.state.units=[];h.viewport.renderFrame(hidden);h.viewport.settle();h.flush();
    assert.equal(scene.getObjectByName('normalized-model'),undefined);assert.equal(downloads,1);assert.equal(lightCount(),lights);assert.equal(h.frames.size,0);
    assert.deepEqual(f,unchanged);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
  assert.ok(cached.size>0);assert.ok([...cached.values()].every(n=>n===1));assert.ok([...clipped.values()].every(n=>n===1));assert.equal(h.frames.size,0);assert.equal(h.disposed,1);
});

test('unobserved Ghosts never load an appearance or schedule movement effects',async()=>{
  const f=fixture(16,'blue');f.state.physical.figures=[];f.state.units=[];let downloads=0;
  const options=miniatureOptions({fetchBytes:async()=>{downloads++;throw Error('Hidden Ghost must not load');}});options.modelDescriptors={test:{...miniatureDescriptor,appearance:'ghost'}};
  const h=await harness(f,options);try{
    h.viewport.setPresentation('tabletop');h.viewport.setRepresentation('miniatures');h.viewport.settle();h.flush();
    const before=h.viewport.facing.inspect(),camera=h.viewport.camera.current(),renders=h.viewport.inspect().renderCount;
    await h.viewport.present({role:'Movement',unitId:'hidden-ghost',movingFigure:{typeId:'test',cellSpan:1,posture:'Upright',sideId:'red'},movementSegments:[[{x:9,y:9},{x:10,y:9}]]},{},{skipping:()=>false,pause:async()=>{throw Error('Hidden Ghost must not animate');}});
    h.flush();assert.equal(downloads,0);assert.equal(h.viewport.models.inspect().length,0);assert.equal(h.viewport.inspect().transient,false);
    assert.equal(h.rendered.at(-1).scene.getObjectByName('normalized-model'),undefined);assert.equal(h.frames.size,0);assert.equal(h.viewport.inspect().renderCount,renders);
    assert.deepEqual(h.viewport.facing.inspect(),before);assert.deepEqual(h.viewport.camera.current(),camera);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

test('miniature Lying fallback and stand up keep identity and facing, without cardboard yaw or model turnover',async()=>{
  const f=fixture();f.state.physical.figures[0].posture='Upright';const h=await harness(f,miniatureOptions());try{
    await readyMiniature(h);const root=h.rendered.at(-1).scene.getObjectByName('unit:hero'),yaw=root.userData.body.facingNode.rotation.y;
    const next=structuredClone(f);next.state.physical.figures[0].posture='Lying';h.viewport.renderFrame(next);h.viewport.afterOccurrence({kind:'PostureChanged',unitId:'hero'});h.viewport.settle();h.flush();
    assert.ok(root.getObjectByName('cardboard-body'));assert.equal(root.getObjectByName('cardboard-body').userData.angle,Math.PI);assert.equal(root.rotation.y,0);assert.equal(h.viewport.inspect().flips,0);
    h.viewport.renderFrame(f);h.viewport.afterOccurrence({kind:'PostureChanged',unitId:'hero'});h.viewport.settle();h.flush();assert.equal(root.userData.body.facingNode.rotation.y,yaw);assert.equal(h.viewport.inspect().flips,0);
  }finally{h.cleanup();}
});
test('late model completion after session replacement or removal never resurrects a Unit',async()=>{
  const f=fixture();f.state.physical.figures[0].posture='Upright';let release;const h=await harness(f,miniatureOptions({fetchBytes:()=>new Promise(r=>release=r)}));try{
    h.viewport.setPresentation('tabletop');h.viewport.setRepresentation('miniatures');const e=h.viewport.models.request(miniatureDescriptor);
    const next=structuredClone(f);next.metadata.sessionId='replacement';next.state.physical.figures=[];next.state.units=[];h.viewport.renderFrame(next);
    const b=fs.readFileSync(path.join(__dirname,'fixtures/miniatures/static-figure.glb'));release(b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength));await e.promise;h.flush();assert.equal(h.rendered.at(-1).scene.getObjectByName('units').children.length,0);assert.equal(h.viewport.inspect().units,0);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});
test('miniature facing eases shortest yaw, retargets from display, and Skip settles desired observed angle',async()=>{
  const f=fixture();f.state.physical.figures[0].posture='Upright';const h=await harness(f,miniatureOptions());try{
    await readyMiniature(h);const node=h.rendered.at(-1).scene.getObjectByName('miniature-facing');node.rotation.y=Math.PI-.02;h.viewport.facing.get('hero').yaw=-Math.PI+.02;h.viewport.syncFacing();h.flush(performance.now()+60);
    assert.ok(node.rotation.y>Math.PI-.02&&node.rotation.y<Math.PI+.02);const mid=node.rotation.y;h.viewport.facing.get('hero').yaw=1;h.viewport.syncFacing();assert.equal(node.rotation.y,mid);h.viewport.settle();h.flush();assert.equal(node.rotation.y,1);assert.equal(h.viewport.inspect().rotations,0);
  }finally{h.cleanup();}
});
test('nested imported miniature movement clips all PBR/Off meshes while yaw remains synchronized and world planes fixed',async()=>{
  const f=fixture(16,'blue');f.state.physical.figures[0].posture='Upright';const h=await harness(f,miniatureOptions());try{
    await readyMiniature(h);const scene=h.rendered.at(-1).scene;
    const event={role:'Movement',unitId:'hero',movingFigure:{typeId:'test',name:'Figure',sideId:'blue',cellSpan:1,posture:'Upright'},movementSegments:[[{x:1,y:2},{x:2,y:2}],[{x:5,y:2},{x:6,y:2}]]};
    const owned=new Map();await h.viewport.present(event,{}, {skipping:()=>false,pause:async()=>{
      h.flush(performance.now()+230);const pool=scene.getObjectByName('clipped-movement');assert.ok(pool.getObjectByName('body'));
      for(const enabled of [false,true]){h.viewport.lighting.set({enabled});h.flush(performance.now()+240);
        const yaw=pool.children[0].getObjectByName('miniature-facing').rotation.y;
        for(const copy of pool.children){assert.equal(copy.getObjectByName('miniature-facing').rotation.y,yaw);copy.traverse(n=>{if(!n.isMesh)return;for(const m of Array.isArray(n.material)?n.material:[n.material]){assert.equal(m.clippingPlanes.length,4);assert.equal(m.clipIntersection,false);if(!owned.has(m)){owned.set(m,0);m.addEventListener('dispose',()=>owned.set(m,owned.get(m)+1));}}});}
        for(const copy of pool.children.filter(c=>c.visible))assert.equal(copy.getObjectByName('body').material.clippingPlanes[0].constant,-2);
      }
      assert.equal(h.host.querySelector('.three-token-info').hidden,true);
    }});
    assert.ok([...owned.values()].every(n=>n===1));assert.equal(h.viewport.inspect().transient,false);assert.equal(scene.getObjectByName('unit:hero').userData.body.facingNode.rotation.y,Math.PI/2+h.viewport.facing.get('hero').offset);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});
test('miniature coarse proxies use current projection and retain legal Position priority without triangle raycasting',async()=>{
  const f=fixture();f.state.physical.figures[0].posture='Upright';const h=await harness(f,miniatureOptions());try{
    await readyMiniature(h);for(const projection of ['orthographic','perspective']){
      h.viewport.camera.setProjection(projection,false);h.viewport.camera.setOrientation({tilt:55,yaw:35},false);h.viewport.settle();h.flush();
      const p=h.viewport.project({x:2.5,y:2.5,height:.85}),ground=h.viewport.camera.ground(p);h.viewport.showInteraction({candidates:[]});assert.equal(h.viewport.pick(p).unitId,'hero');
      h.viewport.showInteraction({candidates:[{key:'position',interaction:{kind:'Position',position:{x:Math.floor(ground.x),y:Math.floor(ground.y)}}}]});const hit=h.viewport.pick(p);assert.deepEqual(hit.cell,{x:Math.floor(ground.x),y:Math.floor(ground.y)});
    }
    h.viewport.showInteraction({candidates:[]});h.viewport.camera.setProjection('perspective',false);h.viewport.camera.setFov(80,false);h.viewport.camera.setOrientation({tilt:55,yaw:0},false);h.viewport.camera.manual({...h.viewport.camera.current(),x:2.5,y:2.5,scale:180});h.viewport.settle();h.flush();
    const head=h.viewport.project({x:2.5,y:2.5,height:1.2});assert.equal(h.viewport.camera.ground(head),null);assert.equal(h.viewport.pick(head).unitId,'hero');
    const canvas=h.host.children.find(n=>n.tagName==='canvas');canvas.emit('webglcontextlost');canvas.emit('webglcontextrestored');h.viewport.setActive(true);h.flush();assert.equal(h.viewport.inspect().models[0].state,'ready');
  }finally{h.cleanup();}
});
test('model clipping preserves authored matrices and material arrays without changing stable materials',async()=>{
  const T=await moduleAt('vendor/three/0.180.0/build/three.module.js'),{createMovementClipPool}=await moduleAt('three-tabletop.js');
  const source=new T.Group(),matrixNode=new T.Group(),yaw=new T.Group();yaw.name='miniature-facing';matrixNode.matrixAutoUpdate=false;matrixNode.matrix.makeTranslation(.05,.3,0);
  const g=new T.BoxGeometry(.2,1.2,.2),a=new T.MeshBasicMaterial(),b=new T.MeshStandardMaterial();g.clearGroups();g.addGroup(0,18,0);g.addGroup(18,18,1);
  const mesh=new T.Mesh(g,[a,b]);matrixNode.add(mesh);yaw.add(matrixNode);source.add(yaw);source.position.set(2.5,.012,2.5);source.userData.visualBounds={radius:.46,height:1.5};source.updateMatrixWorld(true);
  const pool=createMovementClipPool(source,new Set(['2,2']));pool.update(2.5,2.5,1,.7);pool.root.updateMatrixWorld(true);
  const copy=pool.copies[0].body.getObjectByName('miniature-facing').children[0];assert.equal(copy.matrixAutoUpdate,false);assert.deepEqual(copy.matrix.elements,matrixNode.matrix.elements);assert.equal(copy.parent.rotation.y,.7);
  for(const m of copy.children[0].material){assert.equal(m.clippingPlanes.length,4);assert.notEqual(m,a);assert.notEqual(m,b);}assert.equal(a.clippingPlanes,null);assert.equal(b.clippingPlanes,null);pool.dispose();g.dispose();a.dispose();b.dispose();
});
test('hidden stable content and entirely hidden movement do not load or instantiate models or change facing',async()=>{
  const f=fixture(16,'blue');f.state.physical.figures=[];f.state.units=[];let requests=0;const h=await harness(f,miniatureOptions({fetchBytes:async()=>{requests++;throw Error('Must not load');}}));try{
    h.viewport.setPresentation('tabletop');h.viewport.setRepresentation('miniatures');h.viewport.settle();h.flush();const before=h.viewport.facing.inspect(),camera=h.viewport.camera.current();
    await h.viewport.present({role:'Movement',unitId:'hidden',movingFigure:{typeId:'test',cellSpan:1,posture:'Upright',sideId:'red'},movementSegments:[[{x:9,y:9},{x:10,y:9}]]},{},{skipping:()=>false,pause:async()=>{throw Error('Must not animate');}});
    assert.equal(requests,0);assert.deepEqual(h.viewport.facing.inspect(),before);assert.deepEqual(h.viewport.camera.current(),camera);assert.equal(h.viewport.inspect().transient,false);
  }finally{h.cleanup();}
});
test('session replacement during model movement cannot restore stale roots or dispose a new movement pool',async()=>{
  const f=fixture(16,'blue');f.state.physical.figures[0].posture='Upright';const h=await harness(f,miniatureOptions());try{
    await readyMiniature(h);let resume;const old=h.viewport.present({role:'Movement',unitId:'hero',path:[{x:2,y:2},{x:3,y:2}]},{},{skipping:()=>false,pause:()=>new Promise(r=>resume=r)});
    const next=structuredClone(f);next.metadata.sessionId='replacement';next.state.physical.figures=next.state.physical.figures.filter(f=>f.id!=='hero');next.state.units=next.state.units.filter(u=>u.id!=='hero');h.viewport.renderFrame(next);resume();await old;h.flush();
    assert.equal(h.rendered.at(-1).scene.getObjectByName('unit:hero'),undefined);assert.equal(h.viewport.facing.inspect().hero,undefined);assert.equal(h.viewport.inspect().transient,false);
  }finally{h.cleanup();}
});
test('model readiness while inactive or Lying defers attachment; reduced motion settles yaw immediately',async()=>{
  const f=fixture();f.state.physical.figures[0].posture='Upright';let release;const h=await harness(f,{...miniatureOptions({fetchBytes:()=>new Promise(r=>release=r)}),animate:()=>false});try{
    h.viewport.setPresentation('tabletop');h.viewport.setRepresentation('miniatures');const e=h.viewport.models.request(miniatureDescriptor);h.viewport.setActive(false);
    const next=structuredClone(f);next.state.physical.figures[0].posture='Lying';h.viewport.renderFrame(next);const b=fs.readFileSync(path.join(__dirname,'fixtures/miniatures/static-figure.glb'));release(b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength));await e.promise;
    assert.equal(h.frames.size,0);h.viewport.setActive(true);h.flush();const root=h.rendered.at(-1).scene.getObjectByName('unit:hero');assert.equal(root.getObjectByName('miniature-body'),undefined);
    h.viewport.renderFrame(f);h.viewport.facing.get('hero').yaw=2;h.viewport.syncFacing();assert.equal(root.userData.body.facingNode.rotation.y,2);assert.equal(h.viewport.inspect().rotations,0);
  }finally{h.cleanup();}
});
test('an observable imported 2x2 body remains whole over Unknown Cells and selects its complete logical footprint',async()=>{
  const f=fixture(16,'blue');f.state.units.find(u=>u.id==='large').typeId='large-fixture';f.state.types.push({id:'large-fixture',hp:1});
  const descriptor={...miniatureDescriptor,version:'fixture-large-v1',cellSpan:2,radius:.76},options=miniatureOptions();options.modelDescriptors={'large-fixture':descriptor};const h=await harness(f,options);try{
    h.viewport.setPresentation('tabletop');h.viewport.setRepresentation('miniatures');await h.viewport.models.request(descriptor).promise;h.viewport.checkpoint();h.viewport.settle();h.flush();
    const root=h.rendered.at(-1).scene.getObjectByName('unit:large');assert.ok(root.userData.body.facingNode);assert.equal(root.userData.footprint.length,4);root.traverse(n=>{for(const m of (Array.isArray(n.material)?n.material:[n.material]).filter(Boolean))assert.equal(m.clippingPlanes,null);});
    for(const cell of root.userData.footprint){const p=h.viewport.project({x:cell.x+.5,y:cell.y+.5});assert.equal(h.viewport.pick(p).unitId,'large');}
    h.viewport.showInteraction({candidates:[{key:'door',interaction:{kind:'Door',door:{a:{x:4,y:3},b:{x:5,y:3},kind:'OpenDoor'}}}]});assert.ok(h.viewport.pick(h.viewport.project({x:5,y:3.5})).door);
  }finally{h.cleanup();}
});

test('lighting swaps shared physical materials once while artwork, instances, camera and observation remain intact',async()=>{
  const f=fixture(16,'blue');f.state.physical.board.terrain=[{kind:'Tree',position:{x:2,y:2}},{kind:'SolidRock',position:{x:4,y:3}}];
  f.state.resources=[{position:{x:2,y:2},type:{id:'diamond',name:'Diamond'}}];
  const unchanged=structuredClone(f),h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();
    const scene=h.rendered.at(-1).scene,objects=scene.getObjectByName('objects'),batches=[...objects.children],root=scene.getObjectByName('unit:hero');
    const printed=[];scene.traverse(n=>{if(n.material?.isMeshBasicMaterial)printed.push([n,n.material]);});
    const side=root.getObjectByName('turnover').children[0],lit=side.material;
    assert.equal(lit.isMeshLambertMaterial,true);assert.equal(lit.vertexColors,false);assert.equal(scene.getObjectByName('unit:large').getObjectByName('turnover').children[0].material,lit);
    assert.ok(objects.children.some(n=>n.isInstancedMesh&&n.material.isMeshLambertMaterial));
    const pose=h.viewport.camera.current(),requests=h.requests.length,materials=h.viewport.inspect().lighting.materials;
    h.viewport.lighting.set({enabled:true});assert.equal(h.frames.size,0);
    for(let i=0;i<12;i++){
      h.viewport.lighting.set({enabled:false});assert.equal(side.material.isMeshBasicMaterial,true);assert.equal(side.material.vertexColors,true);assert.equal(h.frames.size,1);h.flush();assert.equal(h.frames.size,0);
      h.viewport.lighting.set({enabled:true});h.flush();assert.equal(side.material,lit);assert.deepEqual(objects.children,batches);
      for(const [node,m]of printed)assert.equal(node.material,m);
    }
    // New frames use canonical batch identities regardless of lighting mode.
    h.viewport.lighting.set({enabled:false});h.viewport.renderFrame(f);h.flush();assert.deepEqual(objects.children,batches);
    assert.equal(h.requests.length,requests);assert.equal(h.viewport.inspect().lighting.materials,materials);assert.deepEqual(h.viewport.camera.current(),pose);assert.deepEqual(f,unchanged);
    const direction=scene.getObjectByName('tabletop-key').position.clone();
    for(const projection of ['perspective','orthographic']){h.viewport.camera.setProjection(projection,false);h.viewport.camera.setOrientation({yaw:135,tilt:45},false);h.viewport.settle();h.flush();assert.equal(side.material.isMeshBasicMaterial,true);assert.deepEqual(scene.getObjectByName('tabletop-key').position,direction);}
    h.viewport.setPresentation('flat');h.viewport.settle();h.flush();assert.equal(h.viewport.inspect().lighting.active,false);assert.equal(h.controls.root.querySelector('#three-lighting-controls').hidden,true);
    scene.getObjectByName('units').traverse(n=>{if(n.material)assert.equal(n.material.isMeshBasicMaterial,true);});
    h.viewport.setActive(false);h.viewport.lighting.set({enabled:true});assert.equal(h.frames.size,0);h.viewport.setActive(true);h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();
    const replacement=structuredClone(f);replacement.metadata.sessionId='restart';h.viewport.renderFrame(replacement);h.viewport.settle();h.flush();assert.equal(h.viewport.inspect().lighting.active,true);assert.equal(h.frames.size,0);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

test('lighting never derives content, direction or preferences from unexplored terrain',async()=>{
  const f=fixture(16,'blue');f.state.physical.board.terrain=[{kind:'SolidRock',position:{x:15,y:15}}];
  const h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();
    const scene=h.rendered.at(-1).scene,fog=scene.getObjectByName('fog'),cover=fog.children[0],large=scene.getObjectByName('unit:large');
    const before=structuredClone(f.state.exploredCells),requests=h.requests.length;
    for(const enabled of [false,true]){
      h.viewport.lighting.set({enabled,key:2,fill:0,azimuth:180});h.flush();
      f.state.physical.board.terrain[0].kind='FruitTree';h.viewport.renderFrame(f);h.flush();
      assert.equal(scene.getObjectByName('objects').children.length,0);assert.equal(h.requests.length,requests);assert.equal(fog.children[0],cover);assert.equal(cover.material.isMeshBasicMaterial,true);assert.equal(large.visible,true);
      large.traverse(n=>{if(n.material)assert.equal(n.material.clippingPlanes,null);});assert.deepEqual(f.state.exploredCells,before);
    }
    assert.equal(h.viewport.inspect().lighting.key,2);assert.equal(h.viewport.inspect().lighting.fill,0);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

test('shadow receivers cover only explored Cells and unknown terrain cannot create casters',async()=>{
  const f=fixture(8,'blue');f.state.physical.board.terrain=[{kind:'Tree',position:{x:2,y:2}},{kind:'SolidRock',position:{x:6,y:6}}];
  const h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();
    const scene=h.rendered.at(-1).scene,receiver=scene.getObjectByName('observed-ground-shadows'),fog=scene.getObjectByName('fog');
    assert.ok(receiver.isInstancedMesh&&receiver.receiveShadow);assert.equal(receiver.castShadow,false);assert.equal(receiver.count,2);
    const cells=[];for(let i=0;i<receiver.count;i++){const matrix=new h.THREE.Matrix4();receiver.getMatrixAt(i,matrix);cells.push([matrix.elements[12]-.5,matrix.elements[14]-.5]);assert.ok(matrix.elements[13]>0&&matrix.elements[13]<.006);}
    assert.deepEqual(cells,[[2,2],[4,3]]);
    fog.traverse(n=>{assert.equal(n.castShadow,false);assert.equal(n.receiveShadow,false);if(n.material)assert.equal(n.material.isMeshBasicMaterial,true);});
    const casters=()=>{const result=[];scene.traverse(n=>{if(n.isMesh&&n.castShadow)result.push([n.uuid,n.geometry.id,n.count??1]);});return result;};
    const before=casters(),key=scene.getObjectByName('tabletop-key'),lightPosition=key.position.clone(),shadowProjection=key.shadow.camera.projectionMatrix.clone();
    assert.ok(before.length>0);
    for(const kind of ['FruitTree','Lava','StoneFloorWithTable']){
      f.state.physical.board.terrain[1].kind=kind;h.viewport.renderFrame(f);h.viewport.settle();h.flush();
      assert.deepEqual(casters(),before);assert.equal(scene.getObjectByName('observed-ground-shadows'),receiver);
      assert.deepEqual(key.position,lightPosition);assert.deepEqual(key.shadow.camera.projectionMatrix,shadowProjection);
    }
    h.viewport.setPresentation('flat');h.viewport.settle();h.flush();assert.equal(scene.getObjectByName('observed-ground-shadows'),undefined);assert.equal(h.renderer.shadowMap.enabled,false);
    assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

test('shadow map refreshes for scene changes and reuses its contents for camera and overlay renders',async()=>{
  const f=fixture(),h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();
    const passes=()=>h.rendered.slice(-2).map(r=>r.shadow.needsUpdate);
    assert.equal(h.renderer.shadowMap.autoUpdate,false);assert.deepEqual(passes(),[true,false]);
    const key=h.rendered.at(-1).scene.getObjectByName('tabletop-key'),position=key.position.clone(),projection=key.shadow.camera.projectionMatrix.clone();
    h.viewport.camera.setOrientation({tilt:45,yaw:135},false);h.flush();assert.deepEqual(passes(),[false,false]);
    h.viewport.camera.setProjection('perspective',false);h.flush();assert.deepEqual(passes(),[false,false]);
    assert.deepEqual(key.position,position);assert.deepEqual(key.shadow.camera.projectionMatrix,projection);
    h.viewport.showInteraction({candidates:[{key:'move',interaction:{kind:'Position',position:{x:3,y:2}}}]});h.flush();assert.deepEqual(passes(),[false,false]);
    h.viewport.lighting.set({azimuth:135});h.flush();assert.deepEqual(passes(),[true,false]);
    h.controls.cutaway.checked=true;h.controls.cutaway.emit('change');h.flush();assert.deepEqual(passes(),[true,false]);
    assert.equal(h.rendered.at(-1).scene.getObjectByName('objects').visible,false);
    const next=structuredClone(f);next.state.physical.figures[0].position.x=3;h.viewport.renderFrame(next);h.viewport.settle();h.flush();assert.deepEqual(passes(),[true,false]);
    h.viewport.lighting.set({enabled:false});h.flush();assert.equal(h.renderer.shadowMap.enabled,false);assert.deepEqual(passes(),[false,false]);
    h.viewport.lighting.set({enabled:true});h.flush();assert.deepEqual(passes(),[true,false]);
    assert.equal(h.frames.size,0);assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

test('lighting changes during elevated movement preserve every clipping plane and dispose both variants',async()=>{
  const h=await harness(fixture(16,'blue')),owned=new Map();try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();
    const scene=h.rendered.at(-1).scene,materialCount=h.viewport.inspect().lighting.materials;
    await h.viewport.present({role:'Movement',unitId:'hero',movingFigure:{name:'Hero',sideId:'blue',cellSpan:1,posture:'Upright'},movementSegments:[[{x:1,y:2},{x:2,y:2},{x:3,y:2}]]},{},{skipping:()=>false,pause:async()=>{
      h.flush(performance.now()+230);const mover=scene.getObjectByName('clipped-movement');
      for(const enabled of [false,true,false,true]){
        h.viewport.lighting.set({enabled});h.flush();mover.traverse(n=>{if(!n.isMesh)return;
          const m=n.material;assert.equal(m.clippingPlanes.length,4);assert.equal(m.clipIntersection,false);assert.equal(m.clipShadows,true);
          assert.equal(n.castShadow,!m.transparent);assert.equal(n.receiveShadow,!m.transparent);
          if(!owned.has(m)){owned.set(m,0);m.addEventListener('dispose',()=>owned.set(m,owned.get(m)+1));}
          if(m.userData.sourceMaterial.isMeshLambertMaterial)assert.equal(enabled,true);
          if(m.userData.sourceMaterial.vertexColors)assert.equal(enabled,false);
        });
      }
      assert.ok(h.viewport.inspect().clippedCopies<=1);
    }});
    assert.ok(owned.size>0);assert.ok([...owned.values()].every(n=>n===1));assert.equal(h.viewport.inspect().lighting.materials,materialCount);h.flush();assert.equal(h.frames.size,0);
  }finally{h.cleanup();}
});

test('missing and loaded printed textures stay Basic through light changes and all cached physical materials dispose once',async()=>{
  const f=fixture();f.state.units[0].typeId='barbarian-type';f.state.types.push({id:'barbarian-type',hp:5});
  const h=await harness(f),disposed=new Map();try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();
    h.requests[0].error();for(const request of h.requests.slice(1))request.ready(new h.THREE.Texture());h.flush();
    const scene=h.rendered.at(-1).scene;
    for(const enabled of [false,true]){h.viewport.lighting.set({enabled});h.flush();scene.traverse(n=>{
      if(n.material?.map)assert.equal(n.material.isMeshBasicMaterial,true);
      if(n.material?.isMeshLambertMaterial||n.material?.vertexColors){const m=n.material;if(!disposed.has(m)){disposed.set(m,0);m.addEventListener('dispose',()=>disposed.set(m,disposed.get(m)+1));}}
    });}
    assert.ok(Object.values(h.viewport.inspect().textureStates).includes('missing'));assert.equal(h.renderer.shadowMap.enabled,true);
  }finally{h.cleanup();}assert.ok(disposed.size>0);assert.ok([...disposed.values()].every(n=>n===1));
});
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
    const {tabletopDimensions}=await moduleAt('three-tabletop.js');
    const scene=h.rendered.at(-1).scene,slab=scene.getObjectByName('board').children[0].children.find(n=>n.geometry?.type==='BoxGeometry'&&n.scale.y===tabletopDimensions.boardThickness);
    assert.ok(slab);assert.equal(slab.scale.x,16+2*tabletopDimensions.border);assert.equal(slab.scale.y,tabletopDimensions.boardThickness);assert.ok(slab.position.y<0);
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

test('large presentation envelopes cover sixteen columns while excluding unknown cells',async()=>{
  const T=await moduleAt('vendor/three/0.180.0/build/three.module.js'),{createMovementClipPool}=await moduleAt('three-tabletop.js');
  const source=new T.Group();source.userData.visualBounds={radius:1.25};
  const all=new Set();for(let y=0;y<4;y++)for(let x=0;x<4;x++)all.add(`${x},${y}`);
  const pool=createMovementClipPool(source,all);pool.update(2,2,2);
  assert.equal(pool.copies.length,16);assert.equal(pool.copies.filter(c=>c.body.visible).length,16);
  all.delete('0,0');pool.update(2,2,2);assert.equal(pool.copies.filter(c=>c.body.visible).length,15);
  all.clear();pool.update(2,2,2);assert.equal(pool.copies.filter(c=>c.body.visible).length,0);pool.dispose();
  source.userData.visualBounds.radius=.92;const small=createMovementClipPool(source,null);assert.equal(small.copies.length,9);small.dispose();
});

test('production Dragon preserves 2x2 picking, grounding and every explored clipping column at all yaws',async()=>{
 const disk=JSON.parse(fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot/art/models/unit-visuals.json'))),{validateUnitVisualCatalog,resolveUnitModel}=await moduleAt('unit-visuals.js');
 const catalog=validateUnitVisualCatalog(disk,disk.units.map(u=>({unitTypeId:u.typeId,cellSpan:u.typeId==='red-dragon-type'?2:1}))),d=resolveUnitModel(catalog,'red-dragon-type',2),f=fixture(16,'blue');f.state.units[1].typeId='red-dragon-type';
 const b=fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot',d.url));const h=await harness(f,{visualCatalog:catalog,modelCacheOptions:{fetchBytes:async()=>b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength),load:parseGeometryOnly}});
 try{h.viewport.setPresentation('tabletop');h.viewport.setRepresentation('miniatures');await h.viewport.models.request(d).promise;h.viewport.checkpoint();h.viewport.settle();h.flush();
 const root=h.rendered.at(-1).scene.getObjectByName('unit:large');root.updateMatrixWorld(true);const box=new h.THREE.Box3().setFromObject(root.getObjectByName('normalized-model'));assert.ok(Math.abs(box.min.y-.072)<1e-6);assert.ok(Math.abs(box.max.y-box.min.y-1.45)<1e-6);assert.equal(root.userData.footprint.length,4);assert.equal(root.userData.base.radius,.9);assert.equal(root.userData.body.pickRadius,.92);
 for(const projection of ['perspective','orthographic']){h.viewport.camera.setProjection(projection,false);h.viewport.camera.setOrientation({tilt:0,yaw:0},false);h.viewport.settle();h.flush();assert.equal(h.viewport.pick(h.viewport.project({x:5,y:4,height:0})).unitId,'large');assert.notEqual(h.viewport.pick(h.viewport.project({x:6.2,y:4,height:0}))?.unitId,'large');}
 const {createMovementClipPool}=await moduleAt('three-tabletop.js'),known=new Set();for(let y=0;y<9;y++)for(let x=0;x<9;x++)if((x+y)%3)known.add(`${x},${y}`);const pool=createMovementClipPool(root,known,h.THREE,h.viewport.lighting.clippedVariants);assert.equal(pool.copies.length,16);
 try{for(const enabled of [true,false])for(const yaw of [0,.4,Math.PI/2,Math.PI,Math.PI*1.7])for(const x of [2,2.15,2.5,2.9,3])for(const z of [2,2.2,2.5,2.9]){h.viewport.lighting.set({enabled});pool.update(x,z,2,yaw);const radius=root.userData.visualBounds.radius,expected=[];for(let cy=Math.floor(z-radius);cy<=Math.floor(z+radius);cy++)for(let cx=Math.floor(x-radius);cx<=Math.floor(x+radius);cx++)if(known.has(`${cx},${cy}`))expected.push(`${cx},${cy}`);
 const actual=pool.copies.filter(c=>c.body.visible).map(c=>{assert.equal(c.body.getObjectByName('miniature-facing').rotation.y,yaw);return `${-c.planes[0].constant},${-c.planes[2].constant}`;});assert.deepEqual(actual,expected);}
 known.clear();pool.update(3,3,2);assert.equal(pool.copies.filter(c=>c.body.visible).length,0);
 }finally{pool.dispose();}
 const lying=structuredClone(f);lying.state.physical.figures[1].posture='Lying';h.viewport.renderFrame(lying);h.viewport.settle();h.flush();assert.ok(root.getObjectByName('cardboard-body'));h.viewport.renderFrame(f);h.viewport.settle();h.flush();assert.ok(root.getObjectByName('normalized-model'));assert.deepEqual(h.failures,[]);
 }finally{h.cleanup();}
});

test('director manual controls suspend transitions, Resume restores control, and only focused activations resume',async()=>{
 const f=fixture(50,'blue');f.metadata.assignments.push({sideId:'red',controller:'Automated'});f.state.units[1].sideId='red';const h=await harness(f);
 try{h.viewport.setPresentation('tabletop');const c=h.viewport.camera;
 assert.deepEqual(h.controls.side.children.map(o=>o.value),['','blue','red']);
 for(const key of ['in','out','turn-left','turn-right','overhead','angle','reset']){c.resume();h.controls[key].emit('click');assert.equal(c.inspect().suspended,true,key);const target=c.inspect().target;c.stable({unitId:'hero',candidates:[]});assert.deepEqual(c.inspect().target,target);}
 for(const key of ['tilt','yaw']){c.resume();h.controls[key].value='25';h.controls[key].emit('input');assert.equal(c.inspect().suspended,true);}
 c.resume();c.pan({x:300,y:300},{x:330,y:310});assert.equal(c.inspect().suspended,true);
 c.occurrence({kind:'ActivationStarted',unitId:'large'},{});assert.equal(c.inspect().suspended,true);
 c.occurrence({kind:'ActivationStarted',unitId:'hero'},{});assert.equal(c.inspect().suspended,false);
 h.controls.side.value='';h.controls.side.emit('change');c.zoom(1.1);c.occurrence({kind:'ActivationStarted',unitId:'hero'},{});assert.equal(c.inspect().suspended,true);assert.equal(c.inspect().side,null);
 h.controls.resume.emit('click');assert.equal(c.inspect().suspended,false);
 h.viewport.renderFrame(f);assert.equal(c.inspect().side,null);assert.equal(h.viewport.inspect().camera.side,null);
 h.controls.side.value='red';h.controls.side.emit('change');c.zoom(1.1);c.occurrence({kind:'ActivationStarted',unitId:'large'},{});assert.equal(c.inspect().suspended,false);
 assert.equal(f.presentation.viewingSideId,'blue');assert.equal(h.viewport.inspect().camera.side,'red');
 }finally{h.cleanup();}
});
test('director cancellation, session replacement and renderer switching clear sequence state',async()=>{
 const f=fixture(50),h=await harness(f);try{h.viewport.setPresentation('tabletop');const c=h.viewport.camera;
 const attack=()=>c.occurrence({kind:'AttackStarted',attackContext:{attackerId:'hero',targets:[{targetId:'large'}]}},{});
 attack();assert.equal(c.inspect().director.context,'Combat');c.cancel();assert.equal(c.inspect().director.context,'Overview');
 attack();h.viewport.setPresentation('flat');assert.equal(c.inspect().director.context,'Overview');h.viewport.setPresentation('tabletop');
 attack();c.zoom(1.2);h.viewport.renderFrame({...f,metadata:{...f.metadata,sessionId:'replacement'}});assert.equal(c.inspect().suspended,false);assert.equal(c.inspect().director.context,'Overview');assert.equal(c.inspect().transitionActive,false);
 }finally{h.cleanup();}
});

test('Follow all keeps observed other-Side decisions active instead of returning to the focus party',async()=>{
 const f=fixture(50,'blue');f.metadata.assignments.push({sideId:'red',controller:'Automated'});f.state.units[1].sideId='red';
 const h=await harness(f);try{h.viewport.setPresentation('tabletop');const c=h.viewport.camera;
 c.stable({unitId:'large',candidates:[]});assert.equal(c.inspect().director.context,'Overview');
 h.controls.follow.value='all';h.controls.follow.emit('change');c.stable({unitId:'large',candidates:[]});
 assert.equal(c.inspect().side,'blue');assert.equal(c.inspect().director.context,'Decision');
 c.resize(950,650);assert.equal(c.inspect().director.context,'Decision');
 h.controls.follow.value='own';h.controls.follow.emit('change');assert.equal(c.inspect().director.context,'Overview');
 }finally{h.cleanup();}
});
test('director honors Skip Effects, animation disabled and reduced motion without extra playback',async()=>{
 for(const mode of ['skip','disabled','reduced']){const h=await harness(fixture(50),{animate:()=>mode!=='disabled'});try{h.viewport.setPresentation('tabletop');const c=h.viewport.camera;
 if(mode==='reduced')globalThis.matchMedia=()=>({matches:true});
 c.manual({...c.current(),x:40,y:40});c.resume();c.occurrence({kind:'AttackStarted',attackContext:{attackerId:'hero',targets:[{targetId:'large'}]}},{},mode==='skip');
 assert.equal(c.inspect().transitionActive,false,mode);h.viewport.settle();h.flush(performance.now()+1000);assert.equal(h.frames.size,0);
 }finally{h.cleanup();}}
});

test('director scales the hidden 1x1 viewport pose when Tabletop first receives its real layout',async()=>{
 const h=await harness(fixture(50));try{const c=h.viewport.camera;h.viewport.setPresentation('tabletop');c.resize(1,1);c.setProjection('perspective',false);c.resume();c.resize(1100,790);c.stable({unitId:'hero',candidates:[]});h.viewport.settle();const p=c.current();assert.ok(p.scale>20,JSON.stringify(p));const screen=c.screen({x:2.5,y:2.5});assert.ok(screen.x>=0&&screen.x<=1100&&screen.y>=0&&screen.y<=790);}
 finally{h.cleanup();}
});

test('Resume from a manual overhead preset restores the director tilt range',async()=>{
 const h=await harness(fixture(30));try{const c=h.viewport.camera;h.viewport.setPresentation('tabletop');c.stable({unitId:'hero',candidates:[]});c.setProjection('perspective',false);h.controls.overhead.emit('click');c.resume();assert.ok(c.inspect().target.tilt>=20&&c.inspect().target.tilt<=60,JSON.stringify(c.inspect()));}
 finally{h.cleanup();}
});

test('Tabletop explicit mode and span changes preserve Full Fixed and Auto framing policies',async()=>{
 const h=await harness(fixture(50));try{const c=h.viewport.camera,g=await moduleAt('three-camera-geometry.js');h.viewport.setPresentation('tabletop');c.setMode('full');h.viewport.settle();c.setMode('fixed');h.viewport.settle();assert.ok(Math.abs(g.threeSpanScale(c.size,1,c.current())/c.current().scale-15)<1e-6);h.controls.span.value='10';h.controls.span.emit('change');h.viewport.settle();assert.ok(Math.abs(g.threeSpanScale(c.size,1,c.current())/c.current().scale-10)<1e-6);c.setMode('full');h.viewport.settle();c.setMode('auto');h.viewport.settle();assert.ok(c.current().scale>20);}
 finally{h.cleanup();}
});

test('layout changes during a Fixed transition preserve the selected span',async()=>{
 const h=await harness(fixture(50));try{const c=h.viewport.camera,g=await moduleAt('three-camera-geometry.js');h.viewport.setPresentation('tabletop');c.setMode('full');h.viewport.settle();c.setMode('fixed');h.controls.span.value='10';h.controls.span.emit('change');h.flush(performance.now()+80);c.resize(1090,788);assert.ok(Math.abs(g.threeSpanScale(c.size,1,c.current())/c.current().scale-10)<1e-6);}
 finally{h.cleanup();}
});

for(const projection of ['orthographic','perspective'])for(const context of ['Decision','Movement','Combat'])test(`Auto ${context} keeps its displayed pose and easing through layout changes (${projection})`,async t=>{
 let now=100;t.mock.method(performance,'now',()=>now);
 const h=await harness(fixture(50));try{
  const c=h.viewport.camera,g=await moduleAt('three-camera-geometry.js');
  h.viewport.setPresentation('tabletop');c.setProjection(projection,false);c.setMode('auto');h.viewport.settle();
  c.manual({...c.current(),x:40,y:40});c.resume();
  if(context==='Decision')c.stable({unitId:'hero',candidates:[]});
  else if(context==='Combat')c.occurrence({kind:'AttackStarted',attackContext:{attackerId:'hero',targets:[{targetId:'large'}]}},{});
  else{
   c.occurrence({kind:'MovementCompleted'},{role:'Movement',unitId:'hero',movementSegments:[[{x:2,y:2},{x:3,y:2}]],movingFigure:{sideId:'blue',cellSpan:1}});
   c.movementStep({x:3,y:2});
  }
  now+=80;h.flush(now);
  assert.equal(c.inspect().transitionActive,true);
  assert.ok(Math.hypot(c.current().x-c.inspect().target.x,c.current().y-c.inspect().target.y)>1,'the camera has not reached its destination');
  for(const [width,height]of [[899,599],[390,700]]){
   const before=c.current(),span=g.threeSpanScale(c.size,1,before)/before.scale;
   h.host.clientWidth=width;h.host.clientHeight=height;c.resize(width,height);
   const after=c.current();
   for(const key of ['x','y','tilt','yaw','fov','projectionMix'])assert.equal(after[key],before[key],`${key} must not jump to the destination on resize`);
   assert.ok(Math.abs(g.threeSpanScale(c.size,1,after)/after.scale-span)<1e-8,'resize preserves the displayed span');
   assert.equal(c.inspect().transitionActive,true,'Auto keeps easing instead of settling');
   assert.equal(c.inspect().director.context,context,'resize retains the observed shot instead of replacing Movement/Combat with a decision');
   c.tick(now);
   for(const key of ['tilt','yaw'])assert.ok(Math.abs(c.current()[key]-after[key])<1e-8,`${key} stays continuous on the next animation frame`);
   // Orthographic fitting can correct the active footprint when aspect changes;
   // a one-pixel layout fluctuation must still move it less than one screen pixel.
   if(width===899||projection==='perspective')for(const key of ['x','y'])
    assert.ok(Math.abs(c.current()[key]-after[key])<(projection==='perspective'?1e-8:1/after.scale),`${key} must not jump on the next animation frame`);
  }
  now+=2000;h.flush(now);
  assert.equal(c.inspect().transitionActive,false);
  assert.deepEqual(c.current(),c.inspect().target);
  assert.deepEqual(h.failures,[]);
 }finally{h.cleanup();}
});

for(const mode of ['disabled','reduced'])test(`Auto resize settles immediately when motion is ${mode}`,async()=>{
 let animated=true;const h=await harness(fixture(50),{animate:()=>animated});try{
  const c=h.viewport.camera;h.viewport.setPresentation('tabletop');c.setMode('auto');h.viewport.settle();
  c.manual({...c.current(),x:40,y:40});c.resume();assert.equal(c.inspect().transitionActive,true);
  if(mode==='disabled')animated=false;else globalThis.matchMedia=()=>({matches:true,addEventListener(){}});
  h.host.clientWidth=899;c.resize(899,600);
  assert.equal(c.inspect().transitionActive,false);
  assert.deepEqual(c.current(),c.inspect().target);
 }finally{h.cleanup();}
});

test('local illumination follows displayed checkpoints, switches off, survives movement and clears on restart',async()=>{
  const f=fixture(8,'blue');f.state.physical.board.terrain=[{kind:'Lava',position:{x:3,y:2}}];
  const h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();assert.equal(h.viewport.inspect().lighting.local.sources,0);
    const discovered=structuredClone(f);discovered.state.exploredCells.blue.push({x:3,y:2});h.viewport.renderFrame(discovered);h.flush();assert.equal(h.viewport.inspect().lighting.local.sources,1);
    const field=h.viewport.inspect().lighting.local;h.viewport.renderFrame(discovered);h.flush();assert.deepEqual(h.viewport.inspect().lighting.local,field);
    await h.viewport.present({role:'Movement',unitId:'hero',path:[{x:2,y:2},{x:2,y:3}]},{},{skipping:()=>false,pause:async()=>{h.flush(performance.now()+230);h.viewport.lighting.set({enabled:false});assert.equal(h.viewport.inspect().lighting.local.enabled,false);h.viewport.lighting.set({enabled:true});}});
    h.viewport.renderFrame(f);h.flush();assert.equal(h.viewport.inspect().lighting.local.sources,0);
    h.viewport.renderFrame(discovered);h.viewport.setPresentation('flat');h.viewport.settle();h.flush();assert.equal(h.viewport.inspect().lighting.local.enabled,false);
    h.viewport.setPresentation('tabletop');h.viewport.settle();h.flush();assert.equal(h.viewport.inspect().lighting.local.enabled,true);assert.equal(h.frames.size,0);
    const restart=structuredClone(f);restart.metadata.sessionId='new';h.viewport.renderFrame(restart);h.viewport.settle();h.flush();assert.equal(h.viewport.inspect().lighting.local.sources,0);assert.equal(h.frames.size,0);
  }finally{h.cleanup();}
});

test('Tabletop batches supplied choices without changing Cell picks and releases replaced instance buffers',async()=>{
  const f=fixture(),h=await harness(f),move={key:'move-a',interaction:{kind:'Position',position:{x:1,y:1}},boardVisual:{kind:'move'}};
  try{
    h.viewport.showInteraction({candidates:[move]});h.flush();const scene=h.rendered.at(-1).scene;
    assert.equal(scene.getObjectByName('tabletop-marks'),undefined,'Flat keeps its existing feedback renderer');
    h.viewport.setPresentation('tabletop');h.viewport.camera.setOrientation({tilt:38,yaw:25},false);h.viewport.settle();h.flush();
    const feedback=scene.getObjectByName('feedback'),first=feedback.children.find(n=>n.userData.tabletopMarks),counts=first.children.map(m=>m.count);
    assert.ok(first);assert.ok(first.children.length<=3);let instances=0,finishes=0,geometry=0;
    for(const mesh of first.children){mesh.addEventListener('dispose',()=>instances++);mesh.material.addEventListener('dispose',()=>finishes++);assert.equal(mesh.castShadow,false);assert.equal(mesh.receiveShadow,false);assert.equal(mesh.material.depthTest,false);assert.equal(mesh.material.depthWrite,false);}
    first.children[0].geometry.addEventListener('dispose',()=>geometry++);
    h.viewport.showInteraction({candidates:[move,{...move,key:'another-opaque-choice'}]});h.flush();
    const second=feedback.children.find(n=>n.userData.tabletopMarks);assert.notEqual(second,first);assert.deepEqual(second.children.map(m=>m.count),counts);
    assert.equal(instances,first.children.length);assert.equal(finishes,0);assert.equal(geometry,0);
    const point=h.viewport.project({x:1.5,y:1.5}),hit=h.viewport.pick(point);assert.deepEqual(hit.cell,{x:1,y:1});
    const {boardCandidates}=await moduleAt('playtest-viewport.js');assert.deepEqual(boardCandidates(hit,{candidates:[move,{...move,key:'another-opaque-choice'}]},f.state,f.presentation.figures).map(c=>c.key),['move-a','another-opaque-choice']);
    h.viewport.setPresentation('flat');h.viewport.settle();h.flush();assert.equal(scene.getObjectByName('tabletop-marks'),undefined);assert.equal(finishes,0);
    h.viewport.dispose();assert.equal(finishes,first.children.length);assert.equal(geometry,1);
  }finally{h.cleanup();}
});

test('Tabletop important labels retain HP, Side, posture and combat role and Flat labels restore their original text',async()=>{
  const f=fixture(),h=await harness(f);
  try{
    const labels=h.host.querySelector('.three-labels'),hero=()=>labels.children.find(n=>n.appearance?.name==='Hero');
    const originalState=hero().children[2].textContent;
    h.viewport.setPresentation('tabletop');h.viewport.camera.manual({...h.viewport.camera.current(),x:3,y:3,scale:100,tilt:38,yaw:0});h.viewport.settle();
    h.viewport.showInteraction({focusedUnitId:'hero',candidates:[]});h.flush();
    const label=hero();assert.equal(label.hidden,false);assert.equal(label.dataset.compact,'false');assert.equal(label.children[0].textContent,'3/5');assert.equal(label.children[1].textContent,'Hero');assert.equal(label.children[3].textContent,'blue');assert.match(label.children[2].textContent,/Lying/);assert.match(label.children[2].textContent,/Active/);assert.match(label.title,/Side blue/);
    h.viewport.showInteraction({relationship:{attackerId:'large',targets:[{targetId:'hero'}]},candidates:[]});h.flush();
    assert.equal(label.hidden,false);assert.match(label.children[2].textContent,/Lying/);assert.match(label.children[2].textContent,/Target/);assert.equal(label.children[0].textContent,'3/5');
    h.viewport.setPresentation('flat');h.viewport.camera.manual({...h.viewport.camera.current(),x:3,y:3,scale:100,tilt:0,yaw:0});h.viewport.settle();h.flush();
    assert.equal(label.children[1].textContent,'Hero');assert.equal(label.children[2].textContent,originalState);assert.equal(label.children[0].textContent,'3/5');
    assert.equal(labels.attributes['aria-hidden'],'true','visual plaques do not duplicate the accessible Cell descriptions');
  }finally{h.cleanup();}
});

test('Tabletop special action symbols and public Objective boundaries survive batching without filling unknown terrain',async()=>{
  const f=fixture(16,'blue');f.presentation.objectives=[{sideId:'blue',name:'Reach Area',area:{x:10,y:10,w:3,h:2}}];
  const h=await harness(f);try{
    h.viewport.setPresentation('tabletop');h.viewport.showInteraction({candidates:[{key:'special',interaction:{kind:'Position',position:{x:2,y:2}},boardVisual:{kind:'special',icon:'!'}}]});h.viewport.settle();h.flush();
    const scene=h.rendered.at(-1).scene,area=scene.getObjectByName('areas').children.find(n=>n.userData.tabletopMarks),feedback=scene.getObjectByName('feedback');
    assert.ok(area);assert.equal(area.getObjectByName('mark-wash'),undefined);
    const symbol=feedback.children.find(n=>n.isMesh&&!n.isInstancedMesh);assert.ok(symbol?.material.map);assert.equal(symbol.material.depthTest,false);assert.equal(symbol.castShadow,false);
    assert.equal(h.viewport.inspect().units,2,'public zones do not manufacture Units or labels in unknown Cells');
    assert.deepEqual(h.viewport.pick(h.viewport.project({x:2.5,y:2.5})).cell,{x:2,y:2});assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});

test('a Position preview with no placement Cells emphasizes its supplied Cell without changing the pick',async()=>{
  const f=fixture();f.state.currentUnitId=null;const h=await harness(f);
  const candidate={key:'opaque-move',interaction:{kind:'Position',position:{x:1,y:1}},placementCells:[],boardVisual:{kind:'move'}},original=structuredClone(candidate);
  try{
    h.viewport.setPresentation('tabletop');h.viewport.camera.setMode('full');h.viewport.settle();
    h.viewport.showInteraction({candidates:[candidate]});h.flush();
    const scene=h.rendered.at(-1).scene,ink=()=>scene.getObjectByName('feedback').getObjectByName('mark-ink');
    const ordinaryCount=ink().count,point=h.viewport.project({x:1.5,y:1.5}),before=h.viewport.pick(point);
    h.viewport.showInteraction({candidates:[candidate],preview:candidate});h.flush();
    assert.ok(ink().count>ordinaryCount,'hover adds a distinct preview even when placementCells is empty');
    const matrix=new h.THREE.Matrix4(),position=new h.THREE.Vector3();
    for(let i=0;i<ink().count;i++){ink().getMatrixAt(i,matrix);position.setFromMatrixPosition(matrix);assert.ok(position.x>=1&&position.x<=2&&position.z>=1&&position.z<=2,'preview stays at the supplied Cell');}
    assert.deepEqual(h.viewport.pick(point),before);assert.deepEqual(before.cell,{x:1,y:1});assert.deepEqual(candidate,original);
  }finally{h.cleanup();}
});

test('replacing a session clears inspected tabletop label emphasis for reused Unit IDs',async()=>{
  const f=fixture();f.state.currentUnitId=null;f.state.physical.figures[0].posture='Upright';const h=await harness(f);
  try{
    h.viewport.setPresentation('tabletop');h.viewport.camera.manual({x:2.5,y:2.5,scale:110});h.viewport.settle();h.flush();
    const label=()=>h.host.querySelector('.three-token-info');assert.equal(label().dataset.important,'false');
    for(const key of ['ArrowRight','ArrowRight','ArrowDown','ArrowDown'])h.host.emit('keydown',{key});
    h.flush();assert.equal(h.hovered.at(-1),'hero');assert.equal(label().dataset.important,'true');
    h.viewport.renderFrame(structuredClone(f));h.flush();assert.equal(label().dataset.important,'true','inspection survives updates within the same session');
    const replacement=structuredClone(f);replacement.metadata.sessionId='replacement';h.viewport.renderFrame(replacement);h.viewport.settle();h.flush();
    assert.equal(label().dataset.important,'false','the same Unit ID in a new session does not inherit inspection');
    assert.equal(label().dataset.expanded,'false');assert.deepEqual(h.failures,[]);
  }finally{h.cleanup();}
});
