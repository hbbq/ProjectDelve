import * as THREE from './vendor/three/0.180.0/build/three.module.js';

export const lightingDefaults=Object.freeze({enabled:true,key:.85,fill:.65,azimuth:315,elevation:50});
const preferenceKey='delve.three.lighting.v1';
export function lightingPreferences(value={}){
  const number=(key,min,max)=>typeof value?.[key]==='number'&&Number.isFinite(value[key])?Math.max(min,Math.min(max,value[key])):lightingDefaults[key];
  return {enabled:typeof value?.enabled==='boolean'?value.enabled:lightingDefaults.enabled,key:number('key',0,2),fill:number('fill',0,2),azimuth:number('azimuth',0,360),elevation:number('elevation',15,85)};
}

// World-space lamp, independent of camera, observed content and gameplay state.
// Printed materials never enter this cache. Off retains the original vertex tones;
// Lambert omits them so directional illumination is not multiplied by fake shade.
export function createTabletopLighting({scene,root,invalidate,environment=globalThis}){
  let saved;try{saved=JSON.parse(environment.localStorage?.getItem(preferenceKey)??'null');}catch{/* optional */}
  let preferences=lightingPreferences(saved),physical=false,disposed=false,changedAt=null,firstFrameMs=null;
  const now=()=>environment.performance?.now()??performance.now();
  const pairs=new Map(),variants=new WeakMap(),subscriptions=new AbortController();
  const rig=new THREE.Group();rig.name='tabletop-lighting';
  const key=new THREE.DirectionalLight('#fff5e8',preferences.key),fill=new THREE.AmbientLight('#ffffff',preferences.fill);
  key.name='tabletop-key';fill.name='tabletop-fill';key.castShadow=false;
  rig.add(key,key.target,fill);scene.add(rig);
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
    if(!pair){pair=register([new THREE.MeshBasicMaterial({color,vertexColors}),new THREE.MeshLambertMaterial({color})]);pairs.set(id,pair);}
    return resolve(pair[0]);
  }
  // Only the movement pool owns/disposes these copies. Weak registration does
  // not retain completed movement or introduce per-object stable clones.
  function clippedVariants(m,planes){
    const sources=variants.get(m)??[m];
    const copies=sources.map(source=>{const c=source.clone();c.clippingPlanes=planes;c.clipIntersection=false;c.userData={sourceMaterial:source};return c;});
    if(copies.length===2)register(copies);
    return copies;
  }
  function apply(){
    rig.visible=physical&&preferences.enabled;
    key.intensity=preferences.key;fill.intensity=preferences.fill;
    const a=preferences.azimuth*Math.PI/180,e=preferences.elevation*Math.PI/180;
    key.position.set(Math.sin(a)*Math.cos(e),Math.sin(e),-Math.cos(a)*Math.cos(e));
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
  return {material,clippedVariants,resolve,set,controls,identity:m=>variants.get(m)?.[0].id??m.id,
    setPresentation(value){physical=value==='tabletop';changedAt=now();apply();},
    frameRendered(){if(changedAt!==null){firstFrameMs=+(now()-changedAt).toFixed(2);changedAt=null;}},
    inspect:()=>({...preferences,active:rig.visible,materials:pairs.size*2,firstFrameMs}),
    dispose(){if(disposed)return;disposed=true;subscriptions.abort();rig.removeFromParent();key.dispose();fill.dispose();for(const pair of pairs.values())for(const m of pair)m.dispose();pairs.clear();panel.remove();}
  };
}
