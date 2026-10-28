import { cameraBounds, cameraUnion, cameraMargin } from './camera-geometry.js';
import { cameraFootprint, cameraEventIntent, cameraAnchorBounds } from './camera-observation.js';
import { threeScreenBounds, threeFitRect, threeSpanScale, threeLookAt, threeCameraPosition, threeProject } from './three-camera-geometry.js';

const union = values => values.reduce(cameraUnion,null);
const center = b => ({x:b.x+b.w/2,y:b.y+b.h/2});
const heading = (a,b) => Math.abs(((a-b+540)%360)-180);
const inside = (b,v,m) => !b.behind && b.x>=m && b.y>=m && b.x+b.w<=v.width-m && b.y+b.h<=v.height-m;
const safeTilt = p => p.tilt>=20 && p.tilt<=60; // 70–30 degrees DOWN from horizontal.
const distance = (a,b) => Math.hypot(center(a).x-center(b).x,center(a).y-center(b).y);
const unique = bounds => [...new Map(bounds.map(b=>[`${b.x},${b.y},${b.w},${b.h}`,b])).values()];
export const directorReadability = viewport => ({minimum:Math.max(40,Math.min(64,Math.min(viewport.width,viewport.height)*.09)),desired:Math.max(76,Math.min(110,Math.min(viewport.width,viewport.height)*.15))});
export function directorSubjectSize(bounds,pose,viewport){
  const envelope=threeScreenBounds(bounds,pose,viewport,pose.visualHeight??0);
  // A catalog-wide safety overhang must not count as visible figure detail.
  const footprint=threeScreenBounds(bounds,{...pose,visualOverhang:0},viewport,0);
  return envelope.behind?0:Math.min(envelope.w,envelope.h,footprint.w);
}

// A bearing is a presentation hint, not a predicted path or a legality query.
// Normalize each hint so a distant Objective cannot drag the focus across the map.
function interestDirection(primary,hints){
  const origin=center(union(primary));let x=0,y=0,total=0;
  for(const {point,weight} of hints){const dx=point.x-origin.x,dy=point.y-origin.y,d=Math.hypot(dx,dy);if(d<.25)continue;x+=dx/d*weight;y+=dy/d*weight;total+=weight;}
  const length=Math.hypot(x,y);return total&&length/total>.18?{x:x/length,y:y/length,strength:Math.min(1,length/total)}:null;
}

// Only projected choices and displayed, observed footprints enter composition.
export function directorDecision(frame,decision,side) {
  const footprint=id=>cameraBounds(cameraFootprint(frame,id));
  const selectable=(decision?.candidates??[]).filter(c=>c.boardVisual?.kind==='activate').map(c=>c.interaction?.unitId).filter(Boolean);
  const ids=decision?.unitId?[decision.unitId]:selectable;
  const own=ids.filter(id=>!side||frame.units.some(u=>u.id===id&&u.sideId===side));
  if(!own.length)return null;
  const primary=own.map(footprint).filter(Boolean),legal=[],targets=[];
  for(const c of decision.candidates??[]){
    if(c.isEndTurn)continue;
    const ref=c.interaction;
    if(ref?.unitId){const b=footprint(ref.unitId);legal.push(b);if(b&&!own.includes(ref.unitId))targets.push(b);}
    if(c.placementCells?.length)legal.push(cameraBounds(c.placementCells));
    else if(ref?.position)legal.push(cameraBounds([ref.position]));
    if(ref?.door)legal.push(cameraBounds([ref.door.a,ref.door.b]));
    for(const id of c.affectedUnitIds??[])legal.push(footprint(id));
  }
  return primary.length?{context:'Decision',primary,legal:unique(legal.filter(Boolean)),targets:unique(targets),subjects:own}:null;
}

export function scoreDirectorPose(pose,current,shot,viewport) {
  const screen=b=>threeScreenBounds(b,pose,viewport,pose.visualHeight??0);
  const margin=Math.min(48,viewport.width*.10,viewport.height*.10);
  const readability=directorReadability(viewport),sizes=shot.primary.map(b=>directorSubjectSize(b,pose,viewport));
  let score=0;
  for(const b of shot.primary){const p=screen(b);score+=inside(p,viewport,4)?0:100000;score+=inside(p,viewport,margin)?0:240;}
  const smallest=Math.min(...sizes);
  score+=4000*Math.max(0,1-smallest/readability.minimum)**2;
  score+=360*Math.max(0,1-smallest/readability.desired)**2;
  // Category budgets, not a vote per Cell: 400 destinations cannot outweigh
  // the active figure, and adding duplicate action variants changes nothing.
  const missed=list=>list.length?list.filter(b=>!inside(screen(b),viewport,margin)).length/list.length:0;
  score+=180*missed(shot.targets??[])+110*missed(shot.legal??[])+24*missed(shot.secondary??[]);
  if(shot.direction){
    const origin=center(union(shot.primary)),a=threeProject(origin,pose,viewport),b=threeProject({x:origin.x+shot.direction.x*3,y:origin.y+shot.direction.y*3},pose,viewport);
    const dx=b.x-a.x,dy=b.y-a.y,length=Math.hypot(dx,dy);
    // Useful activity reads into the upper half of the table, with the subject
    // below it. This rewards meaningful heading changes and asymmetric space.
    if(length)score+=150*shot.direction.strength*(1+dy/length)/2;
    score+=80*shot.direction.strength*Math.min(1,Math.hypot((a.x-viewport.width*.5)/viewport.width,(a.y-viewport.height*.62)/viewport.height));
  }
  const a=threeCameraPosition(current,viewport),b=threeCameraPosition(pose,viewport);
  const travel=Math.hypot(a.x-b.x,a.y-b.y,a.height-b.height);
  // Perspective has an actual optical position; the orthographic 100-Cell
  // reference is not a physical trip and must not veto useful yaw changes.
  const chargedTravel=(pose.projectionMix??0)>0?travel:Math.hypot(pose.x-current.x,pose.y-current.y);
  score+=chargedTravel*.65+heading(pose.yaw,current.yaw)*.18+Math.abs(pose.tilt-current.tilt)*.35+Math.abs(Math.log(pose.scale/current.scale))*18;
  const safe=safeTilt(pose)&&!((pose.projectionMix??0)>0&&b.height<(pose.visualHeight??1.5)+1);
  if(!safe)score+=20000;
  return {score,travel,safe,subjectPixels:smallest,readable:smallest>=readability.minimum,clipped:shot.primary.some(b=>!inside(screen(b),viewport,4)),comfortable:shot.primary.every(b=>inside(screen(b),viewport,margin))};
}

export function chooseDirectorPose(current,shot,viewport,preferences,board) {
  if(!shot.primary.length)return null;
  // Selectable Units may be scattered. The first supplied choice anchors a
  // bounded region; other choices remain available through existing controls.
  const cap=preferences.mode==='fixed'?preferences.fixedSpan:preferences.maxSpan;
  let bounds=shot.primary[0];const primary=[bounds];
  for(const b of shot.primary.slice(1)){
    const next=cameraUnion(bounds,b);
    if(next.w<=Math.min(7,cap*.8)&&next.h<=Math.min(7,cap*.8)){bounds=next;primary.push(b);}
  }
  // Combat participants are mandatory, even across a larger observed area.
  if(shot.context==='Combat'){bounds=union(shot.primary);primary.splice(0,primary.length,...shot.primary);}
  const legal=unique(shot.legal??[]).filter(b=>distance(b,bounds)<=4);
  const targets=unique(shot.targets??[]).filter(b=>distance(b,bounds)<=7);
  const hints=[...(shot.hints??[]),...targets.map(b=>({point:center(b),weight:2/Math.max(1,targets.length)}))];
  if(legal.length)hints.push({point:{x:legal.reduce((n,b)=>n+center(b).x,0)/legal.length,y:legal.reduce((n,b)=>n+center(b).y,0)/legal.length},weight:.8});
  const direction=interestDirection(primary,hints);
  const bounded={...shot,primary,legal,targets,direction};
  const orientation={...current,tilt:Math.max(20,Math.min(60,current.tilt))};
  const candidates=[{pose:current,adjustment:'retain'}];
  const origin=center(bounds),lead=direction?{x:origin.x+direction.x*1.5,y:origin.y+direction.y*1.5}:origin;
  const look=threeLookAt(current,lead,viewport);
  if(preferences.mode!=='fixed'&&look&&safeTilt(look)&&heading(look.yaw,current.yaw)<=40)candidates.push({pose:look,adjustment:'look-around'});
  const padded=cameraMargin(bounds,Math.max(1,Math.min(2,preferences.margin)),board);
  const angles=[[orientation.yaw,orientation.tilt],[orientation.yaw,40],[orientation.yaw,50]];
  if(direction){
    const bearing=Math.atan2(-direction.x,-direction.y)*180/Math.PI,delta=((bearing-orientation.yaw+540)%360)-180;
    const directedYaw=orientation.yaw+Math.max(-85,Math.min(85,delta));
    angles.push([orientation.yaw-35,40],[orientation.yaw+35,40],[orientation.yaw-70,40],[orientation.yaw+70,40],[directedYaw,40],[directedYaw,50]);
  }
  for(const [yaw,tilt] of angles.filter((p,i)=>angles.findIndex(q=>q[0]===p[0]&&q[1]===p[1])===i)){
    for(const focus of direction?[origin,lead]:[origin]){
      const base={...orientation,yaw,tilt,...focus};
      let fit=threeFitRect(padded,viewport,base,current.visualHeight,Math.min(48,viewport.width*.1));
      if(preferences.mode==='fixed')fit.scale=threeSpanScale(viewport,preferences.fixedSpan,fit);
      else{
        // Fit only primary envelopes and local breathing room. Search scale in
        // a fixed ten steps to reach the desired projected size, in either direction.
        let lo=0,hi=Math.min(fit.scale,threeSpanScale(viewport,preferences.minSpan,fit));
        for(let i=0;i<10;i++){const scale=(lo+hi)/2,p={...fit,scale};if(Math.min(...primary.map(b=>directorSubjectSize(b,p,viewport)))<directorReadability(viewport).desired)lo=scale;else hi=scale;}
        fit.scale=hi;
      }
      fit.scale=Math.min(fit.scale,threeFitRect(bounds,viewport,fit,current.visualHeight,24).scale);
      candidates.push({pose:fit,adjustment:yaw===current.yaw?'translation + zoom':'orbit + translation + zoom'});
    }
  }
  const ranked=candidates.map((c,index)=>({...c,index,...scoreDirectorPose(c.pose,current,bounded,viewport)})).sort((a,b)=>a.score-b.score||a.index-b.index);
  const existing=ranked.find(c=>c.index===0);
  // Readability is a constraint when a safe, fully visible readable shot exists.
  // Very dispersed combat can be geometrically impossible; then keep all
  // participants visible and minimize the deficit rather than clipping one.
  const readable=ranked.filter(c=>c.readable&&!c.clipped&&c.safe);
  const best=preferences.mode==='fixed'||!readable.length?ranked[0]:readable[0];
  // Separate comfort/intervention bands, with a meaningful improvement threshold.
  if(best.index===0||(!existing.clipped&&existing.readable&&existing.safe&&existing.score-best.score<40))return null;
  return {...best,reason:existing.clipped?'primary clipped':!existing.readable?'primary too small':'composition improvement',context:shot.context,subjects:shot.subjects,overflow:legal.length<(shot.legal??[]).length||primary.length<shot.primary.length};
}

export function createCameraDirector() {
  let shot=null,combat=null,movement=null,lastAnchor=null,segment=null,diagnostic=null;
  function reset(){shot=combat=movement=lastAnchor=segment=diagnostic=null;}
  function stable(frame,decision,side){
    movement=lastAnchor=segment=null;
    if(combat&&decision?.roll){shot=combat;return;}
    combat=null;shot=directorDecision(frame,decision,side);
    if(!shot){const units=frame.units.filter(u=>!side||u.sideId===side),bounds=units.map(u=>cameraBounds(cameraFootprint(frame,u.id))).filter(Boolean);shot={context:'Overview',idle:true,primary:bounds,legal:[],subjects:[]};}
  }
  function event(frame,occurrence,event){
    const intent=cameraEventIntent(occurrence,event,frame);
    if(occurrence?.kind==='AttackStarted'){
      movement=lastAnchor=segment=null;
      const ids=[occurrence.attackContext?.attackerId,...(occurrence.attackContext?.targets??[]).map(t=>t.targetId)].filter(Boolean);
      const primary=ids.map(id=>cameraBounds(cameraFootprint(frame,id))).filter(Boolean);
      combat=primary.length?{context:'Combat',primary,legal:[],subjects:ids.filter(id=>cameraFootprint(frame,id).length)}:null;
      shot=combat;return;
    }
    if(intent?.kind==='movement'){combat=null;movement=intent;lastAnchor=segment=null;shot=null;return;}
    if(combat&& !['ActivationStarted','ActivationEnded'].includes(occurrence?.kind))return;
    combat=null;movement=lastAnchor=segment=null;
    shot=intent?.bounds?{context:'Overview',primary:[intent.bounds],legal:[],subjects:[]}:null;
  }
  function step(anchor,frame){
    const run=movement?.segments.find(s=>s.some(c=>c.x===anchor.x&&c.y===anchor.y));if(!run)return;
    if(segment!==run){segment=run;lastAnchor=null;}
    const primary=[cameraAnchorBounds(anchor,movement.span)],legal=[];
    if(lastAnchor){
      // Direction comes only from already presented anchors in this segment.
      const dx=Math.sign(anchor.x-lastAnchor.x),dy=Math.sign(anchor.y-lastAnchor.y),ahead={x:anchor.x+dx,y:anchor.y+dy};
      if(!frame.explored||frame.explored.has(`${ahead.x},${ahead.y}`))legal.push(cameraAnchorBounds(ahead,movement.span));
    }
    lastAnchor=anchor;shot={context:'Movement',primary,legal,subjects:['moving Unit']};
  }
  function compose(frame,pose,viewport,preferences,side){
    if(!shot||!pose)return null;
    if(shot.idle&&diagnostic)return null;
    const anchor=shot.primary[0];if(!anchor)return null;
    const nearby=b=>b&&Math.hypot(b.x-anchor.x,b.y-anchor.y)<=5;
    // Same-Side is known. Other Sides are never presumed hostile.
    const secondary=[],hints=[];
    for(const unit of frame.units){
      if(shot.subjects.includes(unit.id))continue;
      const b=cameraBounds(cameraFootprint(frame,unit.id));if(!nearby(b))continue;
      secondary.push(b);
      // Observed other-Side activity is a hint, not an assertion of hostility.
      hints.push({point:center(b),weight:side&&unit.sideId===side ? .15 : .45});
    }
    for(const objective of frame.objectives)if(objective.area&&(!side||objective.sideId===side)){hints.push({point:center(objective.area),weight:.65});if(nearby(objective.area))secondary.push(objective.area);}
    const result=chooseDirectorPose(pose,{...shot,secondary,hints},viewport,preferences,frame.board);
    if(result)diagnostic={context:shot.context,subjects:shot.subjects,reason:result.reason,adjustment:result.adjustment,subjectPixels:Math.round(result.subjectPixels),translation:result.travel<.001?'avoided':`${result.travel.toFixed(2)} Cells`};
    return result;
  }
  return {reset,stable,event,step,compose,inspect:()=>({context:shot?.context??'Overview',diagnostic}),finish:()=>movement?.segments.at(-1)?.at(-1)};
}
