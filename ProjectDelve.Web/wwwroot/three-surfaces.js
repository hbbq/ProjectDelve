import * as THREE from './vendor/three/0.180.0/build/three.module.js';

// Printed tile finish, not geometry or rules. UVs belong to each observed Cell;
// the treatment never samples neighbouring terrain or changes the pick surface.
export function createTabletopSurfaces(){
  const enabled={value:0};
  const shadow=new THREE.ShadowMaterial({color:'#141b25',opacity:.58,depthWrite:false});
  shadow.name='observed-ground-shadows';
  return {
    shadow,
    setPresentation(value){enabled.value=value==='tabletop'?1:0;},
    attach(material){
      const previous=material.onBeforeCompile,cache=material.customProgramCacheKey();
      material.onBeforeCompile=function(shader,...args){
        previous.call(this,shader,...args);shader.uniforms.delveTileFinish=enabled;
        shader.vertexShader='varying vec2 vDelveTileUV;\n'+shader.vertexShader.replace('#include <uv_vertex>','#include <uv_vertex>\nvDelveTileUV = uv;');
        shader.fragmentShader='varying vec2 vDelveTileUV;\nuniform float delveTileFinish;\n'+shader.fragmentShader.replace('#include <color_fragment>',`#include <color_fragment>
          if (delveTileFinish > 0.5) {
            vec2 edge = min(vDelveTileUV, 1.0 - vDelveTileUV);
            float inset = min(edge.x, edge.y);
            float seam = smoothstep(0.006, 0.022, inset);
            float bevel = 1.0 - smoothstep(0.022, 0.048, inset);
            float facing = (vDelveTileUV.y - vDelveTileUV.x) * 0.28;
            diffuseColor.rgb *= mix(vec3(0.15,0.17,0.19), vec3(0.80,0.85,0.89), seam);
            diffuseColor.rgb *= 1.0 + bevel * facing;
          }`);
      };
      material.customProgramCacheKey=()=>cache+'|delve-tile-finish-v1';return material;
    },
    dispose(){shadow.dispose();}
  };
}
