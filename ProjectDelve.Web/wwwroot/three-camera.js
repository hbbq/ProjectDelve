import { cameraPreferences, cameraBounds, cameraUnion, cameraSpanScale, cameraClampPose, cameraFull, cameraProject, cameraUnproject, cameraAutoFit, cameraEnsureVisible, cameraObjectiveIndicator, cameraIndicatorSlot } from './camera-geometry.js';
import { cameraObservedFrame, cameraFootprint, cameraEventIntent, cameraAnchorBounds, cameraHumanSides, cameraOwnHuman, cameraAllowsFollow } from './camera-observation.js';
import { interpolatePose } from './playtest-viewport.js';

// Cell-space policy and time-based pose. No CSS transforms or separate RAF loop.
export function createThreeCamera({ controls, invalidate, animate, onChange = () => {}, environment = globalThis }) {
  let saved; try { saved = JSON.parse(environment.localStorage?.getItem('delve.three.camera.v1') ?? 'null'); } catch { /* optional */ }
  let preferences = cameraPreferences(saved ?? {}), frame, metadata = {}, side, sideChoice, session;
  let size = { width: 1, height: 1 }, pose, target, transition, suspended = false, activation = 0, manualActivation;
  let decision, activationKey, fittedSpan, moveSignature, movement, overflow = false;
  const subscriptions = new AbortController(), indicators = new Map();
  const reduced = () => environment.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
  const persist = () => { try { environment.localStorage?.setItem('delve.three.camera.v1', JSON.stringify(preferences)); } catch { /* optional */ } };
  const assignments = () => metadata.assignments?.length ? metadata.assignments : Object.entries(metadata.controllers ?? {}).map(([sideId, controller]) => ({ sideId, controller }));
  const own = id => cameraOwnHuman(frame, id, side, assignments());
  const automatic = () => !suspended && preferences.follow !== 'off' && preferences.mode !== 'full';
  function renderControls() {
    for (const [id, value] of Object.entries({ mode: preferences.mode, span: [10,15,20,25].includes(preferences.fixedSpan) ? preferences.fixedSpan : 'custom', follow: preferences.follow, min: preferences.minSpan, max: preferences.maxSpan, margin: preferences.margin })) controls[id].value = String(value);
    controls.span.hidden = preferences.mode !== 'fixed';
    controls.resume.hidden = !suspended || preferences.follow === 'off' || preferences.mode === 'full';
    controls.status.textContent = `${suspended ? 'Manual · ' : ''}${pose ? (Math.max(size.width, size.height) / pose.scale).toFixed(1) : '—'} Cells max${overflow && preferences.mode === 'auto' ? ' · some Moves offscreen' : ''}`;
  }
  function apply(next, smooth = true) {
    if (!frame) return;
    next = cameraClampPose(next, frame.board, size);
    if (target && Math.abs(target.x-next.x)+Math.abs(target.y-next.y)+Math.abs(target.scale-next.scale) < .0001) return;
    target = next;
    transition = pose && smooth && animate() && !reduced() ? { from: { ...pose }, to: next, start: environment.performance.now(), duration: 260 } : null;
    if (!transition) pose = { ...next };
    invalidate(); renderControls();
  }
  function follow(intent, smooth = true) {
    if (!pose || !cameraAllowsFollow(preferences, suspended, intent, side)) return;
    apply(cameraEnsureVisible(pose, intent.bounds, frame.board, size, intent.preferred), smooth);
  }
  function fit(force = false) {
    if (!decision?.unitId || !own(decision.unitId)) return false;
    const moves = (decision.candidates ?? []).filter(c => c.boardVisual?.kind === 'move').map(c => c.placementCells ?? []);
    const signature = JSON.stringify(moves); if (!force && signature === moveSignature) return true;
    const result = cameraAutoFit({ active: cameraFootprint(frame, decision.unitId), moves,
      friends: frame.units.filter(u => u.sideId === side && u.id !== decision.unitId).map(u => cameraFootprint(frame,u.id)), board: frame.board, viewport: size, preferences });
    if (!result) return false;
    moveSignature = signature; overflow = result.overflow;
    if (!force && fittedSpan != null && result.span <= fittedSpan) follow({ kind: 'frame', bounds: result.core, sides: [side] });
    else { fittedSpan = result.span; apply(result.pose); }
    return true;
  }
  function useful(smooth = true) {
    if (!frame) return;
    if (preferences.mode === 'full') return apply(cameraFull(frame.board,size), smooth);
    if (preferences.mode === 'auto' && fit(true)) return;
    const party = frame.units.filter(u => (!side || u.sideId === side) && cameraFootprint(frame,u.id).length);
    let bounds = party.reduce((b,u) => cameraUnion(b,cameraBounds(cameraFootprint(frame,u.id))),null);
    const scale = cameraSpanScale(size,preferences.mode === 'fixed' ? preferences.fixedSpan : preferences.maxSpan);
    if (bounds && (bounds.w*scale > size.width || bounds.h*scale > size.height)) bounds = cameraBounds(cameraFootprint(frame,(party.find(u=>own(u.id)) ?? party[0]).id));
    apply({ x: bounds ? bounds.x+bounds.w/2 : frame.board.width/2, y: bounds ? bounds.y+bounds.h/2 : frame.board.height/2, scale },smooth);
  }
  function setFrame(state,presentation,meta) {
    const replacement = session !== (meta.sessionId ?? meta.scenarioId);
    if (replacement) { session = meta.sessionId ?? meta.scenarioId; pose = target = transition = null; suspended = false; decision = movement = null; activationKey = null; activation = 0; manualActivation = null; fittedSpan = moveSignature = null; }
    frame = cameraObservedFrame(state,presentation); metadata = meta;
    const humans = cameraHumanSides(assignments()), sides = [...new Set(frame.units.map(u=>u.sideId))];
    side = frame.side ?? (sides.includes(sideChoice) ? sideChoice : humans[0] ?? null);
    controls['side-label'].hidden = !!frame.side || humans.length === 1;
    const choices = frame.side ? [frame.side] : humans.length ? humans : ['',...sides];
    if (controls.side.dataset.options !== JSON.stringify(choices)) {
      controls.side.replaceChildren(...choices.map(id=> { const o=document.createElement('option'); o.value=id; o.textContent=id || 'Spectator'; return o; })); controls.side.dataset.options=JSON.stringify(choices);
    }
    controls.side.value = side ?? '';
    if (!pose) useful(false);
    renderControls(); invalidate();
  }
  function stable(next) {
    decision = next;
    if (!next?.unitId || !own(next.unitId)) return;
    const key = `${metadata.round}:${next.unitId}:${activation}`;
    if (key !== activationKey) { activationKey = key; fittedSpan = moveSignature = null; if (manualActivation !== activation && preferences.follow !== 'off') suspended = false; }
    if (!automatic()) return;
    if (preferences.mode === 'auto') fit();
    else follow({ kind: 'frame', bounds: cameraBounds(cameraFootprint(frame,next.unitId)), sides:[side] });
  }
  function occurrence(occurrence,event,instant=false) {
    if (!frame) return;
    movement = cameraEventIntent(occurrence,event,frame);
    if (occurrence?.kind === 'ActivationStarted' && own(occurrence.unitId)) { activation++; activationKey = null; if (preferences.follow !== 'off') suspended=false; if(preferences.mode==='auto') return; }
    if (movement?.kind === 'movement') { if(instant) finishMovement(); return; }
    follow(movement,!instant);
  }
  function movementStep(anchor) {
    if (movement?.kind === 'movement' && movement.segments.some(s=>s.some(c=>c.x===anchor.x && c.y===anchor.y))) follow({ ...movement,bounds:cameraAnchorBounds(anchor,movement.span) });
  }
  function finishMovement() {
    if (movement?.kind === 'movement') { const last=movement.segments.at(-1).at(-1); follow({...movement,bounds:cameraAnchorBounds(last,movement.span)},false); }
    settle();
  }
  function settle() { if(target) pose={...target}; transition=null; invalidate(); }
  function manual(next,smooth=false) { if(!frame) return; if(preferences.mode==='full') preferences.mode='fixed'; suspended=true; manualActivation=activation; apply(next,smooth); persist(); }
  function zoom(factor,point={x:size.width/2,y:size.height/2},smooth=false) {
    if(!pose) return;
    const anchor=cameraUnproject(point,pose,size), span=Math.max(5,Math.min(50,Math.max(size.width,size.height)/(pose.scale*factor))),scale=cameraSpanScale(size,span);
    if(preferences.mode==='full') preferences.mode='fixed'; if(preferences.mode==='fixed') preferences.fixedSpan=span;
    manual({x:anchor.x-(point.x-size.width/2)/scale,y:anchor.y-(point.y-size.height/2)/scale,scale},smooth);
  }
  function resize(width,height) {
    if(!width||!height||width===size.width&&height===size.height) return;
    const span=pose ? Math.max(size.width,size.height)/pose.scale : preferences.fixedSpan;
    size={width,height}; transition=null; target=null;
    if(!frame) return;
    if(preferences.mode==='full'||preferences.mode==='auto'&&automatic()) useful(false);
    else apply({...pose,scale:cameraSpanScale(size,preferences.mode==='fixed'?preferences.fixedSpan:span)},false);
  }
  function tick(time) {
    if(transition) { const fraction=(time-transition.start)/transition.duration; pose=interpolatePose(transition.from,transition.to,fraction); if(fraction>=1) transition=null; }
    renderControls(); onChange(); return !!transition;
  }
  function renderIndicators(blocked=[]) {
    const retained=new Set(),placed=[]; if(!frame||!pose) return;
    for(const [i,objective] of frame.objectives.entries()) {
      if(!objective.area) continue;
      let location=cameraObjectiveIndicator(objective.area,pose,size,26); if(!location) continue;
      const key=`${objective.sideId}:${i}`;retained.add(key);
      let node=indicators.get(key);
      if(!node) {node=document.createElement('button');node.type='button';node.className='camera-objective';node.textContent='➜';node.addEventListener('click',()=>manual({...pose,x:node.area.x+node.area.w/2,y:node.area.y+node.area.h/2},true));controls.objectives.append(node);indicators.set(key,node);}
      node.area=objective.area; location=cameraIndicatorSlot(location,placed,size,blocked);placed.push(location);
      node.style.left=`${location.x}px`;node.style.top=`${location.y}px`;node.style.setProperty('--direction',`${location.angle}rad`);node.style.rotate=`${location.angle}rad`;
      node.title=`Pan to ${objective.sideId} Reach Area`;node.setAttribute('aria-label',node.title);
    }
    for(const [key,node] of indicators) if(!retained.has(key)){node.remove();indicators.delete(key);}
  }
  function resume(){suspended=false;moveSignature=fittedSpan=null;useful();renderControls();}
  function setMode(mode){preferences.mode=mode;suspended=false;moveSignature=fittedSpan=null;persist();useful();}
  const bind=(id,type,fn)=>controls[id].addEventListener(type,fn,{signal:subscriptions.signal});
  bind('mode','change',()=>setMode(controls.mode.value));
  bind('span','change',()=>{preferences.fixedSpan=Number(controls.span.value);resume();persist();});
  bind('follow','change',()=>{preferences.follow=controls.follow.value;persist();if(preferences.follow!=='off')resume();else settle();});
  for(const [id,key] of [['min','minSpan'],['max','maxSpan'],['margin','margin']]) bind(id,'change',()=>{preferences=cameraPreferences({...preferences,[key]:controls[id].value});persist();resume();});
  bind('side','change',()=>{sideChoice=controls.side.value||null;side=sideChoice;activationKey=null;resume();});
  bind('reset','click',resume);bind('resume','click',resume);bind('in','click',()=>zoom(1.2,undefined,true));bind('out','click',()=>zoom(1/1.2,undefined,true));
  const media=environment.matchMedia?.('(prefers-reduced-motion: reduce)');media?.addEventListener?.('change',()=>{if(reduced())settle();},{signal:subscriptions.signal});
  renderControls();
  return {setFrame,stable,occurrence,movementStep,finishMovement,settle,cancel:()=>{movement=null;settle();},resize,tick,manual,zoom,resume,setMode,renderIndicators,
    screen:point=>pose?cameraProject(point,pose,size):point,
    current:()=>pose&&({...pose}), get size(){return size;},
    dispose:()=>{subscriptions.abort();for(const node of indicators.values())node.remove();},
    inspect:()=>({preferences:{...preferences},current:pose&&({...pose}),target:target&&({...target}),suspended,side,fittedSpan,overflow,transitionActive:!!transition})};
}
