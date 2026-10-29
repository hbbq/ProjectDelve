import * as THREE from './vendor/three/0.180.0/build/three.module.js';

// Presentation recipes, in board-cell units and linear RGB. No game queries.
export const localLightRecipes=Object.freeze({Lava:Object.freeze({color:[1,.30,.055],strength:3.4,radius:3.5})});

// Shared with the star's placement. One restrained source per observed pile,
// regardless of its count; known Items/Equipment do not emit light.
export const lootStarStyle=Object.freeze({x:.31,z:.75,color:[1,.57,.12],strength:1.35,radius:1.65});
export function lootLightSources(piles){
  return piles.filter(p=>p.unopened>0).map(p=>({...lootStarStyle,x:p.position.x+lootStarStyle.x,z:p.position.y+lootStarStyle.z}));
}

// A bounded irradiance field avoids per-source shader loops and shadow cubes.
// Unknown space is opaque. Use only the displayed checkpoint's observed data.
export function buildLocalLightField({board,kindAt},sources,edges=board.edges??[]){
  const cellCount=board.width*board.height;
  const edgeKey=(a,b)=>{const i=a.y*board.width+a.x,j=b.y*board.width+b.x;return Math.min(i,j)*cellCount+Math.max(i,j);};
  const width=Math.min(256,board.width*4),height=Math.min(256,board.height*4);
  const pixels=new Uint8Array(width*height*4),sum=new Float32Array(width*height*3);
  const walls=new Set(edges.filter(e=>e.kind!=='OpenDoor'&&kindAt(e.a.x,e.a.y)!=='Unknown'&&kindAt(e.b.x,e.b.y)!=='Unknown').map(e=>edgeKey(e.a,e.b)));
  const blocked=new Uint8Array(board.width*board.height);let blockers=walls.size;
  for(let y=0;y<board.height;y++)for(let x=0;x<board.width;x++)if(['Unknown','SolidRock'].includes(kindAt(x,y))){blocked[y*board.width+x]=1;blockers++;}
  function clear(s,x,z,occluded){
    if(!occluded)return true;
    const dx=x-s.x,dz=z-s.z,steps=Math.ceil(Math.max(Math.abs(dx),Math.abs(dz))*8);
    let a={x:Math.floor(s.x),y:Math.floor(s.z)};
    for(let i=1;i<=steps;i++){
      const b={x:Math.floor(s.x+dx*i/steps),y:Math.floor(s.z+dz*i/steps)};
      if(a.x===b.x&&a.y===b.y)continue;
      if(walls.size&&walls.has(edgeKey(a,b)))return false;
      // At corners conservatively check both crossings, preventing diagonal leaks.
      if(a.x!==b.x&&a.y!==b.y){const c={x:b.x,y:a.y},d={x:a.x,y:b.y};if(blocked[c.y*board.width+c.x]||blocked[d.y*board.width+d.x]||(walls.size&&[edgeKey(a,c),edgeKey(a,d),edgeKey(c,b),edgeKey(d,b)].some(k=>walls.has(k))))return false;}
      if(blocked[b.y*board.width+b.x])return false;a=b;
    }
    return true;
  }
  let accepted=0;
  for(const s of sources){
    if(kindAt(Math.floor(s.x),Math.floor(s.z))==='Unknown')continue;
    accepted++;const radius=Math.min(4,Math.max(0,s.radius));
    // Most sources do not touch an occluder, even on a partly explored board.
    let occluded=false;
    if(blockers){
      for(let y=Math.max(0,Math.floor(s.z-radius));y<Math.min(board.height,Math.ceil(s.z+radius));y++)
        for(let x=Math.max(0,Math.floor(s.x-radius));x<Math.min(board.width,Math.ceil(s.x+radius));x++)if(blocked[y*board.width+x])occluded=true;
      if(!occluded)occluded=edges.some(e=>e.kind!=='OpenDoor'&&Math.abs(e.a.x+.5-s.x)<=radius+1&&Math.abs(e.a.y+.5-s.z)<=radius+1);
    }
    for(let py=Math.max(0,Math.floor((s.z-radius)*height/board.height));py<Math.min(height,Math.ceil((s.z+radius)*height/board.height));py++)
      for(let px=Math.max(0,Math.floor((s.x-radius)*width/board.width));px<Math.min(width,Math.ceil((s.x+radius)*width/board.width));px++){
        const x=(px+.5)*board.width/width,z=(py+.5)*board.height/height,d=Math.hypot(x-s.x,z-s.z);
        if(d>=radius||!clear(s,x,z,occluded))continue;
        const power=s.strength*Math.pow(1-d/radius,2)/(1+d*d*.5),at=(py*width+px)*3;
        for(let c=0;c<3;c++)sum[at+c]+=s.color[c]*power;
      }
  }
  for(let i=0;i<width*height;i++){for(let c=0;c<3;c++)pixels[i*4+c]=Math.round(Math.min(4,sum[i*3+c])*255/4);pixels[i*4+3]=255;}
  return {width,height,pixels,sources:accepted};
}

export function terrainLightSources({board,kindAt}){
  const sources=[];
  for(let z=0;z<board.height;z++)for(let x=0;x<board.width;x++){const recipe=localLightRecipes[kindAt(x,z)];if(recipe)sources.push({x:x+.5,z:z+.5,...recipe});}
  return sources;
}

export function createLocalLighting(){
  const texture=new THREE.DataTexture(new Uint8Array(4),1,1);texture.magFilter=texture.minFilter=THREE.LinearFilter;texture.generateMipmaps=false;texture.needsUpdate=true;
  const uniforms={delveLightField:{value:texture},delveLightSize:{value:new THREE.Vector2(1,1)},delveLightEnabled:{value:0}};
  let signature='',stats={sources:0,buildMs:0,width:1,height:1};
  return {
    setEnabled(enabled){uniforms.delveLightEnabled.value=enabled?1:0;},
    update(data,sources=terrainLightSources(data)){
      const kinds=[];for(let y=0;y<data.board.height;y++)for(let x=0;x<data.board.width;x++)kinds.push(data.kindAt(x,y));
      const edges=(data.board.edges??[]).filter(e=>data.kindAt(e.a.x,e.a.y)!=='Unknown'||data.kindAt(e.b.x,e.b.y)!=='Unknown');
      const next=JSON.stringify([data.board.width,data.board.height,kinds,sources,edges]);if(next===signature)return;
      signature=next;const start=performance.now(),field=buildLocalLightField(data,sources,edges);
      // DataTexture dimensions can change on restart; release its old GPU allocation.
      if(texture.image.width!==field.width||texture.image.height!==field.height)texture.dispose();
      texture.image={data:field.pixels,width:field.width,height:field.height};texture.needsUpdate=true;
      uniforms.delveLightSize.value.set(data.board.width,data.board.height);
      stats={sources:field.sources,width:field.width,height:field.height,buildMs:+(performance.now()-start).toFixed(2)};
    },
    attach(material){
      const previous=material.onBeforeCompile,cache=material.customProgramCacheKey();
      material.onBeforeCompile=function(shader,...args){
        previous.call(this,shader,...args);Object.assign(shader.uniforms,uniforms);
        shader.vertexShader='varying vec3 vDelveLightPosition;\n'+shader.vertexShader.replace('#include <project_vertex>',`#include <project_vertex>
          vec4 delvePosition = vec4(transformed, 1.0);
          #ifdef USE_INSTANCING
            delvePosition = instanceMatrix * delvePosition;
          #endif
          vDelveLightPosition = (modelMatrix * delvePosition).xyz;`);
        shader.fragmentShader='varying vec3 vDelveLightPosition;\nuniform sampler2D delveLightField;\nuniform vec2 delveLightSize;\nuniform float delveLightEnabled;\n'+shader.fragmentShader.replace('#include <opaque_fragment>',`
          vec2 delveUV = vDelveLightPosition.xz / delveLightSize;
          if (delveLightEnabled > 0.5 && all(greaterThanEqual(delveUV, vec2(0.0))) && all(lessThanEqual(delveUV, vec2(1.0)))) {
            vec3 irradiance = texture2D(delveLightField, delveUV).rgb * 4.0;
            float heightFalloff = 1.0 / (1.0 + 1.4 * pow(max(0.0, vDelveLightPosition.y - 0.15), 2.0));
            outgoingLight += diffuseColor.rgb * irradiance * heightFalloff;
          }
          #include <opaque_fragment>`);
      };
      material.customProgramCacheKey=()=>cache+'|delve-local-v1';return material;
    },
    inspect:()=>({...stats,enabled:!!uniforms.delveLightEnabled.value,textureBytes:texture.image.data.byteLength}),
    dispose(){texture.dispose();}
  };
}
