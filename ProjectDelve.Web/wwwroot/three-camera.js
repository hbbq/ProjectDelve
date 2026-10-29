import { cameraPreferences, cameraBounds, cameraUnion, cameraObjectiveIndicator, cameraIndicatorSlot } from './camera-geometry.js';
import { cameraObservedFrame, cameraFootprint, cameraEventIntent, cameraAnchorBounds, cameraHumanSides, cameraOwnHuman, cameraAllowsFollow } from './camera-observation.js';
import { interpolatePose } from './playtest-viewport.js';
import { createCameraDirector } from './three-camera-director.js';
import { interpolateCameraWorld } from './three-camera-geometry.js';
import { sideHue } from './side-colors.js';
import { tabletopOrientation, threeProject, threeUnproject, threeSpanScale, threeClampPose, threeFull, threeAutoFit, threeEnsureVisible, threeScreenBounds, threeFitRect, interpolateOrientation, lensFov } from './three-camera-geometry.js';

// Cell-space policy and time-based pose. No CSS transforms or separate RAF loop.
export function createThreeCamera({ controls, invalidate, animate, onChange = () => {}, environment = globalThis, visualHeight = 1.5, visualOverhang = 0 }) {
  let saved; try { saved = JSON.parse(environment.localStorage?.getItem('delve.three.camera.v1') ?? 'null'); } catch { /* optional */ }
  let preferences = cameraPreferences(saved ?? {}), frame, metadata = {}, side, sideChoice=saved?.focusSide, session;
  let size = { width: 1, height: 1 }, pose, target, transition, suspended = false, activation = 0, manualActivation;
  let decision, activationKey, fittedSpan, moveSignature, movement, overflow = false;
  let orientation = {tilt:0,yaw:0}, physical = false;
  let projection=saved?.projection==='perspective'?'perspective':'orthographic',fov=lensFov(saved?.fov);
  const optics=()=>({projection:physical?projection:'orthographic',projectionMix:physical&&projection==='perspective'?1:0,fov,allowGutters:physical});
  const geometry=()=>({...orientation,...optics(),visualHeight:physical?visualHeight:0,visualOverhang:physical?visualOverhang:0});
  let fitBounds=null,activeBounds=null;
  const director=createCameraDirector();
  function direct(smooth=true){
    if(!physical||!automatic()||!pose)return;
    const result=director.compose(frame,target??pose,size,preferences,side);
    if(result){overflow=result.overflow;apply(result.pose,smooth,result.context);}
  }
  const spanScale = span => threeSpanScale(size,span,geometry());
  const subscriptions = new AbortController(), indicators = new Map();
  const reduced = () => environment.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
  const persist = () => { try { environment.localStorage?.setItem('delve.three.camera.v1', JSON.stringify({...preferences,projection,fov,focusSide:sideChoice})); } catch { /* optional */ } };
  const assignments = () => metadata.assignments?.length ? metadata.assignments : Object.entries(metadata.controllers ?? {}).map(([sideId, controller]) => ({ sideId, controller }));
  const own = id => cameraOwnHuman(frame, id, side, assignments());
  const automatic = () => !suspended && preferences.follow !== 'off' && preferences.mode !== 'full';
  const directorSide = () => preferences.follow==='all'?null:side;
  function renderControls() {
    if(controls['side-label'].firstChild?.nodeType===3)controls['side-label'].firstChild.textContent=physical?'Focus ':'Side ';
    controls.side.setAttribute?.('aria-label',physical?'Camera focus preference':'Camera Side');
    for(const option of controls.follow.options??[])if(option.value==='own')option.textContent=physical?'Focus priority':'Own Side';
    for (const [id, value] of Object.entries({ mode: preferences.mode, span: [10,15,20,25].includes(preferences.fixedSpan) ? preferences.fixedSpan : 'custom', follow: preferences.follow, min: preferences.minSpan, max: preferences.maxSpan, margin: preferences.margin })) controls[id].value = String(value);
    controls.span.hidden = preferences.mode !== 'fixed';
    controls.resume.hidden = !suspended || preferences.follow === 'off' || preferences.mode === 'full';
    if(controls.tilt) { controls.tilt.value=String(Math.round(orientation.tilt));controls.tilt.disabled=!physical; }
    if(controls.yaw) { controls.yaw.value=String(Math.round(((orientation.yaw%360)+360)%360));controls.yaw.disabled=!physical; }
    for(const key of ['tilt','yaw','cutaway'])if(controls[key]?.parentElement)controls[key].parentElement.hidden=!physical;
    for(const key of ['turn-left','turn-right','overhead','angle'])if(controls[key])controls[key].hidden=!physical;
    if(controls.projection){controls.projection.value=projection;controls.projection.parentElement.hidden=!physical;}
    if(controls.fov){controls.fov.value=String(fov);controls.fov.disabled=projection!=='perspective';controls.fov.parentElement.hidden=!physical;}
    controls.status.textContent = `${suspended ? 'Manual · ' : ''}${pose ? (threeSpanScale(size,1,pose) / pose.scale).toFixed(1) : '—'} Cells ${pose?.projectionMix>0?'focus span':'max'}${overflow && preferences.mode === 'auto' ? ' · some Moves offscreen' : ''}`;
    const diagnosis=physical?director.inspect().diagnostic:null;
    controls.status.title=diagnosis?`${diagnosis.context}: ${diagnosis.reason}; ${diagnosis.adjustment}; translation ${diagnosis.translation}`:'';
  }
  function apply(next, smooth = true, context = null) {
    if (!frame) return;
    const directed=!!context;
    next = {...orientation,...next,...optics(),visualHeight:physical?visualHeight:0,visualOverhang:physical?visualOverhang:0};
    if(!directed)next=threeClampPose(next,frame.board,size);
    if(directed)orientation={tilt:next.tilt,yaw:next.yaw};
    if (target && ['x','y','scale','tilt','yaw','projectionMix','fov','visualHeight','visualOverhang'].every(key=>Math.abs(target[key]-next[key])<.0001)) return;
    target = next;
    const orbit=pose?Math.abs(((next.yaw-pose.yaw+540)%360)-180):0;
    const duration=directed?(context==='Movement'?300:Math.min(800,480+orbit*4)):260;
    transition = pose && smooth && animate() && !reduced() ? { from: { ...pose }, to: next, start: environment.performance.now(), duration, directed } : null;
    if (!transition) pose = { ...next };
    invalidate(); renderControls();
  }
  function follow(intent, smooth = true) {
    if (!pose || !cameraAllowsFollow(preferences, suspended, intent, side)) return;
    apply(threeEnsureVisible({...pose,...orientation}, intent.bounds, frame.board, size, intent.preferred), smooth);
  }
  function fit(force = false, smooth = true) {
    if (!decision?.unitId || !own(decision.unitId)) return false;
    const moves = (decision.candidates ?? []).filter(c => c.boardVisual?.kind === 'move').map(c => c.placementCells ?? []);
    const signature = JSON.stringify(moves); if (!force && signature === moveSignature) return true;
    const result = threeAutoFit({ active: cameraFootprint(frame, decision.unitId), moves,
      friends: frame.units.filter(u => u.sideId === side && u.id !== decision.unitId).map(u => cameraFootprint(frame,u.id)), board: frame.board, viewport: size, preferences, orientation:geometry() });
    if (!result) return false;
    activeBounds=cameraBounds(cameraFootprint(frame,decision.unitId));fitBounds=result.overflow?activeBounds:result.bounds;
    moveSignature = signature; overflow = result.overflow;
    if (!force && fittedSpan != null && result.span <= fittedSpan) follow({ kind: 'frame', bounds: result.core, sides: [side] });
    else { fittedSpan = result.span; apply(result.pose,smooth); }
    return true;
  }
  function useful(smooth = true) {
    if (!frame) return;
    if (preferences.mode === 'full') return apply(threeFull(frame.board,size,geometry(),physical), smooth);
    if(physical&&pose){
      // Explicit Resume/mode/preferences changes may restore a useful scale.
      // Ordinary stable decisions and playback never call this reset path.
      const current=target??pose,currentSpan=threeSpanScale(size,1,current)/current.scale;
      const span=preferences.mode==='fixed'?preferences.fixedSpan:Math.max(preferences.minSpan,Math.min(preferences.maxSpan,currentSpan));
      apply({...current,scale:threeSpanScale(size,span,current)},smooth);
      director.stable(frame,decision,directorSide());direct(smooth);return;
    }
    if (preferences.mode === 'auto' && fit(true,smooth)) return;
    const party = frame.units.filter(u => (!side || u.sideId === side) && cameraFootprint(frame,u.id).length);
    let bounds = party.reduce((b,u) => cameraUnion(b,cameraBounds(cameraFootprint(frame,u.id))),null);
    const scale = spanScale(preferences.mode === 'fixed' ? preferences.fixedSpan : preferences.maxSpan);
    if (bounds && (bounds.w*scale > size.width || bounds.h*scale > size.height)) bounds = cameraBounds(cameraFootprint(frame,(party.find(u=>own(u.id)) ?? party[0]).id));
    apply({ x: bounds ? bounds.x+bounds.w/2 : frame.board.width/2, y: bounds ? bounds.y+bounds.h/2 : frame.board.height/2, scale },smooth);
  }
  function setFrame(state,presentation,meta) {
    const replacement = session !== (meta.sessionId ?? meta.scenarioId);
    if(replacement)director.reset();
    if (replacement) { session = meta.sessionId ?? meta.scenarioId; pose = target = transition = null; suspended = false; decision = movement = null; activationKey = null; activation = 0; manualActivation = null; fittedSpan = moveSignature = fitBounds = activeBounds = null; }
    frame = cameraObservedFrame(state,presentation); metadata = meta;
    refreshSides();
    if (!pose) useful(false);
    renderControls(); invalidate();
  }
  function refreshSides(){
    const humans = cameraHumanSides(assignments()), sides = [...new Set(frame.units.map(u=>u.sideId))];
    const available=[...new Set([...sides,...assignments().map(a=>a.sideId)])];
    side = physical ? (sideChoice===null?null:available.includes(sideChoice)?sideChoice:frame.side??humans[0]??available[0]??null) : frame.side ?? (sides.includes(sideChoice) ? sideChoice : humans[0] ?? null);
    controls['side-label'].hidden = physical?false:!!frame.side || humans.length === 1;
    const choices = physical?['',...available]:frame.side ? [frame.side] : humans.length ? humans : ['',...sides];
    if (controls.side.dataset.options !== JSON.stringify(choices)) {
      controls.side.replaceChildren(...choices.map(id=> { const o=document.createElement('option'); o.value=id; o.textContent=id || 'Spectator'; return o; })); controls.side.dataset.options=JSON.stringify(choices);
    }
    controls.side.value = side ?? '';
  }
  function stable(next) {
    decision = next;
    if(physical){activeBounds=fitBounds=cameraBounds(cameraFootprint(frame,next?.unitId));director.stable(frame,next,directorSide());direct();return;}
    if (!next?.unitId || !own(next.unitId)) { fitBounds=activeBounds=null;return; }
    const key = `${metadata.round}:${next.unitId}:${activation}`;
    if (key !== activationKey) { activationKey = key; fittedSpan = moveSignature = null; if (manualActivation !== activation && preferences.follow !== 'off') suspended = false; }
    if (!automatic()) return;
    if (preferences.mode === 'auto') fit();
    else follow({ kind: 'frame', bounds: cameraBounds(cameraFootprint(frame,next.unitId)), sides:[side] });
  }
  function occurrence(occurrence,event,instant=false) {
    if (!frame) return;
    if(physical){
      activeBounds=fitBounds=null;
      if(occurrence?.kind==='ActivationStarted'&&side&&frame.units.some(u=>u.id===occurrence.unitId&&u.sideId===side)&&cameraFootprint(frame,occurrence.unitId).length){activation++;if(preferences.follow!=='off')suspended=false;}
      director.event(frame,occurrence,event);direct(!instant);
      if(instant){if(event.role==='Movement')finishMovement();else settle();}return;
    }
    movement = cameraEventIntent(occurrence,event,frame);
    if (occurrence?.kind === 'ActivationStarted' && own(occurrence.unitId)) { activation++; activationKey = null; if (preferences.follow !== 'off') suspended=false; if(preferences.mode==='auto') return; }
    if (movement?.kind === 'movement') { if(instant) finishMovement(); return; }
    follow(movement,!instant);
  }
  function movementStep(anchor) {
    if(physical){director.step(anchor,frame);direct();return;}
    if (movement?.kind === 'movement' && movement.segments.some(s=>s.some(c=>c.x===anchor.x && c.y===anchor.y))) follow({ ...movement,bounds:cameraAnchorBounds(anchor,movement.span) });
  }
  function finishMovement() {
    if(physical){const last=director.finish();if(last){director.step(last,frame);direct(false);}settle();return;}
    if (movement?.kind === 'movement') { const last=movement.segments.at(-1).at(-1); follow({...movement,bounds:cameraAnchorBounds(last,movement.span)},false); }
    settle();
  }
  function settle() { if(target) pose={...target}; transition=null; invalidate(); }
  function manual(next,smooth=false) {
    if(!frame)return;if(preferences.mode==='full')preferences.mode='fixed';
    orientation={tilt:next.tilt??orientation.tilt,yaw:next.yaw??orientation.yaw};suspended=true;manualActivation=activation;
    const lensChanging=pose&&(pose.fov!==optics().fov||pose.projectionMix!==optics().projectionMix);
    if(lensChanging){
      const span=threeSpanScale(size,1,next)/next.scale;
      if(!smooth){
        // Interrupt position/follow immediately; finish the requested lens from
        // its displayed value, without snapping the ground under the pointer.
        pose=threeClampPose({...next,fov:pose.fov,projectionMix:pose.projectionMix},frame.board,size);
        pose.scale=threeSpanScale(size,span,pose);target=null;
      }
      next={...next,...optics()};next.scale=threeSpanScale(size,span,next);smooth=true;
    }
    apply(next,smooth);persist();
  }
  function zoom(factor,point={x:size.width/2,y:size.height/2},smooth=false) {
    if(!pose) return;
    const anchor=threeUnproject(point,pose,size), span=Math.max(5,Math.min(50,threeSpanScale(size,1,pose)/(pose.scale*factor))),scale=threeSpanScale(size,span,pose);
    if(!anchor)return;
    if(preferences.mode==='full') preferences.mode='fixed'; if(preferences.mode==='fixed') preferences.fixedSpan=span;
    const offset=threeUnproject(point,{...pose,x:0,y:0,scale},size);
    if(!offset)return;
    manual({...pose,x:anchor.x-offset.x,y:anchor.y-offset.y,scale},smooth);
  }
  function resize(width,height) {
    if(!width||!height||width===size.width&&height===size.height) return;
    const previousSize=size;
    if(physical&&preferences.mode==='auto'&&transition&&previousSize.width>1&&previousSize.height>1&&animate()&&!reduced()){
      // Layout changes must not complete an Auto move at its destination. Keep
      // the displayed pose and remaining easing time in the new dimensions.
      const remap=p=>({...p,scale:threeSpanScale({width,height},threeSpanScale(previousSize,1,p)/p.scale,p)});
      const now=environment.performance.now(),remaining=Math.max(1,transition.duration-(now-transition.start));
      size={width,height};pose=remap(pose);target=remap(target);
      transition={...transition,from:{...pose},to:target,start:now,duration:remaining};
      // Preserve an ongoing Movement/Combat shot; only adjust its fit smoothly.
      direct();renderControls();invalidate();return;
    }
    const resizedPose=physical?(target??pose):pose;
    const span=resizedPose ? threeSpanScale(size,1,resizedPose)/resizedPose.scale : preferences.fixedSpan;
    size={width,height}; transition=null; target=null;
    if(!frame) return;
    if(physical&&preferences.mode!=='full'&&pose){
      apply({...resizedPose,scale:threeSpanScale(size,preferences.mode==='fixed'?preferences.fixedSpan:span,resizedPose)},false);
      if(automatic()){director.stable(frame,decision,directorSide());direct(false);}return;
    }
    if(preferences.mode==='full'||preferences.mode==='auto'&&automatic()) useful(false);
    else apply({...pose,scale:spanScale(preferences.mode==='fixed'?preferences.fixedSpan:span)},false);
  }
  function tick(time) {
    if(transition) { const fraction=(time-transition.start)/transition.duration,ease=1-(1-Math.max(0,Math.min(1,fraction)))**3;
      pose={...transition.to,...interpolatePose(transition.from,transition.to,fraction),...interpolateOrientation(transition.from,transition.to,fraction),fov:transition.from.fov+(transition.to.fov-transition.from.fov)*ease,projectionMix:transition.from.projectionMix+(transition.to.projectionMix-transition.from.projectionMix)*ease};
      if(preferences.mode!=='full'){
        const fromSpan=threeSpanScale(size,1,transition.from)/transition.from.scale,toSpan=threeSpanScale(size,1,transition.to)/transition.to.scale;
        pose.scale=threeSpanScale(size,Math.exp(Math.log(fromSpan)+(Math.log(toSpan)-Math.log(fromSpan))*ease),pose);
      }
      // A rectangle can be wider midway through rotation than at either endpoint.
      if(preferences.mode==='full')pose.scale=Math.min(pose.scale,threeFull(frame.board,size,pose,physical).scale);
      const orienting=transition.from.tilt!==transition.to.tilt||transition.from.yaw!==transition.to.yaw;
      if(orienting&&preferences.mode==='auto'&&automatic()&&fitBounds&&activeBounds){
        const scale=threeFitRect(fitBounds,size,pose).scale;
        pose.scale=Math.max(threeSpanScale(size,preferences.maxSpan,pose),Math.min(pose.scale,scale));
        pose=threeEnsureVisible(pose,activeBounds,frame.board,size);
      }
      if(preferences.mode==='auto'&&automatic()&&activeBounds)pose.scale=Math.min(pose.scale,threeFitRect(activeBounds,size,pose).scale);
      if(transition.directed&&transition.to.projectionMix===1&&transition.from.projectionMix===1)pose=interpolateCameraWorld(transition.from,transition.to,fraction,size);
      if(fraction>=1) { pose={...transition.to};transition=null; }
    }
    renderControls(); onChange(); return !!transition;
  }
  function renderIndicators(blocked=[]) {
    const retained=new Set(),placed=[]; if(!frame||!pose) return;
    for(const [i,objective] of frame.objectives.entries()) {
      if(!objective.area) continue;
      const screenArea=threeScreenBounds(objective.area,pose,size);
      let location=cameraObjectiveIndicator(screenArea,{x:size.width/2,y:size.height/2,scale:1},size,26); if(!location) continue;
      const key=`${objective.sideId}:${i}`;retained.add(key);
      let node=indicators.get(key);
      if(!node) {node=document.createElement('button');node.type='button';node.className='camera-objective';
        const bearing=document.createElement('span'),sideLabel=document.createElement('span');bearing.className='objective-bearing';bearing.textContent='➜';sideLabel.className='objective-side';node.append(bearing,sideLabel);
        node.addEventListener('click',()=>manual({...pose,x:node.area.x+node.area.w/2,y:node.area.y+node.area.h/2},true));controls.objectives.append(node);indicators.set(key,node);}
      node.area=objective.area; location=cameraIndicatorSlot(location,placed,size,blocked);placed.push(location);
      node.style.left=`${location.x}px`;node.style.top=`${location.y}px`;node.style.setProperty('--direction',`${location.angle}rad`);node.style.rotate='0rad';node.children[0].style.rotate=`${location.angle}rad`;
      node.style.setProperty('--objective-side',`hsl(${sideHue(objective.sideId)},65%,70%)`);node.children[1].textContent=objective.sideId;node.children[1].hidden=!physical;
      node.title=`Pan to ${objective.name??'Reach Area'} · Side ${objective.sideId}`;node.setAttribute('aria-label',node.title);
    }
    for(const [key,node] of indicators) if(!retained.has(key)){node.remove();indicators.delete(key);}
  }
  function resume(){suspended=false;moveSignature=fittedSpan=null;director.reset();useful();renderControls();}
  function setOrientation(next,smooth=true) {
    orientation={tilt:Math.max(0,Math.min(55,Number(next.tilt??orientation.tilt))),yaw:Number(next.yaw??orientation.yaw)};
    moveSignature=null;
    if(physical&&pose&&preferences.mode!=='full'){
      let desired={...pose,...geometry()};
      if(automatic()&&activeBounds)desired.scale=Math.min(desired.scale,threeFitRect(activeBounds,size,desired).scale);
      apply(desired,smooth);renderControls();return;
    }
    if(preferences.mode==='full'||preferences.mode==='auto'&&automatic())useful(smooth);
    else if(pose)apply({...pose,...orientation,scale:spanScale(preferences.fixedSpan)},smooth);
    renderControls();
  }
  function setPresentation(value,smooth=true){director.reset();physical=value==='tabletop';fitBounds=activeBounds=null;setOrientation(physical?tabletopOrientation:{tilt:0,yaw:0},smooth);if(frame)refreshSides();}
  function setProjection(value,smooth=true){projection=value==='perspective'?'perspective':'orthographic';changeLens(smooth);}
  function setFov(value,smooth=true){fov=lensFov(value);changeLens(smooth);}
  function changeLens(smooth){
    persist();moveSignature=null;
    const focus=target??pose;
    if(focus&&preferences.mode==='full')apply(threeFull(frame.board,size,{...geometry(),x:focus.x,y:focus.y},physical),smooth);
    else if(focus){
      const span=threeSpanScale(size,1,focus)/focus.scale;
      let next={...focus,...geometry()};next.scale=threeSpanScale(size,span,next);
      if(preferences.mode==='auto'&&automatic()&&fitBounds){
        const scale=threeFitRect(fitBounds,size,next).scale;
        next.scale=Math.max(spanScale(preferences.maxSpan),Math.min(next.scale,scale));
        if(activeBounds)next.scale=Math.min(next.scale,threeFitRect(activeBounds,size,next).scale);
      }
      apply(next,smooth);
    }
    renderControls();
  }
  function pan(from,to,start=pose){if(!start)return;const a=threeUnproject(from,start,size),b=threeUnproject(to,start,size);if(a&&b)manual({...start,x:start.x+a.x-b.x,y:start.y+a.y-b.y});}
  function setMode(mode){preferences.mode=mode;suspended=false;moveSignature=fittedSpan=null;persist();useful();renderControls();}
  const bind=(id,type,fn)=>controls[id].addEventListener(type,fn,{signal:subscriptions.signal});
  bind('mode','change',()=>setMode(controls.mode.value));
  bind('span','change',()=>{preferences.fixedSpan=Number(controls.span.value);resume();persist();});
  bind('follow','change',()=>{preferences.follow=controls.follow.value;persist();if(preferences.follow!=='off')resume();else settle();});
  for(const [id,key] of [['min','minSpan'],['max','maxSpan'],['margin','margin']]) bind(id,'change',()=>{preferences=cameraPreferences({...preferences,[key]:controls[id].value});persist();resume();});
  bind('side','change',()=>{sideChoice=controls.side.value||null;side=sideChoice;activationKey=null;persist();resume();});
  bind('reset','click',()=>{if(physical)manualOrientation(tabletopOrientation);else{orientation={tilt:0,yaw:0};resume();}});bind('resume','click',resume);bind('in','click',()=>zoom(1.2,undefined,true));bind('out','click',()=>zoom(1/1.2,undefined,true));
  const manualOrientation=next=>{if(pose)manual({...pose,...next},true);};
  for(const key of ['tilt','yaw'])if(controls[key])bind(key,'input',()=>manualOrientation({[key]:Number(controls[key].value)}));
  if(controls.projection)bind('projection','change',()=>{const value=controls.projection.value;if(pose)manual(pose);setProjection(value);});
  if(controls.fov)bind('fov','input',()=>{const value=controls.fov.value;if(pose)manual(pose);setFov(value);});
  for(const [key,change]of [['turn-left',()=>({yaw:orientation.yaw-45})],['turn-right',()=>({yaw:orientation.yaw+45})],['overhead',()=>({tilt:0})],['angle',()=>({tilt:38})]])
    if(controls[key])bind(key,'click',()=>{if(physical)manualOrientation(change());});
  const media=environment.matchMedia?.('(prefers-reduced-motion: reduce)');media?.addEventListener?.('change',()=>{if(reduced())settle();},{signal:subscriptions.signal});
  renderControls();
  return {setFrame,stable,occurrence,movementStep,finishMovement,settle,cancel:()=>{movement=null;director.reset();settle();},resize,tick,manual,pan,zoom,resume,setMode,setOrientation,setPresentation,setProjection,setFov,renderIndicators,
    screen:point=>pose?threeProject(point,pose,size):point,
    ground:point=>pose?threeUnproject(point,pose,size):point,
    groundAtHeight:(point,height)=>pose?threeUnproject(point,pose,size,height):point,
    current:()=>pose&&({...pose}), get size(){return size;},
    dispose:()=>{subscriptions.abort();for(const node of indicators.values())node.remove();},
    inspect:()=>({preferences:{...preferences},current:pose&&({...pose}),target:target&&({...target}),suspended,side,fittedSpan,overflow,transitionActive:!!transition,director:director.inspect()})};
}
