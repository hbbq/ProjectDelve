// Generates only test scenarios and data-only plans for the restricted dispatcher.
const fs=require('node:fs'),path=require('node:path'),zlib=require('node:zlib');
const output=path.resolve(__dirname,'../.playwright-cli/miniature-qa');fs.mkdirSync(output,{recursive:true});
function scenario(count,width,height,lying=false){
  const units=Array.from({length:count},(_,i)=>({unitTypeId:'qa-miniature-type',sideId:'blue',anchor:{x:2+i%Math.min(width-4,10),y:2+Math.floor(i/Math.min(width-4,10))},unitId:'mini-'+i,posture:lying&&i===0?'Lying':'Upright'}));
  units.push({unitTypeId:'grunt-type',sideId:'red',anchor:{x:width-2,y:height-2},unitId:'cardboard'});
  return {board:{width,height,defaultTerrain:'StoneFloor',cells:[],edges:[]},unitTypeIds:['qa-miniature-type','grunt-type'],unitTypes:[{id:'qa-miniature-type',displayName:'QA Static Figure',mov:3,rng:3,atk:3,def:1,hp:1,actions:'NormalAttack',undying:{}}],units,
    agency:[{unitTypeId:'qa-miniature-type',sideId:'blue',controller:'Human'},{unitTypeId:'grunt-type',sideId:'red',controller:'Human'}],objectives:[{sideId:'blue',objective:'NoObjective'},{sideId:'red',objective:'NoObjective'}]};
}
const transport=s=>'DELVE1:'+zlib.gzipSync(Buffer.from(JSON.stringify(s))).toString('base64url');
function setup(s){return [{action:'click',target:'#tools-toggle'},{action:'fill',target:'#scenario-transport',value:transport(s)},{action:'click',target:'#import-scenario'},{action:'click',target:'#tools-display > summary'},{action:'select',target:'#board-view',value:'tabletop'},{action:'click',target:'#tools-toggle'}];}
for(const [width,height]of [[30,20],[50,50]])for(const count of [1,10,30,100]){
  const s=scenario(count,width,height),name=`${width}x${height}-${count}`;
  const steps=[...setup(s),{action:'select',target:'#three-camera-mode',value:'full'},{action:'screenshot',name:'cardboard.png'},{action:'select',target:'#three-unit-representation',value:'miniatures'},
    {action:'select',target:'#three-camera-projection',value:'perspective'},{action:'select',target:'#three-camera-fov',value:'65'},{action:'screenshot',name:'miniatures.png'},
    {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-developer > summary'},{action:'click',target:'.three-diagnostics > summary'},
    {action:'click',target:'#three-reset-sample'},{action:'click',target:'#three-camera-turn-right'},{action:'snapshot',target:'.three-diagnostics',name:'timing-a.yaml'},
    {action:'click',target:'#three-camera-turn-left'},{action:'snapshot',target:'.three-diagnostics',name:'timing-b.yaml'},
    {action:'click',target:'#three-camera-overhead'},{action:'snapshot',target:'.three-diagnostics',name:'timing-c.yaml'},
    {action:'click',target:'#tools-toggle'},{action:'screenshot',name:'overhead.png'},{action:'console'},{action:'requests'}];
  fs.writeFileSync(path.join(output,name+'.json'),JSON.stringify({steps},null,2));fs.writeFileSync(path.join(output,name+'-scenario.json'),JSON.stringify(s,null,2));
}
const mixed=scenario(3,12,10,true);mixed.units.find(u=>u.unitId==='cardboard').anchor={x:3,y:4};
fs.writeFileSync(path.join(output,'mixed-scenario.json'),JSON.stringify(mixed,null,2));
fs.writeFileSync(path.join(output,'mixed.json'),JSON.stringify({steps:[...setup(mixed),{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'select',target:'#three-camera-projection',value:'perspective'},{action:'select',target:'#three-camera-fov',value:'65'},{action:'screenshot',name:'mixed.png'},
  {action:'click',target:'#three-lighting-controls > summary'},{action:'select',target:'#three-lighting-enabled',value:'off'},{action:'screenshot',name:'unlit.png'},
  {action:'select',target:'#three-lighting-enabled',value:'on'},{action:'click',target:'#three-camera-turn-right'},{action:'screenshot',name:'angled.png'},
  {action:'click',target:'#three-camera-overhead'},{action:'screenshot',name:'overhead.png'},{action:'resize',width:600,height:850},{action:'screenshot',name:'narrow.png'},
  {action:'console'},{action:'requests'}]},null,2));
console.log(output);
const fog={board:{width:14,height:5,defaultTerrain:'StoneFloor',cells:[],edges:[]},fogOfWarSides:['blue'],unitTypeIds:['barbarian-type','qa-miniature-type','qa-observer-type'],
  unitTypes:[{id:'qa-miniature-type',displayName:'QA Patrol',mov:3,rng:1,atk:1,def:1,hp:1},{id:'qa-observer-type',displayName:'QA Observer',mov:0,rng:0,atk:0,def:1,hp:1,actions:'None'}],
  units:[{unitTypeId:'barbarian-type',sideId:'blue',anchor:{x:1,y:2},unitId:'hero'},{unitTypeId:'qa-observer-type',sideId:'blue',anchor:{x:1,y:3},unitId:'observer'},{unitTypeId:'qa-miniature-type',sideId:'red',anchor:{x:8,y:2},unitId:'patrol'}],
  agency:[{unitTypeId:'barbarian-type',sideId:'blue',controller:'Human'},{unitTypeId:'qa-observer-type',sideId:'blue',controller:'Automated'},{unitTypeId:'qa-miniature-type',sideId:'red',controller:'Automated'}],
  objectives:[{sideId:'blue',objective:'NoObjective'},{sideId:'red',objective:'NoObjective'}],
  orders:[{unitTypeId:'qa-miniature-type',sideId:'red',kind:'FollowRoute',route:{routeId:'patrol',currentIndex:1,direction:1}}],
  waypoints:[{id:'hidden',area:{x:8,y:2,w:1,h:1}},{id:'known',area:{x:5,y:2,w:1,h:1}}],routes:[{id:'patrol',waypointIds:['hidden','known'],mode:'PingPong'}]};
const fogSteps=[...setup(fog),{action:'select',target:'#three-unit-representation',value:'miniatures'},{action:'select',target:'#three-camera-projection',value:'perspective'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-gameplay > summary'},{action:'click',target:'#auto'},{action:'click',target:'#tools-toggle'},
  {action:'screenshot',name:'hidden.png'},{action:'click',target:'#start-round'},{action:'click',target:'.three-board-choices > summary'},
  {action:'click',target:'[data-board-choice-kind="stay"]'},{action:'click',target:'#end-turn'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-developer > summary'},{action:'click',target:'#refresh'},{action:'click',target:'.three-diagnostics > summary'},{action:'snapshot',target:'.three-diagnostics',name:'movement-timings.yaml'},
  {action:'click',target:'#tools-toggle'},{action:'screenshot',name:'entered.png'},
  {action:'click',target:'#start-round'},{action:'click',target:'[data-board-choice-kind="stay"]'},{action:'click',target:'#end-turn'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#refresh'},{action:'click',target:'#tools-toggle'},{action:'screenshot',name:'left.png'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#restart-scenario'},{action:'screenshot',name:'restart.png'},{action:'console'},{action:'requests'}];
fs.writeFileSync(path.join(output,'fog.json'),JSON.stringify({steps:fogSteps},null,2));fs.writeFileSync(path.join(output,'fog-scenario.json'),JSON.stringify(fog,null,2));
const switching=[...setup(scenario(10,16,12)),{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-developer > summary'},{action:'click',target:'.three-diagnostics > summary'},{action:'snapshot',target:'.three-diagnostics',name:'before.yaml'}];
for(let i=0;i<5;i++)switching.push({action:'select',target:'#three-unit-representation',value:'cardboard'},{action:'select',target:'#three-unit-representation',value:'miniatures'});
for(const value of ['three','classic','enhanced','tabletop'])switching.push({action:'select',target:'#board-view',value});
switching.push({action:'snapshot',target:'.three-diagnostics',name:'after.yaml'},{action:'click',target:'#tools-toggle'},{action:'screenshot',name:'switched.png'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#restart-scenario'},{action:'screenshot',name:'restart.png'},
  {action:'reload'},{action:'snapshot',name:'reload.yaml'},{action:'console'},{action:'requests'});
fs.writeFileSync(path.join(output,'switching.json'),JSON.stringify({steps:switching},null,2));
const posture=scenario(1,10,8,true);posture.unitTypes.push({id:'grunt-type',displayName:'QA Cardboard',mov:0,rng:0,atk:0,def:1,hp:1,actions:'None'});posture.agency[1].controller='Automated';
fs.writeFileSync(path.join(output,'posture.json'),JSON.stringify({steps:[...setup(posture),{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'screenshot',name:'lying.png'},{action:'click',target:'#start-round'},{action:'click',target:'#start-round'},{action:'click',target:'#three-camera-turn-right'},
  {action:'screenshot',name:'upright.png'},{action:'snapshot',name:'upright.yaml'},{action:'console'},{action:'requests'}]},null,2));
const failures=scenario(1,12,8);for(const [i,type]of ['qa-missing-type','qa-invalid-type'].entries()){
  failures.unitTypeIds.push(type);failures.unitTypes.push({id:type,displayName:type==='qa-missing-type'?'QA Missing GLB':'QA Invalid GLB',mov:1,rng:1,atk:1,def:1,hp:1});failures.units.push({unitTypeId:type,sideId:'blue',anchor:{x:4+i,y:2},unitId:type});failures.agency.push({unitTypeId:type,sideId:'blue',controller:'Human'});}
fs.writeFileSync(path.join(output,'failures.json'),JSON.stringify({steps:[...setup(failures),{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-developer > summary'},{action:'click',target:'.three-diagnostics > summary'},{action:'snapshot',target:'.three-diagnostics',name:'failures.yaml'},
  {action:'select',target:'#three-unit-representation',value:'cardboard'},{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'snapshot',target:'.three-diagnostics',name:'retained.yaml'},{action:'click',target:'#tools-toggle'},{action:'screenshot',name:'fallback.png'},{action:'console'},{action:'requests'}]},null,2));
const combat={board:{width:10,height:8,defaultTerrain:'StoneFloor',cells:[],edges:[]},unitTypeIds:['qa-miniature-type','qa-observer-type'],
  unitTypes:[{id:'qa-miniature-type',displayName:'QA Attacker',mov:1,rng:1,atk:8,def:0,hp:1,undying:{}},{id:'qa-observer-type',displayName:'QA Defender',mov:0,rng:0,atk:0,def:0,hp:1,actions:'None',undying:{}}],
  units:[{unitTypeId:'qa-miniature-type',sideId:'red',anchor:{x:3,y:3},unitId:'attacker'},{unitTypeId:'qa-observer-type',sideId:'blue',anchor:{x:3,y:2},unitId:'defender'}],
  agency:[{unitTypeId:'qa-miniature-type',sideId:'red',controller:'Human'},{unitTypeId:'qa-observer-type',sideId:'blue',controller:'Automated'}],objectives:[{sideId:'blue',objective:'NoObjective'},{sideId:'red',objective:'NoObjective'}]};
fs.writeFileSync(path.join(output,'combat.json'),JSON.stringify({steps:[...setup(combat),{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'screenshot',name:'initial-facing.png'},{action:'click',target:'#start-round'},{action:'click',target:'.three-board-choices > summary'},
  {action:'click',target:'[data-board-choice-kind="stay"]'},{action:'click',target:'[data-board-choice-kind="attack"]'},
  {action:'snapshot',name:'attack.yaml'},{action:'screenshot',name:'attack-facing.png'},{action:'click',target:'.primary-roll'},
  {action:'click',target:'#three-camera-angle'},{action:'screenshot',name:'combat-result.png'},
  {action:'snapshot',name:'completed.yaml'},{action:'console'},{action:'requests'}]},null,2));
fs.writeFileSync(path.join(output,'combat-settled.json'),JSON.stringify({steps:[{action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-display > summary'},{action:'select',target:'#board-view',value:'tabletop'},
  {action:'click',target:'#tools-toggle'},{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'select',target:'#three-camera-projection',value:'perspective'},{action:'select',target:'#three-camera-fov',value:'65'},{action:'screenshot',name:'lying-result.png'},{action:'snapshot',name:'settled.yaml'},{action:'console'},{action:'requests'}]},null,2));
fs.writeFileSync(path.join(output,'generator.json'),JSON.stringify({steps:[{action:'click',target:'#open-generator'},
  {action:'fill',target:'#generator-code-input',value:'DNG1-HVM-30x20-R12-E2-C2-D3-N2-L3-OR2-F1-S0000000000000001'},
  {action:'click',target:'#generator-code-play'},{action:'hover',target:'#generated-game-code'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-display > summary'},{action:'select',target:'#board-view',value:'tabletop'},
  {action:'click',target:'#tools-toggle'},{action:'select',target:'#three-unit-representation',value:'miniatures'},{action:'select',target:'#three-camera-mode',value:'full'},
  {action:'select',target:'#three-camera-projection',value:'perspective'},{action:'screenshot',name:'generated-unseen.png'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-developer > summary'},{action:'click',target:'.three-diagnostics > summary'},{action:'snapshot',target:'.three-diagnostics',name:'generated.yaml'},
  {action:'console'},{action:'requests'}]},null,2));
const textured=scenario(1,10,8);textured.unitTypeIds[0]=textured.unitTypes[0].id=textured.units[0].unitTypeId=textured.agency[0].unitTypeId='qa-textured-type';textured.unitTypes[0].displayName='QA Textured Figure';
fs.writeFileSync(path.join(output,'textured.json'),JSON.stringify({steps:[...setup(textured),{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'click',target:'#three-lighting-controls > summary'},{action:'select',target:'#three-lighting-enabled',value:'off'},{action:'screenshot',name:'texture-off.png'},
  {action:'select',target:'#three-lighting-enabled',value:'on'},{action:'click',target:'#three-camera-turn-right'},{action:'screenshot',name:'texture-on.png'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-developer > summary'},{action:'click',target:'.three-diagnostics > summary'},{action:'snapshot',target:'.three-diagnostics',name:'texture.yaml'},
  {action:'console'},{action:'requests'}]},null,2));
module.exports={scenario,transport,setup};
