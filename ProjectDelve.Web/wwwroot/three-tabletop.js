import * as THREE from './vendor/three/0.180.0/build/three.module.js';
import { boardVisuals, terrainVariantIndex } from './board-visuals.js';
import { sideHue } from './side-colors.js';
import { miniatureBaseLayout } from './unit-visuals.js';

export const tabletopDimensions=Object.freeze({border:.36,boardThickness:.26,tokenThickness:.06,tokenRadius:.46,wallHeight:.52,doorHeight:.6});
// Only observed neighbours may form an excavated boundary. Unknown neighbours
// always retain the continuous top; neither their kind nor hidden extent enters.
export function rockBoundaryMask(x,y,kindAt){
  return [[0,-1],[1,0],[0,1],[-1,0]].reduce((mask,[dx,dy],i)=>{
    const kind=kindAt(x+dx,y+dy);return kind!=='Unknown'&&kind!=='SolidRock'?mask|(1<<i):mask;
  },0);
}

// Concrete visual bodies, not Units or rules. A future model body can supply the
// same group/bounds/anchor without owning identity, footprint, choices or cards.
export function createTabletopVisuals({material,physicalMaterial=(color,vertexColors=true)=>material(color,null,false,vertexColors),glyph,plane,artwork,geometries}) {
  Object.assign(geometries,{
    box:new THREE.BoxGeometry(1,1,1),
    cylinder:new THREE.CylinderGeometry(1,1,1,24,1,true),
    tokenCap:new THREE.CircleGeometry(1,24),
    miniatureCap:new THREE.CircleGeometry(1,32),
    tokenRim:new THREE.RingGeometry(.975,1,24),
    gem:new THREE.OctahedronGeometry(1),
    lump:new THREE.IcosahedronGeometry(1,0),
    dome:new THREE.SphereGeometry(1,12,6),
    branch:new THREE.CylinderGeometry(.65,1,1,8),
    foliage:new THREE.IcosahedronGeometry(1,1),
    banana:new THREE.TorusGeometry(.085,.029,5,12,Math.PI*1.35)
  });
  // Off-only baked tones. Lit physical materials ignore this attribute.
  function bakeFaceTones(geometry){
    const normal=geometry.getAttribute('normal'),colors=[];
    for(let i=0;i<normal.count;i++){const shade=.68+.25*Math.max(0,normal.getY(i))+.07*Math.max(0,normal.getX(i));colors.push(shade,shade,shade);}
    geometry.setAttribute('color',new THREE.Float32BufferAttribute(colors,3));
  }
  for(const key of ['box','cylinder','gem','lump','dome','branch','foliage','banana'])bakeFaceTones(geometries[key]);
  function mesh(group,geometry,color,x,y,z,w,h,d,rotation=0){
    const object=new THREE.Mesh(geometry,physicalMaterial(color));object.position.set(x,y,z);object.scale.set(w,h,d);object.rotation.y=rotation;group.add(object);return object;
  }
  const box=(g,c,x,y,z,w,h,d,r=0)=>mesh(g,geometries.box,c,x,y,z,w,h,d,r);
  function print(group,image,x,z,w,d,height,rotation=0,color='#82796a'){
    // Printed props already have a solid top beneath the image. It is also the
    // loading/failure fallback; registering throwaway source meshes as artwork
    // fallbacks would retain them after their transforms enter static batches.
    group.add(plane(image?material(color,image,true):material(color),x,z,w,d,height+(image?.001:0),rotation));
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
  // Stage finishes use the existing material cache and its disposal ownership.
  // Only board dimensions enter the contact shade; terrain and Units never do.
  function woodMaterial(color,width,height,table=false){
    const m=material(color);
    if(!m.userData.woodUniforms){
      const uniforms={tableSize:{value:new THREE.Vector2(width,height)}};
      m.userData.woodUniforms=uniforms;
      const previous=m.onBeforeCompile,cache=m.customProgramCacheKey();
      m.onBeforeCompile=function(shader,...args){
        previous.call(this,shader,...args);Object.assign(shader.uniforms,uniforms);
        shader.vertexShader='varying vec3 vTablePosition;\n'+shader.vertexShader.replace('#include <project_vertex>',`#include <project_vertex>
          vTablePosition = (modelMatrix * vec4(transformed, 1.0)).xyz;`);
        shader.fragmentShader=`varying vec3 vTablePosition;
          uniform vec2 tableSize;
          float tableHash(vec2 p) {
            vec3 q = fract(vec3(p.xyx) * 0.1031);
            q += dot(q, q.yzx + 33.33);
            return fract((q.x + q.y) * q.z);
          }
          float tableNoise(vec2 p) {
            vec2 cell = floor(p), t = fract(p);
            t = t * t * (3.0 - 2.0 * t);
            return mix(mix(tableHash(cell), tableHash(cell + vec2(1.0, 0.0)), t.x),
                       mix(tableHash(cell + vec2(0.0, 1.0)), tableHash(cell + vec2(1.0)), t.x), t.y);
          }
          `+shader.fragmentShader.replace('#include <opaque_fragment>',`
          vec2 wood = vTablePosition.xz;
          float warp = tableNoise(wood * vec2(0.21, 0.53)) - 0.5;
          vec2 fineUV = vec2(wood.x * 0.38, (wood.y + warp * 0.38) * 18.0);
          float fineWeight = 1.0 - smoothstep(0.18, 0.8, max(length(dFdx(fineUV)), length(dFdy(fineUV))));
          float grain = tableNoise(fineUV) - 0.5;
          float broadGrain = tableNoise(vec2(wood.x * 0.12, wood.y * 2.2 + warp)) - 0.5;
          float plank = abs(fract(wood.y / 2.7) - 0.5);
          float seam = smoothstep(0.491 - fwidth(plank), 0.499, plank);
          float plankTone = tableHash(vec2(floor(wood.y / 2.7), 19.0)) - 0.5;
          outgoingLight *= (0.93 + grain * fineWeight * 0.13 + broadGrain * 0.22 + plankTone * 0.055) * (1.0 - seam * 0.14);
          ${table?`vec2 outside = max(abs(wood - tableSize * 0.5) - tableSize * 0.5 - vec2(0.36), vec2(0.0));
          float distanceToBoard = length(outside);
          float contact = 0.62 * exp(-distanceToBoard * 3.5) + 0.16 * exp(-distanceToBoard * 0.65);
          float pool = exp(-dot((wood-tableSize*0.5)/(tableSize*0.5+vec2(7.0)),(wood-tableSize*0.5)/(tableSize*0.5+vec2(7.0))) * 0.6);
          outgoingLight *= (1.0 - contact) * (0.40 + 0.40 * pool);`:''}
          #include <opaque_fragment>`);
      };
      m.customProgramCacheKey=()=>cache+'|delve-walnut-v2:'+(table?'table':'frame');
    }
    m.userData.woodUniforms.tableSize.value.set(width,height);
    return m;
  }
  function board(width,height){
    const group=new THREE.Group(),b=tabletopDimensions.border,t=tabletopDimensions.boardThickness;
    group.name='tabletop-stage';
    const table=plane(woodMaterial('#49372b',width,height,true),width/2,height/2,width+120,height+120,-t-.075);
    table.name='gaming-table';group.add(table);
    // A substantial walnut tray and inset brass line frame the existing play
    // surface. Everything stays below it, preserving projection and picking.
    box(group,'#201b17',width/2,-t-.035,height/2,width+2*b+.04,.05,height+2*b+.04);
    box(group,'#493124',width/2,-t/2-.012,height/2,width+2*b,t,height+2*b);
    const brass=plane(material('#a68c59'),width/2,height/2,width+2*b-.08,height+2*b-.08,-.009);
    brass.name='frame-inlay';group.add(brass);
    const veneer=plane(woodMaterial('#5b3e28',width,height),width/2,height/2,width+2*b-.14,height+2*b-.14,-.007);
    veneer.name='walnut-frame';group.add(veneer);
    print(group,null,width/2,height/2,width+.045,height+.045,-.004,0,'#292821');
    return group;
  }
  function terrain(kind,layer,x,z,seed,kindAt=()=> 'Unknown'){
    const group=new THREE.Group(),cx=x+.5,cz=z+.5;
    const variation=terrainVariantIndex(seed,'tabletop-shape-v1:'+kind,x,z,3);
    if(kind==='SolidRock'){
      const mask=rockBoundaryMask(x,z,kindAt),height=tabletopDimensions.wallHeight;
      group.add(plane(material(layer.base.fill,layer.base.image),cx,cz,1,1,height,layer.rotation));
      // Shared tops have no gaps or internal walls. Exposed excavation edges
      // use textured, inward-faceted sides, wholly inside the observed Cell.
      if(!geometries.rockWall){
        const mid=height*.45,g=new THREE.BufferGeometry(),p=[-.5,height,0,.5,height,0,.5,mid,.025,-.5,height,0,.5,mid,.025,-.5,mid,.025,-.5,mid,.025,.5,mid,.025,.5,0,0,-.5,mid,.025,.5,0,0,-.5,0,0];
        g.setAttribute('position',new THREE.Float32BufferAttribute(p,3));
        // Keep the same texel scale as the one-Cell top instead of stretching a
        // square texture over a half-height side. Vertex tones define the facets.
        g.setAttribute('uv',new THREE.Float32BufferAttribute([0,height,1,height,1,mid,0,height,1,mid,0,mid,0,mid,1,mid,1,0,0,mid,1,0,0,0],2));
        g.computeVertexNormals();bakeFaceTones(g);geometries.rockWall=g;
      }
      for(let i=0;i<4;i++)if(mask&(1<<i)){
        const wall=new THREE.Mesh(geometries.rockWall,material(layer.base.fill,layer.base.image,false,true));
        wall.position.set(cx+[0,.5,0,-.5][i],0,cz+[-.5,0,.5,0][i]);wall.rotation.y=-i*Math.PI/2;group.add(wall);
      }
    }else if(kind==='Tree'||kind==='FruitTree'){
      const tree=new THREE.Group();tree.name='tree';tree.position.set(cx,0,cz);
      tree.rotation.y=(layer.objectRotation??0)*Math.PI/180;group.add(tree);
      // Closed tapered limbs support a volumetric crown. The flat-view portrait
      // is deliberately not projected across the top of the physical prop.
      function limb(from,to,radius){
        const a=new THREE.Vector3(...from),b=new THREE.Vector3(...to),direction=b.clone().sub(a),mid=a.add(b).multiplyScalar(.5);
        const part=mesh(tree,geometries.branch,'#765238',mid.x,mid.y,mid.z,radius,direction.length(),radius);
        part.quaternion.setFromUnitVectors(new THREE.Vector3(0,1,0),direction.normalize());return part;
      }
      limb([0,.015,0],[.015,.58,-.015],.082);
      for(let i=0;i<4;i++){
        const angle=i*Math.PI/2+.35;
        limb([Math.cos(angle)*.17,.012,Math.sin(angle)*.17],[0,.15,0],.035);
      }
      limb([0,.28,0],[-.20,.57,.05],.044);
      limb([0,.34,0],[.20,.62,.09],.041);
      limb([0,.38,0],[.02,.65,-.21],.038);
      const crowns=[
        [-.19,.58,.04,.25,.24,.27,'#3f6740'],
        [.18,.62,.09,.25,.25,.25,'#527846'],
        [.015,.64,-.18,.27,.25,.25,'#416b3f'],
        [-.04,.73,.015,.29,.24,.28,'#5b8048']
      ];
      for(const [x,y,z,w,h,d,color]of crowns)mesh(tree,geometries.foliage,color,x,y,z,w,h,d,variation*.41);
      if(kind==='FruitTree'){
        for(const [x,y,z]of [[-.34,.59,.12],[-.17,.71,.24],[.16,.75,.25],[.35,.61,.06],[.24,.66,-.22],[-.12,.69,-.34],[-.19,.86,.02],[.10,.86,-.10]]){
          mesh(tree,geometries.dome,'#cf4933',x,y,z,.047,.05,.047);
          mesh(tree,geometries.branch,'#765238',x,y+.052,z,.008,.025,.008);
        }
      }
    }else if(kind==='StoneFloorWithTable'){
      const prop=new THREE.Group();prop.name='table';prop.position.set(cx,0,cz);prop.rotation.y=(layer.objectRotation??0)*Math.PI/180;group.add(prop);
      for(const dx of [-.29,.29])for(const dz of [-.18,.18])box(prop,'#614632',dx,.17,dz,.085,.34,.085);
      // Four aprons and low stretchers make a supported wooden table; separate
      // planks replace the overhead illustration (which included its own legs).
      for(const dz of [-.18,.18])box(prop,'#795538',0,.295,dz,.65,.09,.055);
      for(const dx of [-.29,.29]){
        box(prop,'#795538',dx,.295,0,.055,.09,.39);
        box(prop,'#795538',dx,.105,0,.055,.045,.39);
      }
      box(prop,'#614632',0,.105,0,.58,.045,.045);
      for(let i=0;i<4;i++){
        const z=(i-1.5)*.145;
        box(prop,['#a77b4c','#b58b57','#aa8050','#b18a59'][i],0,.367,z,.84,.065,.139);
        // Flush peg heads at both ends read as joinery from the overhead view.
        for(const x of [-.29,.29])mesh(prop,geometries.dome,'#795538',x,.399,z,.009,.001,.009);
      }
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
  function miniatureBase(a,layout=miniatureBaseLayout({baseRadius:a.baseRadius,width:a.footprintWidth??a.cellSpan??1,depth:a.footprintDepth??a.cellSpan??1})){
    const group=new THREE.Group(),r=layout.radius;group.name='miniature-base';group.userData.base=layout;
    // A shallow lower chamfer and a sloped Side rim give every miniature the
    // same molded base finish, inside its existing radius and height envelope.
    const rim=Math.min(.025,r),top=r-rim,key=`miniature-profile:${r}`,rimKey=`miniature-rim:${r}`;
    if(!geometries[key]){
      geometries[key]=new THREE.LatheGeometry([
        new THREE.Vector2(Math.max(0,r-.012),0),new THREE.Vector2(r,.012),
        new THREE.Vector2(r,.04),new THREE.Vector2(top,.06)
      ],32);
      bakeFaceTones(geometries[key]);
      const ring=new THREE.RingGeometry(top/r,1,32),positions=ring.getAttribute('position');
      // plane() rotates local Z into world Y: outer edge meets the shoulder.
      for(let i=0;i<positions.count;i++)positions.setZ(i,Math.hypot(positions.getX(i),positions.getY(i))>.999?-.021:0);
      ring.computeVertexNormals();geometries[rimKey]=ring;
    }
    mesh(group,geometries[key],'#494642',0,0,0,1,1,1);
    group.add(plane(physicalMaterial('#494642',false),0,0,top,top,.06,0,geometries.miniatureCap));
    group.add(plane(material(`hsl(${sideHue(a.sideId)},70%,65%)`),0,0,r,r,.061,0,geometries[rimKey]));
    return group;
  }
  return {unit,posture,board,terrain,edge,machine,item,miniatureBase};
}

// One transient mover: small envelopes need nine Cell columns, larger ones
// need sixteen. Validated model radius is at most 1.25 Cells.
// Copies share geometry/textures. Only clipping materials are owned by this pool.
export function createMovementClipPool(source,explored,THREEImpl=THREE,clippedVariants=null){
  const root=new THREEImpl.Group(),copies=[],materials=new Set();root.name='clipped-movement';
  const materialMap=new Map();
  const capacity=(source.userData.visualBounds?.radius??0)>=1?16:9;
  for(let i=0;i<capacity;i++){
    const planes=[new THREEImpl.Plane(new THREEImpl.Vector3(1,0,0)),new THREEImpl.Plane(new THREEImpl.Vector3(-1,0,0)),new THREEImpl.Plane(new THREEImpl.Vector3(0,0,1)),new THREEImpl.Plane(new THREEImpl.Vector3(0,0,-1))];
    const copyNode=node=>{
      const copy=node.isMesh?new THREEImpl.Mesh(node.geometry,node.material):new THREEImpl.Group();
      copy.position.copy(node.position);copy.quaternion.copy(node.quaternion);copy.scale.copy(node.scale);
      copy.matrix.copy(node.matrix);copy.matrixAutoUpdate=node.matrixAutoUpdate;copy.visible=node.visible;copy.name=node.name;
      copy.castShadow=node.castShadow;copy.receiveShadow=node.receiveShadow;
      for(const child of node.children)copy.add(copyNode(child));return copy;
    };
    const body=copyNode(source);body.position.set(0,0,0);body.traverse(node=>{if(!node.material)return;
      const clone=m=>{const key=`${i}:${m.id}`;if(!materialMap.has(key)){
        const copies=clippedVariants?clippedVariants(m,planes):[m.clone()];
        for(const c of copies){c.clippingPlanes=planes;c.clipIntersection=false;c.clipShadows=true;materials.add(c);if(!clippedVariants)c.userData={sourceMaterial:m};}
        materialMap.set(key,copies.find(c=>c.type===m.type)??copies[0]);
      }return materialMap.get(key);};
      node.material=Array.isArray(node.material)?node.material.map(clone):clone(node.material);
    });root.add(body);copies.push({body,planes});
  }
  function update(x,z,span,yaw){
    root.position.set(x,source.position.y,z);let index=0;
    const radius=source.userData.visualBounds?.radius??span*.48;
    if(yaw!=null)for(const copy of copies){const node=copy.body.getObjectByName('miniature-facing');if(node)node.rotation.y=yaw;}
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
