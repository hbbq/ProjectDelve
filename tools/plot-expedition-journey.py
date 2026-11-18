#!/usr/bin/env python3
"""Render a seed journey export: python tools/plot-expedition-journey.py journey.json.

Requires Pillow. Maps share an exact geographic cell grid, bounds and pixel scale.
Coverage uses exporter classifications (including final tree/fruit-tree coverage).
Overview uses the most frequent base terrain ID; grass/dirt remain distinct.
Wall/door edges are between neighboring cell centers in region-local coordinates.
"""
import argparse
from collections import Counter
import json
import math
from pathlib import Path

from PIL import Image, ImageColor, ImageDraw, ImageFont


# Existing ExpeditionSpatialCorpusTests palette; additional terrain uses Web boardVisuals.
COLORS = {
    'grass': '#a6c973', 'dirt': '#b5986d', 'tree': '#295a37',
    'solid-rock': '#626575', 'water': '#417fb1', 'wet-grass': '#74aaa1',
    'rocky-water': '#536e8d', 'stone-floor': '#c5bdaa',
    'fruit-tree': '#305331', 'stepping-stones': '#39777d', 'lava': '#a64420',
    'stone-floor-with-table': '#636665', 'asphalt': '#303133', 'gravel': '#766d62',
}
INK = '#263447'
BG = '#f6f7f9'


def font(size=14):
    for name in ('C:/Windows/Fonts/arial.ttf', '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default(size=size)


def label(draw, xy, text, size=14, fill=INK, anchor=None):
    draw.text(xy, str(text), font=font(size), fill=fill, anchor=anchor)


def color(terrain):
    return COLORS.get(terrain, '#ddaf88')


def arrow(draw, a, b, fill='#9d3157'):
    draw.line((a, b), fill=fill, width=3)
    angle = math.atan2(b[1]-a[1], b[0]-a[0])
    tip = (a[0]+(b[0]-a[0])*.70, a[1]+(b[1]-a[1])*.70)
    draw.polygon([tip] + [(tip[0]-11*math.cos(angle+s), tip[1]-11*math.sin(angle+s))
                         for s in (-.45, .45)], fill=fill)


def maps(data, output, scale):
    regions = data['regions']
    stride = data['stride']
    lo_x = min(r['origin']['x'] for r in regions)
    lo_y = min(r['origin']['y'] for r in regions)
    hi_x = max(r['origin']['x'] for r in regions)+stride
    hi_y = max(r['origin']['y'] for r in regions)+stride
    margin, top = 90, 116
    width, height = (hi_x-lo_x)*scale, (hi_y-lo_y)*scale
    used = sorted({t for r in regions for key in ('baseTerrain', 'finalTerrain') for t in (r.get(key) or [])})
    legend_rows = math.ceil((len(used)+3)/6)
    canvas_size = (max(width+margin*2, 1160), height+top+100+legend_rows*30)

    def point(x, y):
        return margin+(x-lo_x)*scale, top+(y-lo_y)*scale

    for mode, filename, title in (
        ('baseTerrain', 'base-nature.png', 'Base nature'),
        ('finalTerrain', 'final-world.png', 'Final generated world'),
        ('overview', 'regional-overview.png', 'Regional overview / chronological journey'),
    ):
        image = Image.new('RGB', canvas_size, BG)
        draw = ImageDraw.Draw(image)
        label(draw, (margin, 18), title, 26)
        label(draw, (margin, 54), f"Seed {data['worldSeed']} | {data.get('baselineRecipe', '')} | {stride} x {stride} cells per region", 15)
        label(draw, (margin, 79), 'North up | X right, Y down | identical bounds and scale in all three maps | empty pale areas = unvisited', 13)
        draw.rectangle((margin, top, margin+width, top+height), fill='#e8ebef')
        for region in regions:
            ox, oy = region['origin']['x'], region['origin']['y']
            x, y = point(ox, oy)
            size = stride*scale
            terrain = region.get('baseTerrain' if mode == 'overview' else mode)
            if terrain is None:
                draw.rectangle((x, y, x+size-1, y+size-1), fill='#f5dfe1')
                draw.line((x, y, x+size, y+size), fill='#a24757', width=2)
                draw.line((x+size, y, x, y+size), fill='#a24757', width=2)
                label(draw, (x+size/2, y+size/2), 'GENERATION FAILED', 12, '#8a253b', 'mm')
            elif mode == 'overview':
                dominant = Counter(terrain).most_common(1)[0][0]
                draw.rectangle((x, y, x+size-1, y+size-1), fill=color(dominant))
                label(draw, (x+size/2, y+size*.70), dominant, 13, INK, 'mm')
                if region.get('error'):
                    label(draw, (x+size/2, y+size*.83), 'final failed', 12, '#8a253b', 'mm')
            else:
                tile = Image.new('RGB', (stride, stride))
                tile.putdata([ImageColor.getrgb(color(t)) for t in terrain])
                image.paste(tile.resize((size, size), Image.Resampling.NEAREST), (x, y))
            # Boundary is a single subtle stroke; compact hollow tag covers only a few cells.
            draw.rectangle((x, y, x+size-1, y+size-1), outline='#75828b', width=1)
            rx, ry = region['region']['x'], region['region']['y']
            tag = f"{region['index']} ({rx},{ry})"
            draw.text((x+4, y+3), tag, font=font(11), fill='#182b3b', stroke_width=1, stroke_fill='#ffffff')
            if mode == 'finalTerrain' and terrain is not None:
                for edge in region.get('edges') or []:
                    a, b = edge['a'], edge['b']
                    cx, cy = ox+(a['x']+b['x']+1)/2, oy+(a['y']+b['y']+1)/2
                    if a['x'] != b['x']:
                        ends = (point(cx, cy-.5), point(cx, cy+.5))
                    else:
                        ends = (point(cx-.5, cy), point(cx+.5, cy))
                    is_door = 'door' in edge['kind'].lower()
                    draw.line(ends, fill='#ed9b35' if is_door else '#282731', width=max(1, scale//2))
        if mode == 'overview':
            centers = [point(r['origin']['x']+stride/2, r['origin']['y']+stride/2) for r in regions]
            for a, b in zip(centers, centers[1:]):
                arrow(draw, a, b)
            for r, center in zip(regions, centers):
                draw.ellipse((center[0]-13, center[1]-13, center[0]+13, center[1]+13), fill=BG, outline='#9d3157', width=2)
                label(draw, center, r['index'], 12, INK, 'mm')
        for x in range(lo_x, hi_x+1, stride):
            px, _ = point(x, lo_y)
            label(draw, (px, top-9), x, 11, INK, 'mb')
        for y in range(lo_y, hi_y+1, stride):
            _, py = point(lo_x, y)
            label(draw, (margin-9, py), y, 11, INK, 'rm')
        legend_y = top+height+28
        for i, terrain in enumerate(used):
            lx, ly = margin+(i%6)*175, legend_y+(i//6)*30
            draw.rectangle((lx, ly, lx+14, ly+14), fill=color(terrain), outline='#647080')
            label(draw, (lx+20, ly), terrain, 12)
        ly = legend_y+math.ceil(len(used)/6)*30
        label(draw, (margin, ly), 'Walls: charcoal | Doors: amber | Failed final region: pink crossed tile | Overview: most frequent base terrain ID (grass/dirt separate)', 12)
        image.save(output/filename)


def charts(data, output):
    regions = data['regions']
    # Each panel has its own explicitly labelled scale. Parameter/count comparisons use two axes.
    panel_w, panel_h = max(760, len(regions)*29+130), 330
    image = Image.new('RGB', (panel_w*2+40, panel_h*5+125), BG)
    draw = ImageDraw.Draw(image)
    label(draw, (30, 18), f"Journey variation | seed {data['worldSeed']}", 26)
    label(draw, (30, 56), 'X = chronological region index (region X,Y). Missing final generation leaves gaps; pink columns mark failures.', 15)
    label(draw, (30, 80), 'Final proxies: forest = tree + fruit-tree; rock = solid-rock; water = water + wet-grass + rocky-water + stepping-stones.', 13)
    label(draw, (30, 99), 'Category ranks are ordered labels, not numeric multipliers. Counts are actual generated features.', 12)

    def values(section, key):
        return [(r.get(section) or {}).get(key) for r in regions]

    def rank(section, key):
        raw = values(section, key)
        known = {
            'campsites': ['None', 'Low', 'Medium', 'High', 'VeryHigh'],
            'settlements': ['None', 'Low', 'Medium', 'High', 'VeryHigh'],
            'populationDensity': ['Off', 'Low', 'Medium', 'High'],
            'lootAbundance': ['Sparse', 'Standard', 'Plentiful'],
        }[key]
        present = list(dict.fromkeys(str(v) for v in raw if v is not None))
        unknown = set(present)-set(known)
        if unknown:
            raise ValueError(f'{key}: unknown category values {sorted(unknown)}; update explicit category order')
        ordered = known
        return [ordered.index(str(v)) if v is not None else None for v in raw], ordered

    def panel(index, title, series, left='value', right=None, categories=None, fixed_max=None):
        col, row = index%2, index//2
        ox, oy = 20+col*panel_w, 115+row*panel_h
        draw.rounded_rectangle((ox, oy, ox+panel_w-12, oy+panel_h-12), radius=8, fill='white', outline='#d8dee6')
        label(draw, (ox+16, oy+12), title, 17)
        x0, y0, x1, y1 = ox+65, oy+74, ox+panel_w-70, oy+panel_h-76
        def xpos(i):
            return x0+(x1-x0)*i/max(1, len(regions)-1)
        for i, r in enumerate(regions):
            if r.get('error') or r.get('finalTerrain') is None:
                px = xpos(i)
                draw.rectangle((px-6, y0, px+6, y1), fill='#f9e6e9')
        maxima = {}
        for axis in (0, 1):
            nums = [v for _, vv, _, aa in series if aa == axis for v in vv if v is not None]
            maxima[axis] = (fixed_max if axis == 0 and fixed_max else max(nums, default=1)) or 1
        if categories:
            maxima[0] = max(1, len(categories)-1)
        for tick in range(5):
            yy = y1-(y1-y0)*tick/4
            draw.line((x0, yy, x1, yy), fill='#e2e7ed')
            if not categories:
                label(draw, (x0-8, yy), f'{maxima[0]*tick/4:g}', 11, INK, 'rm')
            if right:
                label(draw, (x1+8, yy), f'{maxima[1]*tick/4:g}', 11, INK, 'lm')
        if categories:
            for i, name in enumerate(categories):
                label(draw, (x0-8, y1-(y1-y0)*i/maxima[0]), name, 10, INK, 'rm')
        label(draw, (x0, oy+51), left, 11)
        if right:
            label(draw, (x1, oy+51), right, 11, INK, 'ra')
        for j, (name, vv, tint, axis) in enumerate(series):
            label(draw, (ox+16+j*155, oy+34), name, 11, tint)
            previous = None
            for i, value in enumerate(vv):
                if value is None:
                    previous = None
                    continue
                p = (xpos(i), y1-(y1-y0)*value/maxima[axis])
                if previous:
                    draw.line((previous, p), fill=tint, width=2)
                draw.ellipse((p[0]-3, p[1]-3, p[0]+3, p[1]+3), fill=tint)
                previous = p
        for i, r in enumerate(regions):
            px = xpos(i)
            label(draw, (px, y1+10), r['index'], 10, INK, 'mt')
            label(draw, (px, y1+27), f"{r['region']['x']},{r['region']['y']}", 9, INK, 'mt')

    for i, kind in enumerate(('forest', 'rock', 'water')):
        panel(i, f'{kind.capitalize()} coverage', [
            ('sampled', values('sampledCoverage', kind), '#a065a3', 0),
            ('actual base', values('baseCoverage', kind), '#47763c', 0),
            ('actual final', values('finalCoverage', kind), '#2e78b4', 0),
        ], left='coverage (%)', fixed_max=100)
    panel(3, 'Enrichment', [('effective intensity', values('parameters', 'enrichmentIntensity'), '#96529e', 0),
                             ('enriched cells', values('features', 'enrichedCells'), '#328682', 1)],
          left='intensity', right='cells')
    for i, key in enumerate(('campsites', 'settlements')):
        ranked, cats = rank('parameters', key)
        panel(4+i, f'Civilization: {key}', [('effective setting', ranked, '#96529e', 0),
              ('generated '+key, values('features', key), '#328682', 1)], left='setting', right='count', categories=cats)
    panel(6, 'Routes', [('effective intensity', values('parameters', 'routeIntensity'), '#96529e', 0),
                       ('generated routes', values('features', 'routes'), '#328682', 1)], left='intensity', right='count')
    ranked, cats = rank('parameters', 'populationDensity')
    panel(7, 'Population', [('effective density', ranked, '#96529e', 0),
                           ('monsters', values('features', 'monsters'), '#328682', 1)], left='setting', right='count', categories=cats)
    panel(8, 'Difficulty and generated locations', [('effective difficulty', values('parameters', 'difficulty'), '#96529e', 0),
          ('sites', values('features', 'sites'), '#328682', 1), ('dungeons', values('features', 'dungeons'), '#c2852d', 1),
          ('buildings', values('features', 'buildings'), '#627fbe', 1)], left='difficulty', right='count')
    ranked, cats = rank('parameters', 'lootAbundance')
    panel(9, 'Loot', [('effective abundance', ranked, '#96529e', 0),
          ('site loot', values('features', 'siteLoot'), '#328682', 1),
          ('loot markers', values('features', 'lootMarkers'), '#c2852d', 1)], left='setting', right='count', categories=cats)
    image.save(output/'variation-charts.png')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('journey', type=Path)
    parser.add_argument('--cell-pixels', type=int, default=4, help='common integer pixel scale for geographic cells (default 4)')
    args = parser.parse_args()
    if args.cell_pixels < 1:
        parser.error('--cell-pixels must be positive')
    data = json.loads(args.journey.read_text(encoding='utf-8-sig'))
    if data.get('stride') != 60 or not data.get('regions'):
        parser.error('expected a nonempty journey with stride 60')
    for region in data['regions']:
        for key in ('baseTerrain', 'finalTerrain'):
            terrain = region.get(key)
            if (key == 'baseTerrain' or terrain is not None) and (terrain is None or len(terrain) != 3600):
                parser.error(f"region {region['index']}: {key} must contain 3600 row-major cells")
    output = args.journey.resolve().parent
    maps(data, output, args.cell_pixels)
    charts(data, output)
    for name in ('base-nature.png', 'final-world.png', 'regional-overview.png', 'variation-charts.png'):
        print(output/name)


if __name__ == '__main__':
    main()
