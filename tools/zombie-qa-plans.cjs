// Production Zombie acceptance: the model is never registered by a scenario.
// Reuse the test-owned movement/combat setups, replacing the fixture Type with
// canonical Zombie. Normal mixed/performance setups use canonical definitions.
const fs=require('node:fs'),path=require('node:path'),zlib=require('node:zlib');
require('./miniature-qa-plans.cjs');
const input=path.resolve(__dirname,'../.playwright-cli/miniature-qa'),output=path.resolve(__dirname,'../.playwright-cli/zombie-qa');fs.mkdirSync(output,{recursive:true});
for(const name of fs.readdirSync(input).filter(n=>n.endsWith('.json')&&!n.includes('scenario')&&!['failures.json','textured.json'].includes(n))){
  const plan=JSON.parse(fs.readFileSync(path.join(input,name)));
  for(const step of plan.steps)if(step.target==='#scenario-transport'){
    let s=JSON.parse(zlib.gunzipSync(Buffer.from(step.value.slice(7),'base64url')));
    s=JSON.parse(JSON.stringify(s).replaceAll('qa-miniature-type','zombie-type').replaceAll('QA Static Figure','Zombie'));
    if(!['fog.json','combat.json'].includes(name))s.unitTypes=s.unitTypes.filter(t=>t.id!=='zombie-type');
    step.value='DELVE1:'+zlib.gzipSync(Buffer.from(JSON.stringify(s))).toString('base64url');
  }
  fs.writeFileSync(path.join(output,name),JSON.stringify(plan,null,2));
}
console.log(output);
const {scenario,setup}=require('./miniature-qa-plans.cjs');
const mixed=scenario(3,12,10,true);mixed.unitTypeIds[0]='zombie-type';mixed.units.forEach(u=>{if(u.unitTypeId==='qa-miniature-type')u.unitTypeId='zombie-type';});mixed.agency[0].unitTypeId='zombie-type';mixed.unitTypes=[];mixed.units.at(-1).anchor={x:3,y:4};
fs.writeFileSync(path.join(output,'views-settled.json'),JSON.stringify({steps:[...setup(mixed),
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-gameplay > summary'},{action:'click',target:'#animate'},{action:'click',target:'#tools-toggle'},
  {action:'screenshot',name:'cardboard.png'},{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'click',target:'#three-lighting-controls > summary'},{action:'select',target:'#three-lighting-enabled',value:'off'},
  {action:'select',target:'#three-camera-projection',value:'perspective'},{action:'select',target:'#three-camera-fov',value:'65'},
  {action:'click',target:'#three-camera-angle'},{action:'click',target:'#three-camera-in'},
  {action:'click',target:'#three-camera-in'},{action:'screenshot',name:'perspective-off.png'},
  {action:'select',target:'#three-lighting-enabled',value:'on'},{action:'select',target:'#three-lighting-fill',value:'1'},
  {action:'select',target:'#three-lighting-azimuth',value:'135'},{action:'screenshot',name:'perspective-on.png'},
  {action:'click',target:'#three-camera-overhead'},{action:'screenshot',name:'overhead.png'},
  {action:'select',target:'#three-camera-projection',value:'orthographic'},{action:'screenshot',name:'orthographic.png'},
  {action:'click',target:'#three-camera-angle'},{action:'resize',width:600,height:850},{action:'screenshot',name:'narrow.png'},
  {action:'console'},{action:'requests'}]},null,2));

const read=name=>JSON.parse(fs.readFileSync(path.join(output,name+'.json')));
function changeScenario(plan,change){const step=plan.steps.find(s=>s.target==='#scenario-transport'),s=JSON.parse(zlib.gunzipSync(Buffer.from(step.value.slice(7),'base64url')));change(s);step.value='DELVE1:'+zlib.gzipSync(Buffer.from(JSON.stringify(s))).toString('base64url');}
const warm=read('fog');changeScenario(warm,s=>{s.unitTypeIds=['zombie-type'];s.unitTypes=s.unitTypes.filter(t=>t.id==='zombie-type');s.units=s.units.filter(u=>u.unitId!=='observer');s.units.find(u=>u.unitId==='hero').unitTypeId='zombie-type';s.agency=s.agency.filter(a=>a.unitTypeId!=='qa-observer-type');s.agency[0].unitTypeId='zombie-type';});
// This fixture preloads the shared source through an observed friendly Zombie.
// Auto completes its turn after Stay; no disabled End Turn click is necessary.
warm.steps=warm.steps.filter(s=>s.target!=='#end-turn');fs.writeFileSync(path.join(output,'fog-warm.json'),JSON.stringify(warm,null,2));
const instant=read('combat');instant.steps.splice(6,0,{action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-gameplay > summary'},{action:'click',target:'#animate'},{action:'click',target:'#tools-toggle'});
instant.steps.splice(instant.steps.findIndex(s=>s.action==='console'),0,{action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-developer > summary'},{action:'click',target:'#refresh'},{action:'click',target:'#tools-toggle'},{action:'screenshot',name:'settled-attack.png'});
fs.writeFileSync(path.join(output,'combat-instant.json'),JSON.stringify(instant,null,2));
const move=structuredClone(instant);changeScenario(move,s=>{s.unitTypes=s.unitTypes.filter(t=>t.id!=='zombie-type');});
move.steps=move.steps.filter(s=>!['[data-board-choice-kind="attack"]','.primary-roll'].includes(s.target)&&!['snapshot','screenshot'].includes(s.action));
move.steps.find(s=>s.target==='[data-board-choice-kind="stay"]').target='[data-board-choice-cell="3-4"]';
move.steps.splice(move.steps.findIndex(s=>s.target==='#start-round'),0,{action:'click',target:'#three-lighting-controls > summary'},{action:'select',target:'#three-lighting-enabled',value:'off'},{action:'select',target:'#three-camera-projection',value:'perspective'},{action:'select',target:'#three-camera-fov',value:'65'},{action:'screenshot',name:'before-move.png'});
move.steps.splice(move.steps.findIndex(s=>s.action==='console'),0,{action:'click',target:'#three-camera-in'},{action:'click',target:'#three-camera-in'},{action:'screenshot',name:'move-positive-z-front.png'},{action:'snapshot',name:'move-result.yaml'});
fs.writeFileSync(path.join(output,'move.json'),JSON.stringify(move,null,2));
