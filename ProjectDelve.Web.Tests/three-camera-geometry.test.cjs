const {test}=require('node:test');
const assert=require('node:assert/strict');
const {pathToFileURL}=require('node:url');
const path=require('node:path');
const moduleAt=name=>import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot',name)).href);
test('perspective projection, raised inverse rays and distance compensation agree with real Three matrices',async()=>{
  const g=await moduleAt('three-camera-geometry.js'),T=await moduleAt('vendor/three/0.180.0/build/three.module.js');
  assert.equal(g.lensFov(-1),20);assert.equal(g.lensFov(100),80);assert.equal(g.lensFov('bad'),45);
  for(const size of [{width:900,height:600},{width:400,height:700}])for(const tilt of [0,38,55])for(const yaw of [0,45,175,270])for(const fov of [20,45,80])for(const projectionMix of [.001,.5,1]){
    const pose={x:7,y:9,scale:30,tilt,yaw,fov,projectionMix},b=g.orientationBasis(pose),lens=g.perspectiveLens(pose,size),camera=new T.PerspectiveCamera(lens.fov,size.width/size.height,.01,lens.distance*3);
    camera.position.set(pose.x+lens.distance*b.st*b.s,lens.distance*b.ct,pose.y+lens.distance*b.st*b.c);camera.up.set(-b.ct*b.s,b.st,-b.ct*b.c);camera.lookAt(pose.x,0,pose.y);camera.updateMatrixWorld(true);
    for(const point of [{x:1,y:2,height:0},{x:12,y:9,height:.6}]){
      const p=g.threeProject(point,pose,size),v=new T.Vector3(point.x,point.height,point.y).project(camera);
      assert.ok(Math.abs(p.x-(v.x+1)*size.width/2)<1e-6);assert.ok(Math.abs(p.y-(1-v.y)*size.height/2)<1e-6);
      const ground=g.threeUnproject(p,pose,size,point.height);assert.ok(ground&&Math.hypot(ground.x-point.x,ground.y-point.y)<1e-7);
    }
    const center=g.threeProject({x:pose.x,y:pose.y},pose,size);assert.equal(center.x,size.width/2);assert.equal(center.y,size.height/2);
    assert.ok(Math.abs(lens.distance*Math.tan(lens.fov*Math.PI/360)-size.height/(2*pose.scale))<1e-8);
  }
});
test('perspective Full and Auto fit actual depth under lens, aspect, yaw and tilt changes',async()=>{
  const g=await moduleAt('three-camera-geometry.js');
  for(const size of [{width:900,height:600},{width:320,height:700}])for(const tilt of [0,38,55])for(const yaw of [0,45,135,275])for(const fov of [20,45,80]){
    const orientation={tilt,yaw,fov,projectionMix:1},board={width:50,height:30},full=g.threeFull(board,size,orientation,true);
    for(const x of [-.12,50.12])for(const y of [-.12,30.12])for(const height of [0,.75]){
      const p=g.threeProject({x,y,height},full,size);assert.ok(!p.behind&&p.x>=27.99&&p.x<=size.width-27.99&&p.y>=27.99&&p.y<=size.height-27.99,JSON.stringify({p,full}));
    }
    for(const [x,y]of [[0,0],[24,15],[48,28]]){
      const active=[{x,y},{x:x+1,y},{x,y:y+1},{x:x+1,y:y+1}],moves=[[{x:Math.min(48,x+2),y}]];
      const fit=g.threeAutoFit({active,moves,board,viewport:size,preferences:{maxSpan:25,margin:2},orientation});
      for(const c of [...active,...(fit.overflow?[]:moves.flat())])for(const px of [c.x,c.x+1])for(const py of [c.y,c.y+1]){
        const p=g.threeProject({x:px,y:py},fit.pose,size);assert.ok(!p.behind&&p.x>=-.01&&p.x<=size.width+.01&&p.y>=-.01&&p.y<=size.height+.01,JSON.stringify({p,fit,size}));
      }
    }
  }
});
test('Fixed focus-plane coverage compensates lens and zoom independently, without horizon-driven magnification',async()=>{
  const g=await moduleAt('three-camera-geometry.js'),viewport={width:900,height:600};
  for(const tilt of [0,38])for(const yaw of [0,45,90])for(const fov of [20,45,80]){
    const orientation={tilt,yaw,fov,projectionMix:1},scale=g.threeSpanScale(viewport,15,orientation),q=g.groundQuadrilateral({...orientation,x:5,y:5,scale,projectionMix:0},viewport);
    const span=Math.max(Math.max(...q.map(p=>p.x))-Math.min(...q.map(p=>p.x)),Math.max(...q.map(p=>p.y))-Math.min(...q.map(p=>p.y)));
    assert.ok(Math.abs(span-15)<1e-8);
    assert.equal(scale,g.threeSpanScale(viewport,15,{...orientation,projectionMix:0}));
    assert.equal(g.threeSpanScale(viewport,7.5,orientation),scale*2);
  }
  let previous;for(let fov=60;fov<=80;fov+=.1){const scale=g.threeSpanScale(viewport,15,{tilt:55,yaw:45,fov,projectionMix:1});assert.ok(Number.isFinite(scale));if(previous)assert.ok(Math.abs(scale-previous)<4);previous=scale;}
  assert.equal(g.threeUnproject({x:0,y:0},{x:0,y:0,scale:30,tilt:55,yaw:0,fov:80,projectionMix:1},viewport),null);
});
test('angled orthographic math agrees with real Three matrices, inverse rays and full fits',async()=>{
  const g=await moduleAt('three-camera-geometry.js'),T=await moduleAt('vendor/three/0.180.0/build/three.module.js');
  for(const size of [{width:900,height:600},{width:400,height:700}])for(const tilt of [0,38,55])for(const yaw of [0,45,90,175,270]){
    const pose={x:7,y:9,scale:30,tilt,yaw},b=g.orientationBasis(pose),camera=new T.OrthographicCamera(-size.width/2,size.width/2,size.height/2,-size.height/2,.1,250);
    camera.position.set(pose.x+100*b.st*b.s,100*b.ct,pose.y+100*b.st*b.c);camera.up.set(-b.ct*b.s,b.st,-b.ct*b.c);camera.lookAt(pose.x,0,pose.y);camera.zoom=pose.scale;camera.updateProjectionMatrix();camera.updateMatrixWorld(true);
    for(const point of [{x:1,y:2,height:0},{x:12,y:9,height:.6}]){
      const p=g.threeProject(point,pose,size),v=new T.Vector3(point.x,point.height,point.y).project(camera);
      assert.ok(Math.abs(p.x-(v.x+1)*size.width/2)<1e-7);assert.ok(Math.abs(p.y-(1-v.y)*size.height/2)<1e-7);
      const ground=g.threeUnproject(p,pose,size,point.height);assert.ok(Math.hypot(ground.x-point.x,ground.y-point.y)<1e-8);
    }
    const full=g.threeFull({width:50,height:30},size,{tilt,yaw},true);
    for(const x of [-.12,50.12])for(const y of [-.12,30.12])for(const height of [0,.75]){
      const p=g.threeProject({x,y,height},full,size);assert.ok(p.x>=27.9&&p.x<=size.width-27.9);assert.ok(p.y>=27.9&&p.y<=size.height-27.9);
    }
    const scale=g.threeSpanScale(size,15,{tilt,yaw}),q=g.groundQuadrilateral({...pose,scale},size);
    assert.ok(Math.abs(Math.max(Math.max(...q.map(p=>p.x))-Math.min(...q.map(p=>p.x)),Math.max(...q.map(p=>p.y))-Math.min(...q.map(p=>p.y)))-15)<1e-8);
  }
});
test('shortest yaw interpolation is stable through overhead and angled Auto fits supplied footprints',async()=>{
  const g=await moduleAt('three-camera-geometry.js');assert.ok(g.interpolateOrientation({tilt:0,yaw:350},{tilt:38,yaw:10},.5).yaw>350);
  assert.equal(g.interpolateOrientation({tilt:38,yaw:10},{tilt:0,yaw:350},1).yaw,-10);
  const active=[{x:10,y:10},{x:11,y:10},{x:10,y:11},{x:11,y:11}],moves=[[{x:8,y:8},{x:9,y:8},{x:8,y:9},{x:9,y:9}],[{x:13,y:12},{x:14,y:13}]],size={width:900,height:600};
  for(const yaw of [0,40,90,200]){
    const fit=g.threeAutoFit({active,moves,board:{width:50,height:50},viewport:size,preferences:{minSpan:7,maxSpan:25,margin:2},orientation:{tilt:55,yaw}});
    assert.equal(fit.overflow,false);for(const cell of [...active,...moves.flat()])for(const x of [cell.x,cell.x+1])for(const y of [cell.y,cell.y+1]){
      const p=g.threeProject({x,y},fit.pose,size);assert.ok(p.x>=0&&p.x<=size.width&&p.y>=0&&p.y<=size.height,JSON.stringify({yaw,p,fit}));
    }
  }
});

test('angled Auto never loses an active corner footprint on long thin boards',async()=>{
  const g=await moduleAt('three-camera-geometry.js'),size={width:900,height:600};
  for(const board of [{width:14,height:5},{width:5,height:14},{width:50,height:50}])for(const yaw of [0,45,90,135,275])for(const x of [0,board.width-2])for(const y of [0,board.height-2]){
    const active=[{x,y},{x:x+1,y},{x,y:y+1},{x:x+1,y:y+1}];
    const fit=g.threeAutoFit({active,board,viewport:size,preferences:{minSpan:7,maxSpan:15,margin:2},orientation:{tilt:55,yaw}});
    for(const px of [x,x+2])for(const py of [y,y+2]){const p=g.threeProject({x:px,y:py},fit.pose,size);assert.ok(p.x>=-.01&&p.x<=size.width+.01&&p.y>=-.01&&p.y<=size.height+.01,JSON.stringify({board,yaw,p,fit}));}
  }
});
