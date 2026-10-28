// Reproducible core-static GLB export, driven by reviewed per-asset provenance.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'..'),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
function prepare(bytes,sourceHash,{textureLimit=null,rotationYDegrees=0}={}){
  if(![0,90,-90,180].includes(rotationYDegrees))throw Error('Orientation rotation must be 0, 90, -90 or 180 degrees');
  if(hash(bytes)!==sourceHash)throw Error('Source hash differs from reviewed provenance');
  if(bytes.readUInt32LE(0)!==0x46546c67||bytes.readUInt32LE(4)!==2||bytes.readUInt32LE(8)!==bytes.length)throw Error('Invalid GLB');
  const length=bytes.readUInt32LE(12),json=JSON.parse(bytes.subarray(20,20+length));
  function visit(v){if(!v||typeof v!=='object')return;if(v.extensions&&Object.keys(v.extensions).length)throw Error('Review actual extension payloads before export');for(const c of Object.values(v))visit(c);}
  visit(json);delete json.extensionsUsed;delete json.extensionsRequired;
  // Reviewed rigid axis correction. Preserve authored nodes and every geometry
  // buffer; a scene wrapper rotates the entire model without deformation.
  if(rotationYDegrees){
    const angle=rotationYDegrees*Math.PI/180;
    for(const scene of json.scenes){const children=scene.nodes;scene.nodes=[json.nodes.length];json.nodes.push({name:'prepared-orientation',rotation:[0,Math.sin(angle/2),0,Math.cos(angle/2)],children});}
  }
  json.asset.extras={...json.asset.extras,preparation:'ProjectDelve core static GLB v1',sourceSha256:sourceHash};
  let j=Buffer.from(JSON.stringify(json));j=Buffer.concat([j,Buffer.alloc((4-j.length%4)%4,32)]);
  const rest=bytes.subarray(20+length),header=Buffer.alloc(20);header.writeUInt32LE(0x46546c67);header.writeUInt32LE(2,4);header.writeUInt32LE(20+j.length+rest.length,8);header.writeUInt32LE(j.length,12);header.writeUInt32LE(0x4e4f534a,16);
  const core=Buffer.concat([header,j,rest]);
  if(textureLimit==null)return core;
  if(![256,512,1024,2048].includes(textureLimit))throw Error('Texture limit must be 256, 512, 1024 or 2048');
  const resized=require('node:child_process').spawnSync(process.env.PROJECTDELVE_PYTHON??'python',[path.join(__dirname,'resize-model-textures.py'),String(textureLimit)],{input:core,maxBuffer:128*1024*1024});
  if(resized.error||resized.status!==0)throw Error('Offline texture resize requires Python/Pillow: '+(resized.error?.message??resized.stderr.toString()));
  return resized.stdout;
}
if(require.main===module){
  const manifest=process.argv.slice(2).find(a=>a!=='--check')??'assets/source/models/units/zombie/provenance.json',p=JSON.parse(fs.readFileSync(path.resolve(root,manifest)));
  const inside=name=>{const f=path.resolve(root,name);if(!f.startsWith(root+path.sep))throw Error('Asset path must stay within repository');return f;};
  const source=inside(p.original.path),output=inside(p.runtime.path);if(source===output)throw Error('Never overwrite original');
  if(!p.commerciallyCleared||!p.redistributionRightsConfirmed)throw Error('Runtime publishing requires reviewed rights');
  const bytes=fs.readFileSync(source),prepared=prepare(bytes,p.original.sha256,p.preparation);
  if(p.runtime.sha256&&hash(prepared)!==p.runtime.sha256)throw Error('Prepared hash differs from provenance');
  if(process.argv.includes('--check')){if(!fs.readFileSync(output).equals(prepared))throw Error('Runtime export is not reproducible');}
  else{fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,prepared);}
  if(hash(fs.readFileSync(source))!==p.original.sha256)throw Error('Source changed');
  console.log(JSON.stringify({bytes:prepared.length,sha256:hash(prepared),output:p.runtime.path}));
}
module.exports={prepare};
