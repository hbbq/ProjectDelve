// Data-only acceptance plans; all scenes are dedicated QA content.
const fs=require('node:fs'),path=require('node:path'),zlib=require('node:zlib');
require('./troll-qa-plans.cjs');
const {setup}=require('./miniature-qa-plans.cjs'),input=path.resolve(__dirname,'../.playwright-cli/troll-qa'),output=path.resolve(__dirname,'../.playwright-cli/miniature-base-qa');
fs.mkdirSync(output,{recursive:true});
const read=n=>JSON.parse(fs.readFileSync(path.join(input,n+'.json'))),save=(n,steps)=>fs.writeFileSync(path.join(output,n+'.json'),JSON.stringify({steps},null,2));
for(const n of ['views','rotation','move','combat-instant','door-start','picking','posture','fog-warm','switching','reload'])save(n,read(n).steps);
const s=JSON.parse(zlib.gunzipSync(Buffer.from(read('views').steps.find(s=>s.target==='#scenario-transport').value.slice(7),'base64url')));
s.board.width=8;s.board.height=6;s.units.filter(u=>u.unitTypeId==='troll-type').forEach(u=>u.posture='Upright');
save('adjacent',[
  ...setup(s),{action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-gameplay > summary'},{action:'click',target:'#animate'},{action:'click',target:'#tools-toggle'},
  {action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'select',target:'#three-camera-mode',value:'full'},{action:'select',target:'#three-camera-projection',value:'perspective'},{action:'select',target:'#three-camera-fov',value:'65'},
  {action:'click',target:'#three-camera-angle'},{action:'click',target:'#three-camera-in'},{action:'click',target:'#three-camera-in'},
  {action:'screenshot',name:'adjacent-trolls-zombie.png'},
  {action:'click',target:'#three-lighting-controls > summary'},{action:'select',target:'#three-lighting-enabled',value:'off'},{action:'screenshot',name:'adjacent-off.png'},
  {action:'select',target:'#three-lighting-enabled',value:'on'},{action:'click',target:'#three-camera-overhead'},{action:'screenshot',name:'adjacent-overhead.png'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-developer > summary'},{action:'click',target:'.three-diagnostics > summary'},{action:'snapshot',target:'.three-diagnostics',name:'bases.yaml'},
  {action:'click',target:'#tools-toggle'},{action:'resize',width:600,height:850},{action:'screenshot',name:'adjacent-narrow.png'},
  {action:'console'},{action:'requests'}
]);
console.log(output);
