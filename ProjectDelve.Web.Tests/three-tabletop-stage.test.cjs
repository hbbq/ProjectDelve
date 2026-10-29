const {test}=require('node:test'),assert=require('node:assert/strict'),path=require('node:path'),{pathToFileURL}=require('node:url');
const at=name=>import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot',name)).href);

test('rock meets wall height with textured exposed sides and no hidden-neighbour skirts',async()=>{
  const T=await at('vendor/three/0.180.0/build/three.module.js'),{createTabletopVisuals,tabletopDimensions}=await at('three-tabletop.js');
  const geometries={plane:new T.PlaneGeometry(1,1)},materials=[];
  const material=(color,image,transparent=false,vertexColors=false)=>{
    const m=new T.MeshBasicMaterial({color,transparent,vertexColors});m.userData.image=image;materials.push(m);return m;
  };
  const plane=(m,x,z,w,h,y)=>{const n=new T.Mesh(geometries.plane,m);n.rotation.x=-Math.PI/2;n.scale.set(w,h,1);n.position.set(x,y,z);return n;};
  const visuals=createTabletopVisuals({material,plane,geometries}),layer={base:{fill:'#645e66',image:'rock.webp'},rotation:0};
  const rock=visuals.terrain('SolidRock',layer,2,3,'test',()=> 'StoneFloor');
  assert.equal(rock.children.length,5);
  const bounds=new T.Box3().setFromObject(rock);
  assert.ok(Math.abs(bounds.max.y-tabletopDimensions.wallHeight)<1e-6);
  assert.ok(Math.abs(bounds.min.y)<1e-6);
  for(const side of rock.children.slice(1)){
    assert.equal(side.material.userData.image,rock.children[0].material.userData.image);
    assert.equal(side.material.vertexColors,true);
    const uv=side.geometry.getAttribute('uv'),position=side.geometry.getAttribute('position');
    assert.equal(uv.count,position.count);
    for(let i=0;i<uv.count;i++)assert.ok(Math.abs(uv.getY(i)-position.getY(i))<1e-6,'side texture keeps the top texture scale');
  }
  for(const kind of ['SolidRock','Unknown'])assert.equal(visuals.terrain('SolidRock',layer,2,3,'test',()=>kind).children.length,1);
  for(const m of materials)m.dispose();for(const g of Object.values(geometries))g.dispose();
});

test('tabletop stage stays below the play surface and reuses owned finishes across board sizes',async()=>{
  const T=await at('vendor/three/0.180.0/build/three.module.js'),{createTabletopVisuals}=await at('three-tabletop.js');
  const geometries={plane:new T.PlaneGeometry(1,1)},materials=new Map();
  const material=color=>{if(!materials.has(color))materials.set(color,new T.MeshBasicMaterial({color}));return materials.get(color);};
  const plane=(m,x,z,w,h,y,rotation=0,g=geometries.plane)=>{const n=new T.Mesh(g,m);n.rotation.set(-Math.PI/2,0,rotation);n.scale.set(w,h,1);n.position.set(x,y,z);return n;};
  const visuals=createTabletopVisuals({material,plane,geometries}),small=visuals.board(8,12),count=materials.size,large=visuals.board(50,20);
  assert.equal(materials.size,count,'restarting with different board dimensions does not allocate more finishes');
  assert.equal(small.getObjectByName('gaming-table').material,large.getObjectByName('gaming-table').material);
  for(const [group,width,height]of [[small,8,12],[large,50,20]]){
    assert.ok(new T.Box3().setFromObject(group).max.y<0,'no staging geometry covers or lifts the playable surface');
    const table=group.getObjectByName('gaming-table'),bounds=new T.Box3().setFromObject(table);
    assert.ok(bounds.min.x<-40&&bounds.max.x>width+40&&bounds.min.z<-40&&bounds.max.z>height+40);
    assert.equal(table.position.x,width/2);assert.equal(table.position.z,height/2);
    assert.ok(group.getObjectByName('frame-inlay'));
  }
  const table=large.getObjectByName('gaming-table');
  assert.deepEqual(table.material.userData.woodUniforms.tableSize.value.toArray(),[50,20]);
  let disposed=0;for(const m of materials.values())m.addEventListener('dispose',()=>disposed++);
  for(const m of materials.values())m.dispose();assert.equal(disposed,materials.size);
  for(const g of Object.values(geometries))g.dispose();
});

test('movement shadow copies retain caster flags and clip shadows to their explored columns',async()=>{
  const T=await at('vendor/three/0.180.0/build/three.module.js'),{createMovementClipPool}=await at('three-tabletop.js');
  const source=new T.Group(),geometry=new T.BoxGeometry(.4,.6,.4),material=new T.MeshLambertMaterial(),piece=new T.Mesh(geometry,material);
  piece.castShadow=true;piece.receiveShadow=true;source.add(piece);source.userData.visualBounds={radius:.4};
  const pool=createMovementClipPool(source,new Set(['2,2']));
  pool.update(2.5,2.5,1);
  assert.equal(pool.copies.filter(copy=>copy.body.visible).length,1);
  for(const copy of pool.copies){const mesh=copy.body.children[0];assert.equal(mesh.castShadow,true);assert.equal(mesh.receiveShadow,true);assert.equal(mesh.material.clipShadows,true);assert.equal(mesh.material.clippingPlanes.length,4);}
  assert.equal(material.clipShadows,false);assert.equal(material.clippingPlanes,null);
  pool.dispose();material.dispose();geometry.dispose();
});

