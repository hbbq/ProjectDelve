const {test}=require('node:test');
const assert=require('node:assert/strict');
const {pathToFileURL}=require('node:url');
const path=require('node:path');
const at=name=>import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot',name)).href);
const size={width:900,height:600},prefs={mode:'auto',minSpan:7,maxSpan:15,margin:2};
const pose={x:5,y:5,scale:55,tilt:40,yaw:0,fov:45,projectionMix:1,visualHeight:1.5};
function frame(){return {board:{width:60,height:60},units:[{id:'a',sideId:'blue'},{id:'b',sideId:'red'},{id:'c',sideId:'blue'}],figures:[{id:'a',position:{x:4,y:4}},{id:'b',position:{x:8,y:4}},{id:'c',position:{x:5,y:6}}],geometry:{},objectives:[],explored:null};}
const choice=(id,kind='attack')=>({interaction:{kind:'Unit',unitId:id},boardVisual:{kind}});
test('Side focus accepts projected selectable Units without controller assumptions; Spectator accepts all',async()=>{
 const d=await at('three-camera-director.js'),f=frame(),decision={candidates:[choice('a','activate'),choice('c','activate')]};
 assert.deepEqual(d.directorDecision(f,decision,'blue').subjects,['a','c']);
 assert.equal(d.directorDecision(f,decision,'red'),null);
 assert.deepEqual(d.directorDecision(f,{unitId:'b',candidates:[]},null).subjects,['b']);
 assert.deepEqual(d.directorDecision(f,{unitId:'b',candidates:[]},'red').subjects,['b']);
});
test('decision composition uses legal movement, action, placement, Door and affected candidates',async()=>{
 const {directorDecision}=await at('three-camera-director.js'),f=frame();
 const shot=directorDecision(f,{unitId:'a',candidates:[choice('b'),{placementCells:[{x:6,y:4},{x:7,y:4}]},{interaction:{position:{x:5,y:7}}},{interaction:{door:{a:{x:3,y:4},b:{x:4,y:4}}}},{affectedUnitIds:['c']}]},'blue');
 assert.equal(shot.context,'Decision');assert.equal(shot.legal.length,5);assert.deepEqual(shot.legal[1],{x:6,y:4,w:2,h:1});
});
test('unknown Unit IDs and hidden footprints cannot become camera subjects',async()=>{
 const {directorDecision,createCameraDirector}=await at('three-camera-director.js'),f=frame();f.explored=new Set(['4,4']);
 const shot=directorDecision(f,{unitId:'a',candidates:[choice('b'),choice('secret')]},'blue');assert.equal(shot.legal.length,0);
 const d=createCameraDirector();d.event(f,{kind:'AttackStarted',attackContext:{attackerId:'secret',targets:[{targetId:'b'}]}},{});
 assert.equal(d.compose(f,pose,size,prefs,'blue'),null);
});
test('combat pairs retain a coherent context through dice, damage and defeat, then yield to decisions',async()=>{
 const {createCameraDirector}=await at('three-camera-director.js'),d=createCameraDirector(),f=frame();
 d.event(f,{kind:'AttackStarted',attackContext:{attackerId:'a',targets:[{targetId:'b'}]}},{});
 for(const role of ['Dice','Damage','Defeat']){d.event(f,{kind:role},{role,targetId:'b'});assert.equal(d.inspect().context,'Combat');}
 d.stable(f,{roll:{},candidates:[]},'blue');assert.equal(d.inspect().context,'Combat');
 d.stable(f,{unitId:'a',candidates:[]},'blue');assert.equal(d.inspect().context,'Decision');
 d.stable(f,null,'blue');assert.equal(d.inspect().context,'Overview');
});
test('combat composition frames both observed participants with model envelopes',async()=>{
 const {chooseDirectorPose}=await at('three-camera-director.js'),g=await at('three-camera-geometry.js');
 const primary=[{x:20,y:20,w:1,h:1},{x:26,y:22,w:2,h:2}],shot={context:'Combat',primary,legal:[],secondary:[],subjects:['a','b']};
 const result=chooseDirectorPose(pose,shot,size,prefs,frame().board);assert.ok(result);
 for(const b of primary){const p=g.threeScreenBounds(b,result.pose,size,1.5);assert.ok(!p.behind&&p.x>=4&&p.x+p.w<=size.width-4&&p.y>=4&&p.y+p.h<=size.height-4);}
});
test('movement accepts only visible segment anchors and never connects disconnected runs',async()=>{
 const {createCameraDirector}=await at('three-camera-director.js'),d=createCameraDirector(),f=frame();f.explored=new Set(['4,4','5,4','8,4']);
 d.event(f,{kind:'UnitMoved'},{role:'Movement',unitId:'a',path:[{x:4,y:4},{x:5,y:4},{x:6,y:4},{x:8,y:4}]});
 d.step({x:6,y:4},f);assert.equal(d.inspect().context,'Overview');
 d.step({x:4,y:4},f);d.step({x:5,y:4},f);assert.equal(d.inspect().context,'Movement');
 d.step({x:8,y:4},f);assert.deepEqual(d.finish(),{x:8,y:4});
 const twin=createCameraDirector();twin.event(f,{kind:'UnitMoved'},{role:'Movement',unitId:'a',path:[{x:8,y:4}]});twin.step({x:8,y:4},f);
 assert.deepEqual(d.compose(f,pose,size,prefs,'blue'),twin.compose(f,pose,size,prefs,'blue'));
});
test('candidate scoring prioritizes primary visibility and includes actual world travel',async()=>{
 const {scoreDirectorPose}=await at('three-camera-director.js'),g=await at('three-camera-geometry.js'),shot={primary:[{x:4,y:4,w:1,h:1}],legal:[],secondary:[]};
 const same=scoreDirectorPose(pose,pose,shot,size),far=scoreDirectorPose({...pose,x:40},pose,shot,size);assert.ok(far.score>same.score+1000);
 const look=g.threeLookAt(pose,{x:6,y:5},size);assert.ok(scoreDirectorPose(look,pose,shot,size).travel<1e-9);
 assert.ok(scoreDirectorPose({...pose,yaw:15},pose,shot,size).travel>1);
});
test('hysteresis retains comfortable views, is deterministic, and immediately corrects clipped primary',async()=>{
 const {chooseDirectorPose}=await at('three-camera-director.js'),shot={context:'Decision',primary:[{x:4,y:4,w:1,h:1}],legal:[],secondary:[]};
 // A readable close shot is genuinely good; V1's nominal scale 55 left a
 // perspective footprint undersized and is intentionally no longer protected.
 assert.equal(chooseDirectorPose({...pose,scale:85},shot,size,prefs,frame().board),null);
 shot.primary=[{x:30,y:30,w:1,h:1}];const a=chooseDirectorPose(pose,shot,size,prefs,frame().board);
 assert.equal(a.reason,'primary clipped');assert.deepEqual(a,chooseDirectorPose(pose,shot,size,prefs,frame().board));
 assert.equal(chooseDirectorPose(a.pose,shot,size,prefs,frame().board),null);
});
test('widely scattered optional choices cannot force unbounded zoom',async()=>{
 const {chooseDirectorPose}=await at('three-camera-director.js'),shot={context:'Decision',primary:[{x:25,y:25,w:1,h:1}],legal:[{x:59,y:59,w:1,h:1}],secondary:[{x:0,y:0,w:1,h:1}]};
 const r=chooseDirectorPose(pose,shot,size,prefs,frame().board);assert.ok(r.overflow);assert.ok(r.pose.scale>=40);
});
test('perspective look-around preserves world position at every interpolation sample and leaves FOV unchanged',async()=>{
 const g=await at('three-camera-geometry.js'),to=g.threeLookAt(pose,{x:6,y:5},size),a=g.threeCameraPosition(pose,size);
 for(const t of [0,.1,.5,.9,1]){const p=g.interpolateCameraWorld(pose,to,t,size),b=g.threeCameraPosition(p,size);assert.ok(Math.hypot(a.x-b.x,a.y-b.y,a.height-b.height)<1e-9);assert.equal(p.fov,45);}
});
test('orbit changes world position around a focus; translation changes location; zero roll basis stays orthogonal',async()=>{
 const g=await at('three-camera-geometry.js'),a=g.threeCameraPosition(pose,size),orbit={...pose,yaw:30},b=g.threeCameraPosition(orbit,size),c=g.threeCameraPosition({...pose,x:pose.x+4},size);
 assert.equal(orbit.x,pose.x);assert.equal(orbit.y,pose.y);assert.ok(Math.hypot(a.x-b.x,a.y-b.y)>1);assert.ok(Math.abs(c.x-a.x-4)<1e-9);assert.equal(c.height,a.height);
 const T=await at('vendor/three/0.180.0/build/three.module.js');
 for(const tilt of [20,40,60])for(const yaw of [0,90,175]){
  const p={...pose,tilt,yaw},basis=g.orientationBasis(p),location=g.threeCameraPosition(p,size),camera=new T.PerspectiveCamera(45,1.5,.01,1000);
  camera.position.set(location.x,location.height,location.y);camera.up.set(-basis.ct*basis.s,basis.st,-basis.ct*basis.c);camera.lookAt(p.x,0,p.y);camera.updateMatrixWorld(true);
  assert.ok(Math.abs(camera.matrixWorld.elements[1])<1e-12,'screen-right has no world vertical component');
  const foot=new T.Vector3(p.x,0,p.y).project(camera),head=new T.Vector3(p.x,1,p.y).project(camera);assert.ok(Math.abs(head.x-foot.x)<1e-12);assert.ok(head.y>foot.y);
 }
});
test('automatic tilt is bounded independently of manual geometry; orthographic uses framing fallback',async()=>{
 const {chooseDirectorPose}=await at('three-camera-director.js'),g=await at('three-camera-geometry.js'),shot={context:'Decision',primary:[{x:20,y:20,w:1,h:1}],legal:[],secondary:[]};
 for(const projectionMix of [0,1])for(const tilt of [0,75]){const p={...pose,projectionMix,tilt},r=chooseDirectorPose(p,shot,size,prefs,frame().board);assert.ok(r.pose.tilt>=20&&r.pose.tilt<=60);if(!projectionMix){assert.equal(g.threeLookAt(p,{x:20,y:20},size),null);assert.notEqual(r.adjustment,'look-around');}}
});
test('reset clears playback context and accepted diagnostics',async()=>{
 const {createCameraDirector}=await at('three-camera-director.js'),d=createCameraDirector(),f=frame();d.stable(f,{unitId:'a',candidates:[]},'blue');d.compose(f,{...pose,x:50},size,prefs,'blue');assert.ok(d.inspect().diagnostic);d.reset();assert.deepEqual(d.inspect(),{context:'Overview',diagnostic:null});assert.equal(d.finish(),undefined);
});

test('primary readability uses projected footprint and visual envelope across lenses and viewport sizes',async()=>{
 const d=await at('three-camera-director.js');const primary=[{x:20,y:20,w:1,h:1}];
 for(const viewport of [size,{width:380,height:650}])for(const projectionMix of [0,1]){
  const current={...pose,x:20.5,y:20.5,projectionMix,scale:16,visualOverhang:.356,visualHeight:2.05};
  const r=d.chooseDirectorPose(current,{context:'Decision',primary,legal:[],secondary:[]},viewport,prefs,frame().board);
  assert.ok(r);assert.ok(r.safe);assert.ok(d.directorSubjectSize(primary[0],r.pose,viewport)>=d.directorReadability(viewport).minimum,JSON.stringify(r));assert.equal(r.clipped,false);
 }
});
test('hundreds of legal movement Cells cannot vote down readability or force a wide shot',async()=>{
 const d=await at('three-camera-director.js'),primary=[{x:20,y:20,w:1,h:1}],legal=[];
 for(let y=0;y<50;y++)for(let x=0;x<50;x++)legal.push({x,y,w:1,h:1});
 const current={...pose,x:20,y:20,scale:15},shot={context:'Decision',primary,legal,secondary:[]};
 const r=d.chooseDirectorPose(current,shot,size,prefs,frame().board);assert.ok(r.overflow);assert.ok(r.subjectPixels>=54);assert.ok(r.pose.scale>50);
 const duplicate=d.chooseDirectorPose(current,{...shot,legal:[...legal,...legal]},size,prefs,frame().board);assert.deepEqual(duplicate.pose,r.pose);
});
test('empty-space compositions zoom in, then converge without repeated zoom pulses',async()=>{
 const d=await at('three-camera-director.js'),primary=[{x:20,y:20,w:1,h:1}],shot={context:'Decision',primary,legal:[],secondary:[]};
 const current={...pose,x:20.5,y:20.5,scale:22},r=d.chooseDirectorPose(current,shot,size,prefs,frame().board);
 assert.ok(r.pose.scale>current.scale*2);assert.ok(r.subjectPixels>75);
 for(let i=0;i<20;i++)assert.equal(d.chooseDirectorPose(r.pose,shot,size,prefs,frame().board),null);
});
test('compact four-Unit overview improves utilization while keeping the entire party readable',async()=>{
 const d=await at('three-camera-director.js'),primary=[{x:20,y:20,w:1,h:1},{x:22,y:20,w:1,h:1},{x:20,y:22,w:1,h:1},{x:22,y:22,w:1,h:1}];
 const r=d.chooseDirectorPose({...pose,x:21.5,y:21.5,scale:18},{context:'Overview',primary,legal:[],secondary:[]},size,prefs,frame().board);
 assert.ok(r.pose.scale>40);for(const b of primary)assert.ok(d.directorSubjectSize(b,r.pose,size)>=d.directorReadability(size).minimum);
});
test('observed nearby activity changes yaw meaningfully without requiring hostile-Side inference',async()=>{
 const d=await at('three-camera-director.js'),f=frame();f.figures[2].position={x:40,y:40};
 for(const projectionMix of [0,1]){const director=d.createCameraDirector();director.stable(f,{unitId:'a',candidates:[]},'blue');
 const r=director.compose(f,{...pose,scale:25,projectionMix},size,prefs,'blue');assert.ok(r);assert.ok(Math.abs(r.pose.yaw)>=35,JSON.stringify(r));assert.ok(r.subjectPixels>=54);}
});
test('a public spatial Objective influences heading but its distance does not expand framing',async()=>{
 const d=await at('three-camera-director.js'),f=frame();f.units=f.units.slice(0,1);f.figures=f.figures.slice(0,1);
 const run=x=>{f.objectives=[{sideId:'blue',area:{x,y:4,w:1,h:1}}];const director=d.createCameraDirector();director.stable(f,{unitId:'a',candidates:[]},'blue');return director.compose(f,{...pose,scale:25},size,prefs,'blue');};
 const near=run(14),far=run(54);assert.ok(Math.abs(near.pose.yaw)>=35);assert.ok(Math.abs(far.pose.yaw)>=35);assert.ok(far.subjectPixels>=54);assert.ok(Math.abs(near.pose.scale-far.pose.scale)<2);
});
test('distant optional subjects do not force zoom-out and immediate targets get a bounded priority',async()=>{
 const d=await at('three-camera-director.js'),primary=[{x:20,y:20,w:1,h:1}],current={...pose,x:20,y:20,scale:20},shot={context:'Decision',primary,legal:[{x:21,y:20,w:1,h:1}],secondary:[]};
 const a=d.chooseDirectorPose(current,shot,size,prefs,frame().board),b=d.chooseDirectorPose(current,{...shot,secondary:Array.from({length:500},(_,i)=>({x:50+i,y:50,w:1,h:1}))},size,prefs,frame().board);
 assert.deepEqual(a.pose,b.pose);assert.ok(b.subjectPixels>=54);
});
test('nearby combat participants both meet the projected minimum in a closer coherent shot',async()=>{
 const d=await at('three-camera-director.js'),primary=[{x:20,y:20,w:1,h:1},{x:23,y:21,w:1,h:1}],shot={context:'Combat',primary,legal:[],secondary:[]};
 for(const projectionMix of [0,1]){const r=d.chooseDirectorPose({...pose,x:21,y:21,scale:18,projectionMix},shot,size,prefs,frame().board);for(const b of primary)assert.ok(d.directorSubjectSize(b,r.pose,size)>=54);assert.equal(r.clipped,false);assert.equal(d.chooseDirectorPose(r.pose,shot,size,prefs,frame().board),null);}
});
test('hidden Unit positions do not affect either heading hints or zoom; accepted idle overview stays still',async()=>{
 const d=await at('three-camera-director.js'),f=frame();f.explored=new Set(['4,4']);
 const run=()=>{const director=d.createCameraDirector();director.stable(f,{unitId:'a',candidates:[]},'blue');const r=director.compose(f,{...pose,scale:20},size,prefs,'blue');director.stable(f,null,'blue');assert.equal(director.compose(f,r.pose,size,prefs,'blue'),null);return r.pose;};
 const a=run();f.figures[1].position={x:3,y:4};f.figures[2].position={x:4,y:3};assert.deepEqual(run(),a);
});
