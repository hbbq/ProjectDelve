const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),{pathToFileURL}=require('node:url');
const at=n=>import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot',n)).href);

test('all 32 canonical base radii are explicit, finite and within footprint coverage',async()=>{
  const {validateUnitVisualCatalog}=await at('unit-visuals.js');
  const disk=JSON.parse(fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot/art/models/unit-visuals.json')));
  const ids=fs.readFileSync(path.join(__dirname,'../ProjectDelve.Engine/UnitContent.cs'),'utf8').split('public static class UnitRoster')[0];
  const canonical=[...ids.matchAll(/const string \w+ = "([^"]+)"/g)].map(m=>({unitTypeId:m[1],cellSpan:m[1]==='red-dragon-type'?2:1}));
  assert.equal(canonical.length,32);const c=validateUnitVisualCatalog(disk,canonical);
  assert.equal(Object.keys(c.units).length,32);assert.equal(c.units['zombie-type'].baseRadius,.25);assert.equal(c.units['troll-type'].baseRadius,.44);
  for(const bad of [undefined,0,-.2,NaN,Infinity,.451]){const d=structuredClone(disk);d.units[0].baseRadius=bad;assert.throws(()=>validateUnitVisualCatalog(d,canonical));}
});

test('circle limits use the smaller dimension and contact warnings never resize authored bases',async()=>{
  const {miniatureBaseLayout:layout,maximumBaseRadius}=await at('unit-visuals.js');
  for(const [width,depth,cap]of [[1,1,.45],[2,2,.9],[2,1,.45],[1,2,.45]]){
    assert.equal(maximumBaseRadius(width,depth),cap);
    assert.equal(layout({baseRadius:10,width,depth}).radius,cap);
    const b=layout({baseRadius:.24,width,depth,contact:{radius:.18}});assert.equal(b.radius,.24);assert.ok(b.fitsContact);assert.ok(Math.abs(b.margin-.06)<1e-9);
  }
  const short=layout({baseRadius:.24,contact:{radius:.23}});assert.equal(short.radius,.24);assert.equal(short.fitsContact,false);assert.ok(Math.abs(short.shortfall-.03)<1e-9);assert.ok(short.warning);
  const impossible=layout({baseRadius:.44,contact:{radius:.45003}});assert.equal(impossible.radius,.44);assert.equal(impossible.fitsContact,false);assert.ok(Math.abs(impossible.shortfall-.05003)<1e-9);
  assert.throws(()=>maximumBaseRadius(0,1));
});

test('low transformed vertices measure radial contact about the facing pivot, excluding elevated equipment',async()=>{
  const T=await at('vendor/three/0.180.0/build/three.module.js'),{normalizeModel}=await at('three-models.js');
  const scene=new T.Group(),nested=new T.Group();nested.position.set(3,4,-2);scene.add(nested);
  const material=new T.MeshStandardMaterial(),feet=new T.Mesh(new T.BoxGeometry(.4,.1,.2),material);feet.position.set(.1,.05,0);nested.add(feet);
  const weapon=new T.Mesh(new T.BoxGeometry(1.2,.1,.2),material);weapon.position.set(0,.95,0);nested.add(weapon);
  const d={height:1,radius:.7,maxTriangles:12000,maxPrimitives:2,maxTextureDimension:1024};
  const a=normalizeModel({scene},d),b=normalizeModel({scene},{...d,baseRadius:.05});
  assert.ok(Math.abs(a.contact.radius-Math.hypot(.3,.1))<1e-6);assert.ok(a.contact.radius<a.visualBounds.radius);
  assert.ok(Math.abs(a.contact.minX+.1)<1e-6);assert.ok(Math.abs(a.contact.maxX-.3)<1e-6);assert.ok(Math.abs(a.contact.bandHeight-.075)<1e-8);
  assert.equal(a.scale,b.scale);assert.deepEqual(a.visualBounds,b.visualBounds);assert.deepEqual(a.contact,b.contact);
  const box=new T.Box3().setFromObject(a.group);assert.ok(Math.abs(box.min.y)<1e-8);assert.ok(Math.abs(box.max.y-1)<1e-8);
  feet.geometry.dispose();weapon.geometry.dispose();material.dispose();
});

test('beveled bases share geometry and retain their envelope, lit neutral top and constant Side rim',async()=>{
  const T=await at('vendor/three/0.180.0/build/three.module.js'),{createTabletopVisuals}=await at('three-tabletop.js');
  const geometries={},materials=new Map(),material=color=>{if(!materials.has(color))materials.set(color,new T.MeshBasicMaterial({color}));return materials.get(color);};
  const physicalMaterial=(color,vertexColors)=>{const key=`lit:${color}:${vertexColors}`;if(!materials.has(key))materials.set(key,new T.MeshLambertMaterial({color}));return materials.get(key);};
  const plane=(m,x,z,w,h,y,rotation,g)=>{const n=new T.Mesh(g,m);n.rotation.x=-Math.PI/2;n.scale.set(w,h,1);n.position.set(x,y,z);return n;};
  const physical=createTabletopVisuals({material,physicalMaterial,plane,geometries});
  for(const [width,depth,r]of [[1,1,.25],[2,2,.72],[2,1,.44]]){
    const a={sideId:'blue',cellSpan:width,footprintWidth:width,footprintDepth:depth,baseRadius:r},base=physical.miniatureBase(a),other=physical.miniatureBase(a);
    const box=new T.Box3().setFromObject(base);assert.ok(Math.abs(box.min.y)<1e-7);assert.ok(Math.abs(box.max.y-.061)<1e-7);
    assert.ok(Math.abs(box.max.x-r)<1e-6);assert.ok(box.max.x<=width*.45&&box.max.z<=depth*.45);
    assert.equal(base.children.length,3);
    for(let i=0;i<3;i++)assert.equal(base.children[i].geometry,other.children[i].geometry);
    assert.ok(base.children[0].material.isMeshLambertMaterial);assert.ok(base.children[1].material.isMeshLambertMaterial);
    assert.ok(base.children[2].material.isMeshBasicMaterial);assert.notEqual(base.children[1].material,base.children[2].material);
    assert.ok(Math.abs((1-base.children[2].geometry.parameters.innerRadius)*r-.025)<1e-8);
    const body=base.children[0].geometry.getAttribute('position'),tones=base.children[0].geometry.getAttribute('color');
    assert.equal(tones.count,body.count);
    assert.ok(Math.max(...tones.array)>Math.min(...tones.array),'Lighting Off retains molded shape shading');
    const shoulder=[];for(let i=0;i<body.count;i++)if(Math.abs(body.getY(i)-.04)<1e-7)shoulder.push(Math.hypot(body.getX(i),body.getZ(i)));
    assert.ok(shoulder.length>0&&shoulder.every(radius=>Math.abs(radius-r)<1e-7));
    const ring=base.children[2];ring.updateWorldMatrix(true,false);
    const points=ring.geometry.getAttribute('position');
    for(let i=0;i<points.count;i++){
      const point=new T.Vector3().fromBufferAttribute(points,i).applyMatrix4(ring.matrixWorld),radius=Math.hypot(point.x,point.z);
      assert.ok(Math.abs(point.y-(Math.abs(radius-r)<1e-7?.04:.061))<1e-7,'Side rim meets shoulder and top');
    }
  }
  // Cached radii are owned by the same viewport geometry registry and released once.
  let disposed=0;for(const g of Object.values(geometries))g.addEventListener('dispose',()=>disposed++);
  for(const g of Object.values(geometries))g.dispose();assert.equal(disposed,Object.keys(geometries).length);
  for(const m of materials.values())m.dispose();
});

test('production contact measurements use current compressed heights and preserve original GLBs',async()=>{
  const {inspect}=require('../tools/inspect-glb.cjs'),{miniatureBaseLayout}=await at('unit-visuals.js');
  const c=JSON.parse(fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot/art/models/unit-visuals.json')));
  for(const [name,expected]of [['zombie',.203519512],['troll',.450028692]]){
    const u=c.units.find(u=>u.typeId===name+'-type'),asset=c.models.find(m=>m.id===u.model);
    const info=await inspect(path.join(__dirname,'../ProjectDelve.Web/wwwroot',asset.url),u.targetHeight);
    assert.ok(Math.abs(info.normalization.contact.radius-expected)<1e-8);
    assert.ok(info.normalization.contact.samples>400);
    const b=miniatureBaseLayout({baseRadius:u.baseRadius,contact:info.normalization.contact});
    assert.equal(b.radius,u.baseRadius);assert.equal(b.fitsContact,name==='zombie');
    assert.ok(b.radius<=.45);assert.equal(info.normalization.targetHeight,u.targetHeight);
  }
});
