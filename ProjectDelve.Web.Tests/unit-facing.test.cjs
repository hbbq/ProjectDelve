const {test}=require('node:test'),assert=require('node:assert/strict'),path=require('node:path'),{pathToFileURL}=require('node:url');
const moduleAt=()=>import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot/unit-facing.js')).href);
const frame=()=>({metadata:{sessionId:'one'},visualSeed:'stable',state:{physical:{figures:[{id:'a',position:{x:1,y:1}},{id:'b',position:{x:3,y:1}}]}},presentation:{figures:{},machines:[{id:'press',position:{x:1,y:4}}]}});
test('initial sectors and offset are deterministic, survive rerenders and reproduce after restart without session ID',async()=>{
  const {createUnitFacing,initialUnitFacing}=await moduleAt(),f=frame(),a=createUnitFacing();a.setFrame(f);const first=a.get('a').yaw;
  assert.deepEqual(a.get('a'),initialUnitFacing('stable','a'));assert.ok(Math.abs(a.get('a').offset)<=Math.PI/30);
  a.observe({role:'Movement',unitId:'a',path:[{x:1,y:1},{x:2,y:1}]},{},f);const moved=a.get('a').yaw;
  assert.notEqual(moved,first);a.setFrame(f);assert.equal(a.get('a').yaw,moved);f.metadata.sessionId='restart';a.setFrame(f);assert.equal(a.get('a').yaw,first);
});
test('movement uses final nonzero eligible leg without joining disconnected segments',async()=>{
  const {createUnitFacing}=await moduleAt(),f=frame(),a=createUnitFacing();a.setFrame(f);
  a.observe({role:'Movement',unitId:'a',movementSegments:[[{x:1,y:1},{x:2,y:1},{x:2,y:2}],[{x:8,y:8}]]},{},f);
  assert.equal(a.get('a').yaw,a.get('a').offset);
  const old=a.get('a').yaw;for(const path of [[{x:3,y:3}],[{x:3,y:3},{x:3,y:3}]])a.observe({role:'Movement',unitId:'a',path},{},f);assert.equal(a.get('a').yaw,old);
});
test('hidden-only padding does not rotate; observable boundary leg does; hidden gaps supply no heading',async()=>{
  const {createUnitFacing,observedMovementLegs}=await moduleAt(),f=frame(),a=createUnitFacing();f.presentation.viewingSideId='blue';f.state.exploredCells={blue:[{x:2,y:1}]};a.setFrame(f);const first=a.get('a').yaw;
  const e={role:'Movement',unitId:'a',movementSegments:[[{x:8,y:8},{x:9,y:8}]]};a.observe(e,{},f);assert.equal(a.get('a').yaw,first);
  e.movementSegments=[[{x:1,y:1},{x:2,y:1},{x:3,y:1}],[{x:8,y:8},{x:8,y:9}]];
  assert.deepEqual(observedMovementLegs(e,f),[[Math.PI/2,Math.PI/2],[null]]);a.observe(e,{},f);assert.equal(a.get('a').yaw,Math.PI/2+a.get('a').offset);
});
test('AttackStarted prefers observed selected Cell then supplied target order, never turns defender',async()=>{
  const {createUnitFacing}=await moduleAt(),f=frame(),a=createUnitFacing();a.setFrame(f);const defender=a.get('b').yaw;
  a.observe({}, {kind:'AttackStarted',attackContext:{attackerId:'a',selectedCell:{x:1,y:3},targets:[{targetId:'b'}]}},f);assert.equal(a.get('a').yaw,0);assert.equal(a.get('b').yaw,defender);
  a.observe({}, {kind:'AttackStarted',attackContext:{attackerId:'a',targets:[{targetId:'hidden'},{targetId:'b'}]}},f);assert.equal(a.get('a').yaw,Math.PI/2);
  const before=a.inspect();a.observe({}, {kind:'AttackStarted',attackContext:{attackerId:'hidden',targets:[{targetId:'b'}]}},f);assert.deepEqual(a.inspect(),before);
});
test('hidden selected Cell and missing target information retain facing',async()=>{
  const {createUnitFacing}=await moduleAt(),f=frame(),a=createUnitFacing();f.presentation.viewingSideId='blue';f.state.exploredCells={blue:[{x:1,y:1}]};a.setFrame(f);const before=a.get('a').yaw;
  a.observe({}, {kind:'AttackStarted',attackContext:{attackerId:'a',selectedCell:{x:8,y:8},targets:[{targetId:'hidden'}]}},f);assert.equal(a.get('a').yaw,before);
});
test('observed Door, Cell, Unit and Machine interactions turn only their actor',async()=>{
  const {createUnitFacing}=await moduleAt(),f=frame(),a=createUnitFacing();a.setFrame(f);
  a.observe({role:'DoorAttempt',unitId:'a',door:{a:{x:1,y:1},b:{x:2,y:1}}},{},f);assert.equal(a.get('a').yaw,Math.PI/2);
  a.observe({}, {kind:'ActionUsed',unitId:'a',machineId:'press'},f);assert.equal(a.get('a').yaw,0);
  a.observe({}, {kind:'ActionUsed',unitId:'a',targetId:'b'},f);assert.equal(a.get('a').yaw,Math.PI/2);
  const before=a.get('a').yaw;a.observe({}, {kind:'ActionUsed',unitId:'a',abilityName:'Face north'},f);assert.equal(a.get('a').yaw,before);
});
test('shortest yaw takes the short path through either wrap boundary',async()=>{
  const {shortestYaw}=await moduleAt();assert.ok(Math.abs(shortestYaw(Math.PI-.01,-Math.PI+.01)-.02)<1e-10);assert.ok(Math.abs(shortestYaw(-Math.PI+.01,Math.PI-.01)+.02)<1e-10);assert.equal(shortestYaw(0,Math.PI*8),-9.797174393178826e-16);
});
