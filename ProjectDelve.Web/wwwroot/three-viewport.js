import * as THREE from './vendor/three/0.180.0/build/three.module.js';
import { boardVisuals, terrainLayers, railConnections, railGeometry } from './board-visuals.js';
import { sideHue } from './side-colors.js';
import { createThreeCamera } from './three-camera.js';
import { boardCandidates, boardCellKey, boardEdgeKey, tokenDetail } from './playtest-viewport.js';

const heights = Object.freeze({terrain:0,grid:.015,objects:.04,rails:.06,edges:.1,items:.15,movement:.3,fog:.6,units:.8,feedback:1.1,areas:1.2});
export const cellCenter = (cell, span=1) => ({x:cell.x+span/2,z:cell.y+span/2});
export function edgePlacement(edge) {
  return {x:(edge.a.x+edge.b.x)/2+.5,z:(edge.a.y+edge.b.y)/2+.5,vertical:edge.a.y===edge.b.y};
}
export function observedTerrain(frame) {
  const board=frame.state.physical.board, terrain=new Map((board.terrain??[]).map(t=>[boardCellKey(t.position),t]));
  const side=frame.presentation.viewingSideId;
  const explored=side?new Set((frame.state.exploredCells?.[side]??[]).map(boardCellKey)):null;
  return {board,terrain,explored,kindAt:(x,y)=>explored&&!explored.has(`${x},${y}`)?'Unknown':terrain.get(`${x},${y}`)?.kind??'StoneFloor'};
}

// Only browser observations enter this renderer. All game decisions stay in app.js.
export function createThreeViewport({ host, controls, animate, onPick, onHover, onPreview, onProject, onFailure,
  rendererFactory = parameters => new THREE.WebGLRenderer(parameters), textureLoader = new THREE.TextureLoader(), startedAt = performance.now() }) {
  const canvas=document.createElement('canvas');canvas.className='three-canvas';canvas.setAttribute('aria-hidden','true');host.prepend(canvas);
  let renderer;
  try {renderer=rendererFactory({canvas,antialias:true,alpha:false,powerPreference:'default'});}
  catch(error){canvas.remove();throw error;}
  renderer.outputColorSpace=THREE.SRGBColorSpace;renderer.toneMapping=THREE.NoToneMapping;renderer.shadowMap.enabled=false;
  renderer.setClearColor('#11161c');
  const scene=new THREE.Scene(), camera=new THREE.OrthographicCamera(-1,1,1,-1,.1,200);
  camera.up.set(0,0,-1);
  const groups=Object.fromEntries(Object.keys(heights).map(name=>{const group=new THREE.Group();group.name=name;scene.add(group);return[name,group];}));
  const labels=document.createElement('div');labels.className='three-labels';labels.setAttribute('aria-hidden','true');host.append(labels);
  const focus=document.createElement('button');focus.type='button';focus.className='three-focus';focus.hidden=true;host.append(focus);
  const metrics=document.createElement('output');metrics.className='three-metrics';metrics.setAttribute('aria-label','Three.js rendering diagnostics');
  const diagnostics=document.createElement('details');diagnostics.className='three-diagnostics';
  const summary=document.createElement('summary');summary.textContent='Three.js diagnostics';diagnostics.append(summary,metrics);
  const resetSample=document.createElement('button');resetSample.type='button';resetSample.textContent='Reset timing sample';resetSample.id='three-reset-sample';diagnostics.append(resetSample);
  (document.getElementById('tools-developer')??controls.status.parentElement).append(diagnostics);
  const abort=new AbortController(), nodes=new Map(), materials=new Map(), textures=new Map(), labelNodes=new Map(), glyphs=new Map();
  const geometries={plane:new THREE.PlaneGeometry(1,1),circle:new THREE.CircleGeometry(.42,48),ring:new THREE.RingGeometry(.425,.475,48)};
  const raycaster=new THREE.Raycaster(), ground=new THREE.Plane(new THREE.Vector3(0,1,0),0), intersection=new THREE.Vector3();
  let active=false,disposed=false,lost=false,raf=null,frame,view={},data,retained,session,epoch=0,pointerPosition,focusedCell={x:0,y:0};
  let motion,transient,transientId,drag=null,pinch=null,blockClick=false,space=false;
  const arrivals=new Map();
  const pointers=new Map(),samples=[],renderTimes=[],unitRenderTimes=[],unitIntervals=[];
  let lastUnitFrame=0;
  let renderCount=0,firstFrameAt=null,readyAt=null,lastHover='',lastRender=0;
  const started=startedAt;
  function invalidate(){if(active&&!lost&&!disposed&&raf===null)raf=requestAnimationFrame(draw);}
  const cameraControl=createThreeCamera({controls,invalidate,animate,onChange:()=>onProject?.()});
  function material(color,source,transparent=false) {
    const key=JSON.stringify([color,source,transparent]);if(materials.has(key))return materials.get(key);
    const m=new THREE.MeshBasicMaterial({color:color==='transparent'?'#19232c':color??'#666a68',transparent,opacity:source&&transparent?0:1,depthWrite:!transparent,side:THREE.FrontSide});
    materials.set(key,m);
    if(source){
      const url=new URL(source,new URL('./',import.meta.url)).href;
      let entry=textures.get(url);
      if(!entry){
        entry={state:'loading',texture:null,users:new Set()};textures.set(url,entry);
        textureLoader.load(url,texture=>{
          if(disposed){texture.dispose();return;}
          texture.colorSpace=THREE.SRGBColorSpace;texture.magFilter=THREE.LinearFilter;texture.minFilter=THREE.LinearMipmapLinearFilter;
          entry.texture=texture;entry.state='ready';
          for(const user of entry.users){user.map=texture;user.opacity=1;user.color.set('#ffffff');user.needsUpdate=true;for(const fallback of user.userData.fallbacks??[])fallback.visible=false;}
          invalidate();
        },undefined,()=>{entry.state='missing';invalidate();});
      }
      entry.users.add(m);
      if(entry.texture){m.map=entry.texture;m.opacity=1;m.color.set('#ffffff');m.needsUpdate=true;}
    }
    return m;
  }
  function artwork(group,fallback,m,x,z,w,h,height,rotation=0,geometry=geometries.plane){
    group.add(fallback);
    if(m){group.add(plane(m,x,z,w,h,height,rotation,geometry));if(m.map)fallback.visible=false;
      (m.userData.fallbacks??=new Set()).add(fallback);(group.userData.fallbacks??=[]).push({material:m,node:fallback});}
  }
  function glyph(value){
    if(glyphs.has(value))return glyphs.get(value);
    const bitmap=document.createElement('canvas');bitmap.width=bitmap.height=128;
    const ctx=bitmap.getContext('2d');ctx.clearRect(0,0,128,128);ctx.textAlign='center';ctx.textBaseline='middle';ctx.font='bold 70px system-ui';ctx.fillStyle='#fff2d4';ctx.strokeStyle='#11161c';ctx.lineWidth=5;ctx.strokeText(value,64,67);ctx.fillText(value,64,67);
    const texture=new THREE.CanvasTexture(bitmap);texture.colorSpace=THREE.SRGBColorSpace;
    const m=new THREE.MeshBasicMaterial({map:texture,transparent:true,depthWrite:false});glyphs.set(value,m);return m;
  }
  function plane(m,x,z,w=1,h=1,height=0,rotation=0,geometry=geometries.plane){
    const mesh=new THREE.Mesh(geometry,m);mesh.rotation.x=-Math.PI/2;mesh.position.set(x,height,z);mesh.scale.set(w,h,1);
    // A positive catalog/CSS quarter turn is clockwise with north at screen top.
    mesh.rotation.z=-rotation*Math.PI/180;return mesh;
  }
  function retain(key,signature,group,make){
    retained.add(key);let record=nodes.get(key);
    if(record?.signature===signature)return record.object;
    if(record)removeRecord(record);
    const object=make();groups[group].add(object);nodes.set(key,{signature,object});return object;
  }
  function outline(x,y,w,h,color,height=heights.feedback,thickness=.04){
    const group=new THREE.Group(),m=material(color);
    group.add(plane(m,x+w/2,y,w,thickness,height),plane(m,x+w/2,y+h,w,thickness,height),plane(m,x,y+h/2,thickness,h,height),plane(m,x+w,y+h/2,thickness,h,height));return group;
  }
  function brackets(x,y,w,h,color){
    const group=new THREE.Group(),m=material(color),length=Math.min(w,h)*.25;
    for(const dx of [0,w])for(const dy of [0,h]){group.add(plane(m,x+dx+(dx?-length/2:length/2),y+dy,length,.04,heights.feedback),plane(m,x+dx,y+dy+(dy?-length/2:length/2),.04,length,heights.feedback));}return group;
  }
  function token(appearance,position,height=heights.units){
    const group=new THREE.Group(),span=appearance.cellSpan??1,center=cellCenter(position,span),visual={...boardVisuals.unit.default,...boardVisuals.unit[appearance.typeId]};
    group.position.set(center.x,height,center.z);
    group.add(plane(material(`hsl(${sideHue(appearance.sideId)},70%,70%)`),0,0,span,span,0,0,geometries.ring));
    group.add(plane(material('#25323d'),0,0,span,span,.01,0,geometries.circle));
    const fallback=plane(glyph(visual.glyph??appearance.name.split(/\s+/).map(s=>s[0]).join('').slice(0,2)),0,0,span*.52,span*.52,.025);
    artwork(group,fallback,visual.image?material('#25323d',visual.image,true):null,0,0,span,span,.035,0,geometries.circle);
    if(appearance.posture==='Lying'){
      const badge=plane(glyph('Ⅱ'),0,0,span*.28,span*.28,.06);group.add(badge);
      // Broken ring is recognizable without relying on portrait tint or text.
      for(let i=0;i<8;i++){const angle=i*Math.PI/4;group.add(plane(material('#17202e'),Math.cos(angle)*span*.45,Math.sin(angle)*span*.45,.055*span,.055*span,.04));}
    }
    if(appearance.active)group.add(plane(glyph('▶'),-span*.36,-span*.28,span*.22,span*.22,.07));
    return group;
  }
  function appearance(figure){
    const unit=frame.state.units.find(u=>u.id===figure.id),type=frame.state.types.find(t=>t.id===unit?.typeId),card=frame.presentation.cards?.[figure.id];
    return{typeId:unit?.typeId,sideId:unit?.sideId,name:card?.individualName?.trim()||card?.displayName||type?.displayName||'Unit',posture:figure.posture,active:frame.state.currentUnitId===figure.id,cellSpan:frame.presentation.figures?.[figure.id]?.cellSpan??1,hp:unit?.currentHp,maxHp:type?.hp??1};
  }
  function renderFrame(next){
    if(disposed)return;const identity=next.metadata.sessionId??next.metadata.scenarioId;
    if(identity!==session){cancel();session=identity;epoch++;for(const record of nodes.values())removeRecord(record);nodes.clear();for(const label of labelNodes.values())label.remove();labelNodes.clear();}
    frame=next;lastHover='';data=observedTerrain(frame);retained=new Set();const{board,terrain,kindAt}=data;
    // Measured 50x50 rendering was draw-call-bound. Batch only repeated flat
    // terrain and fog by material; all dynamic objects retain ordinary meshes.
    const batches=new Map();
    const batchCell=(group,m,x,y,rotation=0)=>{const key=`${group}-batch:${m.id}`;let batch=batches.get(key);if(!batch){batch={group,material:m,cells:[]};batches.set(key,batch);}batch.cells.push([x,y,rotation]);};
    for(let y=0;y<board.height;y++)for(let x=0;x<board.width;x++){
      const kind=kindAt(x,y),tile=terrain.get(`${x},${y}`);
      if(kind==='Unknown'){
        batchCell('fog',material('#161b22'),x,y);continue;
      }
      const layer=terrainLayers(kind,{seed:frame.visualSeed,x,y,direction:tile?.direction,terrain});
      batchCell('terrain',material(layer.base.fill,layer.base.image),x,y,layer.rotation);
      if(layer.object){retain(`object:${x},${y}`,JSON.stringify(layer.object)+layer.objectRotation,'objects',()=>{
        const group=new THREE.Group(),fallback=plane(glyph(kind==='StoneFloorWithTable'?'▰':'♣'),x+.5,y+.5,.65,.65,heights.objects);
        artwork(group,fallback,layer.object.image?material('#254737',layer.object.image,true):null,x+.5,y+.5,1,1,heights.objects+.005,layer.objectRotation);return group;
      });}
      if(kind==='Rail'){
        const connections=railConnections(terrain,x,y,tile?.direction),rail=railGeometry(connections);
        retain(`rail:${x},${y}`,JSON.stringify([rail,tile?.direction]),'rails',()=>{
          const group=new THREE.Group(),fallback=new THREE.Group();
          for(const side of connections){const[dx,dy]=[[0,-1],[1,0],[0,1],[-1,0]][side];for(const offset of [-.09,.09])fallback.add(plane(material('#a79f8e'),x+.5+dx*.25+(dy?offset:0),y+.5+dy*.25+(dx?offset:0),dx?.5:.025,dy?.5:.025,heights.rails));}
          artwork(group,fallback,rail?material('#68605b',boardVisuals.terrain.Rail.tracks[rail.shape],true):null,x+.5,y+.5,1,1,heights.rails+.005,rail?.rotation??0);
          group.add(plane(glyph('↑'),x+.5,y+.5,.28,.32,heights.rails+.01,['North','East','South','West'].indexOf(tile?.direction)*90));return group;
        });
      }
    }
    for(const[key,batch]of batches)retain(key,JSON.stringify(batch.cells),batch.group,()=>{
      const mesh=new THREE.InstancedMesh(geometries.plane,batch.material,batch.cells.length),transform=new THREE.Object3D();
      batch.cells.forEach(([x,y,rotation],index)=>{transform.position.set(x+.5,heights[batch.group],y+.5);transform.rotation.set(-Math.PI/2,0,-rotation*Math.PI/180);transform.updateMatrix();mesh.setMatrixAt(index,transform.matrix);});
      mesh.instanceMatrix.needsUpdate=true;mesh.computeBoundingSphere();return mesh;
    });
    retain('grid',`${board.width}:${board.height}`,'grid',()=>{
      const positions=[];for(let x=0;x<=board.width;x++)positions.push(x,heights.grid,0,x,heights.grid,board.height);for(let y=0;y<=board.height;y++)positions.push(0,heights.grid,y,board.width,heights.grid,y);
      const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));
      const line=new THREE.LineSegments(geometry,new THREE.LineBasicMaterial({color:'#202c32',transparent:true,opacity:.6}));line.userData.owned=true;return line;
    });
    for(const edge of board.edges??[]){if(edge.kind==='None')continue;const at=edgePlacement(edge),visual={...boardVisuals.edge.default,...boardVisuals.edge[edge.kind]};
      retain(`edge:${boardEdgeKey(edge)}`,edge.kind,'edges',()=>{
        const group=new THREE.Group();
        if(edge.kind==='OpenDoor'){
          for(const offset of [-.4,.4])group.add(plane(material('#6e6049'),at.x+(at.vertical?0:offset),at.z+(at.vertical?offset:0),at.vertical?.12:.2,at.vertical?.2:.12,heights.edges));
        }else group.add(plane(material(visual.fill??'#45494b'),at.x,at.z,1,.13,heights.edges,at.vertical?90:0));
        if(visual.image)group.add(plane(material('#45494b',visual.image,true),at.x,at.z,1,80/512,heights.edges+.005,at.vertical?90:0));return group;
      });
    }
    for(const machine of frame.presentation.machines??[]){const visual={...boardVisuals.machine.default,...boardVisuals.machine[machine.typeId]};
      retain(`machine:${machine.id}`,JSON.stringify(machine),'items',()=>{
        const group=new THREE.Group();const layer=terrainLayers(visual.baseTerrain??'StoneFloor',{seed:frame.visualSeed,x:machine.position.x,y:machine.position.y});
        group.add(plane(material(layer.base.fill,layer.base.image),machine.position.x+.5,machine.position.y+.5,1,1,heights.objects,layer.rotation));
        const fallback=plane(glyph(visual.glyph??'⚙'),machine.position.x+.5,machine.position.y+.5,.7,.7,heights.items);
        artwork(group,fallback,visual.image?material(visual.fill,visual.image,true):null,machine.position.x+.5,machine.position.y+.5,1,1,heights.items+.01);return group;
      });
    }
    for(const [category,objects] of [['resource',frame.state.resources??[]],['cellMarker',frame.state.cellMarkers??[]]])for(const object of objects){const visual={...boardVisuals[category].default,...boardVisuals[category][object.type.id]},p=object.position;
      retain(`${category}:${boardCellKey(p)}`,JSON.stringify(object),'items',()=>{
        const group=new THREE.Group(),cx=p.x+(category==='resource'?.81:.23),cy=p.y+.2;
        const fallback=plane(glyph(visual.glyph??'?'),cx,cy,.3,.3,heights.items);artwork(group,fallback,visual.image?material(visual.fill,visual.image,true):null,cx,cy,.36,.36,heights.items+.01);return group;
      });
    }
    const labelIds=new Set();
    for(const figure of frame.state.physical.figures){const a=appearance(figure);
      retain(`unit:${figure.id}`,JSON.stringify([a,figure.position]),'units',()=>token(a,figure.position));
      labelIds.add(figure.id);let label=labelNodes.get(figure.id);if(!label){label=document.createElement('div');label.className='three-token-info';label.innerHTML='<span class="three-hp"></span><span class="three-name"></span><span class="three-state"></span>';labels.append(label);labelNodes.set(figure.id,label);}
      label.appearance=a;label.position=figure.position;label.title=`${a.name} · Side ${a.sideId} · ${a.posture}${a.maxHp>1?` · HP ${a.hp}/${a.maxHp}`:''}`;
      label.children[0].textContent=a.maxHp>1?`${a.hp}/${a.maxHp}`:'';label.children[1].textContent=a.name;
    }
    for(const[id,label]of labelNodes)if(!labelIds.has(id)){label.remove();labelNodes.delete(id);}
    for(const[key,record]of nodes)if(!retained.has(key)){removeRecord(record);nodes.delete(key);}
    cameraControl.setFrame(frame.state,frame.presentation,frame.metadata);if(frame.state.currentUnitId)focusedCell={...frame.state.physical.figures.find(f=>f.id===frame.state.currentUnitId)?.position??focusedCell};
    showInteraction(view);invalidate();
  }
  function removeRecord(record){record.object.removeFromParent();record.object.traverse(object=>{for(const fallback of object.userData.fallbacks??[])fallback.material.userData.fallbacks.delete(fallback.node);});if(record.object.isInstancedMesh)record.object.dispose();if(record.object.userData.owned){record.object.geometry.dispose();record.object.material.dispose();}}
  function showInteraction(next){
    view=next??{};if(!frame)return;for(const key of [...nodes.keys()])if(key.startsWith('feedback:')||key.startsWith('area:')){removeRecord(nodes.get(key));nodes.delete(key);}
    retained=new Set(nodes.keys());const figures=frame.state.physical.figures;
    const add=(key,object)=>retain(`feedback:${key}`,'feedback','feedback',()=>object);
    for(const candidate of view.candidates??[]){const ref=candidate.interaction,kind=candidate.boardVisual?.kind,color=kind==='attack'?'#ff7373':kind==='move'?'#72caff':kind==='activate'?'#ffe06a':'#ffc786';
      let positions=[];
      if(ref.kind==='Position')positions=[ref.position];
      else if(ref.kind==='Unit'){const f=figures.find(f=>f.id===ref.unitId);if(f)positions=frame.presentation.figures?.[f.id]?.occupiedCells??[f.position];}
      else if(ref.kind==='Door'){const p=edgePlacement(ref.door);add(`door:${candidate.key}`,outline(p.x-(p.vertical?.12:.5),p.z-(p.vertical?.5:.12),p.vertical?.24:1,p.vertical?1:.24,color));}
      for(const p of positions){add(`${candidate.key}:${boardCellKey(p)}`,['activate','attack'].includes(kind)?brackets(p.x+.07,p.y+.07,.86,.86,color):outline(p.x+.07,p.y+.07,.86,.86,color,heights.feedback,.035));
        if(candidate.boardVisual?.icon)add(`icon:${candidate.key}:${boardCellKey(p)}`,plane(glyph(candidate.boardVisual.icon),p.x+.75,p.y+.76,.27,.27,heights.feedback+.02));}
    }
    for(const p of view.preview?.placementCells??[])add(`placement:${boardCellKey(p)}`,outline(p.x+.03,p.y+.03,.94,.94,'#ffe084',heights.feedback+.03));
    for(const id of new Set([...(view.preview?.affectedUnitIds??[]),view.focusedUnitId,...(view.relationship?.targets??[]).map(t=>t.targetId),view.relationship?.attackerId].filter(Boolean))){
      const f=figures.find(f=>f.id===id);if(!f)continue;const span=frame.presentation.figures?.[id]?.cellSpan??1;
      const color=id===view.relationship?.attackerId?'#7edfe6':view.relationship?.targets?.some(t=>t.targetId===id)?'#ffad8e':'#ffe084';add(`focus:${id}`,outline(f.position.x+.025,f.position.y+.025,span-.05,span-.05,color,heights.feedback+.04,.045));
    }
    if(view.focusedDoor){const p=edgePlacement(view.focusedDoor);add('focused-door',outline(p.x-(p.vertical?.12:.5),p.z-(p.vertical?.5:.12),p.vertical?.24:1,p.vertical?1:.24,'#ffe084'));}
    const positions=view.combatPositions,source=positions?.get(view.relationship?.attackerId);
    if(source)for(const target of view.relationship.targets??[]){const destination=positions.get(target.targetId);if(!destination)continue;
      const a=cellCenter(source,source.cellSpan??1),b=cellCenter(destination,destination.cellSpan??1),dx=b.x-a.x,dy=b.z-a.z;
      add(`combat:${target.targetId}`,plane(material('#e7ba80'),(a.x+b.x)/2,(a.z+b.z)/2,Math.hypot(dx,dy),.035,heights.feedback,Math.atan2(dy,dx)*180/Math.PI));
    }
    for(const[index,objective]of(frame.presentation.objectives??[]).entries())if(objective.area){const a=objective.area;retain(`area:objective:${index}`,JSON.stringify(objective),'areas',()=>outline(a.x,a.y,a.w,a.h,`hsl(${sideHue(objective.sideId)},70%,78%)`,heights.areas,.055));}
    if(view.waypoints)for(const waypoint of frame.state.waypoints??[]){const a=waypoint.area;retain(`area:waypoint:${waypoint.id}`,JSON.stringify(waypoint),'areas',()=>outline(a.x,a.y,a.w,a.h,'#a99c7d',heights.areas,.02));}
    invalidate();
  }
  function resize(){const rect=host.getBoundingClientRect(),w=host.clientWidth,h=host.clientHeight;if(!w||!h)return;const ratio=Math.min(globalThis.devicePixelRatio||1,2),width=Math.round(w*ratio),height=Math.round(h*ratio);if(canvas.width!==width||canvas.height!==height)renderer.setSize(width,height,false);camera.left=-w/2;camera.right=w/2;camera.top=h/2;camera.bottom=-h/2;cameraControl.resize(w,h);return rect;}
  function updateCamera(){const pose=cameraControl.current();if(!pose)return;camera.position.set(pose.x,100,pose.y);camera.lookAt(pose.x,0,pose.y);camera.zoom=pose.scale;camera.updateProjectionMatrix();camera.updateMatrixWorld(true);}
  function draw(time){
    try { drawFrame(time); } catch(error) { raf=null;lost=true;onFailure?.(error); }
  }
  function drawFrame(time){
    raf=null;if(!active||lost||disposed||!frame)return;const begin=performance.now();if(!resize())return;const movingCamera=cameraControl.tick(time);updateCamera();
    if(motion){const t=Math.min(1,(time-motion.start)/motion.duration);motion.object.position.x=motion.from.x+(motion.to.x-motion.from.x)*t;motion.object.position.z=motion.from.z+(motion.to.z-motion.from.z)*t;if(t>=1)motion=null;}
    for(const[id,effect]of arrivals){const object=nodes.get(`unit:${id}`)?.object,t=Math.min(1,(time-effect.start)/380);if(object)object.scale.setScalar(effect.kind==='UnitCreated'?.65+.35*(1-(1-t)**3):1+Math.sin(t*Math.PI)*.08);if(t>=1||!object)arrivals.delete(id);}
    updateLabels();updateFocus();if(pointerPosition&&!drag&&!pinch)hover(pick(pointerPosition));
    const overlay=host.querySelector('#board-overlays'),r=overlay?.getBoundingClientRect(),b=host.getBoundingClientRect();cameraControl.renderIndicators(r?.width?[{x:r.left-b.left,y:r.top-b.top,w:r.width,h:r.height}]:[]);
    renderer.render(scene,camera);renderCount++;readyAt??=performance.now();firstFrameAt??=time;
    renderTimes.push(performance.now()-begin);if(renderTimes.length>240)renderTimes.shift();
    if(transient){unitRenderTimes.push(renderTimes.at(-1));if(unitRenderTimes.length>240)unitRenderTimes.shift();if(lastUnitFrame){unitIntervals.push(time-lastUnitFrame);if(unitIntervals.length>240)unitIntervals.shift();}lastUnitFrame=time;}else lastUnitFrame=0;
    const continuous=movingCamera||motion||arrivals.size;
    if(lastRender&&time-lastRender<1000){samples.push(time-lastRender);if(samples.length>240)samples.shift();}lastRender=continuous?time:0;
    const percentile=(values,p)=>[...values].sort((a,b)=>a-b)[Math.floor((values.length-1)*p)]??0;
    const textureBytes=[...textures.values()].reduce((n,e)=>n+(e.texture?.image?.width??0)*(e.texture?.image?.height??0)*4*4/3,0)+glyphs.size*128*128*4*4/3;
    metrics.textContent=JSON.stringify({renders:renderCount,initialRenderMs:+(readyAt-started).toFixed(1),drawCalls:renderer.info.render.calls,textures:renderer.info.memory.textures,geometries:renderer.info.memory.geometries,estimatedTextureMiB:+(textureBytes/1048576).toFixed(2),renderP50Ms:+percentile(renderTimes,.5).toFixed(2),renderP95Ms:+percentile(renderTimes,.95).toFixed(2),frameP50Ms:+percentile(samples,.5).toFixed(2),frameP95Ms:+percentile(samples,.95).toFixed(2),animationSamples:samples.length,unitFrames:unitRenderTimes.length,unitRenderP95Ms:+percentile(unitRenderTimes,.95).toFixed(2),unitFrameP95Ms:+percentile(unitIntervals,.95).toFixed(2),textureLoads:[...textures.values()].filter(t=>t.state==='loading').length,missingTextures:[...textures.values()].filter(t=>t.state==='missing').length});
    if(continuous)invalidate();
  }
  function updateLabels(){const pose=cameraControl.current(),size=cameraControl.size;if(!pose)return;const placed=[];
    const entries=[...labelNodes].sort(([a],[b])=>Number(b===view.focusedUnitId)-Number(a===view.focusedUnitId));
    for(const[id,label]of entries){const a=label.appearance,p=cameraControl.screen({x:label.position.x+a.cellSpan/2,y:label.position.y+a.cellSpan/2}),diameter=pose.scale*.84*a.cellSpan;
      label.detail=tokenDetail(diameter,label.detail);label.dataset.detail=String(label.detail);const width=Math.min(150,Math.max(24,diameter));
      const offscreen=p.x+diameter/2<0||p.y+diameter/2<0||p.x-diameter/2>size.width||p.y-diameter/2>size.height;
      const collision=placed.some(r=>Math.abs(r.x-p.x)<(r.w+width)/2&&Math.abs(r.y-p.y)<diameter*.7);
      label.hidden=offscreen||id===transientId||label.detail<2||collision&&id!==view.focusedUnitId;
      if(label.hidden)continue;placed.push({x:p.x,y:p.y,w:width});label.style.left=`${p.x}px`;label.style.top=`${p.y}px`;label.style.width=`${width}px`;label.style.height=`${diameter}px`;
      label.children[2].textContent=[a.posture==='Lying'?'Lying':'',frame.state.currentUnitId===id?'▶':''].filter(Boolean).join(' ');
    }
    // Coordinates are a presentation setting, never information about hidden contents.
    let coordinates=labels.querySelector('.three-coordinates');if(view.coordinates){if(!coordinates){coordinates=document.createElement('div');coordinates.className='three-coordinates';labels.append(coordinates);}coordinates.replaceChildren();
      const board=frame.state.physical.board;for(let y=Math.max(0,Math.floor(pose.y-size.height/pose.scale/2));y<Math.min(board.height,Math.ceil(pose.y+size.height/pose.scale/2));y++)for(let x=Math.max(0,Math.floor(pose.x-size.width/pose.scale/2));x<Math.min(board.width,Math.ceil(pose.x+size.width/pose.scale/2));x++){const p=cameraControl.screen({x:x+.05,y:y+.05}),n=document.createElement('span');n.textContent=`${x},${y}`;n.style.left=`${p.x}px`;n.style.top=`${p.y}px`;coordinates.append(n);}
    }else coordinates?.remove();
    // Machine labels remain available at useful scales and in the logical focus label.
    let machines=labels.querySelector('.three-machine-labels');if(!machines){machines=document.createElement('div');machines.className='three-machine-labels';labels.append(machines);}machines.replaceChildren();
    if(pose.scale>=45)for(const m of frame.presentation.machines??[]){const p=cameraControl.screen({x:m.position.x+.5,y:m.position.y+.75});if(p.x<0||p.y<0||p.x>size.width||p.y>size.height)continue;const label=document.createElement('span');label.style.left=`${p.x}px`;label.style.top=`${p.y}px`;label.style.maxWidth=`${pose.scale*.94}px`;label.textContent=`${m.name} · ${m.ownerSide??'Public'}${(m.resourceStorage??[]).map(r=>` · ${r.name} ${r.quantity}/${r.capacity}`).join('')}`;machines.append(label);}
  }
  function pick(point){
    if(!frame||!active||lost)return null;updateCamera();const rect=canvas.getBoundingClientRect();if(!rect.width||!rect.height)return null;
    raycaster.setFromCamera(new THREE.Vector2(point.x/rect.width*2-1,1-point.y/rect.height*2),camera);
    if(!raycaster.ray.intersectPlane(ground,intersection))return null;const cell={x:Math.floor(intersection.x),y:Math.floor(intersection.z)},board=frame.state.physical.board;
    if(cell.x<0||cell.y<0||cell.x>=board.width||cell.y>=board.height)return null;
    // Door hit proxies include the transparent open passage. Only supplied Door choices compete.
    const scale=cameraControl.current().scale,tolerance=Math.min(.22,Math.max(.09,8/scale));
    const doors=(view.candidates??[]).filter(c=>c.interaction.kind==='Door').map(c=>c.interaction.door);
    let door,nearest=Infinity;for(const candidate of doors){const p=edgePlacement(candidate),cross=p.vertical?Math.abs(intersection.x-p.x):Math.abs(intersection.z-p.z),along=p.vertical?Math.abs(intersection.z-p.z):Math.abs(intersection.x-p.x);if(cross<tolerance&&along<=.5&&cross<nearest){door=candidate;nearest=cross;}}
    const figure=frame.state.physical.figures.find(f=>(frame.presentation.figures?.[f.id]?.occupiedCells??[f.position]).some(c=>boardCellKey(c)===boardCellKey(cell)));
    return{cell,unitId:figure?.id,...(door?{door}:{})};
  }
  const local=event=>{const rect=canvas.getBoundingClientRect();return{x:event.clientX-rect.left,y:event.clientY-rect.top};};
  function candidates(hit){return boardCandidates(hit,{candidates:view.candidates??[]},frame.state,frame.presentation.figures);}
  function hover(hit){const choices=hit?candidates(hit):[],signature=JSON.stringify([hit,choices.map(c=>c.key)]);if(signature===lastHover)return;lastHover=signature;onHover?.(hit?.unitId??null);onPreview?.(choices.length===1?choices[0]:null);
    canvas.style.cursor=choices.length?'pointer':'grab';host.dataset.hoverCell=hit?boardCellKey(hit.cell):'';
    const description=choices.map(c=>c.boardVisual?.preview??c.displayLabel??c.label).join(' / ');
    const machine=hit&&frame.presentation.machines?.find(m=>boardCellKey(m.position)===boardCellKey(hit.cell));
    const resource=hit&&frame.state.resources?.find(r=>boardCellKey(r.position)===boardCellKey(hit.cell));
    host.title=description||(hit?.unitId?labelNodes.get(hit.unitId)?.title:machine?`${machine.name} · Owner ${machine.ownerSide??'Public'} · ${(machine.resourceStorage??[]).map(r=>`${r.name}: ${r.quantity}/${r.capacity}`).join(', ')}`:resource?.type.name??(hit?data.kindAt(hit.cell.x,hit.cell.y):''))||'';
  }
  function select(hit){if(!hit||view.busy)return;onPick(hit);}
  function updateFocus(){if(!frame)return;const p=cameraControl.screen({x:focusedCell.x+.5,y:focusedCell.y+.5}),s=cameraControl.current()?.scale??1;
    focus.style.left=`${p.x}px`;focus.style.top=`${p.y}px`;focus.style.width=focus.style.height=`${s}px`;
    const figure=frame.state.physical.figures.find(f=>(frame.presentation.figures?.[f.id]?.occupiedCells??[f.position]).some(c=>boardCellKey(c)===boardCellKey(focusedCell))),hit={cell:focusedCell,unitId:figure?.id};
    const choices=candidates(hit),machine=frame.presentation.machines?.find(m=>boardCellKey(m.position)===boardCellKey(focusedCell));
    focus.setAttribute('aria-label',[`Cell ${focusedCell.x},${focusedCell.y}`,figure?labelNodes.get(figure.id)?.title:null,machine?`${machine.name} · ${machine.ownerSide??'Public'} · ${(machine.resourceStorage??[]).map(r=>`${r.name} ${r.quantity}/${r.capacity}`).join(', ')}`:null,...choices.map(c=>c.boardVisual?.preview??c.displayLabel??c.label)].filter(Boolean).join(' · '));
  }
  function bind(node,type,handler,options={}){node.addEventListener(type,handler,{...options,signal:abort.signal});}
  bind(resetSample,'click',()=>{samples.length=renderTimes.length=unitRenderTimes.length=unitIntervals.length=0;lastRender=lastUnitFrame=0;invalidate();});
  bind(canvas,'pointerdown',event=>{if(![0,1].includes(event.button))return;const p=local(event);pointers.set(event.pointerId,p);canvas.setPointerCapture(event.pointerId);pointerPosition=p;blockClick=false;
    if(pointers.size===2){const[a,b]=[...pointers.values()];pinch={distance:Math.hypot(a.x-b.x,a.y-b.y),mid:{x:(a.x+b.x)/2,y:(a.y+b.y)/2}};drag=null;blockClick=true;return;}
    drag={id:event.pointerId,start:p,pose:cameraControl.current(),threshold:event.pointerType==='touch'?10:6,moved:event.button===1||space};
  });
  bind(canvas,'pointermove',event=>{const p=local(event);pointerPosition=p;if(pointers.has(event.pointerId))pointers.set(event.pointerId,p);
    if(pointers.size===2&&pinch){const[a,b]=[...pointers.values()],distance=Math.hypot(a.x-b.x,a.y-b.y),mid={x:(a.x+b.x)/2,y:(a.y+b.y)/2};cameraControl.zoom(distance/Math.max(1,pinch.distance),mid);const pose=cameraControl.current();cameraControl.manual({...pose,x:pose.x-(mid.x-pinch.mid.x)/pose.scale,y:pose.y-(mid.y-pinch.mid.y)/pose.scale});pinch={distance,mid};blockClick=true;event.preventDefault();return;}
    if(drag?.id===event.pointerId&&drag.pose){const dx=p.x-drag.start.x,dy=p.y-drag.start.y;if(drag.moved||Math.hypot(dx,dy)>drag.threshold){drag.moved=true;blockClick=true;hover(null);cameraControl.manual({...drag.pose,x:drag.pose.x-dx/drag.pose.scale,y:drag.pose.y-dy/drag.pose.scale});event.preventDefault();}}
    else hover(pick(p));
  });
  function finishPointer(event){const selected=pointers.has(event.pointerId)&&event.type==='pointerup'&&!blockClick&&!pinch&&!drag?.moved;
    pointers.delete(event.pointerId);if(canvas.hasPointerCapture(event.pointerId))canvas.releasePointerCapture(event.pointerId);
    if(selected)select(pick(local(event)));if(pinch){blockClick=true;pinch=null;}drag=null;
  }
  bind(canvas,'pointerup',finishPointer);bind(canvas,'pointercancel',finishPointer);bind(canvas,'lostpointercapture',()=>{drag=null;});
  bind(canvas,'pointerleave',()=>{if(!drag&&!pinch){pointerPosition=null;hover(null);}});
  bind(canvas,'wheel',event=>{if(!event.altKey||event.ctrlKey)return;event.preventDefault();cameraControl.zoom(Math.exp(-event.deltaY*.002),local(event));},{passive:false});
  bind(host,'keydown',event=>{if(event.target!==host&&event.target!==focus)return;
    const direction={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]}[event.key];
    if(direction){event.preventDefault();if(event.shiftKey){const pose=cameraControl.current();cameraControl.manual({...pose,x:pose.x+direction[0]*2,y:pose.y+direction[1]*2},true);}else{const board=frame.state.physical.board;focusedCell={x:Math.max(0,Math.min(board.width-1,focusedCell.x+direction[0])),y:Math.max(0,Math.min(board.height-1,focusedCell.y+direction[1]))};focus.hidden=false;focus.focus();const pose=cameraControl.current(),p=cameraControl.screen({x:focusedCell.x+.5,y:focusedCell.y+.5});if(p.x<0||p.y<0||p.x>cameraControl.size.width||p.y>cameraControl.size.height)cameraControl.manual({...pose,x:focusedCell.x+.5,y:focusedCell.y+.5},true);onHover?.(frame.state.physical.figures.find(f=>(frame.presentation.figures?.[f.id]?.occupiedCells??[f.position]).some(c=>boardCellKey(c)===boardCellKey(focusedCell)))?.id??null);invalidate();}}
    if(['+','='].includes(event.key)){event.preventDefault();cameraControl.zoom(1.2,undefined,true);}if(event.key==='-'){event.preventDefault();cameraControl.zoom(1/1.2,undefined,true);}
    if(event.key===' '){space=true;event.preventDefault();}if(event.key==='Enter'){event.preventDefault();select({cell:focusedCell});}
  });
  bind(host,'keyup',event=>{if(event.key===' '){space=false;event.preventDefault();if(!blockClick)select({cell:focusedCell});}});
  bind(host,'blur',()=>{space=false;});bind(focus,'click',event=>{event.preventDefault();select({cell:focusedCell});});
  const observer=new ResizeObserver(()=>{if(active){resize();invalidate();}});observer.observe(host);
  bind(globalThis,'resize',()=>{if(active){resize();invalidate();}});
  bind(canvas,'webglcontextlost',event=>{event.preventDefault();lost=true;cancel();if(raf!==null)cancelAnimationFrame(raf);raf=null;onFailure?.(new Error('WebGL context lost. Enhanced 2D remains available.'));});
  bind(canvas,'webglcontextrestored',()=>{lost=false;invalidate();});
  function setActive(value){if(value&&lost)throw Error('WebGL is unavailable. Continue in Enhanced 2D until the context is restored.');active=value;host.hidden=!value;controls.root.hidden=!value;if(!value){cancel();pointers.clear();drag=pinch=null;if(raf!==null)cancelAnimationFrame(raf);raf=null;pointerPosition=null;hover(null);}else{resize();invalidate();}}
  async function present(event,occurrence,playback){
    if(!active||lost||!frame)return;const generation=epoch;
    if(event.role==='Movement'){
      const stable=nodes.get(`unit:${event.unitId}`)?.object,figure=frame.state.physical.figures.find(f=>f.id===event.unitId),a=event.movingFigure??(figure?appearance(figure):null),segments=event.movementSegments??(event.path?.length?[event.path]:[]);
      if(!a||!segments.length)return;
      if(stable)stable.visible=false;transientId=event.unitId;transient=token(a,segments[0][0],heights.movement);groups.movement.add(transient);
      try{for(const segment of segments){if(playback.skipping()||generation!==epoch)break;const start=cellCenter(segment[0],a.cellSpan);transient.position.x=start.x;transient.position.z=start.z;cameraControl.movementStep(segment[0]);invalidate();
        for(const anchor of segment.slice(1)){if(playback.skipping()||generation!==epoch)break;const to=cellCenter(anchor,a.cellSpan);motion={object:transient,from:{x:transient.position.x,z:transient.position.z},to,start:performance.now(),duration:220};cameraControl.movementStep(anchor);invalidate();await playback.pause(250);if(motion){motion.object.position.x=to.x;motion.object.position.z=to.z;motion=null;}}
      }}finally{if(transient)removeRecord({object:transient});transient=null;transientId=null;motion=null;if(stable)stable.visible=true;invalidate();}
      return;
    }
    const id=event.targetId??event.unitId??occurrence?.unitId,node=nodes.get(`unit:${id}`)?.object;
    const duration=event.role==='Defeat'?320:['DoorAttempt','DoorOpened','DoorClosed'].includes(event.role)?300:250;
    if(node){node.scale.setScalar(event.role==='Defeat'?.6:1.06);invalidate();}
    try{await playback.pause(duration);}finally{if(node)node.scale.setScalar(1);invalidate();}
  }
  function settle(){if(motion){motion.object.position.x=motion.to.x;motion.object.position.z=motion.to.z;motion=null;}for(const id of arrivals.keys())nodes.get(`unit:${id}`)?.object.scale.setScalar(1);arrivals.clear();cameraControl.settle();invalidate();}
  function cancel(){epoch++;settle();motion=null;if(transient)removeRecord({object:transient});transient=null;transientId=null;for(const record of nodes.values())record.object.visible=true;cameraControl.cancel();}
  function dispose(){if(disposed)return;setActive(false);disposed=true;abort.abort();observer.disconnect();cameraControl.dispose();for(const record of nodes.values())removeRecord(record);nodes.clear();for(const geometry of Object.values(geometries))geometry.dispose();for(const m of materials.values())m.dispose();for(const entry of textures.values())entry.texture?.dispose();for(const m of glyphs.values()){m.map.dispose();m.dispose();}renderer.dispose();labels.remove();focus.remove();canvas.remove();diagnostics.remove();controls.root.remove();}
  return{host,camera:cameraControl,renderFrame,showInteraction,present,project:cameraControl.screen,settle,cancel,setActive,dispose,pick,
    afterOccurrence:occurrence=>{if(active&&['UnitCreated','PostureChanged'].includes(occurrence?.kind)&&animate()){arrivals.set(occurrence.unitId,{kind:occurrence.kind,start:performance.now()});invalidate();}},
    inspect:()=>({active,lost,renderCount,initialRenderMs:readyAt?readyAt-started:null,calls:renderer.info.render.calls,triangles:renderer.info.render.triangles,memory:{...renderer.info.memory},textureStates:Object.fromEntries([...textures].map(([url,e])=>[url,e.state])),frameIntervals:[...samples],renderTimes:[...renderTimes],camera:cameraControl.inspect(),units:labelNodes.size,transient:!!transient})};
}
