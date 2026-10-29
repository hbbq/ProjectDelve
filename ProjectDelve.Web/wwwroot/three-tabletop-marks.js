import * as THREE from './vendor/three/0.180.0/build/three.module.js';

export const tabletopMarkColors=Object.freeze({
  brass:'#d9b56f',move:'#7fb9b2',attack:'#df957d',ivory:'#f5ead2',ink:'#242b2b'
});

// These are supplied presentation rectangles, never a legality/visibility query.
// Three instanced batches share one plane: faint enamel, dark keyline and ink.
// The viewport owns observation filtering, picking and the existing overlay pass.
export function createTabletopMarks(){
  const geometry=new THREE.PlaneGeometry(1,1),palette=tabletopMarkColors;
  const materials=[.055,.86,1].map(opacity=>new THREE.MeshBasicMaterial({
    color:'#ffffff',transparent:true,opacity,depthTest:false,depthWrite:false,toneMapped:false
  }));
  let disposed=false;
  function build(rectangles=[],links=[]){
    if(disposed)throw Error('Tabletop marks have been disposed');
    const batches=[[],[],[]],seen=new Set(),colors=new Map();
    const quad=(batch,x,y,w,h,angle,color,height)=>{
      if(w>0&&h>0)batches[batch].push({x,y,w,h,angle,color,height});
    };
    function stroke(x,y,length,thickness,angle,color,height){
      quad(1,x,y,length+.012,thickness+.022,angle,palette.ink,height);
      quad(2,x,y,length,thickness,angle,color,height+.001);
    }
    function diamond(x,y,radius,color,height){
      quad(1,x,y,radius+.026,radius+.026,Math.PI/4,palette.ink,height);
      quad(2,x,y,radius,radius,Math.PI/4,color,height+.001);
    }
    for(const mark of rectangles){
      const {kind,x,y,w=1,h=1,height=.02}=mark;
      if(![x,y,w,h,height].every(Number.isFinite)||w<=0||h<=0)continue;
      const color=mark.color??(kind==='move'||kind==='source'?palette.move:kind==='attack'||kind==='target'?palette.attack:kind==='waypoint'?'#a99c7d':palette.brass);
      // Multiple legal actions can refer to the same Cell. Do not darken it or
      // multiply geometry merely because the server supplied multiple choices.
      const key=JSON.stringify([kind,x,y,w,h,color,height]);if(seen.has(key))continue;seen.add(key);
      const strong=['activate','preview','focus','source','target'].includes(kind);
      const corner=strong||kind==='attack',area=kind==='objective'||kind==='waypoint';
      const thickness=strong?.038:kind==='attack'?.032:area?.022:.019;
      const inset=Math.min(.035,w*.1,h*.1),left=x+inset,right=x+w-inset,top=y+inset,bottom=y+h-inset;
      if(kind==='move'||kind==='preview')quad(0,x+w/2,y+h/2,Math.max(0,w-.08),Math.max(0,h-.08),0,color,height-.002);
      if(area){
        // Dashed perimeter reads as a zone, distinct from an actionable Cell.
        // No area fill: public Objective bounds must not wash out unknown space.
        function dashes(start,end,fixed,vertical){
          const length=end-start,count=Math.max(1,Math.ceil(length/.5)),step=length/count;
          for(let i=0;i<count;i++)stroke(vertical?fixed:start+(i+.5)*step,vertical?start+(i+.5)*step:fixed,step*.52,thickness,vertical?Math.PI/2:0,color,height);
        }
        dashes(left,right,top,false);dashes(left,right,bottom,false);dashes(top,bottom,left,true);dashes(top,bottom,right,true);
        if(kind==='objective')for(const [cx,cy]of [[left,top],[right,top],[left,bottom],[right,bottom]])diamond(cx,cy,.075,color,height+.002);
      }else if(corner){
        const length=Math.min(w,h)*.23;
        for(const [cx,sx]of [[left,1],[right,-1]])for(const [cy,sy]of [[top,1],[bottom,-1]]){
          stroke(cx+sx*length/2,cy,length,thickness,0,color,height);
          stroke(cx,cy+sy*length/2,length,thickness,Math.PI/2,color,height);
        }
        // A small terminal lozenge differentiates attack targets even without hue.
        if(kind==='attack'||kind==='target')diamond(x+w/2,bottom,.085,color,height+.002);
        if(kind==='preview'){
          stroke(x+w/2,top,right-left,.012,0,palette.ivory,height+.002);
          stroke(x+w/2,bottom,right-left,.012,0,palette.ivory,height+.002);
        }
      }else{
        stroke(x+w/2,top,right-left,thickness,0,color,height);
        stroke(x+w/2,bottom,right-left,thickness,0,color,height);
        stroke(left,y+h/2,bottom-top,thickness,Math.PI/2,color,height);
        stroke(right,y+h/2,bottom-top,thickness,Math.PI/2,color,height);
      }
    }
    for(const {from,to,color=palette.ivory,height=.026}of links){
      if(!from||!to||![from.x,from.y,to.x,to.y,height].every(Number.isFinite))continue;
      const dx=to.x-from.x,dy=to.y-from.y,distance=Math.hypot(dx,dy);
      const start=Math.max(0,from.radius??.34)+.07,end=Math.max(0,to.radius??.34)+.07,length=distance-start-end;
      if(length<=.05)continue;
      const angle=Math.atan2(dy,dx),ux=dx/distance,uy=dy/distance,tip={x:to.x-ux*end,y:to.y-uy*end};
      stroke(from.x+ux*(start+length/2),from.y+uy*(start+length/2),length,.025,angle,color,height);
      const head=Math.min(.13,length*.35);
      for(const sign of [-1,1]){
        const direction=angle+sign*.65;
        stroke(tip.x-Math.cos(direction)*head/2,tip.y-Math.sin(direction)*head/2,head,.027,direction,palette.brass,height+.002);
      }
    }
    const group=new THREE.Group();group.name='tabletop-marks';group.userData.tabletopMarks=true;
    const transform=new THREE.Object3D();
    for(const [index,items]of batches.entries()){
      if(!items.length)continue;
      const mesh=new THREE.InstancedMesh(geometry,materials[index],items.length);mesh.name=['mark-wash','mark-keyline','mark-ink'][index];mesh.renderOrder=100+index;
      items.forEach((p,i)=>{
        transform.position.set(p.x,p.height,p.y);transform.rotation.set(-Math.PI/2,0,-p.angle);transform.scale.set(p.w,p.h,1);transform.updateMatrix();mesh.setMatrixAt(i,transform.matrix);
        if(!colors.has(p.color))colors.set(p.color,new THREE.Color(p.color));mesh.setColorAt(i,colors.get(p.color));
      });
      mesh.instanceMatrix.needsUpdate=true;mesh.instanceColor.needsUpdate=true;mesh.computeBoundingSphere();group.add(mesh);
    }
    let released=false;
    group.userData.disposeMarks=()=>{if(released)return;released=true;for(const mesh of group.children)mesh.dispose();group.removeFromParent();};
    return group;
  }
  return {build,dispose(){if(disposed)return;disposed=true;geometry.dispose();for(const material of materials)material.dispose();}};
}
