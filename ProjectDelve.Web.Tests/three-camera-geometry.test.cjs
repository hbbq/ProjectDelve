const {test}=require('node:test');
const assert=require('node:assert/strict');
const {pathToFileURL}=require('node:url');
const path=require('node:path');
const moduleAt=name=>import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot',name)).href);
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
