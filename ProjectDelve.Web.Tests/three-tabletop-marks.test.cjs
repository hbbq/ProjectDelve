const {test}=require('node:test'),assert=require('node:assert/strict'),path=require('node:path'),{pathToFileURL}=require('node:url');
const at=name=>import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot',name)).href);

test('dense legal Cell marks stay in three shared draw batches and duplicate choices add no geometry',async()=>{
  const {createTabletopMarks}=await at('three-tabletop-marks.js'),marks=createTabletopMarks();
  const cells=Array.from({length:400},(_,i)=>({kind:'move',x:i%20,y:Math.floor(i/20),w:.86,h:.86}));
  const a=marks.build(cells),b=marks.build([...cells,...cells]);
  assert.equal(a.children.length,3);assert.deepEqual(a.children.map(m=>m.count),b.children.map(m=>m.count));
  assert.ok(a.children.every((m,i)=>m.geometry===b.children[i].geometry&&m.material===b.children[i].material));
  for(const mesh of a.children){assert.equal(mesh.material.depthWrite,false);assert.equal(mesh.material.depthTest,false);assert.ok(mesh.instanceColor);assert.equal(mesh.castShadow,false);assert.equal(mesh.receiveShadow,false);assert.equal(mesh.material.map,null);}
  a.userData.disposeMarks();b.userData.disposeMarks();marks.dispose();
});

test('Objective zones use a perimeter without washing unknown contents and attacks differ by shape',async()=>{
  const {createTabletopMarks}=await at('three-tabletop-marks.js'),marks=createTabletopMarks();
  const area=marks.build([{kind:'objective',x:10,y:20,w:20,h:15}]),move=marks.build([{kind:'move',x:0,y:0}]),attack=marks.build([{kind:'attack',x:0,y:0}]);
  assert.equal(area.getObjectByName('mark-wash'),undefined);
  assert.ok(attack.getObjectByName('mark-ink').count>move.getObjectByName('mark-ink').count);
  const T=await at('vendor/three/0.180.0/build/three.module.js'),matrix=new T.Matrix4(),position=new T.Vector3();
  const ink=area.getObjectByName('mark-ink');
  for(let i=0;i<ink.count;i++){ink.getMatrixAt(i,matrix);position.setFromMatrixPosition(matrix);assert.ok(Math.min(Math.abs(position.x-10),Math.abs(position.x-30),Math.abs(position.z-20),Math.abs(position.z-35))<.05,'all Objective ink stays on supplied perimeter');}
  for(const group of [area,move,attack])group.userData.disposeMarks();marks.dispose();
});

test('combat connectors stop outside supplied bases and overlapping bases add no connector',async()=>{
  const T=await at('vendor/three/0.180.0/build/three.module.js'),{createTabletopMarks}=await at('three-tabletop-marks.js'),marks=createTabletopMarks();
  const group=marks.build([],[{from:{x:2,y:3,radius:.4},to:{x:5,y:3,radius:.6}}]),ink=group.getObjectByName('mark-ink');
  const matrix=new T.Matrix4(),position=new T.Vector3(),scale=new T.Vector3(),rotation=new T.Quaternion();
  ink.getMatrixAt(0,matrix);matrix.decompose(position,rotation,scale);
  assert.ok(position.x-scale.x/2>2.4);assert.ok(position.x+scale.x/2<4.4);assert.equal(position.z,3);assert.equal(ink.count,3,'shaft and directional chevron');
  const overlap=marks.build([],[{from:{x:2,y:3,radius:1},to:{x:3,y:3,radius:1}}]);assert.equal(overlap.children.length,0);
  group.userData.disposeMarks();overlap.userData.disposeMarks();marks.dispose();
});

test('individual mark replacement releases instance buffers while shared finishes live until viewport disposal',async()=>{
  const {createTabletopMarks}=await at('three-tabletop-marks.js'),marks=createTabletopMarks(),a=marks.build([{kind:'move',x:0,y:0}]),b=marks.build([{kind:'focus',x:2,y:2}]);
  let instances=0,finishes=0,geometry=0;
  for(const m of a.children){m.addEventListener('dispose',()=>instances++);m.material.addEventListener('dispose',()=>finishes++);}
  a.children[0].geometry.addEventListener('dispose',()=>geometry++);
  a.userData.disposeMarks();a.userData.disposeMarks();assert.equal(instances,3);assert.equal(finishes,0);assert.equal(geometry,0);
  b.userData.disposeMarks();marks.dispose();marks.dispose();assert.equal(finishes,3);assert.equal(geometry,1);
  assert.throws(()=>marks.build([]),/disposed/);
});
