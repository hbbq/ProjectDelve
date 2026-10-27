// Test-owned geometry; deliberately not a Zombie or production artwork.
const fs=require('node:fs'),path=require('node:path'),{pathToFileURL}=require('node:url');
async function fixture(textured=false){
  const T=await import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot/vendor/three/0.180.0/build/three.module.js')));
  const chunks=[],views=[],accessors=[];let offset=0;
  function attribute(values,type,components){
    const buffer=Buffer.from(values.buffer,values.byteOffset,values.byteLength),index=views.length;
    chunks.push(buffer);views.push({buffer:0,byteOffset:offset,byteLength:buffer.length});offset+=buffer.length;
    const accessor={bufferView:index,componentType:5126,count:values.length/components,type};
    if(type==='VEC3'){accessor.min=Array(3).fill(Infinity);accessor.max=Array(3).fill(-Infinity);for(let i=0;i<values.length;i++){
      accessor.min[i%3]=Math.min(accessor.min[i%3],values[i]);accessor.max[i%3]=Math.max(accessor.max[i%3],values[i]);}}
    accessors.push(accessor);return accessors.length-1;
  }
  function boxes(parts){
    const p=[],n=[],uv=[];for(const [x,y,z,w,h,d]of parts){const g=new T.BoxGeometry(w,h,d).toNonIndexed();g.translate(x,y,z);p.push(...g.attributes.position.array);n.push(...g.attributes.normal.array);uv.push(...g.attributes.uv.array);g.dispose();}
    return {POSITION:attribute(new Float32Array(p),'VEC3',3),NORMAL:attribute(new Float32Array(n),'VEC3',3),TEXCOORD_0:attribute(new Float32Array(uv),'VEC2',2)};
  }
  const body=boxes([[0,1.05,0,.42,.85,.28],[0,1.75,0,.45,.5,.36],[-.14,.3,0,.16,.6,.22],[.14,.3,0,.16,.6,.22],[-.31,1,0,.16,.7,.2],[.31,1,0,.16,.7,.2]]);
  const front=boxes([[0,1.76,.23,.22,.16,.16]]);
  const json={asset:{version:'2.0',generator:'ProjectDelve test fixture v1'},scene:0,scenes:[{nodes:[0]}],
    nodes:[{name:'matrix-parent',matrix:[1,0,0,0,0,1,0,0,0,0,1,0,2,3,4,1],children:[1]},
      {name:'nested-body',translation:[0,.2,0],children:[2,3]},{name:'body',mesh:0},{name:'orange-front',mesh:1}],
    meshes:[{primitives:[{attributes:body,material:0}]},{primitives:[{attributes:front,material:1}]}],
    materials:[{name:'painted-teal',pbrMetallicRoughness:{baseColorFactor:[.16,.53,.48,1],metallicFactor:0,roughnessFactor:.85}},
      {name:'orange-front',pbrMetallicRoughness:{baseColorFactor:[.95,.42,.08,1],metallicFactor:0,roughnessFactor:.85}}],
    buffers:[{byteLength:offset}],bufferViews:views,accessors};
  if(textured){
    // A tiny embedded checker tests browser PNG decode/color space and Off maps.
    const crc=buffer=>{let c=0xffffffff;for(const byte of buffer){c^=byte;for(let i=0;i<8;i++)c=(c>>>1)^((c&1)?0xedb88320:0);}return(c^0xffffffff)>>>0;};
    const chunk=(type,data)=>{const name=Buffer.from(type),out=Buffer.alloc(data.length+12);out.writeUInt32BE(data.length);name.copy(out,4);data.copy(out,8);out.writeUInt32BE(crc(Buffer.concat([name,data])),out.length-4);return out;};
    const ihdr=Buffer.alloc(13);ihdr.writeUInt32BE(8);ihdr.writeUInt32BE(8,4);ihdr[8]=8;ihdr[9]=6;
    const pixels=Buffer.alloc(8*33);for(let y=0;y<8;y++)for(let x=0;x<8;x++){const i=y*33+1+x*4,v=(Math.floor(x/2)+Math.floor(y/2))%2?170:255;pixels[i]=pixels[i+1]=pixels[i+2]=v;pixels[i+3]=255;}
    const png=Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',ihdr),chunk('IDAT',require('node:zlib').deflateSync(pixels,{level:0})),chunk('IEND',Buffer.alloc(0))]);
    const imageView=views.length;views.push({buffer:0,byteOffset:offset,byteLength:png.length});chunks.push(png,Buffer.alloc((4-png.length%4)%4));offset+=png.length+(4-png.length%4)%4;json.buffers[0].byteLength=offset;
    json.images=[{bufferView:imageView,mimeType:'image/png'}];json.textures=[{source:0}];json.materials[0].pbrMetallicRoughness.baseColorTexture={index:0};
  }
  let text=Buffer.from(JSON.stringify(json));text=Buffer.concat([text,Buffer.alloc((4-text.length%4)%4,32)]);
  const bin=Buffer.concat(chunks),header=Buffer.alloc(12),jh=Buffer.alloc(8),bh=Buffer.alloc(8);
  header.writeUInt32LE(0x46546c67);header.writeUInt32LE(2,4);header.writeUInt32LE(28+text.length+bin.length,8);
  jh.writeUInt32LE(text.length);jh.writeUInt32LE(0x4e4f534a,4);bh.writeUInt32LE(bin.length);bh.writeUInt32LE(0x004e4942,4);
  return Buffer.concat([header,jh,text,bh,bin]);
}
module.exports={fixture};
if(require.main===module)(async()=>{for(const textured of [false,true]){const bytes=await fixture(textured),file=path.resolve(__dirname,'../ProjectDelve.Web.Tests/fixtures/miniatures/'+(textured?'textured-figure':'static-figure')+'.glb');
  if(process.argv.includes('--check')){if(!fs.readFileSync(file).equals(bytes))throw Error('Miniature fixture is not reproducible');}
  else{fs.mkdirSync(path.dirname(file),{recursive:true});fs.writeFileSync(file,bytes);}console.log(`Test miniature (${textured?'textured':'static'}): ${bytes.length} bytes`);}})();
