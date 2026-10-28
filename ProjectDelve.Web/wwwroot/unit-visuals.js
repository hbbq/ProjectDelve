// Authored presentation dimensions. Canonical IDs/footprints are supplied by the
// existing public content catalog, never inferred from names or game observation.
// Small envelopes intersect at most 3x3 Cell columns; approved 2x2 envelopes
// up to 1.25 intersect at most 4x4. Logical footprints remain authoritative.
export const maximumModelRadius=.92;
export function maximumModelRadiusForFootprint(cellSpan){
  if(![1,2].includes(cellSpan))throw Error('Invalid model footprint');
  return cellSpan===2?1.25:maximumModelRadius;
}
export const miniatureFootMargin=.04;
export const miniatureFootprintCoverage=.90;
// Presentation dimensions only. Callers can pass actual width/depth independently
// of the current square cellSpan representation (e.g. a future 2x1 figure).
export function maximumBaseRadius(width,depth=width){
  if(![width,depth].every(n=>Number.isFinite(n)&&n>0))throw Error('Invalid base footprint');
  return Math.min(width,depth)*miniatureFootprintCoverage/2;
}
export function miniatureBaseLayout({baseRadius,width=1,depth=width,contact=null}){
  const maximumRadius=maximumBaseRadius(width,depth),requestedRadius=baseRadius??Math.min(.34,maximumRadius);
  if(!Number.isFinite(requestedRadius)||requestedRadius<=0)throw Error('Invalid base radius');
  const requiredRadius=contact?contact.radius+miniatureFootMargin:0;
  const radius=Math.min(maximumRadius,requestedRadius),shortfall=Math.max(0,requiredRadius-radius);
  const fitsContact=shortfall===0;
  return {radius,maximumRadius,requestedRadius,requiredRadius,margin:contact?radius-contact.radius:null,shortfall,fitsContact,
    warning:!fitsContact?'Estimated contact envelope lacks the target foot margin':requestedRadius>maximumRadius?'Base radius capped to 90% footprint coverage':null};
}
const positive=n=>typeof n==='number'&&Number.isFinite(n)&&n>0;
const unique=(rows,key)=>{const result=Object.create(null);for(const row of rows){if(!row||typeof row[key]!=='string'||!row[key].trim()||Object.hasOwn(result,row[key]))throw Error(`Invalid or duplicate ${key}`);result[row[key]]=Object.freeze({...row});}return Object.freeze(result);};
export function validateUnitVisualCatalog(value,canonicalTypes,base=import.meta.url){
  if(value?.schemaVersion!==1||!Array.isArray(value.units)||!Array.isArray(value.models)||!Array.isArray(canonicalTypes))throw Error('Unsupported Unit visual catalog schema');
  const units=unique(value.units,'typeId'),models=unique(value.models,'id'),canonical=unique(canonicalTypes,'unitTypeId');
  for(const id of Object.keys(canonical))if(!Object.hasOwn(units,id))throw Error(`Missing Unit visual metadata: ${id}`);
  for(const [id,u]of Object.entries(units)){
    if(!Object.hasOwn(canonical,id))throw Error(`Unknown Unit visual Type: ${id}`);
    if(!positive(u.targetHeight)||!positive(u.maximumRadius)||u.maximumRadius>maximumModelRadiusForFootprint(canonical[id].cellSpan)||!positive(u.baseRadius)||u.baseRadius>maximumBaseRadius(canonical[id].cellSpan))throw Error(`Invalid Unit visual dimensions: ${id}`);
    if(Object.keys(u).some(k=>!['typeId','targetHeight','maximumRadius','baseRadius','model'].includes(k)))throw Error(`Unknown Unit visual field: ${id}`);
    if(u.model!=null&&!Object.hasOwn(models,u.model))throw Error(`Unknown model reference: ${id}`);
  }
  for(const [id,m]of Object.entries(models)){
    if(m.appearance!=null&&m.appearance!=='ghost')throw Error(`Unknown model appearance: ${id}`);
    const url=new URL(m.url,base),origin=new URL(base);
    if(m.approved!==true||typeof m.version!=='string'||!m.version||!['http:','https:','file:'].includes(url.protocol)||url.origin!==origin.origin||!url.pathname.endsWith('.glb')||url.search||url.hash)throw Error(`Invalid approved model: ${id}`);
    for(const [key,max]of [['maxTriangles',12000],['maxPrimitives',2],['maxTextureDimension',8192]])if(m[key]!=null&&(!Number.isInteger(m[key])||m[key]<=0||m[key]>max))throw Error(`Invalid model budget: ${id}`);
    if(Object.keys(m).some(k=>!['id','approved','url','version','maxTriangles','maxPrimitives','maxTextureDimension','appearance'].includes(k)))throw Error(`Unknown model field: ${id}`);
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
export function unitVisualOverhang(catalog,overrides={}){
  let overhang=0;
  for(const [id,u]of Object.entries(catalog?.units??{}))if(u.model||overrides[id]?.approved)
    overhang=Math.max(overhang,u.maximumRadius-(catalog.cellSpans?.[id]??overrides[id]?.cellSpan??1)/2);
  for(const [id,d]of Object.entries(overrides))if(!catalog?.units[id]&&d.approved)
    overhang=Math.max(overhang,(d.radius??.38)-(d.cellSpan??1)/2);
  return overhang;
}
