import * as THREE from './vendor/three/0.180.0/build/three.module.js';
import { maximumModelRadius } from './unit-visuals.js';

// Presentation defaults; approved assets and per-Type dimensions live in the catalog.
export const zombieModelContract = Object.freeze({version:'1',cellSpan:1,height:1.2,radius:.38,maxTriangles:12000,maxPrimitives:2,maxTextureDimension:1024});
export function validateModelDescriptor(value, base=import.meta.url) {
  if(!value || value.approved!==true)throw Error('Model is not approved');
  const d={...zombieModelContract,...value},url=new URL(d.url,base),origin=new URL(base);
  if(url.origin!==origin.origin||!['http:','https:','file:'].includes(url.protocol)||!url.pathname.endsWith('.glb')||url.search||url.hash)throw Error('Model must use a local GLB URL');
  if(![1,2].includes(d.cellSpan)||typeof d.version!=='string'||!d.version||
    ![d.height,d.radius,d.maxTriangles,d.maxPrimitives,d.maxTextureDimension].every(n=>Number.isFinite(n)&&n>0)||
    d.radius>maximumModelRadius||d.maxTriangles>12000||d.maxPrimitives>2||d.maxTextureDimension>8192)throw Error('Invalid model descriptor budgets');
  return Object.freeze({...d,url:url.href});
}
export function inspectGlb(bytes) {
  const view=new DataView(bytes);
  if(bytes.byteLength<28||view.getUint32(0,true)!==0x46546c67||view.getUint32(4,true)!==2||view.getUint32(8,true)!==bytes.byteLength||view.getUint32(16,true)!==0x4e4f534a)throw Error('Invalid GLB 2.0 container');
  const length=view.getUint32(12,true);if(length%4||20+length>bytes.byteLength)throw Error('Invalid GLB JSON chunk');
  const json=JSON.parse(new TextDecoder().decode(new Uint8Array(bytes,20,length)));
  if(json.asset?.version!=='2.0'||json.scenes?.length!==1||json.scene==null||!json.scenes[json.scene])throw Error('GLB needs one default scene');
  if(json.animations?.length||json.skins?.length||json.cameras?.length||json.extensionsUsed?.length||json.extensionsRequired?.length)throw Error('Only core static glTF materials/meshes are supported');
  if((json.buffers??[]).some(b=>b.uri!=null)||(json.images??[]).some(i=>i.uri!=null||i.bufferView==null||!['image/png','image/jpeg'].includes(i.mimeType)))throw Error('GLB resources must be embedded PNG/JPEG');
  if((json.meshes??[]).some(m=>m.weights?.length||m.primitives?.some(p=>p.targets?.length||(p.mode??4)!==4)))throw Error('Only static triangle meshes are supported');
  return json;
}
export function modelResources(gltf) {
  const geometries=new Set(),materials=new Set(),textures=new Set(),images=new Set();
  for(const scene of gltf.scenes??[gltf.scene])scene?.traverse(n=>{
    if(n.geometry)geometries.add(n.geometry);
    for(const m of (Array.isArray(n.material)?n.material:[n.material]).filter(Boolean)){
      materials.add(m);for(const t of Object.values(m))if(t?.isTexture){textures.add(t);if(t.source?.data)images.add(t.source.data);}
    }
  });
  return {geometries,materials,textures,images,dispose(){for(const g of geometries)g.dispose();for(const m of materials)m.dispose();for(const t of textures)t.dispose();for(const i of images)i.close?.();}};
}
function analyzeModel(gltf,d) {
  if(gltf.animations?.length||!gltf.scene)throw Error('Model must be a static scene');
  const scene=gltf.scene;scene.updateMatrixWorld(true);
  const bounds=new THREE.Box3(),points=[];let triangles=0,primitives=0;
  scene.traverse(n=>{
    if(n.isCamera||n.isLight||n.isSkinnedMesh||n.isInstancedMesh||n.morphTargetInfluences?.length||n.isLine||n.isPoints)throw Error('Unsupported model hierarchy');
    if(![...n.matrix.elements,...n.matrixWorld.elements].every(Number.isFinite)||n.matrixWorld.determinant()<=0)throw Error('Nonfinite, mirrored or degenerate model transform');
    if(!n.isMesh)return;
    const p=n.geometry?.getAttribute('position'),normal=n.geometry?.getAttribute('normal');
    if(!p?.count||p.count>65536||!normal||normal.count!==p.count||Object.values(n.geometry.morphAttributes).some(a=>a.length))throw Error('Model needs bounded static geometry and normals');
    for(const a of Object.values(n.geometry.attributes)){const values=a.isInterleavedBufferAttribute?a.data.array:a.array;for(let i=0;i<values.length;i++)if(!Number.isFinite(values[i]))throw Error('Nonfinite vertex attribute');}
    if(n.geometry.index)for(const i of n.geometry.index.array)if(i>=p.count)throw Error('Invalid vertex index');
    const count=n.geometry.index?.count??p.count;if(count%3)throw Error('Invalid triangle geometry');triangles+=count/3;
    primitives+=Array.isArray(n.material)?n.geometry.groups.length:1;
    for(let i=0;i<p.count;i++){const v=new THREE.Vector3().fromBufferAttribute(p,i).applyMatrix4(n.matrixWorld);if(!v.toArray().every(Number.isFinite))throw Error('Nonfinite geometry');bounds.expandByPoint(v);points.push(v);}
    for(const m of (Array.isArray(n.material)?n.material:[n.material])){
      if(!m?.isMeshStandardMaterial&&!m?.isMeshBasicMaterial)throw Error('Unsupported model material');
      if(!m.color.toArray().every(Number.isFinite)||!Number.isFinite(m.opacity))throw Error('Nonfinite model material');
      if(Object.values(m).some(t=>t?.isTexture)&&!n.geometry.getAttribute('uv'))throw Error('Textured model requires UVs');
      for(const t of Object.values(m))if(t?.isTexture&&Math.max(t.image?.width??0,t.image?.height??0)>d.maxTextureDimension)throw Error('Model texture exceeds descriptor budget');
    }
  });
  const size=bounds.getSize(new THREE.Vector3());
  if(!points.length||!size.toArray().every(Number.isFinite)||Math.min(size.x,size.y,size.z)<1e-6)throw Error('Empty or degenerate model bounds');
  if(triangles>d.maxTriangles||primitives>d.maxPrimitives||!primitives)throw Error('Model exceeds geometry budget');
  const center=bounds.getCenter(new THREE.Vector3());
  const sourceRadius=points.reduce((r,p)=>Math.max(r,Math.hypot(p.x-center.x,p.z-center.z)),0);
  return {group:scene,bounds,center,sourceHeight:size.y,sourceRadius,triangles,primitives,warnings:triangles>8000?['Above recommended 8000 triangles']:[]};
}
function normalizeSource(source,d) {
  const {bounds,center,triangles,primitives,warnings}=source,scale=d.height/source.sourceHeight,radius=source.sourceRadius*scale;
  if(radius>d.radius+1e-6)throw Error('Model has incompatible proportions');
  const normalized=new THREE.Group(),offset=new THREE.Group();normalized.name='normalized-model';
  normalized.scale.setScalar(scale);offset.position.set(-center.x,-bounds.min.y,-center.z);offset.add(source.group.clone(true));normalized.add(offset);
  return {group:normalized,visualBounds:{radius,height:d.height},infoAnchor:{x:0,y:.06+d.height+.08,z:0},triangles,primitives,scale,
    warnings};
}
export function normalizeModel(gltf,d) {return normalizeSource(analyzeModel(gltf,d),d);}

export function createModelCache({onChange=()=>{},fetchBytes=async(url,signal)=>{const r=await fetch(url,{signal});if(!r.ok)throw Error(`GLB HTTP ${r.status}`);return r.arrayBuffer();},
  load=async bytes=>{const {GLTFLoader}=await import('./vendor/three/0.180.0/examples/jsm/loaders/GLTFLoader.js');return new GLTFLoader().parseAsync(bytes,'');},registerMaterial=m=>[m],resolveMaterial=m=>m}={}) {
  const entries=new Map(),invalid=new Map(),abort=new AbortController();let disposed=false;
  function request(value){
    if(disposed)return null;
    let d;try{d=validateModelDescriptor(value);}catch(error){if(!invalid.has(value))invalid.set(value,{state:'failed',url:value?.url,error:String(error.message)});return null;}
    const key=JSON.stringify([d.url,d.version]);let e=entries.get(key);if(e){
      if(['maxTriangles','maxPrimitives','maxTextureDimension'].some(k=>e.descriptor[k]!==d[k])){
        invalid.set(value,{state:'failed',url:d.url,error:'Conflicting contract for cached model URL/version'});return null;
      }return e;
    }
    e={state:'loading',descriptor:d};entries.set(key,e);
    e.promise=(async()=>{
      let resources;const start=performance.now();
      try{
        const bytes=await fetchBytes(d.url,abort.signal);e.loadMs=performance.now()-start;
        if(disposed)return;inspectGlb(bytes);const decode=performance.now();const gltf=await load(bytes);
        resources=modelResources(gltf);
        // GLTFLoader may tolerate a failed image by returning a null texture.
        // Decorative approval requires all embedded textures to decode fully.
        const loadedTextures=await gltf.parser?.getDependencies('texture')??[];
        for(const t of loadedTextures)if(t?.isTexture){resources.textures.add(t);if(t.source?.data)resources.images.add(t.source.data);}
        if(disposed){resources.dispose();return;}
        if(loadedTextures.some(t=>!t?.image?.width||!t?.image?.height))throw Error('Model texture failed to decode');
        if(loadedTextures.some(t=>Math.max(t.image.width,t.image.height)>d.maxTextureDimension))throw Error('Model texture exceeds descriptor budget');
        e.template=analyzeModel(gltf,d);
        const pairs=new Map();for(const m of resources.materials){const pair=registerMaterial(m);pairs.set(m,pair);}
        for(const pair of pairs.values())for(const m of pair)resources.materials.add(m);
        gltf.scene.traverse(n=>{if(n.material)n.material=Array.isArray(n.material)?n.material.map(resolveMaterial):resolveMaterial(n.material);});
        e.resources=resources;e.decodeMs=performance.now()-decode;e.totalMs=performance.now()-start;e.state='ready';
      }catch(error){resources?.dispose();e.state='failed';e.error=String(error.message??error);e.totalMs=performance.now()-start;}
      if(!disposed)onChange(e);
    })();return e;
  }
  return {request,clone(e,value=e?.descriptor){if(disposed||e?.state!=='ready')return null;try{const d=validateModelDescriptor(value);if(d.url!==e.descriptor.url||d.version!==e.descriptor.version)throw Error('Mismatched source model');const result=normalizeSource(e.template,d);result.group.traverse(n=>{if(n.material)n.material=Array.isArray(n.material)?n.material.map(resolveMaterial):resolveMaterial(n.material);});return result;}catch(error){const key=JSON.stringify([value?.url,value?.version,value?.cellSpan,value?.height,value?.radius]);if(!invalid.has(key))invalid.set(key,{state:'failed',url:value?.url,error:String(error.message)});return null;}},
    inspect:()=>[...invalid.values(),...[...entries.values()].map(e=>({state:e.state,url:e.descriptor.url,version:e.descriptor.version,error:e.error,loadMs:e.loadMs,decodeMs:e.decodeMs,totalMs:e.totalMs,networkRequests:performance.getEntriesByName?.(e.descriptor.url).filter(e=>e.initiatorType==='fetch').length??0,triangles:e.template?.triangles,primitives:e.template?.primitives,warnings:e.template?.warnings,
      geometries:e.resources?.geometries.size??0,materials:e.resources?.materials.size??0,textures:e.resources?.textures.size??0,textureBytes:[...(e.resources?.textures??[])].reduce((n,t)=>n+(t.image?.width??0)*(t.image?.height??0)*4*4/3,0)}))],
    dispose(){if(disposed)return;disposed=true;abort.abort();for(const e of entries.values())e.resources?.dispose();entries.clear();invalid.clear();}};
}
