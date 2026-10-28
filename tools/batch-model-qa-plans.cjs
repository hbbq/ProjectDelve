// Dedicated gameplay fixtures and data-only restricted Edge plans. Production
// catalog only; mutable playtest scenarios and shipped registrations stay intact.
const fs=require('node:fs'),path=require('node:path'),zlib=require('node:zlib');
require('./troll-qa-plans.cjs');
const {setup}=require('./miniature-qa-plans.cjs'),out=path.resolve(__dirname,'../.playwright-cli/batch-qa');fs.mkdirSync(out,{recursive:true});
const slugs=['goblin','barbarian','cleric','hobby-knight','worker','zombie','troll'];
const save=(name,steps)=>{if(steps.length>40)throw Error(name+' exceeds plan budget');fs.writeFileSync(path.join(out,name+'.json'),JSON.stringify({steps},null,2));};
const base=(copies=1)=>({board:{width:12,height:8,defaultTerrain:'StoneFloor',cells:[],edges:[]},unitTypeIds:slugs.map(s=>s+'-type'),unitTypes:[],
 units:slugs.flatMap((s,i)=>Array.from({length:copies},(_,j)=>({unitTypeId:s+'-type',sideId:j%2?'red':'blue',anchor:{x:2+i,y:2+j*2},unitId:s+'-'+j}))),
 agency:slugs.flatMap(s=>['blue','red'].map(sideId=>({unitTypeId:s+'-type',sideId,controller:'Human'}))),objectives:['blue','red'].map(sideId=>({sideId,objective:'NoObjective'}))});
const view=[{action:'select',target:'#three-unit-representation',value:'miniatures'},{action:'select',target:'#three-camera-mode',value:'full'},{action:'select',target:'#three-camera-projection',value:'perspective'},{action:'select',target:'#three-camera-fov',value:'65'},{action:'click',target:'#three-camera-angle'}];
const diag=[{action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-developer > summary'},{action:'click',target:'.three-diagnostics > summary'}];
const end=[{action:'console'},{action:'requests'}];
const instant=[{action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-gameplay > summary'},{action:'click',target:'#animate'},{action:'click',target:'#tools-toggle'}];
save('views',[...setup(base(2)),...instant,...view,{action:'click',target:'#three-camera-in'},{action:'screenshot',name:'mixed-on.png'},
 {action:'click',target:'#three-lighting-controls > summary'},{action:'select',target:'#three-lighting-enabled',value:'off'},{action:'screenshot',name:'mixed-off.png'},
 {action:'select',target:'#three-lighting-enabled',value:'on'},{action:'click',target:'#three-camera-turn-right'},{action:'click',target:'#three-camera-turn-right'},{action:'screenshot',name:'side.png'},
 {action:'click',target:'#three-camera-turn-right'},{action:'click',target:'#three-camera-turn-right'},{action:'screenshot',name:'rear.png'},
 {action:'click',target:'#three-camera-overhead'},{action:'select',target:'#three-camera-projection',value:'orthographic'},{action:'screenshot',name:'overhead.png'},
 {action:'click',target:'#three-camera-angle'},{action:'resize',width:600,height:850},{action:'screenshot',name:'narrow.png'},...end]);
for(const copies of [1,2])save('performance-'+copies,[...setup(base(copies)),...view,{action:'screenshot',name:'loaded.png'},...diag,{action:'click',target:'#three-reset-sample'},
 ...Array.from({length:8},(_,i)=>[{action:'click',target:i%2?'#three-camera-turn-left':'#three-camera-turn-right'},{action:'screenshot',name:'warm-'+i+'.png'}]).flat(),
 {action:'snapshot',target:'.three-diagnostics',name:'warm.yaml'},...end]);
for(const slug of slugs.slice(0,5)){
 for(const name of ['move','combat-instant','fog-warm','posture','picking']){
  const plan=JSON.parse(fs.readFileSync(path.resolve(__dirname,`../.playwright-cli/troll-qa/${name}.json`)));
  for(const step of plan.steps)if(step.target==='#scenario-transport'){
   const s=JSON.parse(zlib.gunzipSync(Buffer.from(step.value.slice(7),'base64url'))),changed=JSON.parse(JSON.stringify(s).replaceAll('troll-type',slug+'-type').replaceAll('Troll',slug));
   // Move/Attack/pick fixtures use simple test-owned stats for every Type;
   // canonical Worker has no Attack and Hobby Knight moves only in L shapes.
   if(['move','combat-instant','picking'].includes(name)&&!changed.unitTypes.some(t=>t.id===slug+'-type'))changed.unitTypes.push({id:slug+'-type',displayName:'QA '+slug,mov:3,rng:1,atk:8,def:0,hp:1,undying:{}});
   step.value='DELVE1:'+zlib.gzipSync(Buffer.from(JSON.stringify(changed))).toString('base64url');
  }
  if(name==='picking'){
   plan.steps=plan.steps.slice(0,plan.steps.findIndex(s=>s.target==='[data-board-choice-kind="special"]'));
   plan.steps.push({action:'snapshot',name:'picked.yaml'},{action:'screenshot',name:'picked.png'},...end);
  }
  save(slug+'-'+name,plan.steps);
 }
 // Close views of front/side/back at the same authored production height.
 const s=base();s.board.width=8;s.board.height=6;s.units=s.units.filter(u=>u.unitId===slug+'-0');s.units[0].anchor={x:4,y:3};
 save(slug+'-detail',[...setup(s),...instant,...view,...Array.from({length:6},()=>({action:'click',target:'#three-camera-in'})),
  ...Array.from({length:4},(_,i)=>[{action:'screenshot',name:'yaw-'+i+'.png'},{action:'click',target:'#three-camera-turn-right'},{action:'click',target:'#three-camera-turn-right'}]).flat(),...diag,{action:'snapshot',target:'.three-diagnostics',name:'model.yaml'},...end]);
}
const switchSteps=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../.playwright-cli/troll-qa/switching.json'))).steps;
save('switching',[...setup(base(2)),...switchSteps.slice(6)]);
const reload=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../.playwright-cli/troll-qa/reload.json'))).steps;save('reload',[...setup(base(2)),...reload.slice(6)]);
console.log(out);
