const edge = 4, gap = 3, headroom = 6, binSize = 64;
const overlap = (a,b) => a.x < b.x+b.width && a.x+a.width > b.x && a.y < b.y+b.height && a.y+a.height > b.y;
const rectangle = r => {
  if (!r) return null;
  const rect = {x:r.x,y:r.y,width:r.width??r.w,height:r.height??r.h};
  return Object.values(rect).every(Number.isFinite) && rect.width>0 && rect.height>0 ? rect : null;
};

// Screen-space bins keep dense scenes local. Clip bin ranges before indexing so
// a partially offscreen miniature or panel cannot create unbounded work.
function obstacles(viewport) {
  const bins = new Map();
  function keys(rect) {
    const left=Math.max(0,rect.x),top=Math.max(0,rect.y),right=Math.min(viewport.width,rect.x+rect.width),bottom=Math.min(viewport.height,rect.y+rect.height);
    const result=[];
    if(right<=left||bottom<=top)return result;
    for(let y=Math.floor(top/binSize);y<=Math.floor(bottom/binSize);y++)
      for(let x=Math.floor(left/binSize);x<=Math.floor(right/binSize);x++)result.push(`${x},${y}`);
    return result;
  }
  return {
    add(rect,bodyId) {
      const item={rect,bodyId};
      for(const key of keys(rect)){if(!bins.has(key))bins.set(key,[]);bins.get(key).push(item);}
    },
    blocks(rect,id) {
      const padded={x:rect.x-gap,y:rect.y-gap,width:rect.width+gap*2,height:rect.height+gap*2},seen=new Set();
      for(const key of keys(padded))for(const item of bins.get(key)??[]){
        if(seen.has(item))continue;seen.add(item);
        if(item.bodyId!==undefined&&item.bodyId===id)continue;
        if(overlap(padded,item.rect))return true;
      }
      return false;
    }
  };
}

// Inputs are already observed/projected presentation data. No DOM measurements,
// game queries, LOD decisions or persistent animation state belong in this helper.
// x/y is the head anchor; output x/y is the label's top-left corner.
export function layoutTabletopLabels(candidates,viewport,blockedRects=[]) {
  const result=new Map(),validViewport=Number.isFinite(viewport?.width)&&Number.isFinite(viewport?.height)&&viewport.width>edge*2&&viewport.height>edge*2;
  const index=validViewport?obstacles(viewport):null;
  for(const rect of blockedRects.map(rectangle).filter(Boolean))index?.add(rect);
  for(const candidate of candidates){const body=rectangle(candidate.bodyRect);if(body)index?.add(body,candidate.id);}
  // Stable identity resolves ties independently of observation-array ordering.
  const sorted=[...candidates].sort((a,b)=>(Number.isFinite(b.priority)?b.priority:0)-(Number.isFinite(a.priority)?a.priority:0)||(String(a.id)<String(b.id)?-1:String(a.id)>String(b.id)?1:0));
  for(const candidate of sorted){
    const {id,x,y,width,height,compactWidth}=candidate;
    const hidden={x:Number.isFinite(x)?x:0,y:Number.isFinite(y)?y:0,width:Number.isFinite(width)?Math.max(0,width):0,height:Number.isFinite(height)?Math.max(0,height):0,compact:false,hidden:true};
    result.set(id,hidden);
    if(!validViewport||![x,y,width,height].every(Number.isFinite)||width<=0||height<=0)continue;
    const widths=[width];if(Number.isFinite(compactWidth)&&compactWidth>0&&compactWidth<width)widths.push(compactWidth);
    let placed;
    for(const w of widths){
      if(w>viewport.width-edge*2)continue;
      const shift=Math.min(48,w*.45),preferred=x-w/2,tried=new Set();
      // Three rows and three small horizontal offsets per width. Compact text
      // is preferable to disappearing; never shrink the supplied font height.
      for(let row=0;row<3&&!placed;row++)for(const dx of [0,-shift,shift]){
        const left=Math.max(edge,Math.min(viewport.width-edge-w,preferred+dx)),above=y-headroom-height-row*(height+gap),top=Math.max(edge,above);
        if(Math.abs(left-preferred)>shift||top-above>height+headroom+edge||top+height>viewport.height-edge)continue;
        const key=`${left},${top}`;if(tried.has(key))continue;tried.add(key);
        // At the viewport top a small clamp can recover a label, but must not
        // push it over its own miniature's head.
        const rect={x:left,y:top,width:w,height};if(index.blocks(rect,top===above?id:undefined))continue;
        placed={...rect,compact:w!==width,hidden:false};break;
      }
      if(placed)break;
    }
    if(!placed){
      // A foreground figure may have other miniatures directly above its head.
      // Try two nearby side slots, compact first when supplied. Never hop across
      // a large body or panel to find an unrelated patch of empty board.
      const w=widths.at(-1),body=rectangle(candidate.bodyRect),top=Math.max(edge,Math.min(viewport.height-edge-height,y+headroom));
      if(w<=viewport.width-edge*2&&height<=viewport.height-edge*2&&Math.abs(top-y)<=height+headroom){
        for(const left of [(body?.x??x)-gap-w,(body?body.x+body.width:x)+gap]){
          if(left<edge||left+w>viewport.width-edge||Math.abs(left+w/2-x)>Math.min(110,w/2+48))continue;
          const rect={x:left,y:top,width:w,height};if(index.blocks(rect))continue;
          placed={...rect,compact:w!==width,hidden:false};break;
        }
      }
    }
    if(!placed){
      // One final foreground slot below the base. Keep the full plate centered
      // on its own figure, with no search into unrelated empty board space.
      const body=rectangle(candidate.bodyRect);
      if(body){
        const rect={x:x-width/2,y:body.y+body.height+headroom,width,height};
        if(rect.x>=edge&&rect.x+width<=viewport.width-edge&&rect.y>=edge&&rect.y+height<=viewport.height-edge&&Math.abs(rect.y-y)<=body.height+height+headroom&&!index.blocks(rect))
          placed={...rect,compact:false,hidden:false};
      }
    }
    if(placed){result.set(id,placed);index.add(placed);}
  }
  return result;
}
