import { cameraBounds, cameraUnion, cameraMargin, cameraPreferences, cameraAutoFit as flatAutoFit } from './camera-geometry.js';

// Presentation geometry only. Board y maps to world Z; height is Y.
const radians = degrees => degrees * Math.PI / 180;
export const lensFov = value => Math.max(20,Math.min(80,Number.isFinite(Number(value))?Number(value):45));
// Mix zero is exactly orthographic. Intermediate values are real perspective
// lenses tending continuously to parallel rays, with matching focus-plane scale.
export function perspectiveLens(pose,viewport) {
  const tangent=Math.tan(radians(lensFov(pose.fov))/2)*Math.max(0,Math.min(1,pose.projectionMix??(pose.projection==='perspective'?1:0)));
  const focal=tangent?viewport.height/(2*tangent):Infinity;
  return {focal,distance:focal/pose.scale,fov:2*Math.atan(tangent)*180/Math.PI,perspective:tangent>0};
}
export const tabletopOrientation = Object.freeze({ tilt: 38, yaw: 0 });
export function orientationBasis({ tilt = 0, yaw = 0 } = {}) {
  const t = radians(tilt), a = radians(yaw);
  return { c: Math.cos(a), s: Math.sin(a), ct: Math.cos(t), st: Math.sin(t) };
}
export function threeProject(point, pose, viewport) {
  const b = orientationBasis(pose), dx = point.x - pose.x, dy = point.y - pose.y;
  const u=b.c*dx-b.s*dy,v=b.s*dx+b.c*dy,h=point.height??0,lens=perspectiveLens(pose,viewport);
  const depth=lens.distance-b.st*v-b.ct*h;
  const scale=lens.perspective?lens.focal/depth:pose.scale;
  return { x: viewport.width/2+u*scale,y:viewport.height/2+(b.ct*v-b.st*h)*scale,behind:lens.perspective&&depth<=0 };
}
export function threeUnproject(point, pose, viewport, height = 0) {
  const b = orientationBasis(pose),lens=perspectiveLens(pose,viewport);
  let x=(point.x-viewport.width/2)/pose.scale,y=((point.y-viewport.height/2)/pose.scale+b.st*height)/b.ct;
  if(lens.perspective){
    const sy=(point.y-viewport.height/2)/lens.focal,denominator=b.ct+sy*b.st;
    if(denominator<=1e-6)return null; // ray is parallel to, or above, the ground
    y=(sy*lens.distance+(b.st-sy*b.ct)*height)/denominator;
    x=(point.x-viewport.width/2)/lens.focal*(lens.distance-b.st*y-b.ct*height);
  }
  return { x: pose.x + b.c * x + b.s * y, y: pose.y - b.s * x + b.c * y };
}
export function groundQuadrilateral(pose, viewport) {
  // At the horizon the ground is unbounded. The finite focal-plane reference
  // keeps coordinate-label enumeration bounded; picking still rejects sky rays.
  if(perspectiveLens(pose,viewport).perspective&& !threeUnproject({x:0,y:0},pose,viewport))
    return groundQuadrilateral({...pose,projectionMix:0},viewport);
  return [[0,0],[viewport.width,0],[viewport.width,viewport.height],[0,viewport.height]]
    .map(([x,y]) => threeUnproject({x,y},pose,viewport));
}
export function threeSpanScale(viewport, span, orientation = {}) {
  const corners = groundQuadrilateral({x:0,y:0,scale:1,tilt:orientation.tilt,yaw:orientation.yaw},viewport);
  const extent=q=>Math.max(Math.max(...q.map(p=>p.x))-Math.min(...q.map(p=>p.x)),Math.max(...q.map(p=>p.y))-Math.min(...q.map(p=>p.y)));
  // Zoom measures the matched parallel-ray ground span at the focus plane.
  // Perspective corner rays can approach infinity at a horizon; using those as
  // the zoom metric would magnify the focus dramatically while turning a lens.
  return extent(corners)/span;
}
export function threeClampPose(pose, board, viewport) {
  // A rotated ground quadrilateral cannot include a board corner while also
  // staying wholly inside the board. Allow gutters and bound the focus itself;
  // requiring its enclosing rectangle to fit would push active corner Units out.
  if(pose.allowGutters||pose.tilt||pose.yaw||perspectiveLens(pose,viewport).perspective)return {...pose,x:Math.max(0,Math.min(board.width,pose.x)),y:Math.max(0,Math.min(board.height,pose.y))};
  const corners = groundQuadrilateral({...pose,x:0,y:0},viewport);
  const axis = (center,values,extent) => {
    const lo=-Math.min(...values),hi=extent-Math.max(...values);
    return lo>hi ? extent/2 : Math.max(lo,Math.min(hi,center));
  };
  return {...pose,x:axis(pose.x,corners.map(p=>p.x),board.width),y:axis(pose.y,corners.map(p=>p.y),board.height)};
}
export function projectedBounds(rect, orientation = {}, height = 0) {
  const pose={x:0,y:0,scale:1,tilt:orientation.tilt,yaw:orientation.yaw},size={width:0,height:0},points=[];
  for(const x of [rect.x,rect.x+rect.w])for(const y of [rect.y,rect.y+rect.h])for(const h of [0,height])
    points.push(threeProject({x,y,height:h},pose,size));
  const x=Math.min(...points.map(p=>p.x)),y=Math.min(...points.map(p=>p.y));
  return {x,y,w:Math.max(...points.map(p=>p.x))-x,h:Math.max(...points.map(p=>p.y))-y};
}
export function threeFull(board,viewport,orientation={},physical=false) {
  orientation={...orientation,tilt:orientation.tilt??0,yaw:orientation.yaw??0};
  const border=physical?.12:0,padding=physical?28:0;
  if(orientation.x!=null||perspectiveLens({...orientation,scale:1},viewport).perspective)
    return threeFitRect({x:-border,y:-border,w:board.width+2*border,h:board.height+2*border},viewport,orientation,physical?.75:0,padding);
  const bounds=projectedBounds({x:-border,y:-border,w:board.width+2*border,h:board.height+2*border},orientation,physical?.75:0);
  const scale=Math.min(Math.max(1,viewport.width-2*padding)/bounds.w,Math.max(1,viewport.height-2*padding)/bounds.h);
  const center=threeUnproject({x:bounds.x+bounds.w/2,y:bounds.y+bounds.h/2},{x:0,y:0,scale:1,...orientation},{width:0,height:0});
  return {...center,scale,...orientation};
}
export function threeScreenBounds(rect,pose,viewport,height=0){
  const points=[];
  for(const x of [rect.x,rect.x+rect.w])for(const y of [rect.y,rect.y+rect.h])for(const h of [0,height])points.push(threeProject({x,y,height:h},pose,viewport));
  const x=Math.min(...points.map(p=>p.x)),y=Math.min(...points.map(p=>p.y));
  return {x,y,w:Math.max(...points.map(p=>p.x))-x,h:Math.max(...points.map(p=>p.y))-y,behind:points.some(p=>p.behind)};
}
// Solve each frustum-plane inequality at actual vertex depth. This remains
// valid at narrow aspects, rotated poses, raised bounds and wide-angle horizons.
export function threeFitRect(rect,viewport,orientation,height=.16,padding=16){
  const pose={...orientation,x:orientation.x??rect.x+rect.w/2,y:orientation.y??rect.y+rect.h/2,scale:1},b=orientationBasis(pose),lens=perspectiveLens(pose,viewport);
  if(!lens.perspective){const p=threeScreenBounds(rect,pose,viewport,height);return {...pose,scale:Math.min(Math.max(1,viewport.width/2-padding)/Math.max(Math.abs(p.x-viewport.width/2),Math.abs(p.x+p.w-viewport.width/2)),Math.max(1,viewport.height/2-padding)/Math.max(Math.abs(p.y-viewport.height/2),Math.abs(p.y+p.h-viewport.height/2)))};}
  let distance=.1;
  for(const x of [rect.x,rect.x+rect.w])for(const y of [rect.y,rect.y+rect.h])for(const h of [0,height]){
    const dx=x-pose.x,dy=y-pose.y,u=b.c*dx-b.s*dy,v=b.s*dx+b.c*dy,depth=-b.st*v-b.ct*h;
    distance=Math.max(distance,.1-depth,Math.abs(u)*lens.focal/Math.max(1,viewport.width/2-padding)-depth,Math.abs(b.ct*v-b.st*h)*lens.focal/Math.max(1,viewport.height/2-padding)-depth);
  }
  return {...pose,scale:lens.focal/distance};
}
export function threeEnsureVisible(pose,bounds,board,viewport,preferred=bounds) {
  if(!bounds)return pose;
  if(perspectiveLens(pose,viewport).perspective){
    let result={...pose},target=bounds;
    const fits=p=>!p.behind&&p.x>=viewport.width*.12&&p.y>=viewport.height*.12&&p.x+p.w<=viewport.width*.88&&p.y+p.h<=viewport.height*.88;
    if(threeFitRect(bounds,viewport,pose).scale<pose.scale)target=preferred;
    if(fits(threeScreenBounds(target,result,viewport,.16)))return result;
    const center={x:target.x+target.w/2,y:target.y+target.h/2};
    // Follow only as far as necessary. The fixed iteration count avoids jitter
    // and does not assume screen bounds vary linearly with camera distance.
    let lo=0,hi=1;
    for(let i=0;i<18;i++){const t=(lo+hi)/2,p={...pose,x:pose.x+(center.x-pose.x)*t,y:pose.y+(center.y-pose.y)*t};if(fits(threeScreenBounds(target,p,viewport,.16)))hi=t;else lo=t;}
    result={...pose,x:pose.x+(center.x-pose.x)*hi,y:pose.y+(center.y-pose.y)*hi};
    return threeClampPose(result,board,viewport);
  }
  let b=projectedBounds(bounds,pose);
  if(b.w*pose.scale>viewport.width||b.h*pose.scale>viewport.height)b=projectedBounds(preferred,pose);
  const center=threeProject({x:pose.x,y:pose.y},{x:0,y:0,scale:1,tilt:pose.tilt,yaw:pose.yaw},{width:0,height:0});
  const axis=(value,start,length,pixels)=>{
    const visible=pixels/pose.scale,comfortable=visible*.76,extent=length<=comfortable?comfortable:visible;
    return length>extent?start+length/2:Math.max(start+length-extent/2,Math.min(start+extent/2,value));
  };
  const next=threeUnproject({x:axis(center.x,b.x,b.w,viewport.width),y:axis(center.y,b.y,b.h,viewport.height)},
    {x:0,y:0,scale:1,tilt:pose.tilt,yaw:pose.yaw},{width:0,height:0});
  return threeClampPose({...pose,...next},board,viewport);
}
export function threeAutoFit({active,moves=[],friends=[],board,viewport,preferences,orientation={}}) {
  const perspective=perspectiveLens({...orientation,scale:1},viewport).perspective;
  if(!(orientation.tilt||orientation.yaw||perspective))return flatAutoFit({active,moves,friends,board,viewport,preferences});
  const prefs=cameraPreferences(preferences),activeBounds=cameraBounds(active);if(!activeBounds)return null;
  const core=moves.reduce((b,cells)=>cameraUnion(b,cameraBounds(cells)),activeBounds);
  const required=b=>{if(perspective)return threeSpanScale(viewport,1,orientation)/threeFitRect(b,viewport,orientation).scale;const p=projectedBounds(b,orientation,.16);return threeSpanScale(viewport,1,orientation)*Math.max(p.w/Math.max(1,viewport.width-32),p.h/Math.max(1,viewport.height-32));};
  const fits=b=>required(b)<=prefs.maxSpan;
  let bounds=core;const overflow=!fits(core);
  for(let m=prefs.margin;m>=0;m--){const b=cameraMargin(core,m,board);if(fits(b)){bounds=b;break;}}
  if(!overflow)for(const b of friends.map(cameraBounds).filter(Boolean).sort((a,b)=>Math.hypot(a.x-activeBounds.x,a.y-activeBounds.y)-Math.hypot(b.x-activeBounds.x,b.y-activeBounds.y)||a.y-b.y||a.x-b.x)){
    const next=cameraUnion(bounds,b);if(fits(next))bounds=next;
  }
  const span=Math.max(prefs.minSpan,Math.min(prefs.maxSpan,Math.ceil(required(bounds)-1e-6)));
  let pose={x:bounds.x+bounds.w/2,y:bounds.y+bounds.h/2,scale:threeSpanScale(viewport,span,orientation),...orientation};
  if(perspective){
    // The active footprint outranks every optional region. At a very narrow
    // aspect/wide lens the span cap can otherwise make even that footprint too
    // large; retain it in full rather than clipping a selectable Unit.
    const activeFit=threeFitRect(activeBounds,viewport,{...orientation,x:activeBounds.x+activeBounds.w/2,y:activeBounds.y+activeBounds.h/2});
    if(pose.scale>activeFit.scale)pose={...activeFit};
  }
  pose=threeEnsureVisible(pose,activeBounds,board,viewport);
  return {pose,span,overflow,bounds,core};
}
export function interpolateOrientation(from,to,fraction) {
  const t=1-(1-Math.max(0,Math.min(1,fraction)))**3;
  const delta=(((to.yaw??0)-(from.yaw??0)+540)%360)-180;
  return {tilt:(from.tilt??0)+((to.tilt??0)-(from.tilt??0))*t,yaw:(from.yaw??0)+delta*t};
}
