import * as THREE from './vendor/three/0.180.0/build/three.module.js';
import { createLocalLighting } from './three-local-lighting.js';

export const lightingDefaults=Object.freeze({enabled:true,key:1.5,fill:1.2,azimuth:315,elevation:50});
const preferenceKey='delve.three.lighting.v1';

// A surface-only Ghost treatment: no extra geometry, lights, time uniforms or
// render loop. Keep the authored texture and native depth/FoW clipping chunks.
function ghostMaterial(material){
  material.transparent=true;material.depthWrite=false;material.forceSinglePass=true;
  material.color.multiply(new THREE.Color('#b8e9e5'));
  if(material.emissive){
    material.emissive.set('#8dc9c5');material.emissiveMap=material.map;material.emissiveIntensity=.55;
  }
  material.onBeforeCompile=shader=>{
    const varyings='varying vec3 vGhostNormal;\nvarying vec3 vGhostView;\n';
    shader.vertexShader=varyings+shader.vertexShader.replace('#include <project_vertex>',`#include <project_vertex>
      vGhostNormal = normalize(normalMatrix * normal);
      vGhostView = isOrthographic ? vec3(0.0, 0.0, 1.0) : -mvPosition.xyz;`);
    shader.fragmentShader=varyings+shader.fragmentShader.replace('#include <opaque_fragment>',`
      float ghostRim = pow(1.0 - clamp(abs(dot(normalize(vGhostNormal), normalize(vGhostView))), 0.0, 1.0), 2.0);
      diffuseColor.a *= mix(0.58, 0.82, ghostRim);
      outgoingLight += vec3(0.12, 0.30, 0.27) * (0.35 + ghostRim * 0.65);
      #include <opaque_fragment>`);
  };
  material.customProgramCacheKey=()=> 'delve-ghost-v1';
}
export function lightingPreferences(value={}){
  const number=(key,min,max)=>typeof value?.[key]==='number'&&Number.isFinite(value[key])?Math.max(min,Math.min(max,value[key])):lightingDefaults[key];
  return {enabled:typeof value?.enabled==='boolean'?value.enabled:lightingDefaults.enabled,key:number('key',0,2),fill:number('fill',0,2),azimuth:number('azimuth',0,360),elevation:number('elevation',15,85)};
}

// World-space lamp, independent of camera, observed content and gameplay state.
// Printed materials never enter this cache. Off retains the original vertex tones;
// Lambert omits them so directional illumination is not multiplied by fake shade.
export function createTabletopLighting({scene,root,invalidate,environment=globalThis}){
  let saved;try{saved=JSON.parse(environment.localStorage?.getItem(preferenceKey)??'null');}catch{/* optional */}
  let preferences=lightingPreferences(saved),physical=false,disposed=false,changedAt=null,firstFrameMs=null,boardSize=null;
  const now=()=>environment.performance?.now()??performance.now();
  const local=createLocalLighting();
  const pairs=new Map(),variants=new WeakMap(),subscriptions=new AbortController();
  const rig=new THREE.Group();rig.name='tabletop-lighting';
  // A warm studio key and cool sky / warm table bounce reveal sculpted forms.
  // Hemisphere fill adds no shadow pass and remains independent of observed content.
  const key=new THREE.DirectionalLight('#fff5e8',preferences.key),fill=new THREE.HemisphereLight('#e5edff','#766b59',preferences.fill);
  const rim=new THREE.DirectionalLight('#c6dcff',preferences.fill);rim.name='tabletop-rim';
  key.name='tabletop-key';fill.name='tabletop-fill';key.castShadow=true;
  key.shadow.mapSize.set(2048,2048);key.shadow.bias=-.00015;key.shadow.normalBias=.015;
  rig.add(key,key.target,fill,rim,rim.target);scene.add(rig);
  const panel=environment.document.createElement('details');panel.className='three-lighting';panel.id='three-lighting-controls';
  const summary=environment.document.createElement('summary');summary.textContent='Lighting';panel.append(summary);
  const controls={};
  for(const [id,label,values]of [
    ['enabled','Lighting',[['on','On'],['off','Off']]],
    ['key','Key',Array.from({length:41},(_,i)=>[i/20,(i/20).toFixed(2)])],
    ['fill','Fill',Array.from({length:41},(_,i)=>[i/20,(i/20).toFixed(2)])],
    ['azimuth','Direction',Array.from({length:25},(_,i)=>[i*15,`${i*15}°`])],
    ['elevation','Elevation',Array.from({length:15},(_,i)=>[15+i*5,`${15+i*5}°`])]
  ]){
    const wrapper=environment.document.createElement('label'),select=environment.document.createElement('select');
    wrapper.textContent=label+' ';select.id=`three-lighting-${id}`;select.setAttribute('aria-label',label==='Lighting'?label:`${label} light`);
    for(const [value,text]of values){const option=environment.document.createElement('option');option.value=String(value);option.textContent=String(text);select.append(option);}
    wrapper.append(select);panel.append(wrapper);controls[id]=select;
    select.addEventListener('change',()=>set({[id]:id==='enabled'?select.value==='on':Number(select.value)}),{signal:subscriptions.signal});
  }
  root.append(panel);
  function register(pair){for(const m of pair)variants.set(m,pair);return pair;}
  function resolve(m){return variants.get(m)?.[physical&&preferences.enabled?1:0]??m;}
  function material(color,vertexColors=true){
    const id=JSON.stringify([color,vertexColors]);let pair=pairs.get(id);
    if(!pair){pair=register([new THREE.MeshBasicMaterial({color,vertexColors}),local.attach(new THREE.MeshLambertMaterial({color}))]);pairs.set(id,pair);}
    return resolve(pair[0]);
  }
  // Model cache owns these pairs, including the imported source material.
  function modelMaterial(source,descriptor={}){
    const existing=variants.get(source);if(existing)return existing;
    const basic=new THREE.MeshBasicMaterial();
    for(const key of ['map','alphaMap','alphaTest','transparent','opacity','side','depthTest','depthWrite','vertexColors','wireframe','polygonOffset','polygonOffsetFactor','polygonOffsetUnits'])if(key in source)basic[key]=source[key];
    basic.color.copy(source.color);
    if(descriptor.appearance==='ghost'){ghostMaterial(basic);ghostMaterial(source);}
    local.attach(source);return register([basic,source]);
  }
  // Only the movement pool owns/disposes these copies. Weak registration does
  // not retain completed movement or introduce per-object stable clones.
  function clippedVariants(m,planes){
    const sources=variants.get(m)??[m];
    const copies=sources.map(source=>{const c=source.clone();
      // Three.Material.clone does not copy shader callbacks. Movement must
      // retain the same surface treatment while adding explored-cell clipping.
      c.onBeforeCompile=source.onBeforeCompile;c.customProgramCacheKey=source.customProgramCacheKey;
      c.clippingPlanes=planes;c.clipIntersection=false;c.clipShadows=true;c.userData={sourceMaterial:source};return c;});
    if(copies.length===2)register(copies);
    return copies;
  }
  function apply(){
    rig.visible=physical&&preferences.enabled;local.setEnabled(rig.visible);
    key.intensity=preferences.key;fill.intensity=preferences.fill;rim.intensity=preferences.fill*1.3;
    const a=preferences.azimuth*Math.PI/180,e=preferences.elevation*Math.PI/180;
    key.position.set(Math.sin(a)*Math.cos(e),Math.sin(e),-Math.cos(a)*Math.cos(e));
    rim.position.set(-Math.sin(a),.65,Math.cos(a));
    if(boardSize){
      // Fit public board bounds, never the hidden contents or the viewing camera.
      const {width,height}=boardSize,radius=Math.hypot(width,height)/2+4,distance=radius+10;
      key.target.position.set(width/2,0,height/2);key.position.multiplyScalar(distance).add(key.target.position);
      rim.target.position.copy(key.target.position);rim.position.multiplyScalar(distance).add(rim.target.position);
      Object.assign(key.shadow.camera,{left:-radius,right:radius,top:radius,bottom:-radius,near:.1,far:distance+radius+10});
      key.shadow.camera.updateProjectionMatrix();
    }
    scene.traverse(node=>{if(node.material)node.material=Array.isArray(node.material)?node.material.map(resolve):resolve(node.material);});
    panel.hidden=!physical;
    for(const [id,control]of Object.entries(controls)){control.value=String(id==='enabled'?(preferences.enabled?'on':'off'):preferences[id]);control.disabled=id!=='enabled'&&!preferences.enabled;}
  }
  function set(next){
    if(disposed)return;
    const updated=lightingPreferences({...preferences,...next});if(JSON.stringify(updated)===JSON.stringify(preferences))return;
    changedAt=now();preferences=updated;apply();try{environment.localStorage?.setItem(preferenceKey,JSON.stringify(preferences));}catch{/* optional */}invalidate();
  }
  apply();
  return {local,material,modelMaterial,clippedVariants,resolve,set,controls,identity:m=>variants.get(m)?.[0].id??m.id,
    setBoard(board){if(boardSize?.width===board.width&&boardSize?.height===board.height)return;boardSize={width:board.width,height:board.height};apply();},
    setPresentation(value){physical=value==='tabletop';changedAt=now();apply();},
    frameRendered(){if(changedAt!==null){firstFrameMs=+(now()-changedAt).toFixed(2);changedAt=null;}},
    inspect:()=>({...preferences,active:rig.visible,materials:pairs.size*2,local:local.inspect(),firstFrameMs}),
    dispose(){if(disposed)return;disposed=true;subscriptions.abort();local.dispose();rig.removeFromParent();key.dispose();fill.dispose();rim.dispose();for(const pair of pairs.values())for(const m of pair)m.dispose();pairs.clear();panel.remove();}
  };
}
