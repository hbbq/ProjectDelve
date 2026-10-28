const assert=require('node:assert/strict');

// Only image payloads, packing offsets and preparation metadata may change.
module.exports=function assertTexturePreparation(source,runtime){
 function unpack(bytes){const n=bytes.readUInt32LE(12);return {json:JSON.parse(bytes.subarray(20,20+n)),binary:bytes.subarray(28+n)};}
 const a=unpack(source),b=unpack(runtime),images=new Set(a.json.images.map(i=>i.bufferView));
 assert.equal(b.json.asset.extras.runtimeTextureLimit,1024);
 assert.equal(a.json.bufferViews.length,b.json.bufferViews.length);
 for(let i=0;i<a.json.bufferViews.length;i++)if(!images.has(i)){
  const x=a.json.bufferViews[i],y=b.json.bufferViews[i];
  assert.deepEqual(a.binary.subarray(x.byteOffset??0,(x.byteOffset??0)+x.byteLength),b.binary.subarray(y.byteOffset??0,(y.byteOffset??0)+y.byteLength),`geometry buffer view ${i}`);
 }
 for(const j of [a.json,b.json]){
  delete j.extensionsUsed;delete j.extensionsRequired;
  if(j.asset.extras){delete j.asset.extras.preparation;delete j.asset.extras.sourceSha256;delete j.asset.extras.runtimeTextureLimit;if(!Object.keys(j.asset.extras).length)delete j.asset.extras;}
  j.buffers.forEach(v=>delete v.byteLength);
  j.bufferViews.forEach(v=>{delete v.byteOffset;delete v.byteLength;});
  j.images.forEach(v=>delete v.mimeType);
 }
 assert.deepEqual(b.json,a.json,'preserve hierarchy, accessors, geometry, UVs, materials, texture channels and all other semantics');
};
