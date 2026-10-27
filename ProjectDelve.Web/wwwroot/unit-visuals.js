// Authored presentation dimensions. Canonical IDs/footprints are supplied by the
// existing public content catalog, never inferred from names or game observation.
const positive=n=>typeof n==='number'&&Number.isFinite(n)&&n>0;
const unique=(rows,key)=>{const result=Object.create(null);for(const row of rows){if(!row||typeof row[key]!=='string'||!row[key].trim()||Object.hasOwn(result,row[key]))throw Error(`Invalid or duplicate ${key}`);result[row[key]]=Object.freeze({...row});}return Object.freeze(result);};
export function validateUnitVisualCatalog(value,canonicalTypes,base=import.meta.url){
  if(value?.schemaVersion!==1||!Array.isArray(value.units)||!Array.isArray(value.models)||!Array.isArray(canonicalTypes))throw Error('Unsupported Unit visual catalog schema');
  const units=unique(value.units,'typeId'),models=unique(value.models,'id'),canonical=unique(canonicalTypes,'unitTypeId');
  for(const id of Object.keys(canonical))if(!Object.hasOwn(units,id))throw Error(`Missing Unit visual metadata: ${id}`);
  for(const [id,u]of Object.entries(units)){
    if(!Object.hasOwn(canonical,id))throw Error(`Unknown Unit visual Type: ${id}`);
    if(!positive(u.targetHeight)||!positive(u.maximumRadius)||u.maximumRadius>.46*canonical[id].cellSpan)throw Error(`Invalid Unit visual dimensions: ${id}`);
    if(Object.keys(u).some(k=>!['typeId','targetHeight','maximumRadius','model'].includes(k)))throw Error(`Unknown Unit visual field: ${id}`);
    if(u.model!=null&&!Object.hasOwn(models,u.model))throw Error(`Unknown model reference: ${id}`);
  }
  for(const [id,m]of Object.entries(models)){
    const url=new URL(m.url,base),origin=new URL(base);
    if(m.approved!==true||typeof m.version!=='string'||!m.version||!['http:','https:','file:'].includes(url.protocol)||url.origin!==origin.origin||!url.pathname.endsWith('.glb')||url.search||url.hash)throw Error(`Invalid approved model: ${id}`);
    for(const [key,max]of [['maxTriangles',12000],['maxPrimitives',2],['maxTextureDimension',8192]])if(m[key]!=null&&(!Number.isInteger(m[key])||m[key]<=0||m[key]>max))throw Error(`Invalid model budget: ${id}`);
    if(Object.keys(m).some(k=>!['id','approved','url','version','maxTriangles','maxPrimitives','maxTextureDimension'].includes(k)))throw Error(`Unknown model field: ${id}`);
  }
  return Object.freeze({schemaVersion:1,units,models,cellSpans:Object.freeze(Object.fromEntries(Object.values(canonical).map(t=>[t.unitTypeId,t.cellSpan])))});
}
let loading;
export function loadUnitVisualCatalog(fetcher=globalThis.fetch){
  return loading??=(async()=>{
    const read=async url=>{const r=await fetcher(url);if(!r.ok)throw Error(`Unit visual catalog HTTP ${r.status}`);return r.json();};
    const [visuals,canonical]=await Promise.all([read('./art/models/unit-visuals.json'),read('./api/designer/catalog')]);
    return validateUnitVisualCatalog(visuals,canonical.unitTypes);
  })().catch(error=>{loading=null;throw error;});
}
export function resolveUnitModel(catalog,typeId,cellSpan,overrides={}){
  const unit=catalog?.units[typeId],asset=unit?.model?catalog.models[unit.model]:overrides[typeId];
  if(!asset||asset.approved!==true)return null;
  if(catalog?.cellSpans?.[typeId]!=null&&catalog.cellSpans[typeId]!==cellSpan)return null;
  if(asset.cellSpan!=null&&asset.cellSpan!==cellSpan)return null;
  // Explicit test registrations carry their own dimensions. Canonical
  // production assets always use the one catalog entry for their Type.
  return {...asset,cellSpan,height:unit?.targetHeight??asset.height??1.2,radius:unit?.maximumRadius??asset.radius??.38};
}
export function unitVisualEnvelope(catalog,overrides={}){
  let height=1.5;
  for(const u of Object.values(catalog?.units??{}))if(u.model||overrides[u.typeId]?.approved)height=Math.max(height,u.targetHeight+.15);
  for(const [id,d]of Object.entries(overrides))if(!catalog?.units[id]&&d.approved)height=Math.max(height,(d.height??1.2)+.15);
  return height;
}
