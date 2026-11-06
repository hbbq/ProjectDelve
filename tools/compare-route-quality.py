"""Compare saved server route previews, without running generation or routing.

Usage: python tools/compare-route-quality.py BASELINE REFINED OUTPUT
"""
import argparse
from collections import Counter, defaultdict
import json
import math
from pathlib import Path
import statistics

from PIL import Image, ImageDraw, ImageFont


PREFIX = 'world-routes-refinement-'
COLORS = {'grass': '#809a54', 'dirt': '#967555', 'tree': '#294e39',
          'solid-rock': '#626b76', 'water': '#347f9d', 'stone-floor': '#c6b7a3',
          'gravel': '#aaa596', 'stone-floor-with-table': '#c6b7a3', 'campfire': '#809a54'}
DIRECTIONS = {'North': (0, -1), 'East': (1, 0), 'South': (0, 1), 'West': (-1, 0)}


def font(size):
    try:
        return ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', size)
    except OSError:
        return ImageFont.load_default(size=size)


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def network(frame):
    return frame.get('routeNetwork') or {}


def primary_frame(data):
    frames = data['preview']['frames']
    return next((f for f in frames if f['stepId'] == 'primary-routes'),
                next((f for f in frames if f['stepId'] == 'site-connections'), frames[-1]))


def cells(area):
    return {(x, s['y']) for s in area.get('spans', [])
            for x in range(s['x'], s['x'] + s['length'])}


def route_points(route, data):
    # Geometry is world-space; preview terrain and footprints are window-local.
    window = data['window']
    geometry = route['geometry']
    ox = route['start']['worldX'] - route['start']['anchorIndex'] % window['width']
    oy = route['start']['worldY'] - route['start']['anchorIndex'] // window['width']
    points = [(geometry['worldX'] - ox, geometry['worldY'] - oy)]
    for run in geometry['runs']:
        dx, dy = DIRECTIONS[run['direction']]
        x, y = run['worldX'] - ox, run['worldY'] - oy
        points.extend((x + dx * i, y + dy * i) for i in range(1, run['length'] + 1))
    return points


def summary(values):
    values = sorted(values)
    return {'count': len(values), 'max': max(values, default=None),
            'median': statistics.median(values) if values else None,
            'p90': values[max(0, math.ceil(len(values) * .9) - 1)] if values else None}


def ratio(a, b):
    return a / b if b else None


def diagnostics(data, frame):
    net = network(frame)
    routes = net.get('routes', [])
    run_lengths, manhattan_ratios, euclidean_ratios = [], [], []
    turns = Counter()
    total_length = corridor_count = reused = boundary = eligible = 0
    graph = defaultdict(set)
    route_metrics = []
    for route in routes:
        runs = route['geometry']['runs']
        run_lengths.extend(r['length'] for r in runs)
        angles = Counter()
        for a, b in zip(runs, runs[1:]):
            da, db = DIRECTIONS[a['direction']], DIRECTIONS[b['direction']]
            dot = da[0] * db[0] + da[1] * db[1]
            if dot != 1:
                angles[180 if dot == -1 else 90] += 1
        turns.update(angles)
        points = route_points(route, data)
        for a, b in zip(points, points[1:]):
            graph[a].add(b)
            graph[b].add(a)
        length = sum(r['length'] for r in runs)
        total_length += length
        route_corridor_count = len(cells(route['corridor']))
        corridor_count += route_corridor_count
        reused += route['stats'].get('reusedCells', 0)
        dx, dy = points[-1][0] - points[0][0], points[-1][1] - points[0][1]
        mr, er = ratio(length, abs(dx) + abs(dy)), ratio(length, math.hypot(dx, dy))
        if mr is not None:
            manhattan_ratios.append(mr)
        if er is not None:
            euclidean_ratios.append(er)
        w, h = data['window']['width'], data['window']['height']
        route_eligible = [p for p in points if all(abs(p[0]-e[0]) + abs(p[1]-e[1]) > 3
                                                for e in (points[0], points[-1]))]
        eligible += len(route_eligible)
        boundary += sum(min(x, y, w-1-x, h-1-y) <= 2 for x, y in route_eligible)
        route_metrics.append({'id': route['id'], 'purpose': route['purpose'], 'length': length,
                              'turns': sum(angles.values()), 'angles90': angles[90],
                              'angles180': angles[180], 'lengthOverManhattan': mr,
                              'lengthOverEuclidean': er, 'preparation': route['preparation'],
                              'corridorCells': route_corridor_count,
                              'reuseFraction': ratio(route['stats'].get('reusedCells', 0), route_corridor_count)})
    junctions = []
    for junction in net.get('junctions', []):
        area = cells(junction['contact'])
        external = {(a, b) for a in area for b in graph[a] if b not in area}
        junctions.append({'id': junction['id'], 'routeCount': len(junction['routeIds']),
                          'externalCenterlineEdges': len(external),
                          'maximumCellDegree': max((len(graph[p]) for p in area), default=0)})
    alternatives = []
    for alternative in net.get('alternatives', []):
        previous, new = alternative['previousDistance'], alternative['newDistance']
        alternatives.append({**alternative,
                             'distanceImprovement': previous-new if previous >= 0 else None,
                             'distanceRatio': ratio(new, previous) if previous > 0 else None,
                             'newCellFraction': ratio(alternative['newCorridorCells'],
                                                      alternative['newCorridorCells'] + alternative['sharedCorridorCells'])})
    preparation = Counter()
    for route in routes:
        preparation.update(route['preparation'])
    attachments = net.get('siteAttachments', [])
    site_intents = [intent for intent in net.get('intents', [])
                    if intent['purpose'] == 'SiteConnection']
    return {'routeCount': len(routes), 'straightRunCells': summary(run_lengths),
            'length': total_length, 'turnsPer100Steps': ratio(sum(turns.values()) * 100, total_length),
            'angles90': turns[90], 'angles180': turns[180],
            'lengthOverManhattan': summary(manhattan_ratios),
            'lengthOverEuclidean': summary(euclidean_ratios), 'preparation': dict(preparation),
            'reuseFraction': ratio(reused, corridor_count), 'corridorCellOccurrences': corridor_count,
            'junctionCount': len(junctions), 'junctionDegree': junctions,
            'alternativesUsefulness': alternatives,
            'siteAttachmentStatuses': attachments,
            'siteConnectionIntentsAvailable': 'intents' in net,
            'siteConnectionIntentStatusCounts': dict(Counter(intent['status'] for intent in site_intents)),
            'siteConnectionIntents': site_intents,
            'siteAttachmentSummary': dict(Counter(
                f"ordinary={a['ordinaryOpenAccess']},doorsOpenable={a['doorsOpenableAccess']}"
                for a in attachments)),
            'boundaryFraction': ratio(boundary, eligible), 'boundaryCells': boundary,
            'boundaryEligibleCells': eligible, 'statistics': net.get('statistics', {}),
            'routes': route_metrics}


def panel(data, frame, size, bounds=None):
    w, h = data['window']['width'], data['window']['height']
    x0, y0, cw, ch = bounds or (0, 0, w, h)
    image = Image.new('RGB', (cw * size, ch * size))
    draw = ImageDraw.Draw(image)
    palette = data['preview']['terrainPalette']
    for y in range(y0, y0 + ch):
        for x in range(x0, x0 + cw):
            kind = palette[ord(frame['terrain'][y*w+x])-65]
            px, py = (x-x0)*size, (y-y0)*size
            draw.rectangle((px, py, px+size-1, py+size-1), fill=COLORS.get(kind, 'magenta'))
            if kind in ('campfire', 'stone-floor-with-table'):
                inset = max(1, size//4)
                draw.rectangle((px+inset, py+inset, px+size-inset-1, py+size-inset-1),
                               fill='#ff942e' if kind == 'campfire' else '#6b4226')
    for edge in frame.get('edges', []):
        x, y = (edge['position']['x']-x0)*size, (edge['position']['y']-y0)*size
        xy = (x+size, y, x+size, y+size) if edge['direction'] == 'Right' else (x, y+size, x+size, y+size)
        draw.line(xy, fill='#e5a14a' if 'Door' in edge['kind'] else '#17212b', width=max(1, size//5))
    return image


def comparison(name, baseline, refined, frames, path, bounds=None):
    w, h = baseline['window']['width'], baseline['window']['height']
    cw, ch = (bounds[2], bounds[3]) if bounds else (w, h)
    size = min(22, 500//cw, 540//ch) if bounds else max(2, min(6, 600//w, 640//h))
    canvas = Image.new('RGB', (2*(cw*size+18)+12, ch*size+82), '#19232d')
    draw = ImageDraw.Draw(canvas)
    draw.text((12, 8), name, fill='white', font=font(17))
    for column, (data, frame, label) in enumerate(zip((baseline, refined), frames, ('Baseline', 'Refined'))):
        x = 12 + column*(cw*size+18)
        draw.text((x, 34), label, fill='#bccbd4', font=font(13))
        canvas.paste(panel(data, frame, size, bounds), (x, 58))
    canvas.save(path)


def detail_locations(data):
    frame = data['preview']['frames'][-1]
    net = network(frame)
    w, h = data['window']['width'], data['window']['height']

    def crop(x, y, width=23, height=23):
        width, height = min(width, w), min(height, h)
        return (max(0, min(w-width, x-width//2)),
                max(0, min(h-height, y-height//2)), width, height)

    found = {}
    if net.get('junctions'):
        trails = {r['id'] for r in net.get('routes', []) if r['profile'] == 'ForestTrail'}
        junction = min(net['junctions'], key=lambda j: (not trails.intersection(j['routeIds']), j['id']))
        b = junction['contact']['bounds']
        found['junction'] = crop(b['x'], b['y'])
    sites = {site['id']: site for site in frame.get('sites', [])}
    # A trail bend and a settlement access are more informative than arbitrary first routes.
    routes = sorted(net.get('routes', []), key=lambda r: (r['profile'] != 'ForestTrail', r['id']))
    for route in routes:
        if route['purpose'] == 'SiteConnection':
            p = route['start'] if route['start']['kind'] == 'Site' else route['end']
            site = sites.get(p.get('siteId'))
            if site and site['kind'] == 'Settlement':
                b = site['footprint']['bounds']
                x, y = p['anchorIndex'] % w, p['anchorIndex'] // w
                left, top = min(b['x'], x)-8, min(b['y'], y)-8
                right, bottom = max(b['x']+b['width'], x+1)+8, max(b['y']+b['height'], y+1)+8
                found.setdefault('attachment', crop((left+right)//2, (top+bottom)//2,
                                                     min(45, right-left), min(45, bottom-top)))
        runs = route['geometry']['runs']
        if len(runs) > 1:
            points = route_points(route, data)
            found.setdefault('bend', crop(*points[runs[0]['length']]))
        if route.get('crossings'):
            b = route['crossings'][0]['footprint']['bounds']
            found.setdefault('crossing', crop(b['x']+b['width']//2, b['y']+b['height']//2))
    return found


def performance(baseline, refined):
    old, new = {r['name']: r for r in load(baseline)}, {r['name']: r for r in load(refined)}
    result = {}
    for name in sorted(old.keys() & new.keys()):
        a, b = old[name], new[name]
        if a['code'] != b['code']:
            raise ValueError(f'{name}: performance world codes differ')
        stages_a = {s['stepId']: s for s in a['stages']}
        stages_b = {s['stepId']: s for s in b['stages']}
        result[name] = {'baseline': a, 'refined': b,
                        'generationMsRatio': ratio(b['generationMs'], a['generationMs']),
                        'allocatedBytesRatio': ratio(b['allocatedBytes'], a['allocatedBytes']),
                        'stages': {key: {'millisecondsRatio': ratio(stages_b[key]['milliseconds'], stages_a[key]['milliseconds']),
                                         'allocatedBytesRatio': ratio(stages_b[key]['allocatedBytes'], stages_a[key]['allocatedBytes'])}
                                   for key in sorted(stages_a.keys() & stages_b.keys())}}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('baseline', type=Path)
    parser.add_argument('refined', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    baseline = {p.name: p for p in args.baseline.glob('*.response.json')}
    refined = {p.name: p for p in args.refined.glob('*.response.json')}
    common = sorted(baseline.keys() & refined.keys())
    if not common:
        parser.error('No common response fixtures found')
    args.output.mkdir(parents=True, exist_ok=True)
    report = {'definitions': {
        'straightRunCells': 'Serialized geometry run lengths in steps; nearest-rank p90.',
        'length': 'Sum of serialized centerline run lengths in steps.',
        'reuseFraction': 'Engine reusedCells divided by corridor cell count; aggregate sums per-route counts, preserving overlap occurrences and corridor width.',
        'boundaryFraction': 'Centerline point occurrences at distance <=2 from a window edge, excluding Manhattan distance <=3 from either endpoint of that route.',
        'junctionDegree': 'External centerline graph edges leaving serialized contact area; also route count and maximum cell degree.',
        'alternativesUsefulness': 'Server usefulness flags plus distance improvement and new-cell fraction; no inferred acceptance rule.',
        'siteConnectionIntentStatusCounts': 'Server intent statuses filtered to purpose SiteConnection, including requests without materialized attachments. Availability is explicit because older fixtures may omit intents.',
        'primary': 'Primary-routes checkpoint, or site-connections when no primary checkpoint exists.',
    }, 'fixtures': {}, 'images': [], 'missingFromRefined': sorted(baseline.keys()-refined.keys()),
        'missingFromBaseline': sorted(refined.keys()-baseline.keys())}
    selected = {f'120x100-{p}' for p in ('Grassland', 'Woodland', 'RockyWilds', 'LakeCountry', 'RiverValley')}
    selected.update(('Mixed', 'ThroughOnly', 'RockPassages', 'Narrow', 'SiteNetworkOnly'))
    detail_candidates = defaultdict(list)
    for filename in common:
        a, b = load(baseline[filename]), load(refined[filename])
        name = filename.removesuffix('.response.json')
        if a['code'] != b['code'] or a['window'] != b['window']:
            raise ValueError(f'{name}: world code or window differs')
        natural_a = next(f for f in a['preview']['frames'] if f['stage'] == 'NaturalTerrain')
        natural_b = next(f for f in b['preview']['frames'] if f['stage'] == 'NaturalTerrain')
        # Palette indices can shift when later stages introduce different terrain IDs.
        terrain_a = [a['preview']['terrainPalette'][ord(c)-65] for c in natural_a['terrain']]
        terrain_b = [b['preview']['terrainPalette'][ord(c)-65] for c in natural_b['terrain']]
        if terrain_a != terrain_b or natural_a.get('edges', []) != natural_b.get('edges', []):
            raise ValueError(f'{name}: original NaturalTerrain frame differs')
        af, bf = a['preview']['frames'][-1], b['preview']['frames'][-1]
        ap, bp = primary_frame(a), primary_frame(b)
        report['fixtures'][name] = {'code': a['code'], 'sameOriginalNaturalTerrain': True,
                                     'baseline': {'final': diagnostics(a, af), 'primary': diagnostics(a, ap)},
                                     'refined': {'final': diagnostics(b, bf), 'primary': diagnostics(b, bp)}}
        if name in selected:
            for stage, frames in (('final', (af, bf)), ('primary', (ap, bp))):
                image_name = f'{PREFIX}{name.lower()}-{stage}.png'
                comparison(f'{name} / {stage} / identical Natural Terrain', a, b, frames, args.output/image_name)
                report['images'].append({'fixture': name, 'stage': stage, 'image': image_name})
        for kind, bounds in detail_locations(b).items():
            detail_candidates[kind].append((name, bounds, a, b))
    # Keep paired coordinates identical while preferring examples that show each feature clearly.
    for kind, candidates in sorted(detail_candidates.items()):
        preferred = '120x100-LakeCountry' if kind == 'crossing' else '120x100-Woodland'
        name, bounds, a, b = min(candidates, key=lambda c: (c[0] != preferred, c[0]))
        image_name = f'{PREFIX}detail-{kind}.png'
        comparison(f'{name} / {kind} / same window-local crop', a, b,
                   (a['preview']['frames'][-1], b['preview']['frames'][-1]), args.output/image_name, bounds)
        report['images'].append({'fixture': name, 'detail': kind, 'bounds': bounds, 'image': image_name})
    report['unavailableDetails'] = sorted({'junction', 'attachment', 'bend', 'crossing'} - detail_candidates.keys())
    report['performance'] = performance(args.baseline/'performance.json', args.refined/'performance.json')
    target = args.output/f'{PREFIX}diagnostics.json'
    target.write_text(json.dumps(report, indent=2, allow_nan=False)+'\n', encoding='utf-8')
    print(f'{len(common)} fixture comparisons, {len(report["images"])} images: {target}')


if __name__ == '__main__':
    main()
