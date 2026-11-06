"""Render server-generated route checkpoints for visual review; Pillow, no generation algorithms."""
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

source, output = map(Path, sys.argv[1:3])
output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 17)
small = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 13)
colors = {'grass':'#809a54','dirt':'#967555','tree':'#294e39','solid-rock':'#626b76','water':'#347f9d',
          'stone-floor':'#c6b7a3','gravel':'#aaa596','stone-floor-with-table':'#c6b7a3','campfire':'#809a54'}


def panel(data, frame, size=4):
    w, h = data['window']['width'], data['window']['height']
    image = Image.new('RGB', (w * size, h * size))
    draw = ImageDraw.Draw(image)
    palette = data['preview']['terrainPalette']
    for i, c in enumerate(frame['terrain']):
        kind = palette[ord(c) - 65]
        x, y = i % w * size, i // w * size
        draw.rectangle((x, y, x + size - 1, y + size - 1), fill=colors.get(kind, 'magenta'))
        if kind == 'tree' and size >= 5:
            draw.ellipse((x + 1, y + 1, x + size - 2, y + size - 2), outline='#527249')
        if kind in ('campfire', 'stone-floor-with-table'):
            inset = max(1, size // 4)
            draw.rectangle((x + inset, y + inset, x + size - inset - 1, y + size - inset - 1), fill='#ff942e' if kind == 'campfire' else '#6b4226')
    for e in frame.get('edges', []):
        x, y = e['position']['x'] * size, e['position']['y'] * size
        xy = (x + size, y, x + size, y + size) if e['direction'] == 'Right' else (x, y + size, x + size, y + size)
        draw.line(xy, fill='#e5a14a' if 'Door' in e['kind'] else '#17212b', width=max(1, size // 5))
    for r in (frame.get('routeNetwork') or {}).get('routes', []):
        for p in (r['start'], r['end']):
            if p['kind'] == 'Boundary':
                i = p['anchorIndex']; x, y = (i % w + .5) * size, (i // w + .5) * size
                draw.ellipse((x - 2, y - 2, x + 2, y + 2), fill='white')
    return image


names = [f'120x100-{name}' for name in ['Grassland','Woodland','RockyWilds','LakeCountry','RiverValley']]
names += ['Mixed','ThroughOnly','RockPassages','Narrow','SiteNetworkOnly']
manifest = []
for name in names:
    data = json.loads((source / f'{name}.response.json').read_text(encoding='utf-8-sig'))
    frames = data['preview']['frames']
    primary = next((i for i, f in enumerate(frames) if f['stepId'] == 'primary-routes'), None)
    if primary is None:
        primary = next(i for i, f in enumerate(frames) if f['stepId'] == 'site-connections')
    w, h = data['window']['width'], data['window']['height']
    size = 4 if w >= 100 else min(8, 560 // h)
    labels = ['Before route stage', 'After first route stage', 'Final accepted network']
    canvas = Image.new('RGB', (3 * (w * size + 18) + 12, h * size + 116), '#19232d')
    draw = ImageDraw.Draw(canvas)
    draw.text((12, 8), f'{name} / WindowLocal / identical underlying Natural Terrain', fill='white', font=font)
    for column, frame in enumerate([frames[primary - 1],frames[primary],frames[-1]]):
        x = 12 + column * (w * size + 18)
        draw.text((x, 36), labels[column], fill='#bccbd4', font=small)
        canvas.paste(panel(data, frame, size), (x, 58))
    network = frames[-1].get('routeNetwork') or {}
    stats = network.get('statistics') or {}
    draw.text((12, h * size + 68), f"Routes {stats.get('acceptedRoutes',0)} / junctions {len(network.get('junctions',[]))} / crossings {stats.get('crossings',0)} / tunnel mouths {stats.get('tunnelEntrances',0)} / alternatives {len(network.get('alternatives',[]))}", fill='white', font=small)
    image_name = f'world-routes-{name.lower()}.png'
    canvas.save(output / image_name)
    manifest.append({'name':name,'image':image_name,'code':data['code'],'fingerprint':data['fingerprint'],'statistics':stats})

# Close-ups of real generated crossings, tunnels, branches and alternatives.
details = []
for path in sorted(source.glob('*.response.json')):
    data = json.loads(path.read_text(encoding='utf-8-sig'))
    network = data['preview']['frames'][-1].get('routeNetwork') or {}
    for route in network.get('routes', []):
        if route.get('crossings') and not any(d[0] == 'Crossing' for d in details):
            details.append(('Crossing',data,route['crossings'][0]['footprint']['bounds']))
        if route.get('tunnelEntrances') and not any(d[0] == 'Tunnel mouth' for d in details):
            p = route['tunnelEntrances'][0]['anchorIndex']; w = data['window']['width']
            details.append(('Tunnel mouth',data,{'x':p%w-6,'y':p//w-6,'width':14,'height':14}))
        if route['purpose'] == 'SiteConnection' and not any(d[0] == 'Site branch' for d in details):
            p = route['start']['anchorIndex']; w = data['window']['width']
            details.append(('Site branch',data,{'x':p%w-8,'y':p//w-8,'width':18,'height':18}))
        if route['purpose'] == 'ExplorationLink' and not any(d[0] == 'Exploration link' for d in details):
            details.append(('Exploration link',data,route['corridor']['bounds']))
sheet = Image.new('RGB',(1240,660),'#19232d'); draw = ImageDraw.Draw(sheet)
for col,(label,data,b) in enumerate(details[:4]):
    w,h=data['window']['width'],data['window']['height']
    x,y=max(0,b['x']-2),max(0,b['y']-2)
    cw,ch=min(w-x,b['width']+4),min(h-y,b['height']+4)
    size=min(20,290//cw,570//ch)
    whole=panel(data,data['preview']['frames'][-1],size)
    sheet.paste(whole.crop((x*size,y*size,(x+cw)*size,(y+ch)*size)),(12+col*308,42))
    draw.text((12+col*308,12),label,fill='white',font=font)
sheet.save(output/'world-routes-details.png')
(output/'world-routes-gallery.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
