const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

class CameraElement {
  constructor(id) {
    this.id=id; this.children=[]; this.style={}; this.dataset={}; this.attributes={}; this.listeners={}; this.value=""; this.hidden=false;
    this.rect={left:0,top:0,width:900,height:600,right:900,bottom:600};
    const classes=new Set(); this.classList={add:c=>classes.add(c),remove:c=>classes.delete(c),contains:c=>classes.has(c)};
  }
  append(child) { child.parent=this; this.children.push(child); }
  replaceChildren() { this.children=[]; }
  remove() { this.parent.children=this.parent.children.filter(c=>c!==this); }
  setAttribute(k,v) { this.attributes[k]=v; }
  getBoundingClientRect() { return this.rect; }
  addEventListener(type,cb,options) { const key=options===true?`${type}:capture`:type; (this.listeners[key]??=[]).push(cb); }
  emit(type,event={}) { for(const cb of this.listeners[type]??[]) cb(event); }
  closest() { return null; }
  setPointerCapture(id) { this.captured=id; }
  hasPointerCapture(id) { return this.captured===id; }
  releasePointerCapture() { this.captured=null; }
}
const cameraState = (x=20) => ({round:1,currentUnitId:"actor",physical:{board:{width:50,height:50},figures:[{id:"actor",position:{x,y:20}},{id:"friend",position:{x:x+2,y:20}},{id:"enemy",position:{x:40,y:40}}]},units:[{id:"actor",typeId:"hero",sideId:"blue"},{id:"friend",typeId:"other-hero",sideId:"blue"},{id:"enemy",typeId:"monster",sideId:"red"}]});
const cameraPresentation = (state,side=null) => ({viewingSideId:side,figures:Object.fromEntries(state.physical.figures.map(f=>[f.id,{occupiedCells:[f.position],cellSpan:1}])),objectives:[]});
const cameraDecision = (xs=[18,23]) => ({unitId:"actor",candidates:xs.map(x=>({boardVisual:{kind:"move"},placementCells:[{x,y:20}],relevant:false}))});
function controllerHarness(prefs={}, state=cameraState(), side=null) {
  const nodes=new Map(), get=id=>{ if(!nodes.has(id)) nodes.set(id,new CameraElement(id)); return nodes.get(id); };
  const documentEvents={};
  const document={getElementById:get,createElement:()=>new CameraElement(),addEventListener:(type,cb)=>(documentEvents[type]??=[]).push(cb)};
  const viewport=get("camera-viewport"),world=get("camera-world"),boardElement=get("board");
  get("board-overlays").rect={left:0,top:0,right:0,bottom:0,width:0,height:0};
  const timers=new Map(),frames=new Map(),storage=new Map([["delve.camera.v1",JSON.stringify(prefs)]]); let handle=0,reduce=false,enabled=true,previewCleared=0;
  const media={get matches(){return reduce;},addEventListener(type,cb){this.change=cb;}};
  class Matrix {constructor(text){const p=text.match(/matrix\((.*)\)/)[1].split(",").map(Number); [this.a,this.b,this.c,this.d,this.e,this.f]=p;}}
  const environment={localStorage:{getItem:k=>storage.get(k)??null,setItem:(k,v)=>storage.set(k,v)},
    getComputedStyle:()=>({transform:environment.renderedTransform??world.style.transform??"none"}),DOMMatrixReadOnly:Matrix,
    matchMedia:()=>media,setTimeout:cb=>{timers.set(++handle,cb);return handle;},clearTimeout:id=>timers.delete(id),
    requestAnimationFrame:cb=>{frames.set(++handle,cb);return handle;},cancelAnimationFrame:id=>frames.delete(id),
    ResizeObserver:class {constructor(cb){environment.resize=cb;}observe(){}},addEventListener(){}};
  const context=vm.createContext({document,viewport,world,boardElement,environment,animate:()=>enabled,clearPreview:()=>previewCleared++});
  for(const file of ["camera-geometry.js","camera-observation.js","board-camera.js"])
    vm.runInContext(fs.readFileSync(path.join(__dirname,"../ProjectDelve.Web/wwwroot",file),"utf8").replace(/^import .*\r?\n/gm,"").replace(/^export /gm,""),context);
  const camera=vm.runInContext("createBoardCamera({document,viewport,world,boardElement,environment,animate,clearPreview})",context);
  const metadata={sessionId:"a",round:1,assignments:[{typeId:"hero",sideId:"blue",controller:"Human"},{typeId:"other-hero",sideId:"blue",controller:"Human"},{typeId:"monster",sideId:"red",controller:"Automated"}]};
  const presentation=cameraPresentation(state,side); if(side)state.exploredCells={[side]:state.physical.figures.filter(f=>f.id!=="enemy").map(f=>f.position)};
  camera.setFrame(state,presentation,metadata);
  return {camera,nodes,get,viewport,world,environment,state,presentation,metadata,timers,frames,storage,documentEvents,
    inspect:()=>JSON.parse(JSON.stringify(camera.inspect())),
    change:(id,value)=>{get(`camera-${id}`).value=value;get(`camera-${id}`).emit("change");},
    update:()=>camera.setFrame(state,presentation,metadata),
    reduce:()=>{reduce=true;media.change();},disable:()=>{enabled=false;camera.settle();},preview:()=>previewCleared};
}
function pointerEvent(target,values={}) {return {target,button:0,pointerId:1,pointerType:"mouse",clientX:450,clientY:300,detail:1,preventDefault(){this.prevented=true;},stopImmediatePropagation(){this.stopped=true;},...values};}
test("Auto uses all projected placements and expands without shrinking or rerender oscillation",()=>{
  const h=controllerHarness({margin:0});h.camera.stable(cameraDecision());const first=h.inspect();
  h.camera.stable(cameraDecision());assert.deepEqual(h.inspect().target,first.target);
  h.camera.stable(cameraDecision([10,30]));const expanded=h.inspect();assert.ok(expanded.fittedSpan>first.fittedSpan);
  h.camera.stable(cameraDecision([]));assert.equal(h.inspect().target.scale,expanded.target.scale);
  h.update();h.camera.stable(cameraDecision([]));assert.equal(h.inspect().target.scale,expanded.target.scale);
});
test("initial view of a scattered party frames an observed Human Unit instead of empty space",()=>{
  const state=cameraState();state.physical.figures.find(f=>f.id==="friend").position={x:49,y:49};
  const h=controllerHarness({},state);assert.equal(h.inspect().target.x,20.5);assert.equal(h.inspect().target.y,20.5);
});
test("Bonus Action allowance changes and Move-after-Attack expand from projected placements only",()=>{
  const h=controllerHarness({margin:0});h.camera.stable(cameraDecision([]));const small=h.inspect().target.scale;
  h.camera.stable({unitId:"actor",candidates:[{boardVisual:{kind:"special"},placementCells:[{x:49,y:49}]}]});
  assert.equal(h.inspect().target.scale,small); // Displacing someone else is not a Move destination.
  h.camera.stable(cameraDecision([12,28]));const expanded=h.inspect().target.scale;assert.ok(expanded<small);
  h.camera.stable(cameraDecision([]));assert.equal(h.inspect().target.scale,expanded);
  h.camera.stable(cameraDecision([10,30]));assert.ok(h.inspect().target.scale<=expanded);
});
test("manual zoom suspends Auto through decisions and rerenders; new Human activation resumes",()=>{
  const h=controllerHarness();h.camera.stable(cameraDecision());h.camera.zoom(1.2);const manual=h.inspect();
  h.camera.stable(cameraDecision([10,30]));h.update();h.camera.stable(cameraDecision([10,30]));
  assert.deepEqual(h.inspect().target,manual.target);assert.equal(h.inspect().suspended,true);
  h.camera.occurrence({kind:"ActivationStarted",unitId:"actor"},{role:"Notice"});h.camera.stable(cameraDecision());
  assert.equal(h.inspect().suspended,false);
});
test("Follow Off stays off through new activations and movement without zoom or pan",()=>{
  const h=controllerHarness({follow:"off"});h.camera.zoom(1.2);const manual=h.inspect().target;
  h.camera.occurrence({kind:"ActivationStarted",unitId:"actor"},{});h.camera.stable(cameraDecision([0,49]));
  h.camera.occurrence({kind:"MovementCompleted",unitId:"actor"},{role:"Movement",unitId:"actor",path:[{x:20,y:20},{x:40,y:40}]},true);
  assert.deepEqual(h.inspect().target,manual);assert.equal(h.inspect().preferences.follow,"off");
});
test("visible Monster following retains zoom and hidden events cannot start a transition",()=>{
  const h=controllerHarness({mode:"fixed",follow:"all"},cameraState(),"blue");const before=h.inspect().target;
  for(const kind of ["ActivationStarted","UnitCreated","AttackStarted","WorldCardDrawn"])
    h.camera.occurrence({kind,unitId:"enemy",attackContext:{attackerId:"enemy",targets:[]}},{role:"Notice"});
  assert.deepEqual(h.inspect().target,before);assert.equal(h.inspect().transitionActive,false);
  h.state.exploredCells.blue.push({x:40,y:40});h.update();h.camera.occurrence({kind:"ActivationStarted",unitId:"enemy"},{role:"Notice"});
  assert.equal(h.inspect().target.scale,before.scale);assert.ok(h.inspect().target.x>before.x);
});
test("movement uses current displayed exploration and never its hidden boundary padding",()=>{
  const h=controllerHarness({mode:"fixed",follow:"all"},cameraState(),"blue");
  h.state.exploredCells.blue=[{x:20,y:20},{x:30,y:20}];h.update();
  const event={role:"Movement",unitId:"enemy",movementSegments:[[{x:29,y:20},{x:30,y:20},{x:31,y:20}]],movingFigure:{cellSpan:1,sideId:"red"}};
  h.camera.occurrence({kind:"MovementObserved"},event);const before=h.inspect().target;
  h.camera.movementStep({x:29,y:20});assert.deepEqual(h.inspect().target,before);
  h.camera.movementStep({x:30,y:20});const visible=h.inspect().target;
  h.camera.movementStep({x:31,y:20});assert.deepEqual(h.inspect().target,visible);
  assert.ok(visible.x>before.x);
});
test("drag captures the pointer and suppresses a gameplay click; a small movement remains a click",()=>{
  const h=controllerHarness({mode:"fixed"});const cell=new CameraElement();
  h.viewport.emit("pointerdown",pointerEvent(cell));h.viewport.emit("pointermove",pointerEvent(cell,{clientX:454}));
  const click=pointerEvent(cell);h.viewport.emit("click:capture",click);assert.ok(!click.stopped);assert.equal(h.inspect().suspended,false);
  h.viewport.emit("pointerup",pointerEvent(cell));
  h.viewport.emit("pointerdown",pointerEvent(cell));h.viewport.emit("pointermove",pointerEvent(cell,{clientX:510}));
  assert.equal(h.viewport.captured,1);assert.equal(h.preview(),1);assert.equal(h.inspect().suspended,true);
  h.viewport.emit("pointerup",pointerEvent(cell));const draggedClick=pointerEvent(cell);h.viewport.emit("click:capture",draggedClick);
  assert.equal(draggedClick.stopped,true);assert.equal(h.viewport.captured,null);
});
test("touch threshold, pointer cancellation, middle drag and right click preserve intent",()=>{
  const h=controllerHarness({mode:"fixed"});const cell=new CameraElement();
  h.viewport.emit("pointerdown",pointerEvent(cell,{button:2}));h.viewport.emit("pointermove",pointerEvent(cell,{clientX:520}));assert.equal(h.inspect().suspended,false);
  h.viewport.emit("pointerdown",pointerEvent(cell,{pointerType:"touch"}));h.viewport.emit("pointermove",pointerEvent(cell,{pointerType:"touch",clientX:458}));assert.equal(h.inspect().suspended,false);
  h.viewport.emit("pointermove",pointerEvent(cell,{pointerType:"touch",clientX:462}));assert.equal(h.inspect().suspended,true);
  h.viewport.emit("pointercancel",pointerEvent(cell));assert.equal(h.viewport.captured,null);
  h.camera.resume();h.viewport.emit("pointerdown",pointerEvent(cell,{button:1}));h.viewport.emit("pointermove",pointerEvent(cell,{clientX:452}));assert.equal(h.inspect().suspended,true);
});
test("a click ending outside the viewport and a cancelled drag cannot poison later input",()=>{
  const h=controllerHarness({mode:"fixed"}),cell=new CameraElement();
  h.viewport.emit("pointerdown",pointerEvent(cell));
  for(const cb of h.documentEvents.pointerup)cb(pointerEvent(cell));
  h.viewport.emit("pointerdown",pointerEvent(cell));h.viewport.emit("pointermove",pointerEvent(cell,{clientX:500}));
  assert.equal(h.inspect().suspended,true);h.viewport.emit("pointercancel",pointerEvent(cell));
  const overlay=new CameraElement();overlay.closest=()=>overlay;
  h.viewport.emit("pointerdown",pointerEvent(overlay));const click=pointerEvent(overlay);h.viewport.emit("click:capture",click);
  assert.ok(!click.stopped);
});
test("wheel leaves browser zoom and ordinary scrolling alone; Alt wheel anchors the current rendered transform",()=>{
  const h=controllerHarness({mode:"fixed"});
  const normal=pointerEvent(h.viewport,{deltaY:-100});h.viewport.emit("wheel",normal);assert.ok(!normal.prevented);
  const browser=pointerEvent(h.viewport,{deltaY:-100,ctrlKey:true,altKey:true});h.viewport.emit("wheel",browser);assert.ok(!browser.prevented);
  // An interrupted CSS transition has a different current pose than its target.
  h.environment.renderedTransform="matrix(1, 0, 0, 1, -600, -900)";
  const actual=h.inspect().current;const anchored={x:actual.x+(300-450)/actual.scale,y:actual.y};
  const alt=pointerEvent(h.viewport,{deltaY:-100,altKey:true,clientX:300});h.viewport.emit("wheel",alt);assert.equal(alt.prevented,true);
  const next=h.inspect().target;assert.ok(Math.abs(next.x+(300-450)/next.scale-anchored.x)<.00001);
});
test("Full zoom switches to Custom Fixed, persists preferences and reload excludes transient state",()=>{
  const h=controllerHarness({mode:"full"});h.camera.zoom(1.2);const view=h.inspect();
  assert.equal(view.preferences.mode,"fixed");assert.equal(h.get("camera-span").value,"custom");
  const saved=JSON.parse(h.storage.get("delve.camera.v1"));assert.ok(!("target" in saved));assert.ok(!("suspended" in saved));
  const reloaded=controllerHarness(saved);assert.equal(reloaded.inspect().preferences.fixedSpan,saved.fixedSpan);assert.equal(reloaded.inspect().suspended,false);
});
test("zoom buttons animate while direct dragging tracks the pointer without easing",()=>{
  const h=controllerHarness({mode:"fixed"});h.get("camera-in").emit("click");
  assert.equal(h.inspect().transitionActive,true);assert.match(h.world.style.transition,/transform/);
  const cell=new CameraElement();h.viewport.emit("pointerdown",pointerEvent(cell));h.viewport.emit("pointermove",pointerEvent(cell,{clientX:500}));
  assert.equal(h.inspect().transitionActive,false);assert.equal(h.world.style.transition,"none");
});
test("Reset and Resume restore Auto; replacement clears caches and transitions while preserving preferences",()=>{
  const h=controllerHarness();h.camera.stable(cameraDecision());h.camera.zoom(1.2);h.get("camera-resume").emit("click");assert.equal(h.inspect().suspended,false);
  h.camera.zoom(1.2);h.get("camera-reset").emit("click");assert.equal(h.inspect().suspended,false);
  h.camera.zoom(1.2);h.metadata.sessionId="replacement";h.update();assert.equal(h.inspect().suspended,false);assert.equal(h.inspect().activationKey,null);
  assert.equal(h.inspect().preferences.mode,"auto");assert.equal(h.inspect().transitionActive,false);assert.equal(h.frames.size,0);
});
test("resize preserves Fixed center, recomputes span and centers small board axes",()=>{
  const h=controllerHarness({mode:"fixed",fixedSpan:15});const before=h.inspect().target;
  h.viewport.rect={left:0,top:0,width:600,height:900,right:600,bottom:900};h.environment.resize();
  assert.equal(h.inspect().target.x,before.x);assert.equal(h.inspect().target.y,before.y);assert.equal(h.inspect().target.scale,60);
  assert.match(h.world.style.transform,/matrix\(1, 0, 0, 1, -/);
  h.state.physical.board.height=3;h.update();h.viewport.rect.width=700;h.environment.resize();assert.equal(h.inspect().target.y,1.5);
});
test("cancelled movement cannot continue following stale playback steps",()=>{
  const h=controllerHarness({mode:"fixed",follow:"all"});
  h.camera.occurrence({kind:"MovementCompleted"},{role:"Movement",unitId:"enemy",path:[{x:20,y:20},{x:35,y:35}]});
  h.camera.cancel();const before=h.inspect().target;h.camera.movementStep({x:35,y:35});
  assert.deepEqual(h.inspect().target,before);assert.equal(h.frames.size,0);
});
test("a second Human-controlled group on the viewing Side gets its own activation fit",()=>{
  const h=controllerHarness();h.camera.stable(cameraDecision());h.camera.zoom(1.2);
  h.camera.occurrence({kind:"ActivationStarted",unitId:"friend"},{});
  h.camera.stable({unitId:"friend",candidates:[{boardVisual:{kind:"move"},placementCells:[{x:28,y:20}]}]});
  assert.equal(h.inspect().suspended,false);assert.match(h.inspect().activationKey,/friend/);assert.ok(h.inspect().fittedSpan>=7);
});
test("Skip, disabled animation, reduced motion and cancellation remove every camera animation callback",()=>{
  for(const action of [h=>h.camera.settle(),h=>h.disable(),h=>h.reduce(),h=>h.camera.cancel()]) {
    const h=controllerHarness({mode:"fixed",follow:"all"});
    h.camera.occurrence({kind:"MovementCompleted"},{role:"Movement",unitId:"actor",path:[{x:20,y:20},{x:28,y:20}]});
    h.camera.movementStep({x:28,y:20});
    assert.equal(h.inspect().transitionActive,true);assert.ok(h.frames.size>0 && h.timers.size>0);
    action(h);assert.equal(h.inspect().transitionActive,false);assert.equal(h.frames.size,0);assert.equal(h.timers.size,0);assert.equal(h.world.style.transition,"none");
    if(h.camera.inspect().preferences.follow==="off")assert.fail("settling must not change Follow preference");
  }
});
test("multiple observed Objective buttons preserve zoom and exploration; hidden Objectives stay absent",()=>{
  const h=controllerHarness({mode:"fixed"},cameraState(),"blue");
  h.presentation.objectives=[{sideId:"blue",area:{x:0,y:0,w:2,h:2}},{sideId:"blue",area:{x:0,y:1,w:2,h:2}},{sideId:"red",area:{x:49,y:49,w:1,h:1}}];h.update();
  const buttons=h.get("camera-objectives").children;assert.equal(buttons.length,2);
  assert.ok(Math.hypot(parseFloat(buttons[0].style.left)-parseFloat(buttons[1].style.left),parseFloat(buttons[0].style.top)-parseFloat(buttons[1].style.top))>=45);
  const explored=JSON.stringify(h.state.exploredCells),scale=h.inspect().target.scale;buttons[0].emit("click");
  assert.equal(h.inspect().target.scale,scale);assert.equal(h.inspect().suspended,true);assert.equal(JSON.stringify(h.state.exploredCells),explored);
  assert.match(buttons[0].attributes["aria-label"],/blue Reach Area/);
});
test("camera Side is stable under active token changes and multiple Human groups; FoW fixes ownership",()=>{
  const h=controllerHarness();h.metadata.assignments.push({typeId:"monster",sideId:"red",controller:"Human"});h.update();
  assert.equal(h.get("camera-side-label").hidden,false);h.change("side","red");assert.equal(h.inspect().side,"red");
  h.state.activeToken={sideId:"blue"};h.update();assert.equal(h.inspect().side,"red");
  h.presentation.viewingSideId="blue";h.state.exploredCells={blue:[{x:20,y:20}]};h.update();assert.equal(h.inspect().side,"blue");assert.equal(h.get("camera-side-label").hidden,true);
});
