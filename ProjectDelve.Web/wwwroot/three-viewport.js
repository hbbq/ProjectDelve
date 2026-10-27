import * as THREE from './vendor/three/0.180.0/build/three.module.js';
import { boardVisuals, terrainLayers, railConnections, railGeometry } from './board-visuals.js';
import { sideHue } from './side-colors.js';
import { createThreeCamera } from './three-camera.js';
import { boardCandidates, boardCellKey, boardEdgeKey, tokenDetail } from './playtest-viewport.js';
import { createTabletopVisuals, createMovementClipPool } from './three-tabletop.js';
import { createTabletopLighting } from './three-lighting.js';
import { orientationBasis, groundQuadrilateral, perspectiveLens } from './three-camera-geometry.js';

const flatHeights = Object.freeze({terrain:0,grid:.015,objects:.04,rails:.06,edges:.1,items:.15,movement:.3,fog:.6,units:.8,feedback:1.1,areas:1.2,board:-.1});
const physicalHeights = Object.freeze({...flatHeights,objects:.02,rails:.008,edges:.02,items:.03,movement:.012,fog:.006,units:.012,feedback:.018,areas:.02});
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
  let presentation='flat',heights=flatHeights,representationTransition=null;
  const canvas=document.createElement('canvas');canvas.className='three-canvas';canvas.setAttribute('aria-hidden','true');host.prepend(canvas);
  let renderer;
  try {renderer=rendererFactory({canvas,antialias:true,alpha:false,powerPreference:'default'});}
  catch(error){canvas.remove();throw error;}
  renderer.outputColorSpace=THREE.SRGBColorSpace;renderer.toneMapping=THREE.NoToneMapping;renderer.shadowMap.enabled=false;
  renderer.localClippingEnabled=true;
  renderer.setClearColor('#11161c');
  const scene=new THREE.Scene(), orthographicCamera=new THREE.OrthographicCamera(-1,1,1,-1,.1,200),perspectiveCamera=new THREE.PerspectiveCamera(45,1,.01,200);
  let camera=orthographicCamera;
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
  const arrivals=new Map(),flips=new Map(),doorMotions=new Map(),unitRoots=new Map(),postureChanges=new Map(),pieceEffects=new Map();
  let clipPool=null;
  const pointers=new Map(),samples=[],renderTimes=[],unitRenderTimes=[],unitIntervals=[],reconcileTimes=[],cameraRenderTimes=[],cameraIntervals=[];
  let presentationStartedAt=null,presentationFirstFrameMs=null,lastCameraFrame=0;
  let lastUnitFrame=0;
  let renderCount=0,firstFrameAt=null,readyAt=null,lastHover='',lastRender=0;
  const started=startedAt;
  function invalidate(){if(active&&!lost&&!disposed&&raf===null)raf=requestAnimationFrame(draw);}
  const cameraControl=createThreeCamera({controls,invalidate,animate,onChange:()=>onProject?.()});
  const lighting=createTabletopLighting({scene,root:controls.root,invalidate});
  const physical=createTabletopVisuals({material,physicalMaterial:lighting.material,glyph,plane,artwork,geometries});
  function material(color,source,transparent=false,vertexColors=false) {
    const key=JSON.stringify([color,source,transparent,vertexColors]);if(materials.has(key))return materials.get(key);
    const m=new THREE.MeshBasicMaterial({color:color==='transparent'?'#19232c':color??'#666a68',transparent,opacity:source&&transparent?0:1,depthWrite:!transparent,side:THREE.FrontSide,vertexColors});
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
  function flatToken(appearance,position,height=heights.units){
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
    group.userData.infoAnchor={x:0,y:-heights.units,z:0};group.userData.visualBounds={radius:span*.42,height:0};
    return group;
  }
  function token(a,position,height=heights.units){
    if(presentation==='flat')return flatToken(a,position,height);
    const root=new THREE.Group(),center=cellCenter(position,a.cellSpan??1);root.position.set(center.x,height,center.z);
    const body=physical.unit(a);root.add(body);root.userData.infoAnchor=body.userData.infoAnchor;root.userData.visualBounds=body.userData.bounds;return root;
  }
  function updateUnit(figure,a){
    const id=figure.id,key=`unit:${id}`,signature=JSON.stringify([presentation,a]);retained.add(key);
    let root=unitRoots.get(id);
    if(!root){root=new THREE.Group();root.name=`unit:${id}`;root.userData.unitId=id;groups.units.add(root);unitRoots.set(id,root);}
    const old=nodes.get(key),previous=root.userData.appearance;
    if(old?.signature!==signature){
      for(const child of [...root.children])removeRecord({object:child});
      const body=token(a,{x:0,y:0},0);for(const child of [...body.children])root.add(child);
      root.userData.infoAnchor=body.userData.infoAnchor;root.userData.visualBounds=body.userData.visualBounds;
      if(presentation==='tabletop'&&previous&&previous.posture!==a.posture&&animate()){
        const from=previous.posture==='Lying'?Math.PI:0,to=a.posture==='Lying'?Math.PI:0;
        postureChanges.set(id,{from,to});
      }
      nodes.set(key,{signature,object:root});
    }
    const center=cellCenter(figure.position,a.cellSpan);root.position.set(center.x,heights.units,center.z);
    if(presentation==='tabletop'&&a.active&&!root.getObjectByName('active-mark')){const mark=plane(glyph('▶'),-a.cellSpan*.34,-a.cellSpan*.3,a.cellSpan*.18,a.cellSpan*.18,.095);mark.name='active-mark';root.add(mark);}
    root.userData.appearance={...a};root.userData.footprint=frame.presentation.figures?.[id]?.occupiedCells??[figure.position];
    return root;
  }
  function appearance(figure){
    const unit=frame.state.units.find(u=>u.id===figure.id),type=frame.state.types.find(t=>t.id===unit?.typeId),card=frame.presentation.cards?.[figure.id];
    return{typeId:unit?.typeId,sideId:unit?.sideId,name:card?.individualName?.trim()||card?.displayName||type?.displayName||'Unit',posture:figure.posture,active:frame.state.currentUnitId===figure.id,cellSpan:frame.presentation.figures?.[figure.id]?.cellSpan??1,hp:unit?.currentHp,maxHp:type?.hp??1};
  }
  function renderFrame(next){
    if(disposed)return;const begin=performance.now();postureChanges.clear();const identity=next.metadata.sessionId??next.metadata.scenarioId;
    if(identity!==session){cancel();session=identity;epoch++;for(const record of nodes.values())removeRecord(record);nodes.clear();unitRoots.clear();for(const label of labelNodes.values())label.remove();labelNodes.clear();}
    frame=next;lastHover='';data=observedTerrain(frame);retained=new Set();const{board,terrain,kindAt}=data;
    // Measured 50x50 rendering was draw-call-bound. Batch only repeated flat
    // terrain and fog by material; all dynamic objects retain ordinary meshes.
    const batches=new Map(),solidBatches=new Map();
    const batchSolid=object=>{
      object.updateMatrixWorld(true);object.traverse(mesh=>{if(!mesh.isMesh)return;
        const key=`physical-batch:${mesh.geometry.id}:${lighting.identity(mesh.material)}`;let batch=solidBatches.get(key);
        if(!batch){batch={geometry:mesh.geometry,material:mesh.material,matrices:[]};solidBatches.set(key,batch);}batch.matrices.push(mesh.matrixWorld.clone());
      });
    };
    if(presentation==='tabletop')retain('board-slab',`${board.width}:${board.height}`,'board',()=>physical.board(board.width,board.height));
    const batchCell=(group,m,x,y,rotation=0)=>{const key=`${group}-batch:${m.id}`;let batch=batches.get(key);if(!batch){batch={group,material:m,cells:[]};batches.set(key,batch);}batch.cells.push([x,y,rotation]);};
    for(let y=0;y<board.height;y++)for(let x=0;x<board.width;x++){
      const kind=kindAt(x,y),tile=terrain.get(`${x},${y}`);
      if(kind==='Unknown'){
        batchCell('fog',material('#161b22'),x,y);continue;
      }
      const layer=terrainLayers(kind,{seed:frame.visualSeed,x,y,direction:tile?.direction,terrain});
      batchCell('terrain',material(layer.base.fill,layer.base.image),x,y,layer.rotation);
      if(presentation==='tabletop'&&(layer.object||kind==='SolidRock'))batchSolid(physical.terrain(kind,layer,x,y,frame.visualSeed,kindAt));
      else if(layer.object){retain(`object:${x},${y}`,JSON.stringify(layer.object)+layer.objectRotation,'objects',()=>{
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
      if(presentation==='tabletop'){
        if(['ClosedDoor','OpenDoor'].includes(edge.kind)){
          const key=`edge:${boardEdgeKey(edge)}`,previous=nodes.get(key),changed=previous&&previous.signature!==`physical:${edge.kind}`;
          const object=retain(key,`physical:${edge.kind}`,'edges',()=>physical.edge(edge,at));
          if(changed&&animate())doorMotions.set(key,{open:edge.kind==='OpenDoor',start:performance.now()});
          object.userData.edge=edge;continue;
        }
        batchSolid(physical.edge(edge,at));continue;
      }
      retain(`edge:${boardEdgeKey(edge)}`,edge.kind,'edges',()=>{
        const group=new THREE.Group();
        if(edge.kind==='OpenDoor'){
          for(const offset of [-.4,.4])group.add(plane(material('#6e6049'),at.x+(at.vertical?0:offset),at.z+(at.vertical?offset:0),at.vertical?.12:.2,at.vertical?.2:.12,heights.edges));
        }else group.add(plane(material(visual.fill??'#45494b'),at.x,at.z,1,.13,heights.edges,at.vertical?90:0));
        if(visual.image)group.add(plane(material('#45494b',visual.image,true),at.x,at.z,1,80/512,heights.edges+.005,at.vertical?90:0));return group;
      });
    }
    for(const machine of frame.presentation.machines??[]){const visual={...boardVisuals.machine.default,...boardVisuals.machine[machine.typeId]};
      if(presentation==='tabletop'){retain(`machine:${machine.id}`,`physical:${JSON.stringify(machine)}`,'items',()=>physical.machine(machine,visual));continue;}
      retain(`machine:${machine.id}`,JSON.stringify(machine),'items',()=>{
        const group=new THREE.Group();const layer=terrainLayers(visual.baseTerrain??'StoneFloor',{seed:frame.visualSeed,x:machine.position.x,y:machine.position.y});
        group.add(plane(material(layer.base.fill,layer.base.image),machine.position.x+.5,machine.position.y+.5,1,1,heights.objects,layer.rotation));
        const fallback=plane(glyph(visual.glyph??'⚙'),machine.position.x+.5,machine.position.y+.5,.7,.7,heights.items);
        artwork(group,fallback,visual.image?material(visual.fill,visual.image,true):null,machine.position.x+.5,machine.position.y+.5,1,1,heights.items+.01);return group;
      });
    }
    for(const [category,objects] of [['resource',frame.state.resources??[]],['cellMarker',frame.state.cellMarkers??[]]])for(const object of objects){const visual={...boardVisuals[category].default,...boardVisuals[category][object.type.id]},p=object.position;
      if(presentation==='tabletop'){
        const key=`${category}:${boardCellKey(p)}`,existed=nodes.has(key);
        retain(key,`physical:${JSON.stringify(object)}`,'items',()=>physical.item(category,object,visual));
        if(!existed&&active&&animate()&&presentationStartedAt===null)pieceEffects.set(key,{start:performance.now()});continue;
      }
      retain(`${category}:${boardCellKey(p)}`,JSON.stringify(object),'items',()=>{
        const group=new THREE.Group(),cx=p.x+(category==='resource'?.81:.23),cy=p.y+.2;
        const fallback=plane(glyph(visual.glyph??'?'),cx,cy,.3,.3,heights.items);artwork(group,fallback,visual.image?material(visual.fill,visual.image,true):null,cx,cy,.36,.36,heights.items+.01);return group;
      });
    }
    const labelIds=new Set();
    for(const figure of frame.state.physical.figures){const a=appearance(figure);
      updateUnit(figure,a);
      labelIds.add(figure.id);let label=labelNodes.get(figure.id);if(!label){label=document.createElement('div');label.className='three-token-info';label.innerHTML='<span class="three-hp"></span><span class="three-name"></span><span class="three-state"></span>';labels.append(label);labelNodes.set(figure.id,label);}
      label.appearance=a;label.position=figure.position;label.title=`${a.name} · Side ${a.sideId} · ${a.posture}${a.maxHp>1?` · HP ${a.hp}/${a.maxHp}`:''}`;
      label.children[0].textContent=a.maxHp>1?`${a.hp}/${a.maxHp}`:'';label.children[1].textContent=a.name;
    }
    for(const[id,label]of labelNodes)if(!labelIds.has(id)){label.remove();labelNodes.delete(id);}
    for(const[key,batch]of solidBatches)retain(key,JSON.stringify(batch.matrices.map(m=>m.elements)),'objects',()=>{
      const mesh=new THREE.InstancedMesh(batch.geometry,batch.material,batch.matrices.length);batch.matrices.forEach((m,i)=>mesh.setMatrixAt(i,m));mesh.instanceMatrix.needsUpdate=true;mesh.computeBoundingSphere();return mesh;
    });
    for(const[key,record]of nodes)if(!retained.has(key)){removeRecord(record);nodes.delete(key);if(key.startsWith('unit:'))unitRoots.delete(key.slice(5));}
    cameraControl.setFrame(frame.state,frame.presentation,frame.metadata);if(frame.state.currentUnitId)focusedCell={...frame.state.physical.figures.find(f=>f.id===frame.state.currentUnitId)?.position??focusedCell};
    showInteraction(view);reconcileTimes.push(performance.now()-begin);if(reconcileTimes.length>120)reconcileTimes.shift();invalidate();
  }
  function removeRecord(record){record.object.removeFromParent();record.object.traverse(object=>{for(const fallback of object.userData.fallbacks??[])fallback.material.userData.fallbacks.delete(fallback.node);if(object.userData.overlayMaterial)object.material.dispose();});if(record.object.isInstancedMesh)record.object.dispose();if(record.object.userData.owned){record.object.geometry.dispose();record.object.material.dispose();}}
  function setPresentation(value){
    if(!['flat','tabletop'].includes(value))throw Error('Unknown Three presentation');
    if(presentation===value)return;
    // Called only by the router at an authoritative checkpoint. Unit roots stay.
    settle();presentationStartedAt=performance.now();presentation=value;heights=value==='tabletop'?physicalHeights:flatHeights;
    host.dataset.presentation=value;
    lighting.setPresentation(value);
    for(const [key,record]of nodes)if(!key.startsWith('unit:')){removeRecord(record);nodes.delete(key);}
    cameraControl.setPresentation(value,active);
    if(frame)renderFrame(frame);
    representationTransition=active&&animate()?{start:performance.now(),parts:[...unitRoots.values()].flatMap(root=>root.children.filter(child=>child.name!=='active-mark').map(object=>({object,scale:object.scale.clone()})))}:null;invalidate();
  }
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
    if(presentation==='tabletop')for(const name of ['feedback','areas'])groups[name].traverse(object=>{
      object.renderOrder=100; if(object.material){object.material=object.material.clone();object.material.depthTest=false;object.material.depthWrite=false;object.userData.overlayMaterial=true;}
    });
    invalidate();
  }
  function resize(){const rect=host.getBoundingClientRect(),w=host.clientWidth,h=host.clientHeight;if(!w||!h)return;const ratio=Math.min(globalThis.devicePixelRatio||1,2),width=Math.round(w*ratio),height=Math.round(h*ratio);if(canvas.width!==width||canvas.height!==height)renderer.setSize(width,height,false);orthographicCamera.left=-w/2;orthographicCamera.right=w/2;orthographicCamera.top=h/2;orthographicCamera.bottom=-h/2;perspectiveCamera.aspect=w/h;cameraControl.resize(w,h);return rect;}
  function updateCamera(){const pose=cameraControl.current();if(!pose)return;const b=orientationBasis(pose),board=frame.state.physical.board,lens=perspectiveLens(pose,cameraControl.size),distance=lens.perspective?lens.distance:Math.max(100,Math.hypot(board.width,board.height)*2);
    camera=lens.perspective?perspectiveCamera:orthographicCamera;
    camera.position.set(pose.x+distance*b.st*b.s,distance*b.ct,pose.y+distance*b.st*b.c);
    const closest=Math.min(...[-.12,board.width+.12].flatMap(x=>[-.12,board.height+.12].map(y=>distance-b.st*(b.s*(x-pose.x)+b.c*(y-pose.y))-b.ct*.75)));
    camera.up.set(-b.ct*b.s,b.st,-b.ct*b.c);camera.lookAt(pose.x,0,pose.y);camera.far=distance+Math.hypot(board.width,board.height)+10;camera.near=lens.perspective?Math.max(.01,closest*.5):.1;camera.zoom=lens.perspective?1:pose.scale;if(lens.perspective)camera.fov=lens.fov;camera.updateProjectionMatrix();camera.updateMatrixWorld(true);}
  function draw(time){
    try { drawFrame(time); } catch(error) { raf=null;lost=true;onFailure?.(error); }
  }
  function drawFrame(time){
    raf=null;if(!active||lost||disposed||!frame)return;const begin=performance.now();if(!resize())return;const movingCamera=cameraControl.tick(time);updateCamera();
    if(motion){const t=Math.min(1,(time-motion.start)/motion.duration);motion.object.position.x=motion.from.x+(motion.to.x-motion.from.x)*t;motion.object.position.z=motion.from.z+(motion.to.z-motion.from.z)*t;if(t>=1)motion=null;}
    if(clipPool&&transient)clipPool.update(transient.position.x,transient.position.z,transient.userData.span);
    for(const [id,effect]of flips){const body=unitRoots.get(id)?.getObjectByName('cardboard-body'),t=Math.min(1,(time-effect.start)/360);if(body)physical.posture(body,effect.from+(effect.to-effect.from)*(1-(1-t)**3));if(t>=1||!body)flips.delete(id);}
    for(const [key,effect]of doorMotions){const leaf=nodes.get(key)?.object.getObjectByName('door-leaf'),t=Math.min(1,(time-effect.start)/280);if(leaf){leaf.visible=t<1||!effect.open;leaf.position.y=effect.open?t*.3:(1-t)*.3;leaf.scale.y=effect.open?1-t:t;}if(t>=1||!leaf){if(leaf){leaf.position.y=0;leaf.scale.y=1;}doorMotions.delete(key);}}
    for(const [key,effect]of pieceEffects){const object=nodes.get(key)?.object,t=Math.min(1,(time-effect.start)/240);if(object)object.scale.setScalar(.65+.35*(1-(1-t)**3));if(t>=1||!object)pieceEffects.delete(key);}
    if(representationTransition){const t=Math.min(1,(time-representationTransition.start)/300),ease=1-(1-t)**3;
      if(presentation==='tabletop')for(const name of ['objects','edges','items'])groups[name].scale.y=.08+.92*ease;
      // Never lower Unit roots beneath fog during a representation transition.
      for(const {object,scale}of representationTransition.parts)object.scale.copy(scale).multiplyScalar(.92+.08*ease);
      if(t>=1)representationTransition=null;
    }
    const cutaway=presentation==='tabletop'&&controls.cutaway?.checked;groups.objects.visible=groups.edges.visible=!cutaway;
    for(const [key,record]of nodes)if(key.startsWith('machine:'))record.object.visible=!cutaway;
    for(const[id,effect]of arrivals){const object=nodes.get(`unit:${id}`)?.object,t=Math.min(1,(time-effect.start)/380);if(object)object.scale.setScalar(effect.kind==='UnitCreated'?.65+.35*(1-(1-t)**3):1+Math.sin(t*Math.PI)*.08);if(t>=1||!object)arrivals.delete(id);}
    updateLabels();updateFocus();if(pointerPosition&&!drag&&!pinch)hover(pick(pointerPosition));
    const overlay=host.querySelector('#board-overlays'),r=overlay?.getBoundingClientRect(),b=host.getBoundingClientRect();cameraControl.renderIndicators(r?.width?[{x:r.left-b.left,y:r.top-b.top,w:r.width,h:r.height}]:[]);
    // Draw information last, including above transparent portraits. Ground-space
    // feedback stays aligned with the selectable Cells at every camera angle.
    if(presentation==='tabletop'){
      groups.feedback.visible=groups.areas.visible=false;renderer.render(scene,camera);
      const calls=renderer.info.render.calls,triangles=renderer.info.render.triangles;
      const hidden=[];for(const [name,group]of Object.entries(groups))if(!['feedback','areas'].includes(name)){hidden.push([group,group.visible]);group.visible=false;}
      groups.feedback.visible=groups.areas.visible=true;
      const autoClear=renderer.autoClear;renderer.autoClear=false;renderer.render(scene,camera);renderer.autoClear=autoClear;
      renderer.info.render.calls+=calls;renderer.info.render.triangles+=triangles;for(const [group,visible]of hidden)group.visible=visible;
    }else renderer.render(scene,camera);
    lighting.frameRendered();renderCount++;readyAt??=performance.now();firstFrameAt??=time;
    if(presentationStartedAt!==null){presentationFirstFrameMs=performance.now()-presentationStartedAt;presentationStartedAt=null;}
    renderTimes.push(performance.now()-begin);if(renderTimes.length>240)renderTimes.shift();
    if(movingCamera){cameraRenderTimes.push(renderTimes.at(-1));if(cameraRenderTimes.length>240)cameraRenderTimes.shift();if(lastCameraFrame){cameraIntervals.push(time-lastCameraFrame);if(cameraIntervals.length>240)cameraIntervals.shift();}lastCameraFrame=time;}else lastCameraFrame=0;
    if(transient){unitRenderTimes.push(renderTimes.at(-1));if(unitRenderTimes.length>240)unitRenderTimes.shift();if(lastUnitFrame){unitIntervals.push(time-lastUnitFrame);if(unitIntervals.length>240)unitIntervals.shift();}lastUnitFrame=time;}else lastUnitFrame=0;
    const continuous=movingCamera||motion||arrivals.size||flips.size||doorMotions.size||pieceEffects.size||representationTransition;
    if(lastRender&&time-lastRender<1000){samples.push(time-lastRender);if(samples.length>240)samples.shift();}lastRender=continuous?time:0;
    const percentile=(values,p)=>[...values].sort((a,b)=>a-b)[Math.floor((values.length-1)*p)]??0;
    const textureBytes=[...textures.values()].reduce((n,e)=>n+(e.texture?.image?.width??0)*(e.texture?.image?.height??0)*4*4/3,0)+glyphs.size*128*128*4*4/3;
    metrics.textContent=JSON.stringify({presentation,lighting:lighting.inspect(),materials:materials.size+glyphs.size+lighting.inspect().materials,renders:renderCount,initialRenderMs:+(readyAt-started).toFixed(1),presentationFirstFrameMs:presentationFirstFrameMs==null?null:+presentationFirstFrameMs.toFixed(1),reconcileP95Ms:+percentile(reconcileTimes,.95).toFixed(2),drawCalls:renderer.info.render.calls,triangles:renderer.info.render.triangles,textures:renderer.info.memory.textures,geometries:renderer.info.memory.geometries,programs:renderer.info.programs?.length??0,estimatedTextureMiB:+(textureBytes/1048576).toFixed(2),renderP50Ms:+percentile(renderTimes,.5).toFixed(2),renderP95Ms:+percentile(renderTimes,.95).toFixed(2),frameP50Ms:+percentile(samples,.5).toFixed(2),frameP95Ms:+percentile(samples,.95).toFixed(2),animationSamples:samples.length,cameraFrames:cameraRenderTimes.length,cameraRenderP95Ms:+percentile(cameraRenderTimes,.95).toFixed(2),cameraFrameP95Ms:+percentile(cameraIntervals,.95).toFixed(2),unitFrames:unitRenderTimes.length,unitRenderP95Ms:+percentile(unitRenderTimes,.95).toFixed(2),unitFrameP95Ms:+percentile(unitIntervals,.95).toFixed(2),textureLoads:[...textures.values()].filter(t=>t.state==='loading').length,missingTextures:[...textures.values()].filter(t=>t.state==='missing').length});
    if(continuous)invalidate();
  }
  function updateLabels(){const pose=cameraControl.current(),size=cameraControl.size;if(!pose)return;const placed=[];
    const priority=id=>id===view.focusedUnitId?3:id===frame.state.currentUnitId?2:view.relationship?.targets?.some(t=>t.targetId===id)?1:0;
    const entries=[...labelNodes].sort(([a],[b])=>priority(b)-priority(a));
    for(const[id,label]of entries){const a=label.appearance,root=unitRoots.get(id),anchor=root?.userData.infoAnchor??{x:0,y:0,z:0},height=(root?.position.y??0)+anchor.y;
      const center={x:label.position.x+a.cellSpan/2+anchor.x,y:label.position.y+a.cellSpan/2+anchor.z,height},p=cameraControl.screen(center),radius=root?.userData.visualBounds?.radius??a.cellSpan*.42;
      const samples=presentation==='tabletop'?Array.from({length:16},(_,i)=>cameraControl.screen({...center,x:center.x+Math.cos(i*Math.PI/8)*radius,y:center.y+Math.sin(i*Math.PI/8)*radius})):null;
      const diameter=samples?Math.max(...samples.map(p=>p.x))-Math.min(...samples.map(p=>p.x)):pose.scale*.84*a.cellSpan;
      const screenHeight=samples?Math.max(...samples.map(p=>p.y))-Math.min(...samples.map(p=>p.y)):diameter;
      label.detail=tokenDetail(diameter,label.detail);label.dataset.detail=String(label.detail);const width=Math.min(150,Math.max(24,diameter));
      const offscreen=p.behind||p.x+diameter/2<0||p.y+diameter/2<0||p.x-diameter/2>size.width||p.y-diameter/2>size.height;
      const collision=placed.some(r=>Math.abs(r.x-p.x)<(r.w+width)/2&&Math.abs(r.y-p.y)<diameter*.7);
      label.hidden=offscreen||id===transientId||label.detail<2||collision&&id!==view.focusedUnitId;
      if(label.hidden)continue;placed.push({x:p.x,y:p.y,w:width});label.style.left=`${p.x}px`;label.style.top=`${p.y}px`;label.style.width=`${width}px`;label.style.height=`${Math.max(22,screenHeight)}px`;
      label.children[2].textContent=[a.posture==='Lying'?'Lying':'',frame.state.currentUnitId===id?'▶':''].filter(Boolean).join(' ');
    }
    // Coordinates are a presentation setting, never information about hidden contents.
    let coordinates=labels.querySelector('.three-coordinates');if(view.coordinates){if(!coordinates){coordinates=document.createElement('div');coordinates.className='three-coordinates';labels.append(coordinates);}coordinates.replaceChildren();
      const board=frame.state.physical.board,q=groundQuadrilateral(pose,size);for(let y=Math.max(0,Math.floor(Math.min(...q.map(p=>p.y))));y<Math.min(board.height,Math.ceil(Math.max(...q.map(p=>p.y))));y++)for(let x=Math.max(0,Math.floor(Math.min(...q.map(p=>p.x))));x<Math.min(board.width,Math.ceil(Math.max(...q.map(p=>p.x))));x++){const p=cameraControl.screen({x:x+.05,y:y+.05}),n=document.createElement('span');n.textContent=`${x},${y}`;n.style.left=`${p.x}px`;n.style.top=`${p.y}px`;coordinates.append(n);}
    }else coordinates?.remove();
    // Machine labels remain available at useful scales and in the logical focus label.
    let machines=labels.querySelector('.three-machine-labels');if(!machines){machines=document.createElement('div');machines.className='three-machine-labels';labels.append(machines);}machines.replaceChildren();
    if(pose.scale>=45)for(const m of frame.presentation.machines??[]){const p=cameraControl.screen({x:m.position.x+.5,y:m.position.y+.75,height:presentation==='tabletop'?.4:0});if(p.x<0||p.y<0||p.x>size.width||p.y>size.height)continue;const label=document.createElement('span');label.style.left=`${p.x}px`;label.style.top=`${p.y}px`;label.style.maxWidth=`${pose.scale*.94}px`;label.textContent=`${m.name} · ${m.ownerSide??'Public'}${(m.resourceStorage??[]).map(r=>` · ${r.name} ${r.quantity}/${r.capacity}`).join('')}`;machines.append(label);}
    let items=labels.querySelector('.three-item-indicators');if(!items){items=document.createElement('div');items.className='three-item-indicators';labels.append(items);}items.replaceChildren();
    if(presentation==='tabletop'&&pose.scale>=28)for(const object of [...frame.state.resources??[],...frame.state.cellMarkers??[]]){
      if(!frame.state.physical.figures.some(f=>(frame.presentation.figures?.[f.id]?.occupiedCells??[f.position]).some(c=>boardCellKey(c)===boardCellKey(object.position))))continue;
      const p=cameraControl.screen({x:object.position.x+.84,y:object.position.y+.16,height:.15});if(p.x<0||p.y<0||p.x>size.width||p.y>size.height)continue;
      const n=document.createElement('span');n.textContent=boardVisuals.resource[object.type.id]?.glyph??boardVisuals.cellMarker[object.type.id]?.glyph??'●';n.title=object.type.name;n.style.left=`${p.x}px`;n.style.top=`${p.y}px`;items.append(n);
    }
  }
  function pick(point){
    if(!frame||!active||lost)return null;updateCamera();const rect=canvas.getBoundingClientRect();if(!rect.width||!rect.height)return null;
    raycaster.setFromCamera(new THREE.Vector2(point.x/rect.width*2-1,1-point.y/rect.height*2),camera);
    if(!raycaster.ray.intersectPlane(ground,intersection))return null;const cell={x:Math.floor(intersection.x),y:Math.floor(intersection.z)},board=frame.state.physical.board;
    // Door hit proxies include the transparent open passage. Only supplied Door choices compete.
    const doors=(view.candidates??[]).filter(c=>c.interaction.kind==='Door').map(c=>c.interaction.door);
    let door,nearest=Infinity;for(const candidate of doors){const p=edgePlacement(candidate),a=cameraControl.screen({x:p.x-(p.vertical?0:.5),y:p.z-(p.vertical?.5:0)}),b=cameraControl.screen({x:p.x+(p.vertical?0:.5),y:p.z+(p.vertical?.5:0)}),dx=b.x-a.x,dy=b.y-a.y,t=Math.max(0,Math.min(1,((point.x-a.x)*dx+(point.y-a.y)*dy)/(dx*dx+dy*dy)));
      let distance=Math.hypot(point.x-a.x-t*dx,point.y-a.y-t*dy);
      if(presentation==='tabletop'&&candidate.kind!=='OpenDoor'){
        const topA=cameraControl.screen({x:p.x-(p.vertical?0:.5),y:p.z-(p.vertical?.5:0),height:.6}),topB=cameraControl.screen({x:p.x+(p.vertical?0:.5),y:p.z+(p.vertical?.5:0),height:.6});
        const polygon=[a,b,topB,topA],crosses=polygon.map((v,i)=>{const w=polygon[(i+1)%4];return(w.x-v.x)*(point.y-v.y)-(w.y-v.y)*(point.x-v.x);});
        if(!polygon.some(v=>v.behind)&&(crosses.every(v=>v>=-1e-6)||crosses.every(v=>v<=1e-6)))distance=0;
      }
      if(distance<10&&distance<nearest){door=candidate;nearest=distance;}
    }
    let figure=frame.state.physical.figures.find(f=>(frame.presentation.figures?.[f.id]?.occupiedCells??[f.position]).some(c=>boardCellKey(c)===boardCellKey(cell)));
    if(presentation==='tabletop'&&!door&&!(view.candidates??[]).some(c=>c.interaction.kind==='Position'&&boardCellKey(c.interaction.position)===boardCellKey(cell))){
      const at=cameraControl.groundAtHeight?.(point,.084)??cameraControl.ground(point);
      const proxy=at&&frame.state.physical.figures.find(f=>{const span=frame.presentation.figures?.[f.id]?.cellSpan??1,center=cellCenter(f.position,span);return Math.hypot(at.x-center.x,at.y-center.z)<=span*.46;});
      if(proxy){figure=proxy;const cells=frame.presentation.figures?.[proxy.id]?.occupiedCells??[proxy.position];if(!cells.some(c=>boardCellKey(c)===boardCellKey(cell))){const nearest=[...cells].sort((a,b)=>Math.hypot(a.x+.5-at.x,a.y+.5-at.y)-Math.hypot(b.x+.5-at.x,b.y+.5-at.y))[0];cell.x=nearest.x;cell.y=nearest.y;}}
    }
    const outside=()=>cell.x<0||cell.y<0||cell.x>=board.width||cell.y>=board.height;
    if(door&&outside()){cell.x=door.a.x;cell.y=door.a.y;}if(outside())return null;
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
    const corners=[{x:focusedCell.x,y:focusedCell.y},{x:focusedCell.x+1,y:focusedCell.y},{x:focusedCell.x,y:focusedCell.y+1},{x:focusedCell.x+1,y:focusedCell.y+1}].map(cameraControl.screen);
    focus.style.left=`${p.x}px`;focus.style.top=`${p.y}px`;focus.style.width=`${Math.max(...corners.map(c=>c.x))-Math.min(...corners.map(c=>c.x))||s}px`;focus.style.height=`${Math.max(...corners.map(c=>c.y))-Math.min(...corners.map(c=>c.y))||s}px`;
    const figure=frame.state.physical.figures.find(f=>(frame.presentation.figures?.[f.id]?.occupiedCells??[f.position]).some(c=>boardCellKey(c)===boardCellKey(focusedCell))),hit={cell:focusedCell,unitId:figure?.id};
    const choices=candidates(hit),machine=frame.presentation.machines?.find(m=>boardCellKey(m.position)===boardCellKey(focusedCell));
    focus.setAttribute('aria-label',[`Cell ${focusedCell.x},${focusedCell.y}`,figure?labelNodes.get(figure.id)?.title:null,machine?`${machine.name} · ${machine.ownerSide??'Public'} · ${(machine.resourceStorage??[]).map(r=>`${r.name} ${r.quantity}/${r.capacity}`).join(', ')}`:null,...choices.map(c=>c.boardVisual?.preview??c.displayLabel??c.label)].filter(Boolean).join(' · '));
  }
  function bind(node,type,handler,options={}){node.addEventListener(type,handler,{...options,signal:abort.signal});}
  bind(resetSample,'click',()=>{samples.length=renderTimes.length=unitRenderTimes.length=unitIntervals.length=cameraRenderTimes.length=cameraIntervals.length=reconcileTimes.length=0;lastRender=lastUnitFrame=lastCameraFrame=0;invalidate();});
  bind(canvas,'pointerdown',event=>{if(![0,1].includes(event.button))return;const p=local(event);pointers.set(event.pointerId,p);canvas.setPointerCapture(event.pointerId);pointerPosition=p;blockClick=false;
    if(pointers.size===2){const[a,b]=[...pointers.values()];pinch={distance:Math.hypot(a.x-b.x,a.y-b.y),mid:{x:(a.x+b.x)/2,y:(a.y+b.y)/2}};drag=null;blockClick=true;return;}
    drag={id:event.pointerId,start:p,pose:cameraControl.current(),threshold:event.pointerType==='touch'?10:6,moved:event.button===1||space};
  });
  bind(canvas,'pointermove',event=>{const p=local(event);pointerPosition=p;if(pointers.has(event.pointerId))pointers.set(event.pointerId,p);
    if(pointers.size===2&&pinch){const[a,b]=[...pointers.values()],distance=Math.hypot(a.x-b.x,a.y-b.y),mid={x:(a.x+b.x)/2,y:(a.y+b.y)/2};cameraControl.zoom(distance/Math.max(1,pinch.distance),mid);cameraControl.pan(pinch.mid,mid);pinch={distance,mid};blockClick=true;event.preventDefault();return;}
    if(drag?.id===event.pointerId&&drag.pose){const dx=p.x-drag.start.x,dy=p.y-drag.start.y;if(drag.moved||Math.hypot(dx,dy)>drag.threshold){drag.moved=true;blockClick=true;hover(null);cameraControl.pan(drag.start,p,drag.pose);event.preventDefault();}}
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
  if(controls.cutaway)bind(controls.cutaway,'change',invalidate);
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
      if(stable)stable.visible=false;transientId=event.unitId;const source=token(a,segments[0][0],heights.movement);
      if(data.explored&&(presentation==='tabletop'||cameraControl.current().tilt)){
        clipPool=createMovementClipPool(source,data.explored,THREE,lighting.clippedVariants);transient=clipPool.root;transient.userData.span=a.cellSpan??1;clipPool.update(source.position.x,source.position.z,a.cellSpan??1);
      }else transient=source;
      groups.movement.add(transient);
      try{for(const segment of segments){if(!transient||playback.skipping()||generation!==epoch)break;const start=cellCenter(segment[0],a.cellSpan);transient.position.x=start.x;transient.position.z=start.z;cameraControl.movementStep(segment[0]);invalidate();
        for(const anchor of segment.slice(1)){if(!transient||playback.skipping()||generation!==epoch)break;const to=cellCenter(anchor,a.cellSpan);motion={object:transient,from:{x:transient.position.x,z:transient.position.z},to,start:performance.now(),duration:220};cameraControl.movementStep(anchor);invalidate();await playback.pause(250);if(motion){motion.object.position.x=to.x;motion.object.position.z=to.z;motion=null;}}
      }}finally{clipPool?.dispose();clipPool=null;removeRecord({object:source});if(transient)transient.removeFromParent();transient=null;transientId=null;motion=null;if(stable)stable.visible=true;invalidate();}
      return;
    }
    const id=event.targetId??event.unitId??occurrence?.unitId,node=nodes.get(`unit:${id}`)?.object;
    const duration=event.role==='Defeat'?320:['DoorAttempt','DoorOpened','DoorClosed'].includes(event.role)?300:250;
    if(node){node.scale.setScalar(event.role==='Defeat'?.6:1.06);invalidate();}
    try{await playback.pause(duration);}finally{if(node)node.scale.setScalar(1);invalidate();}
  }
  function settle(){if(motion){motion.object.position.x=motion.to.x;motion.object.position.z=motion.to.z;motion=null;}
    for(const id of arrivals.keys())nodes.get(`unit:${id}`)?.object.scale.setScalar(1);arrivals.clear();
    for(const [id,effect]of flips){const body=unitRoots.get(id)?.getObjectByName('cardboard-body');if(body)physical.posture(body,effect.to);}flips.clear();
    for(const [key,effect]of doorMotions){const leaf=nodes.get(key)?.object.getObjectByName('door-leaf');if(leaf){leaf.visible=!effect.open;leaf.scale.y=1;leaf.position.y=0;}}doorMotions.clear();
    for(const key of pieceEffects.keys())nodes.get(key)?.object.scale.setScalar(1);pieceEffects.clear();postureChanges.clear();
    if(representationTransition)for(const {object,scale}of representationTransition.parts)object.scale.copy(scale);
    representationTransition=null;for(const group of Object.values(groups))group.scale.y=1;
    clipPool?.dispose();clipPool=null;if(transient){transient.removeFromParent();transient=null;transientId=null;for(const root of unitRoots.values())root.visible=true;}
    cameraControl.settle();invalidate();}
  function cancel(){epoch++;settle();motion=null;for(const record of nodes.values())record.object.visible=true;cameraControl.cancel();}
  function dispose(){if(disposed)return;setActive(false);disposed=true;abort.abort();observer.disconnect();cameraControl.dispose();lighting.dispose();for(const record of nodes.values())removeRecord(record);nodes.clear();unitRoots.clear();labelNodes.clear();for(const geometry of Object.values(geometries))geometry.dispose();for(const m of materials.values())m.dispose();for(const entry of textures.values())entry.texture?.dispose();for(const m of glyphs.values()){m.map.dispose();m.dispose();}renderer.dispose();labels.remove();focus.remove();canvas.remove();diagnostics.remove();controls.root.remove();}
  return{host,lighting,camera:cameraControl,renderFrame,showInteraction,present,project:cameraControl.screen,settle,cancel,setActive,dispose,pick,setPresentation,
    afterOccurrence:occurrence=>{
      if(!active||!animate())return;
      if(presentation==='tabletop'&&occurrence?.kind==='PostureChanged'){
        const effect=postureChanges.get(occurrence.unitId),body=unitRoots.get(occurrence.unitId)?.getObjectByName('cardboard-body');
        if(effect&&body){physical.posture(body,effect.from);flips.set(occurrence.unitId,{...effect,start:performance.now()});}postureChanges.delete(occurrence.unitId);
      }else if(occurrence?.kind==='UnitCreated'||presentation==='flat'&&occurrence?.kind==='PostureChanged')arrivals.set(occurrence.unitId,{kind:occurrence.kind,start:performance.now()});
      invalidate();
    },
    inspect:()=>({active,lost,presentation,lighting:lighting.inspect(),renderCount,flips:flips.size,doorMotions:doorMotions.size,clippedCopies:clipPool?.copies.filter(c=>c.body.visible).length??0,initialRenderMs:readyAt?readyAt-started:null,calls:renderer.info.render.calls,triangles:renderer.info.render.triangles,memory:{...renderer.info.memory},textureStates:Object.fromEntries([...textures].map(([url,e])=>[url,e.state])),frameIntervals:[...samples],renderTimes:[...renderTimes],camera:cameraControl.inspect(),units:labelNodes.size,transient:!!transient})};
}
