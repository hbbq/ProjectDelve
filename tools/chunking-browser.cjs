// Local renderer benchmark. Uses a fresh Edge context, localhost:5081 only,
// detached generated observations, and route-only instrumentation (no app edits).
// Requires separate browser execution approval; not a browser-qa dispatcher plan.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const runtime=JSON.parse(fs.readFileSync('.codex/browser-qa.runtime.json','utf8'));
const {chromium}=require(runtime.playwright);
const [phase='after',output='.playwright-cli/chunking/after',baseline='.playwright-cli/chunking/baseline-three-viewport.js',fixtures='.playwright-cli/large-map-after']=process.argv.slice(2);
const origin='http://127.0.0.1:5081',root='ProjectDelve.Web/wwwroot';
fs.mkdirSync(output,{recursive:true});
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
function instrument(source){
  const marker='  return{host,lighting,models,facing,camera:cameraControl';
  assert.ok(source.includes(marker));
  return source.replace(marker,`
  const measuredRender=renderer.render.bind(renderer), measuredDraw=drawFrame;
  let measurements=[],builds=[];
  renderer.render=(...args)=>{const start=performance.now();measuredRender(...args);
    measurements.push({at:performance.now(),cpu:performance.now()-start,calls:renderer.info.render.calls,triangles:renderer.info.render.triangles});};
  drawFrame=time=>{const start=performance.now();measuredDraw(time);if(measurements.length)measurements.at(-1).frameCpu=performance.now()-start;};
  window.chunkProbe={camera:cameraControl,render(next){const start=performance.now();renderFrame(next);builds.push(performance.now()-start);},
    frame:()=>frame,invalidate,settle,setRepresentation,showInteraction,present,pick,project:cameraControl.screen,
    clear(){measurements=[];builds=[];},result:()=>({measurements,builds,memory:{...renderer.info.memory},metrics:JSON.parse(metrics.textContent),pose:cameraControl.current(),
      instances:[...nodes.values()].filter(n=>n.object.isInstancedMesh).length}),dispose,
    device:()=>{const gl=renderer.getContext(),ext=gl.getExtension('WEBGL_debug_renderer_info');return {renderer:ext?gl.getParameter(ext.UNMASKED_RENDERER_WEBGL):gl.getParameter(gl.RENDERER),dpr:devicePixelRatio};}};
`+marker);
}
function generated(size,preset='Default'){
  const state=read(path.join(fixtures,`120x120-${preset}.state.json`));
  const response=read(path.join(fixtures,`120x120-${preset}.response.json`));
  const b=state.physical.board;b.width=b.height=size;
  b.terrain=b.terrain.filter(t=>t.position.x<size&&t.position.y<size);
  b.edges=(b.edges??[]).filter(e=>[e.a,e.b].every(p=>p.x<size&&p.y<size));
  return {metadata:{sessionId:'chunk-benchmark',round:1},visualSeed:response.visualSeed,state,
    presentation:{cards:{},figures:{},machines:[],objectives:[],groundObjects:[],orders:[],viewingSideId:null}};
}
const pct=(a,p)=>[...a].sort((a,b)=>a-b)[Math.floor((a.length-1)*p)]??0;
function summarize(r){const m=r.measurements.slice(10),intervals=m.slice(1).map((v,i)=>v.at-m[i].at);return {
  calls:pct(m.map(v=>v.calls),.5),triangles:pct(m.map(v=>v.triangles),.5),
  cpuP50:pct(m.map(v=>v.cpu),.5),cpuP95:pct(m.map(v=>v.cpu),.95),
  frameCpuP50:pct(m.map(v=>v.frameCpu),.5),frameCpuP95:pct(m.map(v=>v.frameCpu),.95),
  intervalP50:pct(intervals,.5),intervalP95:pct(intervals,.95),fps:1000/(intervals.reduce((a,b)=>a+b,0)/intervals.length),memory:r.memory,instances:r.instances};}
async function run(){
 const browser=await chromium.launch({channel:'msedge',headless:false});const rows=[];
 try{
  const context=await browser.newContext({viewport:{width:1440,height:1000},deviceScaleFactor:1,reducedMotion:'no-preference'});
  await context.route('**/*',async route=>{const u=new URL(route.request().url());if(u.origin!==origin)return route.abort();
    if(u.pathname==='/api/game/updates')return route.fulfill({contentType:'text/event-stream',body:''});
    if(u.pathname==='/three-viewport.js'){
      let source=fs.readFileSync(phase==='baseline'?baseline:path.join(root,'three-viewport.js'),'utf8');
      // Compare a larger chunk without exposing a runtime quality setting.
      const chunk=/^chunk(16|24|32)$/.exec(phase)?.[1];
      if(chunk)source=source.replace(/Math.floor\(cell.x\/\d+\)},\$\{Math.floor\(cell.y\/\d+\)/,`Math.floor(cell.x/${chunk})},\${Math.floor(cell.y/${chunk})`);
      return route.fulfill({contentType:'application/javascript',body:instrument(source)});
    }
    return route.continue();});
  const page=await context.newPage(),errors=[],warnings=[],failed=[];
  page.on('pageerror',e=>errors.push(e.message));page.on('console',m=>{if(['warning','error'].includes(m.type()))warnings.push(m.text());});
  page.on('requestfailed',r=>{if(!r.url().endsWith('/updates'))failed.push(r.url());});
  await page.addInitScript(()=>{window.EventSource=undefined;localStorage.setItem('delve.board.view.v1','tabletop');});
  await page.goto(origin+'/#play');await page.waitForFunction(()=>window.chunkProbe&&document.querySelector('.three-metrics')?.textContent,{},{timeout:60000});
  // Test-owned observation: never assert on the mutable server scenario.
  const small={metadata:{sessionId:'small',round:1},visualSeed:'chunk-fixture',
    state:{round:1,currentUnitId:'hero',types:[{id:'barbarian-type',hp:5},{id:'grunt-type',hp:3},{id:'light-elemental-type',hp:1}],
      units:[{id:'hero',typeId:'barbarian-type',sideId:'blue',currentHp:5},{id:'enemy',typeId:'grunt-type',sideId:'red',currentHp:3}],
      physical:{board:{width:8,height:8,terrain:[{position:{x:3,y:2},kind:'Tree'}],edges:[]},figures:[{id:'hero',position:{x:2,y:2},posture:'Upright'},{id:'enemy',position:{x:5,y:4},posture:'Upright'}]}},
    presentation:{viewingSideId:null,cards:{hero:{displayName:'Fixture Barbarian'},enemy:{displayName:'Fixture Grunt'}},figures:{},machines:[],objectives:[]}};
  const large=generated(120),medium=generated(60),forest=generated(120,'Woodland');
  const populated=structuredClone(large);populated.state.physical.figures=structuredClone(small.state.physical.figures);populated.state.units=structuredClone(small.state.units);populated.state.types=small.state.types;populated.presentation=small.presentation;
  // A dedicated renderer fixture exercises surfaces, assembly props, doors and local lights.
  for(let y=0;y<10;y++)for(let x=0;x<14;x++)populated.state.physical.board.terrain.push({position:{x,y},kind:y===5?'Asphalt':'StoneFloor'});
  populated.state.physical.board.terrain.push({position:{x:5,y:3},kind:'Lava'},{position:{x:6,y:3},kind:'StoneFloorWithTable'});
  (populated.state.physical.board.terrainTypes??=[]).push({id:'fixture-camp',name:'Fixture Camp',presentation:{recipe:'generic'},assembly:{surface:{presentation:{recipe:'StoneFloor'}},components:[{slot:'fire',definition:{id:'fixture-fire',presentation:{recipe:'Campfire',layer:'Object'}}}]}});
  populated.state.physical.board.terrain.push({position:{x:8,y:3},typeId:'fixture-camp'});
  populated.state.units.push({id:'lamp',typeId:'light-elemental-type',sideId:'blue',currentHp:1});
  populated.state.physical.figures.push({id:'lamp',position:{x:9,y:4},posture:'Upright'});
  populated.state.physical.board.edges=[{a:{x:5,y:4},b:{x:6,y:4},kind:'Wall'},{a:{x:6,y:4},b:{x:7,y:4},kind:'ClosedDoor'}];
  const fog=structuredClone(large);fog.presentation.viewingSideId='blue';fog.state.exploredCells={blue:[]};
  for(let y=0;y<33;y++)for(let x=0;x<32;x++)if(x<16||y<16)fog.state.exploredCells.blue.push({x,y});
  fs.writeFileSync(path.join(output,'fixtures.json'),JSON.stringify({small,medium,large,forest,populated,fog}));
  for(const mobile of [false,true]){
   await page.setViewportSize(mobile?{width:390,height:844}:{width:1440,height:1000});
   for(const [name,frame,span,projection,rotation,center] of [
    ['small',small,0,'perspective',false],['medium',medium,16,'perspective',false],
    ['large-full',large,0,'perspective',false],['large-close',large,16,'perspective',false],
    ['large-rotate',large,16,'perspective',true],['forest',forest,16,'perspective',false],
    ['populated',populated,16,'perspective',false,{x:7,y:5}],['orthographic',large,16,'orthographic',false],
    ['fog',fog,20,'perspective',false,{x:16,y:16}]]){
     await page.evaluate(({frame,span,projection,center})=>{const p=chunkProbe;p.clear();p.render(frame);p.camera.setProjection(projection,false);p.camera.setMode('full');p.settle();}, {frame,span,projection,center});
     await page.waitForFunction(()=>JSON.parse(document.querySelector('.three-metrics').textContent).textureLoads===0);
     await page.evaluate(async({span,center})=>{const p=chunkProbe,c=p.camera,g=await import('/three-camera-geometry.js');if(span){const pose=c.current();c.manual({...pose,x:center?.x??30,y:center?.y??30,scale:g.threeSpanScale(c.size,span,pose)});}p.settle();},{span,center});
     await page.waitForTimeout(600);
     const build=await page.evaluate(()=>chunkProbe.result().builds);
     await page.evaluate(()=>chunkProbe.clear());
     await page.evaluate(async rotation=>{const p=chunkProbe,pose=p.camera.current();for(let i=0;i<100;i++){await new Promise(requestAnimationFrame);if(rotation)p.camera.manual({...pose,yaw:pose.yaw+i*.6,scale:pose.scale*(1+.15*Math.sin(i/20))});else p.invalidate();}await new Promise(requestAnimationFrame);},rotation);
     const result=await page.evaluate(()=>chunkProbe.result());
     const row={phase,name,mobile,build,summary:summarize(result),...result};rows.push(row);
     console.log(JSON.stringify({phase,name,mobile,build,summary:row.summary}));
     await page.screenshot({path:path.join(output,`${mobile?'mobile':'desktop'}-${name}.png`)});
     fs.writeFileSync(path.join(output,'measurements.json'),JSON.stringify(rows,null,2));
   }
  }
  const resources=[];
  for(let i=0;i<6;i++){
   await page.evaluate(({f,i})=>{f.metadata.sessionId='replace-'+i;chunkProbe.render(f);chunkProbe.settle();},{f:i%2?large:small,i});
   await page.waitForTimeout(250);resources.push(await page.evaluate(()=>chunkProbe.result().memory));
  }
  // Representation and interaction overlays use the real retained viewport.
  await page.evaluate(f=>{chunkProbe.render(f);chunkProbe.camera.setMode('full');chunkProbe.settle();chunkProbe.setRepresentation('miniatures');},small);
  await page.waitForTimeout(2500);
  await page.screenshot({path:path.join(output,'mobile-miniatures.png')});
  const pick=await page.evaluate(()=>{const p=chunkProbe,f=p.frame(),id=f.state.physical.figures[0].id,pos=f.state.physical.figures[0].position;p.showInteraction({focusedUnitId:id,preview:{placementCells:[{x:pos.x+1,y:pos.y}],affectedUnitIds:[id]}});return p.pick(p.project({x:pos.x+.5,y:pos.y+.5}));});
  await page.waitForTimeout(100);await page.screenshot({path:path.join(output,'mobile-selection.png')});
  assert.equal(pick.unitId,small.state.physical.figures[0].id);
  assert.deepEqual(resources.slice(2),[resources[0],resources[1],resources[0],resources[1]],'resource counts stabilize on repeated replacement');
  const final={resources,errors,warnings,failed,pick,device:await page.evaluate(()=>chunkProbe.device())};
  fs.writeFileSync(path.join(output,'checks.json'),JSON.stringify(final,null,2));console.log('CHECKS',JSON.stringify(final));
  assert.deepEqual(errors,[]);assert.deepEqual(failed,[]);
  await context.close();
 }finally{await browser.close();}
}
run().catch(e=>{console.error(e);process.exitCode=1;});
