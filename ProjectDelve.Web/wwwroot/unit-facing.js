// Client-local decoration. Inputs are displayed observations, never engine state.
function hash(domain,seed,id){let h=2166136261;for(const c of JSON.stringify([domain,String(seed),id])){const n=c.charCodeAt(0);h=Math.imul(h^(n&255),16777619)>>>0;h=Math.imul(h^(n>>>8),16777619)>>>0;}return h;}
export function initialUnitFacing(seed,id){const offset=(hash('unit-facing-offset-v1',seed,id)/0xffffffff*2-1)*Math.PI/30;return {yaw:hash('unit-facing-initial-v1',seed,id)%8*Math.PI/4+offset,offset};}
export const shortestYaw=(from,to)=>Math.atan2(Math.sin(to-from),Math.cos(to-from));
export function observedMovementLegs(event,frame){
  const side=frame?.presentation.viewingSideId,explored=side?new Set((frame.state.exploredCells?.[side]??[]).map(c=>`${c.x},${c.y}`)):null;
  const span=event.movingFigure?.cellSpan??frame?.presentation.figures?.[event.unitId]?.cellSpan??1;
  const visible=p=>!explored||Array.from({length:span},(_,y)=>Array.from({length:span},(_,x)=>`${p.x+x},${p.y+y}`)).flat().some(k=>explored.has(k));
  const segments=event.movementSegments??(event.path?.length?[event.path]:[]);
  return segments.map(segment=>segment.slice(1).map((to,i)=>{const from=segment[i],dx=to.x-from.x,dz=to.y-from.y;return dx||dz?(visible(from)||visible(to)?Math.atan2(dx,dz):null):null;}));
}
export function createUnitFacing(){
  let session,seed;const records=new Map();
  function setFrame(frame){const identity=frame.metadata.sessionId??frame.metadata.scenarioId;if(identity!==session){records.clear();session=identity;}seed=frame.visualSeed;for(const f of frame.state.physical.figures)get(f.id);}
  function get(id){let r=records.get(id);if(!r){r=initialUnitFacing(seed,id);records.set(id,r);}return r;}
  function observe(event,occurrence,frame){
    if(!frame)return null;setFrame(frame);
    if(event.role==='Movement'){
      const legs=observedMovementLegs(event,frame).flat().filter(y=>y!=null);if(!legs.length||!event.unitId)return null;
      const r=get(event.unitId);r.yaw=legs.at(-1)+r.offset;return {id:event.unitId,yaw:r.yaw};
    }
    const figures=frame.state.physical.figures,geometry=frame.presentation.figures??{};
    const center=id=>{const f=figures.find(f=>f.id===id);if(!f)return null;const s=geometry[id]?.cellSpan??1;return {x:f.position.x+s/2,y:f.position.y+s/2};};
    const knownCell=c=>c&&(!frame.presentation.viewingSideId||(frame.state.exploredCells?.[frame.presentation.viewingSideId]??[]).some(p=>p.x===c.x&&p.y===c.y));
    let id,target;
    if(occurrence?.kind==='AttackStarted'){
      const a=occurrence.attackContext;if(!a)return null;id=a.attackerId;
      target=knownCell(a.selectedCell)?{x:a.selectedCell.x+.5,y:a.selectedCell.y+.5}:(a.targets??[]).map(t=>center(t.targetId)).find(Boolean);
    }else if(occurrence?.kind==='ActionUsed'||['DoorAttempt','DoorOpened','DoorClosed'].includes(event.role)){
      id=occurrence?.unitId??event.unitId;
      const door=occurrence?.door??event.door;
      if(door&&(knownCell(door.a)||knownCell(door.b)))target={x:(door.a.x+door.b.x)/2+.5,y:(door.a.y+door.b.y)/2+.5};
      else if(knownCell(occurrence?.cell))target={x:occurrence.cell.x+.5,y:occurrence.cell.y+.5};
      else if(occurrence?.targetId)target=center(occurrence.targetId);
      else if(occurrence?.machineId){const m=frame.presentation.machines?.find(m=>m.id===occurrence.machineId);if(m)target={x:m.position.x+.5,y:m.position.y+.5};}
    }
    const from=center(id);if(!from||!target||from.x===target.x&&from.y===target.y)return null;
    const r=get(id);r.yaw=Math.atan2(target.x-from.x,target.y-from.y);return {id,yaw:r.yaw};
  }
  return {setFrame,get,observe,inspect:()=>Object.fromEntries([...records].map(([id,r])=>[id,{...r}])),clear(){records.clear();session=undefined;}};
}
