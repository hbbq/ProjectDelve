// Manual inbox only. Review/validation stays explicit; preparation reuses the
// production exporter. No filename inference beyond exact catalog Unit slugs.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{pathToFileURL}=require('node:url');
const {inspect}=require('./inspect-glb.cjs'),{prepare}=require('./prepare-unit-model.cjs');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
async function importModels(root,{apply=false,only=null,textureLimit=null,rotationYDegrees=0,canonicalTypes=[]}={}){
  const inbox=path.join(root,'assets/import/models'),catalogPath=path.join(root,'ProjectDelve.Web/wwwroot/art/models/unit-visuals.json');
  const catalog=JSON.parse(fs.readFileSync(catalogPath)),results=[];
  const {inspectGlb}=await import(pathToFileURL(path.join(__dirname,'../ProjectDelve.Web/wwwroot/three-models.js')));
  const {miniatureBaseLayout,maximumModelRadiusForFootprint}=await import(pathToFileURL(path.join(__dirname,'../ProjectDelve.Web/wwwroot/unit-visuals.js')));
  for(const entry of fs.existsSync(inbox)?fs.readdirSync(inbox,{withFileTypes:true}).sort((a,b)=>a.name.localeCompare(b.name)):[]){
    if(!entry.isFile()||!entry.name.endsWith('.glb')||(only&&!only.includes(entry.name.slice(0,-4))))continue;
    const slug=entry.name.slice(0,-4),unit=catalog.units.find(u=>u.typeId===slug+'-type'),result={file:entry.name};results.push(result);
    try{
      if(!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(slug)||!unit)throw Error('No exact canonical Unit slug; left in inbox');
      const sourcePath=`assets/source/models/units/${slug}/original.glb`,runtimePath=`ProjectDelve.Web/wwwroot/art/models/units/${slug}/v1/${slug}.glb`,provenancePath=path.join(root,`assets/source/models/units/${slug}/provenance.json`);
      if(unit.model||catalog.models.some(m=>m.id===slug+'/v1')||fs.existsSync(path.join(root,sourcePath))||fs.existsSync(path.join(root,runtimePath)))throw Error('Existing model/source/registration; replacement requires separate review');
      const cellSpan=canonicalTypes.find(t=>t.unitTypeId===unit.typeId)?.cellSpan;
      if(![1,2].includes(cellSpan))throw Error('Canonical footprint required: supply the public designer catalog');
      if(unit.maximumRadius>maximumModelRadiusForFootprint(cellSpan))throw Error('Model radius exceeds footprint budget');
      const input=path.join(inbox,entry.name),bytes=fs.readFileSync(input),before=hash(bytes),prepared=prepare(bytes,before,{textureLimit,rotationYDegrees}),runtimeHash=hash(prepared);
      inspectGlb(prepared.buffer.slice(prepared.byteOffset,prepared.byteOffset+prepared.byteLength));
      const sourceInfo=await inspect(input,unit.targetHeight),info=await inspect(input,unit.targetHeight,prepared),base=miniatureBaseLayout({width:cellSpan,baseRadius:unit.baseRadius,contact:info.normalization.contact});
      if([sourceInfo,info].some((scan,index)=>!scan.finite||scan.hierarchy.some(n=>!Number.isFinite(n.determinant)||n.determinant<=0)||scan.triangles>12000||scan.primitives>2||scan.images.some(i=>!i.width||!i.height||Math.max(i.width,i.height)>(index===0?2048:textureLimit??2048))))throw Error('Static model exceeds production geometry/texture/transform contract');
      if(info.normalization.maximumRadius>unit.maximumRadius+1e-6)throw Error(`Hard radius incompatibility: ${info.normalization.maximumRadius} > ${unit.maximumRadius}`);
      Object.assign(result,{state:'compatible-needs-review',typeId:unit.typeId,sourceSha256:before,runtimeSha256:runtimeHash,triangles:info.triangles,dimensions:info.normalization.dimensions,radius:info.normalization.maximumRadius,contact:info.normalization.contact,base,inspection:sourceInfo,runtimeInspection:info});
      if(!apply)continue;
      const p=JSON.parse(fs.readFileSync(provenancePath));
      if(p.status!=='reviewed-import'||p.generator!=='Tripo'||p.plan!=='Pro'||!p.planEvidence||p.commerciallyCleared!==true||p.redistributionRightsConfirmed!==true||!p.rightsReview?.basis||!p.preparation?.orientation)throw Error('Reviewed paid provenance and orientation required');
      if(p.original.path!==sourcePath||p.original.sha256!==before||p.runtime.path!==runtimePath||p.runtime.sha256!==runtimeHash)throw Error('Review hashes/paths differ from this import');
      if((p.preparation?.textureLimit??null)!==textureLimit)throw Error('Texture limit differs from reviewed preparation');
      if((p.preparation?.rotationYDegrees??0)!==rotationYDegrees)throw Error('Orientation differs from reviewed preparation');
      if(p.validation?.source?.errors!==0||p.validation?.runtime?.errors!==0||!p.validation.validator||!p.validation.version)throw Error('Review source and prepared Khronos validation before importing');
      for(const ref of [p.projectPortraitReference,...(p.projectConceptReferences??[])])if(!ref||!fs.existsSync(path.join(root,ref)))throw Error('Missing recorded reference artwork');
      const model={id:slug+'/v1',approved:true,url:`./art/models/units/${slug}/v1/${slug}.glb`,version:'tripo-v1',maxTriangles:12000,maxPrimitives:2,maxTextureDimension:textureLimit??2048};
      // Keep existing entries, including numeric spelling. Check the small
      // formatting-preserving edit against the parsed catalog before any writes.
      const text=fs.readFileSync(catalogPath,'utf8'),newline=text.includes('\r\n')?'\r\n':'\n';
      const rows=text.match(/    \{\r?\n      "typeId": [\s\S]*?\r?\n    \}/g)??[],row=rows.find(s=>JSON.parse(s).typeId===unit.typeId);
      const marker=newline+'  ],'+newline+'  "units": [';
      if(!row||!text.includes(marker))throw Error('Catalog formatting changed; review registration before retrying');
      const formatted=JSON.stringify(model,null,2).split('\n').map(l=>'    '+l).join(newline);
      const updated=text.replace(marker,','+newline+formatted+marker).replace(row,row.slice(0,-(newline.length+5))+','+newline+'      "model": '+JSON.stringify(slug+'/v1')+newline+'    }');
      const expected=structuredClone(catalog);expected.models.push(model);expected.units.find(u=>u.typeId===unit.typeId).model=slug+'/v1';
      if(JSON.stringify(JSON.parse(updated))!==JSON.stringify(expected))throw Error('Catalog edit did not match intended registration');
      // Exclusive copies prevent overwriting approved assets. Inbox remains until
      // both copies, provenance and catalog have succeeded. Failure retains input.
      fs.mkdirSync(path.dirname(path.join(root,runtimePath)),{recursive:true});
      fs.writeFileSync(path.join(root,runtimePath),prepared,{flag:'wx'});
      fs.copyFileSync(input,path.join(root,sourcePath),fs.constants.COPYFILE_EXCL);
      const after=hash(fs.readFileSync(path.join(root,sourcePath)));
      if(after!==before||hash(fs.readFileSync(input))!==before)throw Error('Source changed during import; inbox retained');
      p.status='approved-production';p.original={...p.original,bytes:bytes.length,preservedUnchanged:true};p.runtime={...p.runtime,bytes:prepared.length};
      p.import={inboxPath:`assets/import/models/${entry.name}`,sha256BeforeMove:before,sha256AfterMove:after};
      p.inspection={...sourceInfo,file:sourcePath};p.runtimeInspection={...info,file:runtimePath};p.compatibility={targetHeight:unit.targetHeight,maximumRadius:unit.maximumRadius,baseRadius:unit.baseRadius,measuredMaximumRadius:info.normalization.maximumRadius,contact:info.normalization.contact,base,logicalFootprint:'unchanged'};
      fs.writeFileSync(provenancePath,JSON.stringify(p,null,2)+'\n');
      fs.writeFileSync(catalogPath,updated);catalog.models.push(model);unit.model=slug+'/v1';
      fs.unlinkSync(input);result.state='imported';
    }catch(error){result.state='deferred';result.error=error.message;}
  }
  return results;
}
module.exports={importModels};
if(require.main===module){
  const args=process.argv.slice(2),apply=args.includes('--apply');if(apply)args.splice(args.indexOf('--apply'),1);
  let textureLimit=null;const limitIndex=args.indexOf('--texture-limit');if(limitIndex>=0){textureLimit=Number(args[limitIndex+1]);if(![256,512,1024,2048].includes(textureLimit))throw Error('Invalid texture limit');args.splice(limitIndex,2);}
  let rotationYDegrees=0;const rotationIndex=args.indexOf('--rotation-y');if(rotationIndex>=0){rotationYDegrees=Number(args[rotationIndex+1]);if(![0,90,-90,180].includes(rotationYDegrees))throw Error('Invalid orientation');args.splice(rotationIndex,2);}
  let canonicalTypes=[];const catalogIndex=args.indexOf('--canonical-catalog');if(catalogIndex>=0){canonicalTypes=JSON.parse(fs.readFileSync(args[catalogIndex+1])).unitTypes;args.splice(catalogIndex,2);}
  let only=null;if(args[0]==='--only'&&args.length===2){only=args[1].split(',');if(only.some(s=>!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(s)))throw Error('Use exact comma-separated Unit slugs');args.length=0;}
  if(args.length)throw Error('Usage: node tools/import-unit-models.cjs [--apply] [--only slug,slug] [--texture-limit 1024] [--rotation-y 90] [--canonical-catalog path]');
  importModels(path.resolve(__dirname,'..'),{apply,only,textureLimit,rotationYDegrees,canonicalTypes}).then(results=>{console.log(JSON.stringify(results,null,2));if(results.some(r=>r.state==='deferred'))process.exitCode=1;}).catch(e=>{console.error(e);process.exitCode=1;});
}
