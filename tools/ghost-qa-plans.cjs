// Dedicated presentation fixture; no dependency on mutable playtest scenarios.
const fs=require('node:fs'),path=require('node:path'),zlib=require('node:zlib');
const output=path.resolve(__dirname,'../.playwright-cli/ghost-qa');fs.mkdirSync(output,{recursive:true});
const cells=[];
for(let y=0;y<6;y++)for(let x=0;x<9;x++)cells.push({position:{x,y},terrain:x<3?'StoneFloor':x<6?'Grass':'Asphalt'});
const scenario={board:{width:9,height:6,defaultTerrain:'StoneFloor',cells,edges:[]},unitTypeIds:['ghost-type','barbarian-type'],
  units:[1,4,7].map((x,i)=>({unitTypeId:'ghost-type',sideId:'red',anchor:{x,y:2},unitId:'ghost-'+i})),
  agency:[{unitTypeId:'ghost-type',sideId:'red',controller:'Human'},{unitTypeId:'barbarian-type',sideId:'blue',controller:'Human'}],
  objectives:['blue','red'].map(sideId=>({sideId,objective:'NoObjective'}))};
scenario.units.push({unitTypeId:'barbarian-type',sideId:'blue',anchor:{x:4,y:4},unitId:'hero'});
const steps=[{action:'click',target:'#tools-toggle'},
  {action:'fill',target:'#scenario-transport',value:'DELVE1:'+zlib.gzipSync(Buffer.from(JSON.stringify(scenario))).toString('base64url')},
  {action:'click',target:'#import-scenario'},{action:'click',target:'#tools-display > summary'},
  {action:'select',target:'#board-view',value:'tabletop'},{action:'click',target:'#tools-toggle'},
  {action:'select',target:'#three-unit-representation',value:'miniatures'},
  {action:'select',target:'#three-camera-mode',value:'full'},{action:'click',target:'#three-camera-angle'},
  {action:'screenshot',name:'loading.png'},{action:'click',target:'#three-lighting-controls > summary'},
  {action:'screenshot',name:'lighting-on.png'},
  {action:'select',target:'#three-lighting-enabled',value:'off'},{action:'screenshot',name:'lighting-off.png'},
  {action:'select',target:'#three-lighting-enabled',value:'on'},
  {action:'select',target:'#three-lighting-key',value:'0'},{action:'select',target:'#three-lighting-fill',value:'0'},
  {action:'screenshot',name:'dark.png'},
  {action:'select',target:'#three-lighting-key',value:'0.85'},{action:'select',target:'#three-lighting-fill',value:'0.65'},
  {action:'click',target:'#three-camera-turn-right'},{action:'screenshot',name:'side.png'},
  {action:'click',target:'#three-camera-overhead'},{action:'screenshot',name:'overhead.png'},
  {action:'click',target:'#three-camera-angle'},
  {action:'click',target:'#tools-toggle'},{action:'click',target:'#tools-developer > summary'},
  {action:'click',target:'.three-diagnostics > summary'},{action:'snapshot',target:'.three-diagnostics',name:'before.yaml'},
  ...['enhanced','three','tabletop'].map(value=>({action:'select',target:'#board-view',value})),
  {action:'snapshot',target:'.three-diagnostics',name:'after.yaml'},{action:'click',target:'#tools-toggle'},
  {action:'screenshot',name:'switched.png'},{action:'console'},{action:'requests'}];
fs.writeFileSync(path.join(output,'views.json'),JSON.stringify({steps},null,2));
