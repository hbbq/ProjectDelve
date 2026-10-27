import * as THREE from './vendor/three/0.180.0/build/three.module.js';
import { boardVisuals, terrainVariantIndex } from './board-visuals.js';
import { sideHue } from './side-colors.js';

export const tabletopDimensions=Object.freeze({border:.12,boardThickness:.12,tokenThickness:.06,tokenRadius:.46,wallHeight:.52,doorHeight:.6});

// Concrete visual bodies, not Units or rules. A future model body can supply the
// same group/bounds/anchor without owning identity, footprint, choices or cards.
export function createTabletopVisuals({material,glyph,plane,artwork,geometries}) {
  Object.assign(geometries,{
    box:new THREE.BoxGeometry(1,1,1),
    cylinder:new THREE.CylinderGeometry(1,1,1,24,1,true),
    tokenCap:new THREE.CircleGeometry(1,24),
    tokenRim:new THREE.RingGeometry(.975,1,24),
    gem:new THREE.OctahedronGeometry(1),
    lump:new THREE.IcosahedronGeometry(1,0),
    dome:new THREE.SphereGeometry(1,12,6),
    banana:new THREE.TorusGeometry(.085,.029,5,12,Math.PI*1.35)
  });
  // Baked face tones keep the components legible without lights or shadow maps.
  for(const key of ['box','cylinder','gem','lump','dome','banana']){
    const geometry=geometries[key],normal=geometry.getAttribute('normal'),colors=[];
    for(let i=0;i<normal.count;i++){const shade=.68+.25*Math.max(0,normal.getY(i))+.07*Math.max(0,normal.getX(i));colors.push(shade,shade,shade);}
    geometry.setAttribute('color',new THREE.Float32BufferAttribute(colors,3));
  }
  function mesh(group,geometry,color,x,y,z,w,h,d,rotation=0){
    const object=new THREE.Mesh(geometry,material(color,null,false,true));object.position.set(x,y,z);object.scale.set(w,h,d);object.rotation.y=rotation;group.add(object);return object;
  }
  const box=(g,c,x,y,z,w,h,d,r=0)=>mesh(g,geometries.box,c,x,y,z,w,h,d,r);
  function print(group,image,x,z,w,d,height,rotation=0,color='#82796a'){
    const fallback=plane(material(color),x,z,w,d,height,rotation);group.add(fallback);
    if(image)group.add(plane(material(color,image,true),x,z,w,d,height+.001,rotation));
  }
  function unit(a){
    const body=new THREE.Group(),s=a.cellSpan??1,r=s*tabletopDimensions.tokenRadius,t=tabletopDimensions.tokenThickness;
    body.name='turnover';
    const side=`hsl(${sideHue(a.sideId)},70%,65%)`,visual={...boardVisuals.unit.default,...boardVisuals.unit[a.typeId]};
    mesh(body,geometries.cylinder,side,0,0,0,r,t,r);
    for(const reverse of [false,true]){
      const face=new THREE.Group();face.position.y=reverse?-t/2:t/2;if(reverse)face.rotation.x=Math.PI;
      face.name=reverse?'reverse':'front';body.add(face);
      face.add(plane(material(side),0,0,r,r,0,0,geometries.tokenCap));
      face.add(plane(material('#29333b'),0,0,r*.86,r*.86,.001,0,geometries.tokenCap));
      const fallback=plane(glyph(visual.glyph??a.name?.split(/\s+/).map(n=>n[0]).join('').slice(0,2)??'?'),0,0,r,r,.004);
      artwork(face,fallback,visual.image?material('#29333b',visual.image,true):null,0,0,r*.86,r*.86,.003,0,geometries.tokenCap);
      face.add(plane(material('#ead7ad'),0,0,r,r,.006,0,geometries.tokenRim));
      if(reverse){
        // A translucent printed wash reuses the portrait without another bitmap.
        const wash=material('#17202e',null,true);wash.opacity=.55;
        face.add(plane(wash,0,0,r*.9,r*.9,.008,0,geometries.tokenCap));
        face.add(plane(glyph('Ⅱ'),0,0,s*.3,s*.3,.012));
        for(let i=0;i<8;i++){const angle=i*Math.PI/4;face.add(plane(material('#17202e'),Math.cos(angle)*r*.94,Math.sin(angle)*r*.94,s*.045,s*.045,.012));}
      }
    }
    const lift=new THREE.Group();lift.name='cardboard-body';lift.add(body);
    lift.userData={representation:'cardboard',radius:r,infoAnchor:{x:0,y:.09,z:0},bounds:{radius:r,height:t}};
    posture(lift,a.posture==='Lying'?Math.PI:0);
    return lift;
  }
  function posture(body,angle){
    body.getObjectByName('turnover').rotation.x=angle;
    const settled=Math.abs(Math.sin(angle))<1e-7;
    body.getObjectByName('front').visible=!settled||Math.cos(angle)>0;
    body.getObjectByName('reverse').visible=!settled||Math.cos(angle)<0;
    const vertical=(body.userData.radius*Math.abs(Math.sin(angle))+.03*Math.abs(Math.cos(angle)));
    // Compress in world Y around edge-on instead of sweeping a large token up.
    body.scale.y=Math.min(1,.12/Math.max(.03,vertical));body.position.y=vertical*body.scale.y+.012;
    body.userData.angle=angle;
  }
  function board(width,height){
    const group=new THREE.Group(),b=tabletopDimensions.border,t=tabletopDimensions.boardThickness;
    box(group,'#50483d',width/2,-t/2-.005,height/2,width+2*b,t,height+2*b);
    // Neutral top border and a darker underside make the cardboard edge readable.
    box(group,'#292725',width/2,-t-.025,height/2,width+2*b,.035,height+2*b);
    print(group,null,width/2,height/2,width+2*b,height+2*b,-.004,0,'#766b59');
    return group;
  }
  function terrain(kind,layer,x,z,seed){
    const group=new THREE.Group(),cx=x+.5,cz=z+.5;
    const variation=terrainVariantIndex(seed,'tabletop-shape-v1:'+kind,x,z,3);
    if(kind==='SolidRock'){
      const h=.36+variation*.035;
      mesh(group,geometries.lump,['#625e63','#777076','#514e55'][variation],cx,h/2,cz,.49,h*.64,.49,variation*.17);
      print(group,layer.base.image,cx,cz,.78,.78,h,layer.rotation,'#625c64');
    }else if(kind==='Tree'||kind==='FruitTree'){
      mesh(group,geometries.cylinder,'#69503a',cx,.19,cz,.065,.38,.065);
      mesh(group,geometries.dome,'#35563b',cx,.41,cz,.34,.18,.34);
      if(layer.object)group.add(plane(material('#35563b',layer.object.image,true),cx,cz,.88,.88,.6,layer.objectRotation));
    }else if(kind==='StoneFloorWithTable'){
      for(const dx of [-.27,.27])for(const dz of [-.22,.22])box(group,'#614632',cx+dx,.105,cz+dz,.065,.21,.065);
      box(group,'#9b7752',cx,.235,cz,.78,.07,.69);
      print(group,layer.object?.image,cx,cz,.90,.90,.273,layer.objectRotation,'#8a6747');
    }
    return group;
  }
  function edge(edge,at){
    const group=new THREE.Group(),r=at.vertical?Math.PI/2:0;
    group.position.set(at.x,0,at.z);group.rotation.y=r;
    const visual={...boardVisuals.edge.default,...boardVisuals.edge[edge.kind]};
    if(['ClosedDoor','OpenDoor'].includes(edge.kind)){
      for(const x of [-.44,.44])box(group,'#8a7355',x,.30,0,.12,.60,.17);
      const leaf=new THREE.Group();leaf.name='door-leaf';group.add(leaf);
      box(leaf,'#ab7943',0,.27,0,.76,.54,.09);box(leaf,'#dfbb65',.24,.27,-.057,.045,.045,.025);
      print(leaf,boardVisuals.edge.ClosedDoor.image,0,0,.76,.12,.542,0,'#ab7943');
      leaf.visible=edge.kind==='ClosedDoor';
    }else if(edge.kind==='WallWithWindow'){
      for(const x of [-.42,.42])box(group,'#707476',x,.26,0,.16,.52,.12);
      for(const y of [.06,.49])box(group,'#8c9395',0,y,0,1,.10,.12);
      box(group,'#97c4cc',0,.25,0,.025,.34,.06);
    }else{
      box(group,'#68696a',0,.26,0,1,.52,.13);
      print(group,visual.image,0,0,1,.156,.522,0,'#99948a');
    }
    return group;
  }
  function machine(m,visual){
    const group=new THREE.Group(),x=m.position.x+.5,z=m.position.y+.5;
    box(group,'#3c4951',x,.15,z,.76,.30,.68);box(group,'#91836b',x,.34,z,.57,.09,.56);
    print(group,visual.image,x,z,.85,.85,.39,0,visual.fill);
    const color=m.ownerSide?`hsl(${sideHue(m.ownerSide)},65%,65%)`:'#c8b998';
    box(group,color,x,.04,z,.86,.05,.79);return group;
  }
  function item(category,object,visual){
    const group=new THREE.Group(),p=object.position,x=0,z=0;
    group.position.set(p.x+(category==='resource'?.83:.18),0,p.y+.17);
    group.name=`${category}:${object.type.id}`;
    if(category==='cellMarker'){
      mesh(group,geometries.cylinder,'#c6aa70',x,.022,z,.16,.035,.16);
      group.add(plane(material('#d5bb80'),x,z,.16,.16,.041,0,geometries.tokenCap));
      group.add(plane(visual.image?material(visual.fill,visual.image,true):glyph(visual.glyph??'?'),x,z,.29,.29,.043));
      return group;
    }
    switch(object.type.id){
      case 'coal':mesh(group,geometries.lump,'#323139',x,.10,z,.12,.10,.11);break;
      case 'diamond':mesh(group,geometries.gem,'#a8e8ef',x,.14,z,.105,.15,.105);break;
      case 'banana':{const m=mesh(group,geometries.banana,'#f5d348',x,.075,z,1,1,1);m.rotation.x=-Math.PI/2;break;}
      case 'fruit':for(const dx of [-.055,.055])mesh(group,geometries.dome,'#d9473a',x+dx,.09,z,.075,.08,.075);box(group,'#6fa45a',x,.19,z,.025,.10,.02);break;
      case 'mushroom':mesh(group,geometries.cylinder,'#e4d0a0',x,.06,z,.035,.12,.035);mesh(group,geometries.dome,'#d64c42',x,.13,z,.12,.065,.12);break;
      case 'shell':mesh(group,geometries.dome,'#66a44e',x,.075,z,.13,.075,.115);box(group,'#ead7a0',x,.035,z,.27,.035,.22);break;
      default:
        mesh(group,geometries.cylinder,visual.fill??'#c6aa70',x,.055,z,.13,.09,.13);
        group.add(plane(visual.image?material(visual.fill,visual.image,true):glyph(visual.glyph??object.type.name?.slice(0,2)??'?'),x,z,.23,.23,.103));
    }
    return group;
  }
  return {unit,posture,board,terrain,edge,machine,item};
}

// One transient mover; at most nine Cell columns intersect its 2x2-sized body.
// Copies share geometry/textures. Only clipping materials are owned by this pool.
export function createMovementClipPool(source,explored,THREEImpl=THREE){
  const root=new THREEImpl.Group(),copies=[],materials=new Set();root.name='clipped-movement';
  const materialMap=new Map();
  for(let i=0;i<9;i++){
    const planes=[new THREEImpl.Plane(new THREEImpl.Vector3(1,0,0)),new THREEImpl.Plane(new THREEImpl.Vector3(-1,0,0)),new THREEImpl.Plane(new THREEImpl.Vector3(0,0,1)),new THREEImpl.Plane(new THREEImpl.Vector3(0,0,-1))];
    const copyNode=node=>{const copy=node.isMesh?new THREEImpl.Mesh(node.geometry,node.material):new THREEImpl.Group();copy.position.copy(node.position);copy.quaternion.copy(node.quaternion);copy.scale.copy(node.scale);copy.visible=node.visible;copy.name=node.name;for(const child of node.children)copy.add(copyNode(child));return copy;};
    const body=copyNode(source);body.position.set(0,0,0);body.traverse(node=>{if(!node.material)return;
      const clone=m=>{const key=`${i}:${m.id}`;if(!materialMap.has(key)){const c=m.clone();c.clippingPlanes=planes;c.clipIntersection=false;materialMap.set(key,c);materials.add(c);c.userData={};c.userData.sourceMaterial=m;}return materialMap.get(key);};
      node.material=Array.isArray(node.material)?node.material.map(clone):clone(node.material);
    });root.add(body);copies.push({body,planes});
  }
  function update(x,z,span){
    root.position.set(x,source.position.y,z);let index=0;
    const radius=span*.48;
    for(let y=Math.floor(z-radius);y<=Math.floor(z+radius);y++)for(let cellX=Math.floor(x-radius);cellX<=Math.floor(x+radius);cellX++){
      if(explored&&!explored.has(`${cellX},${y}`))continue;
      const copy=copies[index++];if(!copy)continue;
      copy.body.visible=true;copy.body.position.x=copy.body.position.z=0;
      copy.planes[0].constant=-cellX;copy.planes[1].constant=cellX+1;copy.planes[2].constant=-y;copy.planes[3].constant=y+1;
    }
    for(let i=index;i<copies.length;i++)copies[i].body.visible=false;
    for(const m of materials){const source=m.userData.sourceMaterial;if(m.map!==source.map){m.map=source.map;m.opacity=source.opacity;m.color.copy(source.color);m.needsUpdate=true;}}
  }
  return {root,copies,update,dispose(){root.removeFromParent();for(const m of materials)m.dispose();materials.clear();}};
}
