// Test-owned scenarios exercise the ordinary production catalog, without model
// registration overrides, special routes, or changes to authored playtest content.
const fs=require('node:fs'),path=require('node:path'),zlib=require('node:zlib');
require('./zombie-qa-plans.cjs');
const {scenario,setup}=require('./miniature-qa-plans.cjs'),input=path.resolve(__dirname,'../.playwright-cli/zombie-qa'),output=path.resolve(__dirname,'../.playwright-cli/troll-qa');fs.mkdirSync(output,{recursive:true});
const save=(name,steps)=>fs.writeFileSync(path.join(output,name+'.json'),JSON.stringify({steps},null,2));
for(const name of ['fog-warm','combat-instant','move','generator']){
  const plan=JSON.parse(fs.readFileSync(path.join(input,name+'.json')));
  for(const step of plan.steps)if(step.target==='#scenario-transport'){
    const s=JSON.parse(zlib.gunzipSync(Buffer.from(step.value.slice(7),'base64url')));
    const changed=JSON.parse(JSON.stringify(s).replaceAll('zombie-type','troll-type').replaceAll('Zombie','Troll'));
    step.value='DELVE1:'+zlib.gzipSync(Buffer.from(JSON.stringify(changed))).toString('base64url');
  }
  save(name,plan.steps);
}
function trolls(count,width=30,height=20,lying=false){
  const s=scenario(count,width,height,lying);s.unitTypeIds[0]='troll-type';s.units.forEach(u=>{if(u.unitTypeId==='qa-miniature-type')u.unitTypeId='troll-type';});s.agency[0].unitTypeId='troll-type';s.unitTypes=[];return s;
}
for(const count of [1,10,30,100])save('performance-'+count,[...setup(trolls(count)),
  {action:'select',target:'#three-unit-representation',value:'miniatures'},{action:'select',target:'#three-camera-mode',value:'full'},
  {action:'select',target:'#three-camera-projection',value:'perspective'},{action:'select',target:'#three-camera-fov',value:'65'},
  {action:'screenshot',name:'loaded.png'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-developer > summary'},{action:'click',target:'.three-diagnostics > summary'},
  {action:'click',target:'#three-reset-sample'},
  ...Array.from({length:8},(_,i)=>[{action:'click',target:i%2?'#three-camera-turn-left':'#three-camera-turn-right'},{action:'screenshot',name:`warm-${i}.png`}]).flat(),
  {action:'snapshot',target:'.three-diagnostics',name:'warm.yaml'},{action:'console'},{action:'requests'}]);
const mixed=trolls(3,12,10,true);mixed.units.at(-1).anchor={x:3,y:4};mixed.unitTypeIds.push('zombie-type');mixed.units.push({unitTypeId:'zombie-type',sideId:'red',anchor:{x:3,y:3},unitId:'zombie'});mixed.agency.push({unitTypeId:'zombie-type',sideId:'red',controller:'Human'});
const views=JSON.parse(fs.readFileSync(path.join(input,'views-settled.json'))).steps;save('views',[...setup(mixed),...views.slice(6)]);
const posture=trolls(1,10,8,true);posture.unitTypes.push({id:'grunt-type',displayName:'QA Cardboard',mov:0,rng:0,atk:0,def:1,hp:1,actions:'None'});posture.agency[1].controller='Automated';
save('posture',[...setup(posture),{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'screenshot',name:'lying.png'},{action:'click',target:'#start-round'},{action:'click',target:'#start-round'},{action:'click',target:'#three-camera-turn-right'},
  {action:'screenshot',name:'upright.png'},{action:'snapshot',name:'upright.yaml'},{action:'console'},{action:'requests'}]);
const switching=JSON.parse(fs.readFileSync(path.join(input,'switching.json'))).steps;save('switching',[...setup(trolls(10,16,12)),...switching.slice(6)]);
const switchPlan=JSON.parse(fs.readFileSync(path.join(output,'switching.json'))).steps,index=switchPlan.findIndex(s=>s.name==='after.yaml')+1;
switchPlan.splice(index,0,...['three','classic','enhanced','tabletop'].map(value=>({action:'select',target:'#board-view',value})),{action:'snapshot',target:'.three-diagnostics',name:'retained.yaml'});save('switching',switchPlan);
save('rotation',[...setup(trolls(1,10,8)),{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-gameplay > summary'},{action:'click',target:'#animate'},{action:'click',target:'#tools-toggle'},
  {action:'select',target:'#three-camera-projection',value:'perspective'},{action:'select',target:'#three-camera-fov',value:'65'},
  {action:'click',target:'#three-camera-in'},{action:'click',target:'#three-camera-in'},
  ...Array.from({length:4},(_,i)=>[{action:'screenshot',name:`yaw-${i*90}.png`},{action:'click',target:'#three-camera-turn-right'},{action:'click',target:'#three-camera-turn-right'}]).flat(),
  {action:'console'},{action:'requests'}]);
const revealed=JSON.parse(fs.readFileSync(path.join(output,'fog-warm.json'))),transportStep=revealed.steps.find(s=>s.target==='#scenario-transport'),revealedScenario=JSON.parse(zlib.gunzipSync(Buffer.from(transportStep.value.slice(7),'base64url')));
delete revealedScenario.fogOfWarSides;transportStep.value='DELVE1:'+zlib.gzipSync(Buffer.from(JSON.stringify(revealedScenario))).toString('base64url');save('fog-revealed',revealed.steps);
const door=trolls(1,10,8);door.board.edges=[{position:{x:2,y:2},direction:'Right',kind:'ClosedDoor'}];door.unitTypeIds=['troll-type'];door.units=door.units.filter(u=>u.unitTypeId==='troll-type');door.agency=door.agency.filter(a=>a.unitTypeId==='troll-type');door.objectives=door.objectives.filter(o=>o.sideId==='blue');
save('door-start',[...setup(door),{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-gameplay > summary'},{action:'click',target:'#animate'},{action:'click',target:'#tools-toggle'},
  {action:'click',target:'#start-round'},{action:'click',target:'.three-board-choices > summary'},{action:'click',target:'[data-board-choice-kind="stay"]'},
  {action:'snapshot',name:'door-choices.yaml'},{action:'screenshot',name:'choices.png'},
  {action:'click',target:'[data-board-choice-kind="special"]'},{action:'screenshot',name:'door-facing.png'},
  {action:'click',target:'.primary-roll'},{action:'snapshot',name:'door-result.yaml'},{action:'screenshot',name:'door-result.png'},{action:'console'},{action:'requests'}]);
const picking=JSON.parse(fs.readFileSync(path.join(output,'door-start.json'))).steps;
const start=picking.findIndex(s=>s.target==='#start-round');picking.splice(start,0,{action:'select',target:'#three-camera-mode',value:'fixed'},{action:'click',target:'#three-camera-overhead'});
picking.find(s=>s.target==='[data-board-choice-kind="stay"]').target='#three-viewport canvas';save('picking',picking);
const generated=JSON.parse(fs.readFileSync(path.join(output,'generator.json'))).steps;
generated.find(s=>s.target==='#generator-code-input').value=generated.find(s=>s.target==='#generator-code-input').value.replace('-F1-','-F0-');save('generator-revealed',generated);
save('reload',[...setup(trolls(1,10,8)),{action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'screenshot',name:'before.png'},{action:'reload'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-display > summary'},{action:'select',target:'#board-view',value:'tabletop'},
  {action:'click',target:'#tools-developer > summary'},{action:'click',target:'.three-diagnostics > summary'},{action:'snapshot',target:'.three-diagnostics',name:'reload.yaml'},
  {action:'click',target:'#tools-toggle'},{action:'screenshot',name:'restored.png'},{action:'console'},{action:'requests'}]);
console.log(output);
