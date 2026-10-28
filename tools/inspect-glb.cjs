// Read-only inspection of uncompressed glTF 2.0 geometry and embedded images.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{pathToFileURL}=require('node:url');
async function inspect(file,targetHeight=1.2,preparedBytes=null){
  const bytes=preparedBytes??fs.readFileSync(file);
  if(bytes.length<20||bytes.readUInt32LE(0)!==0x46546c67||bytes.readUInt32LE(4)!==2||bytes.readUInt32LE(8)!==bytes.length)throw Error('Invalid GLB 2.0 header');
  let json,bin;const chunks=[];
  for(let offset=12;offset<bytes.length;){const length=bytes.readUInt32LE(offset),type=bytes.readUInt32LE(offset+4);if(length%4||offset+8+length>bytes.length)throw Error('Invalid GLB chunk');const data=bytes.subarray(offset+8,offset+8+length);chunks.push({type,length});if(type===0x4e4f534a)json=JSON.parse(data.toString('utf8'));if(type===0x004e4942)bin=data;offset+=8+length;}
  if(!json||!bin)throw Error('GLB requires JSON and binary chunks');
  const T=await import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot/vendor/three/0.180.0/build/three.module.js')));
  const info={5120:[1,'readInt8'],5121:[1,'readUInt8'],5122:[2,'readInt16LE'],5123:[2,'readUInt16LE'],5125:[4,'readUInt32LE'],5126:[4,'readFloatLE']};
  const lengths={SCALAR:1,VEC2:2,VEC3:3,VEC4:4,MAT4:16};
  function accessor(index){const a=json.accessors[index],v=json.bufferViews[a.bufferView],[size,read]=info[a.componentType]??[];if(!size||!lengths[a.type]||a.sparse)throw Error('Unsupported accessor for inspection');const stride=v.byteStride??size*lengths[a.type],offset=(v.byteOffset??0)+(a.byteOffset??0);
    const value=(i,k)=>{const n=bin[read](offset+i*stride+k*size);if(!a.normalized)return n;return a.componentType===5120?Math.max(-1,n/127):a.componentType===5122?Math.max(-1,n/32767):n/(2**(size*8)-1);};
    return {...a,values(i){return Array.from({length:lengths[a.type]},(_,k)=>value(i,k));}};}
  const bounds=new T.Box3(),points=[],hierarchy=[];let triangles=0,primitives=0,finite=true;
  function node(index,parent){const n=json.nodes[index],local=n.matrix?new T.Matrix4().fromArray(n.matrix):new T.Matrix4().compose(new T.Vector3(...(n.translation??[0,0,0])),new T.Quaternion(...(n.rotation??[0,0,0,1])),new T.Vector3(...(n.scale??[1,1,1]))),world=parent.clone().multiply(local);
    hierarchy.push({index,name:n.name??null,mesh:n.mesh??null,matrix:n.matrix,translation:n.translation,rotation:n.rotation,scale:n.scale,determinant:world.determinant(),children:n.children??[]});
    if(n.mesh!=null)for(const p of json.meshes[n.mesh].primitives){primitives++;const a=accessor(p.attributes.POSITION);triangles+=((p.indices!=null?json.accessors[p.indices].count:a.count)/3);for(let i=0;i<a.count;i++){const xyz=a.values(i),point=new T.Vector3(...xyz).applyMatrix4(world);finite&&=point.toArray().every(Number.isFinite);bounds.expandByPoint(point);points.push(point);}for(const key of ['NORMAL','TEXCOORD_0'])if(p.attributes[key]!=null){const attribute=accessor(p.attributes[key]);for(let i=0;i<attribute.count;i++)finite&&=attribute.values(i).every(Number.isFinite);}}
    for(const child of n.children??[])node(child,world);
  }
  for(const index of json.scenes[json.scene??0].nodes)node(index,new T.Matrix4());
  function image(i){const def=json.images[i];if(def.uri)return {index:i,external:def.uri};const v=json.bufferViews[def.bufferView],data=bin.subarray(v.byteOffset??0,(v.byteOffset??0)+v.byteLength);let width,height;
    if(data.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10]))){width=data.readUInt32BE(16);height=data.readUInt32BE(20);}
    else if(data[0]===255&&data[1]===216){let p=2;while(p<data.length){if(data[p++]!==255)continue;const marker=data[p++];if(marker===0xd9||marker===0xda)break;if(marker===0xd8||marker===0x01)continue;const length=data.readUInt16BE(p);if([0xc0,0xc1,0xc2,0xc3,0xc5,0xc6,0xc7,0xc9,0xca,0xcb,0xcd,0xce,0xcf].includes(marker)){height=data.readUInt16BE(p+3);width=data.readUInt16BE(p+5);break;}p+=length;}}
    return {index:i,mimeType:def.mimeType,bytes:data.length,width,height,sha256:crypto.createHash('sha256').update(data).digest('hex')};}
  const size=bounds.getSize(new T.Vector3()),center=bounds.getCenter(new T.Vector3()),scale=targetHeight/size.y,radius=points.reduce((r,p)=>Math.max(r,Math.hypot(p.x-center.x,p.z-center.z)*scale),0);
  const {estimateContactEnvelope}=await import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot/three-models.js')));
  const contact=Object.fromEntries(Object.entries(estimateContactEnvelope(points,bounds)).map(([k,v])=>[k,k==='samples'?v:v*scale]));
  return {file:path.relative(path.resolve(__dirname,'..'),path.resolve(file)).replaceAll('\\','/'),bytes:bytes.length,sha256:crypto.createHash('sha256').update(bytes).digest('hex'),asset:json.asset,chunks,
    scenes:json.scenes.length,meshes:json.meshes?.length??0,primitives,triangles,materials:json.materials??[],textures:json.textures?.length??0,images:(json.images??[]).map((_,i)=>image(i)),
    externalResources:[...(json.buffers??[]),...(json.images??[])].filter(v=>v.uri).map(v=>v.uri),hierarchy,animations:json.animations?.length??0,skins:json.skins?.length??0,cameras:json.cameras?.length??0,extensionsUsed:json.extensionsUsed??[],extensionsRequired:json.extensionsRequired??[],
    attributes:(json.meshes??[]).flatMap(m=>m.primitives.map(p=>Object.keys(p.attributes))),finite,bounds:{min:bounds.min.toArray(),max:bounds.max.toArray(),dimensions:size.toArray()},normalization:{targetHeight,scale,maximumRadius:radius,contact,dimensions:size.multiplyScalar(scale).toArray()},orientation:'Requires visual verification; axes are interpreted as glTF +Y up, +Z forward'};
}
module.exports={inspect};
if(require.main===module)inspect(process.argv[2]).then(v=>console.log(JSON.stringify(v,null,2))).catch(e=>{console.error(e.message);process.exitCode=1;});
