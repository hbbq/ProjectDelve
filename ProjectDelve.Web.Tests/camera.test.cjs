const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const cameraContext = vm.createContext({});
for (const file of ["camera-geometry.js", "camera-observation.js"])
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", file), "utf8").replace(/^import .*\r?\n/gm, "").replace(/^export /gm, ""), cameraContext);
const run = code => vm.runInContext(code, cameraContext);
const plain = value => JSON.parse(JSON.stringify(value));
const near = (actual, expected) => assert.ok(Math.abs(actual - expected) < .00001, `${actual} != ${expected}`);
const geometry = (code, values) => { Object.assign(cameraContext, values); return plain(run(code)); };

test("Full fits landscape and portrait boards with square Cells", () => {
  for (const [board, viewport] of [[{width:50,height:50},{width:900,height:600}], [{width:50,height:20},{width:600,height:900}], [{width:3,height:5},{width:600,height:900}]]) {
    const pose = geometry("cameraFull(board, viewport)", {board,viewport});
    assert.ok(board.width * pose.scale <= viewport.width); assert.ok(board.height * pose.scale <= viewport.height);
    assert.ok(board.width * pose.scale === viewport.width || board.height * pose.scale === viewport.height);
    assert.equal(pose.x, board.width/2); assert.equal(pose.y, board.height/2);
  }
});
test("Fixed presets use the longer viewport axis, including portrait", () => {
  for (const span of [10,15,20,25]) for (const viewport of [{width:900,height:600},{width:600,height:900}]) {
    const scale = geometry("cameraSpanScale(viewport, span)", {viewport,span});
    near(Math.max(viewport.width,viewport.height)/scale, span);
    assert.ok(Math.min(viewport.width,viewport.height)/scale < span);
  }
});
test("clamping centers small board axes with gutters", () => {
  const pose = geometry("cameraClampPose({x:-20,y:100,scale:60},board,viewport)", {board:{width:50,height:3},viewport:{width:900,height:600}});
  assert.equal(pose.x,7.5); assert.equal(pose.y,1.5);
});
test("projection and matrix inversion agree at intermediate animated zooms", () => {
  assert.equal(run("cameraBaseCell"),60);
  assert.deepEqual(geometry("cameraProject({x:11,y:21},{x:10,y:20,scale:60},{width:900,height:600})",{}),{x:510,y:360});
  const viewport = {width:900,height:600}, point = {x:12.25,y:18.75};
  for (const scale of [12,33.5,60,113]) {
    const result = geometry("cameraUnproject(cameraProject(point,pose,viewport),cameraPoseFromMatrix(cameraMatrix(pose,viewport),viewport),viewport)", {viewport,point,pose:{x:15.7,y:21.2,scale}});
    near(result.x,point.x); near(result.y,point.y);
  }
});
const auto = overrides => geometry("cameraAutoFit(autoOptions)", {autoOptions:{active:[{x:20,y:20}],moves:[],friends:[],board:{width:50,height:50},viewport:{width:900,height:600},preferences:{},...overrides}});
test("Auto fits whole movement placements, including multi-Cell outer boundaries and irrelevant choices", () => {
  const result = auto({active:[{x:20,y:20},{x:21,y:20},{x:20,y:21},{x:21,y:21}], moves:[[{x:17,y:18},{x:18,y:18},{x:17,y:19},{x:18,y:19}], [{x:24,y:23},{x:25,y:23},{x:24,y:24},{x:25,y:24}]]});
  assert.deepEqual(result.core,{x:17,y:18,w:9,h:7}); assert.ok(result.span <=15);
  const p = geometry("cameraProject({x:26,y:25},pose,viewport)",{pose:result.pose,viewport:{width:900,height:600}});
  assert.ok(p.x<=900 && p.y<=600);
});
test("distant friends never sacrifice the active movement margin or exceed maximum span", () => {
  const base = auto({moves:[[{x:24,y:22}]]});
  const spread = auto({moves:[[{x:24,y:22}]],friends:[[{x:49,y:49}],[{x:0,y:0}]]});
  assert.deepEqual(spread,base);
});
test("oversized movement stays capped with active footprint visible", () => {
  const result = auto({moves:[[{x:0,y:0}],[{x:49,y:49}]]});
  assert.equal(result.span,15); assert.equal(result.overflow,true);
  const a = geometry("cameraProject({x:20,y:20},pose,viewport)",{pose:result.pose,viewport:{width:900,height:600}});
  const b = geometry("cameraProject({x:21,y:21},pose,viewport)",{pose:result.pose,viewport:{width:900,height:600}});
  assert.ok(a.x>=0 && a.y>=0 && b.x<=900 && b.y<=600);
});
test("no Move and new movement allowance use supplied geometry only", () => {
  const first = auto({preferences:{margin:0}}), expanded = auto({preferences:{margin:0},moves:[[{x:30,y:25}]]});
  assert.equal(first.span,7); assert.ok(expanded.span>first.span);
});
test("comfortable region avoids recentering; distant Attack consequence wins at unchanged scale", () => {
  const pose={x:25,y:25,scale:60},viewport={width:900,height:600},board={width:50,height:50};
  const unchanged=geometry("cameraEnsureVisible(pose,{x:24,y:24,w:2,h:2},board,viewport)",{pose,viewport,board});
  assert.deepEqual(unchanged,pose);
  const distant=geometry("cameraEnsureVisible(pose,{x:0,y:0,w:45,h:45},board,viewport,{x:40,y:40,w:1,h:1})",{pose,viewport,board});
  assert.equal(distant.scale,60); assert.ok(distant.x>35 && distant.y>35);
});
test("Attack framing retains both participants when they fit outside the comfortable inset", () => {
  const pose={x:20,y:20,scale:60},viewport={width:900,height:600},board={width:50,height:50};
  const next=geometry("cameraEnsureVisible(pose,{x:18,y:19,w:13,h:2},board,viewport,{x:30,y:19,w:1,h:1})",{pose,viewport,board});
  const edges=geometry("[cameraProject({x:18,y:19},pose,viewport),cameraProject({x:31,y:21},pose,viewport)]",{pose:next,viewport});
  assert.ok(edges[0].x>=0 && edges[1].x<=900);assert.equal(next.scale,pose.scale);
});
test("perimeter slots avoid corners, one another and screen controls on narrow viewports",()=>{
  const viewport={width:320,height:280},pose={x:20,y:20,scale:60};
  const first=geometry("cameraObjectiveIndicator({x:49,y:49,w:1,h:1},pose,viewport)",{pose,viewport});
  const next=geometry("cameraIndicatorSlot(location,[location],viewport,[{x:220,y:180,w:100,h:100}])",{location:first,viewport});
  assert.ok(Math.hypot(first.x-next.x,first.y-next.y)>=45);assert.ok(next.x<196 || next.y<156);
});
function frame(known=[0,1,4], span=1) {
  return {board:{width:50,height:5},side:"blue",explored:new Set(known.map(x=>`${x},0`)),
    units:[{id:"own",typeId:"hero",sideId:"blue"},{id:"other",sideId:"red"}],figures:[{id:"own",position:{x:0,y:0}},{id:"other",position:{x:4,y:0}}],
    geometry:{own:{occupiedCells:[{x:0,y:0}],cellSpan:1},other:{occupiedCells:[{x:4,y:0}],cellSpan:span}},objectives:[]};
}
test("hidden activity, summoning, World Effects and Orders produce no camera intent", () => {
  for(const kind of ["ActivationStarted","MovementCompleted","AttackStarted","UnitCreated","WorldCardDrawn","ActionUsed"]) {
    cameraContext.frame=frame(); cameraContext.occurrence={kind,unitId:"hidden",cell:{x:30,y:0},attackContext:{attackerId:"hidden",targets:[{targetId:"unknown"}]}};
    cameraContext.event={role:kind==="MovementCompleted"?"Movement":"Notice",unitId:"hidden",path:[{x:30,y:0},{x:31,y:0}]};
    assert.equal(run("cameraEventIntent(occurrence,event,frame)"),null,kind);
  }
});
test("movement excludes hidden animation padding and preserves disconnected visible runs", () => {
  const event={role:"Movement",unitId:"other",movementSegments:[[0,1,2,3,4,5].map(x=>({x,y:0}))],movingFigure:{cellSpan:1,sideId:"red"}};
  const result=geometry("cameraSafeMovement(event,frame)",{event,frame:frame()});
  assert.deepEqual(result.segments,[[{x:0,y:0},{x:1,y:0}],[{x:4,y:0}]]);
});
test("event-time display mask excludes later exploration and permits whole observable footprints", () => {
  const event={path:[{x:2,y:0},{x:3,y:0}],movingFigure:{cellSpan:2}};
  assert.deepEqual(geometry("cameraSafeMovement(event,frame).segments",{event,frame:frame([4],2)}),[[{x:3,y:0}]]);
  assert.deepEqual(geometry("cameraSafeMovement(event,frame).segments",{event,frame:frame([0],2)}),[]);
});
test("redacted Attack source cannot be recovered from positions or Orders", () => {
  const result=geometry("cameraEventIntent({kind:'AttackStarted',attackContext:{attackerId:'',targets:[{targetId:'own'}]}},{},frame)",{frame:frame()});
  assert.deepEqual(result.bounds,{x:0,y:0,w:1,h:1}); assert.deepEqual(result.sides,["blue"]);
});
test("World Effects without spatial consequences stay still; observed damage uses only its visible recipient", () => {
  const result=geometry("cameraEventIntent({kind:'WorldDamageResolved',unitId:'hidden'},{role:'Damage',targetId:'own'},frame)",{frame:frame()});
  assert.deepEqual(result.bounds,{x:0,y:0,w:1,h:1}); assert.deepEqual(result.sides,["blue"]);
  assert.equal(run("cameraEventIntent({kind:'WorldDamageResolved'},{role:'Damage',targetId:'hidden'},frame)"),null);
});
test("defeat uses only an observable last footprint", () => {
  for(const x of [0,30]) {
    const occurrence={kind:"UnitDefeated",unitId:"gone",defeatContext:{occupiedCells:[{x,y:0}],unit:{sideId:"red"}}};
    const result=geometry("cameraEventIntent(occurrence,{},frame)",{occurrence,frame:frame()});
    assert.equal(!!result,x===0);
  }
});
test("Follow Off and Full never follow; Own Side needs an observed own participant", () => {
  for(const follow of ["off","own","all"]) for(const mode of ["full","fixed","auto"]) for(const suspended of [false,true]) {
    const result=geometry("cameraAllowsFollow({follow,mode},suspended,{sides:['red']},'blue')",{follow,mode,suspended});
    assert.equal(result,!suspended && mode!=="full" && follow==="all");
  }
});
test("ownership follows Side plus Human agency, never Unit theme or active token", () => {
  assert.equal(geometry("cameraOwnHuman(frame,'own','blue',[{sideId:'blue',typeId:'hero',controller:'Human'}])",{frame:frame()}),true);
  assert.equal(run("cameraOwnHuman(frame,'own','blue',[{sideId:'blue',typeId:'hero',controller:'Automated'}])"),false);
});
test("Objective indicators cover partial, distant, oversized and fully visible Areas", () => {
  const pose={x:25,y:25,scale:60},viewport={width:900,height:600};
  assert.equal(geometry("cameraObjectiveIndicator({x:24,y:24,w:2,h:2},pose,viewport)",{pose,viewport}),null);
  for(const area of [{x:32,y:24,w:3,h:2},{x:0,y:0,w:2,h:2},{x:0,y:0,w:50,h:50},{x:24,y:0,w:2,h:2}]) {
    const indicator=geometry("cameraObjectiveIndicator(area,pose,viewport)",{area,pose,viewport});
    assert.ok(indicator.x>=25 && indicator.x<=875 && indicator.y>=25 && indicator.y<=575);
    assert.ok(indicator.x===25 || indicator.x===875 || indicator.y===25 || indicator.y===575);
  }
});
test("observed frame keeps only viewing Side Objectives and its mask", () => {
  const result=geometry("cameraObservedFrame(state,presentation).objectives",{state:{physical:{board:{},figures:[]},units:[],exploredCells:{blue:[]}},presentation:{viewingSideId:"blue",objectives:[{sideId:"blue",area:{x:0,y:0,w:1,h:1}},{sideId:"red",area:{x:30,y:0,w:1,h:1}}]}});
  assert.equal(result.length,1); assert.equal(result[0].sideId,"blue");
});
