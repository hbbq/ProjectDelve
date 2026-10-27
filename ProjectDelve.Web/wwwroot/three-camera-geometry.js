import { cameraBounds, cameraUnion, cameraMargin, cameraPreferences, cameraAutoFit as flatAutoFit } from './camera-geometry.js';

// Orthographic presentation geometry only. Board y maps to world Z; height is Y.
const radians = degrees => degrees * Math.PI / 180;
export const tabletopOrientation = Object.freeze({ tilt: 38, yaw: 0 });
export function orientationBasis({ tilt = 0, yaw = 0 } = {}) {
  const t = radians(tilt), a = radians(yaw);
  return { c: Math.cos(a), s: Math.sin(a), ct: Math.cos(t), st: Math.sin(t) };
}
export function threeProject(point, pose, viewport) {
  const b = orientationBasis(pose), dx = point.x - pose.x, dy = point.y - pose.y;
  return { x: viewport.width / 2 + (b.c * dx - b.s * dy) * pose.scale,
    y: viewport.height / 2 + (b.ct * (b.s * dx + b.c * dy) - b.st * (point.height ?? 0)) * pose.scale };
}
export function threeUnproject(point, pose, viewport, height = 0) {
  const b = orientationBasis(pose), x = (point.x - viewport.width / 2) / pose.scale;
  const y = ((point.y - viewport.height / 2) / pose.scale + b.st * height) / b.ct;
  return { x: pose.x + b.c * x + b.s * y, y: pose.y - b.s * x + b.c * y };
}
export function groundQuadrilateral(pose, viewport) {
  return [[0,0],[viewport.width,0],[viewport.width,viewport.height],[0,viewport.height]]
    .map(([x,y]) => threeUnproject({x,y},pose,viewport));
}
export function threeSpanScale(viewport, span, orientation = {}) {
  const corners = groundQuadrilateral({x:0,y:0,scale:1,tilt:orientation.tilt,yaw:orientation.yaw},viewport);
  return Math.max(Math.max(...corners.map(p=>p.x))-Math.min(...corners.map(p=>p.x)),
    Math.max(...corners.map(p=>p.y))-Math.min(...corners.map(p=>p.y))) / span;
}
export function threeClampPose(pose, board, viewport) {
  // A rotated ground quadrilateral cannot include a board corner while also
  // staying wholly inside the board. Allow gutters and bound the focus itself;
  // requiring its enclosing rectangle to fit would push active corner Units out.
  if(pose.tilt||pose.yaw)return {...pose,x:Math.max(0,Math.min(board.width,pose.x)),y:Math.max(0,Math.min(board.height,pose.y))};
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
  orientation={tilt:orientation.tilt??0,yaw:orientation.yaw??0};
  const border=physical?.12:0,padding=physical?28:0;
  const bounds=projectedBounds({x:-border,y:-border,w:board.width+2*border,h:board.height+2*border},orientation,physical?.75:0);
  const scale=Math.min(Math.max(1,viewport.width-2*padding)/bounds.w,Math.max(1,viewport.height-2*padding)/bounds.h);
  const center=threeUnproject({x:bounds.x+bounds.w/2,y:bounds.y+bounds.h/2},{x:0,y:0,scale:1,...orientation},{width:0,height:0});
  return {...center,scale,...orientation};
}
export function threeEnsureVisible(pose,bounds,board,viewport,preferred=bounds) {
  if(!bounds)return pose;
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
  if(!(orientation.tilt||orientation.yaw))return flatAutoFit({active,moves,friends,board,viewport,preferences});
  const prefs=cameraPreferences(preferences),activeBounds=cameraBounds(active);if(!activeBounds)return null;
  const core=moves.reduce((b,cells)=>cameraUnion(b,cameraBounds(cells)),activeBounds);
  const required=b=>{const p=projectedBounds(b,orientation,.16);return threeSpanScale(viewport,1,orientation)*Math.max(p.w/Math.max(1,viewport.width-32),p.h/Math.max(1,viewport.height-32));};
  const fits=b=>required(b)<=prefs.maxSpan;
  let bounds=core;const overflow=!fits(core);
  for(let m=prefs.margin;m>=0;m--){const b=cameraMargin(core,m,board);if(fits(b)){bounds=b;break;}}
  if(!overflow)for(const b of friends.map(cameraBounds).filter(Boolean).sort((a,b)=>Math.hypot(a.x-activeBounds.x,a.y-activeBounds.y)-Math.hypot(b.x-activeBounds.x,b.y-activeBounds.y)||a.y-b.y||a.x-b.x)){
    const next=cameraUnion(bounds,b);if(fits(next))bounds=next;
  }
  const span=Math.max(prefs.minSpan,Math.min(prefs.maxSpan,Math.ceil(required(bounds)-1e-6)));
  let pose={x:bounds.x+bounds.w/2,y:bounds.y+bounds.h/2,scale:threeSpanScale(viewport,span,orientation),...orientation};
  pose=threeEnsureVisible(pose,activeBounds,board,viewport);
  return {pose,span,overflow,bounds,core};
}
export function interpolateOrientation(from,to,fraction) {
  const t=1-(1-Math.max(0,Math.min(1,fraction)))**3;
  const delta=(((to.yaw??0)-(from.yaw??0)+540)%360)-180;
  return {tilt:(from.tilt??0)+((to.tilt??0)-(from.tilt??0))*t,yaw:(from.yaw??0)+delta*t};
}
