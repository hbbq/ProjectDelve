// Real generated PNGs, native-size browser swatches, and a deliberately missing asset.
// The temporary gallery is QA-only and never becomes a product asset editor.
module.exports = async function cursorAssetsBrowser(page) {
  const assert = require('node:assert/strict');
  const base='http://127.0.0.1:5081';
  await page.goto(base);
  const dimensions=await page.evaluate(async()=>{
    const catalog=await (await fetch('/presentation/cursors/catalog.json')).json();
    const gallery=document.createElement('section');gallery.id='cursor-qa-gallery';
    Object.assign(gallery.style,{position:'fixed',inset:'0',zIndex:'100000',padding:'16px',background:'#ddd',overflow:'auto',font:'12px sans-serif'});
    document.body.append(gallery);
    const dimensions=[];
    for(const size of [32,40,48])for(const dark of [false,true]){
      const row=document.createElement('div');Object.assign(row.style,{display:'flex',height:'120px',alignItems:'center',background:dark?'#17201d':'#f1ead5',color:dark?'white':'#222'});
      const title=document.createElement('strong');title.textContent=`${size}px / ${dark?'dark':'light'}`;title.style.width='100px';row.append(title);
      for(const cursor of catalog.cursors){
        const cell=document.createElement('div');Object.assign(cell.style,{width:'88px',textAlign:'center'});
        const image=document.createElement('img');image.src=cursor.images[size];image.width=image.height=size;image.style.display='inline-block';
        const label=document.createElement('div');label.textContent=cursor.id;cell.append(image,label);row.append(cell);
        await image.decode();dimensions.push({id:cursor.id,size,width:image.naturalWidth,height:image.naturalHeight});
      }
      gallery.append(row);
    }
    return dimensions;
  });
  assert.equal(dimensions.length,72);
  for(const image of dimensions){assert.equal(image.width,image.size);assert.equal(image.height,image.size);}
  await page.locator('#cursor-qa-gallery').screenshot({path:'.playwright-cli/cursor-b-browser-gallery.png'});
  await page.locator('#cursor-qa-gallery').evaluate(node=>node.remove());
  const missing=await page.context().newPage();
  try{
    await missing.route('**/presentation/cursors/generated/40/fireball.png',route=>route.abort());
    await missing.goto(`${base}/#play`);
    const result=await missing.evaluate(async()=>{
      const cursors=await import('/presentation-cursors.js'),profiles=await import('/presentation-profiles.js');
      await Promise.all([cursors.loadCursorAssets(),profiles.loadPresentationProfiles()]);
      cursors.setCursorSize(40);
      return {missing:profiles.resolvePresentationCursor('fireball','crosshair'),known:profiles.resolvePresentationCursor('standard-attack','crosshair'),unknown:profiles.resolvePresentationCursor('future.imported','pointer')};
    });
    assert.equal(result.missing,'crosshair');assert.equal(result.unknown,'pointer');assert.match(result.known,/generated\/40\/attack.png/);
  }finally{await missing.close();}
  return {images:72,sizes:[32,40,48],backgrounds:['light','dark'],checks:'decoded PNG dimensions, native-size swatches, missing-image and unknown-profile fallback'};
};
