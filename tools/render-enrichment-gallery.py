"""Render deterministic stage data as compact comparison sheets (Pillow, no browser).

Usage: python tools/render-enrichment-gallery.py fixtures output
The actual Enhanced/Classic browser workflow is verified separately in Edge.
"""
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

source, destination = map(Path, sys.argv[1:3])
destination.mkdir(parents=True, exist_ok=True)
COLORS = {"grass": "#809a54", "dirt": "#967555", "tree": "#294e39",
          "solid-rock": "#626b76", "water": "#347f9d", "stone-floor": "#c6b7a3"}
font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 17)
small = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 13)


def panel(data, frame, pixels, changed=False):
    w, h = data["window"]["width"], data["window"]["height"]
    image = Image.new("RGB", (w * pixels, h * pixels))
    draw = ImageDraw.Draw(image)
    palette = data["preview"]["terrainPalette"]
    for i, value in enumerate(frame["terrain"]):
        x, y = i % w * pixels, i // w * pixels
        kind = palette[ord(value) - 65]
        draw.rectangle((x, y, x + pixels - 1, y + pixels - 1), fill=COLORS[kind])
        if pixels >= 5 and kind == "tree":
            draw.ellipse((x + 1, y + 1, x + pixels - 2, y + pixels - 2), outline="#527249")
    if changed:
        for i in frame["changedCells"]:
            x, y = i % w * pixels, i // w * pixels
            draw.rectangle((x, y, x + pixels - 1, y + pixels - 1), outline="#ffd34f")
    return image


manifest = []
cases = [("Woodland", "ForestEnrichment"), ("RockyWilds", "RockEnrichment"),
         ("LakeCountry", "WaterEnrichment"), ("RiverValley", "WaterEnrichment"),
         ("Grassland", "GrasslandEnrichment")]
for preset, stage in cases:
    path = source / f"120x100-{preset}.response.json"
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    frames = data["preview"]["frames"]
    index = next(i for i, f in enumerate(frames) if f["stage"] == stage)
    selected = [frames[index - 1], frames[index], frames[-1]]
    canvas = Image.new("RGB", (1488, 522), "#19232d")
    draw = ImageDraw.Draw(canvas)
    draw.text((14, 8), f"{preset} / {stage} / 120 x 100 / offset (-17,-13)", fill="white", font=font)
    for column, (frame, label) in enumerate(zip(selected, ["Before this stage", "After this stage (gold edits)", "Final recipe"])):
        x = 14 + column * 494
        draw.text((x, 38), label, fill="#c2d1dd", font=small)
        canvas.paste(panel(data, frame, 4, column == 1), (x, 60))
        d = frame["diagnostics"]
        draw.text((x, 466), f'Walkable {d["walkableArea"]}; largest {d["largestComponentArea"]}; edits {d["changedCells"]}', fill="#c2d1dd", font=small)
    draw.text((14, 497), "Grass green / Trees dark green / Rock grey / Water blue / Dirt brown / Cave floor light stone", fill="#a9bac8", font=small)
    output = f"world-enrichment-{preset.lower()}.png"
    canvas.save(destination / output)
    manifest.append({"image": output, "code": data["code"], "stage": stage, "fingerprint": data["fingerprint"]})

# Reuse every original fixed-seed landscape, with unchanged underlying natural sampling.
paths = sorted(source.glob("world-*.response.json"))
sheet = Image.new("RGB", (1260, 390 * ((len(paths) + 1) // 2) + 35), "#19232d")
draw = ImageDraw.Draw(sheet)
draw.text((12, 7), "Original fixed-seed corpus / Natural Terrain vs final enrichment / same source samples", fill="white", font=font)
for case, path in enumerate(paths):
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    x, y = case % 2 * 630 + 12, case // 2 * 390 + 38
    draw.text((x, y), path.stem.removesuffix(".response"), fill="#c2d1dd", font=small)
    for column, frame in enumerate([data["preview"]["frames"][0], data["preview"]["frames"][-1]]):
        sheet.paste(panel(data, frame, 6), (x + column * 307, y + 25))
    draw.text((x, y + 330), "Natural Terrain                          Final enrichment", fill="#a9bac8", font=small)
    manifest.append({"case": path.stem, "code": data["code"], "fingerprint": data["fingerprint"]})
sheet.save(destination / "world-enrichment-fixed-seeds.png")

# Largest-size final result checks, retained at enough resolution to inspect topology.
paths = sorted(source.glob("120x120-*.response.json"))
sheet = Image.new("RGB", (1240, len(paths) * 660 + 35), "#19232d")
draw = ImageDraw.Draw(sheet)
draw.text((12, 7), "120 x 120 / Natural Terrain vs final enrichment", fill="white", font=font)
for row, path in enumerate(paths):
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    y = row * 660 + 38
    draw.text((12, y), path.stem.removesuffix(".response"), fill="#c2d1dd", font=font)
    for column, frame in enumerate([data["preview"]["frames"][0], data["preview"]["frames"][-1]]):
        sheet.paste(panel(data, frame, 5), (12 + column * 615, y + 30))
sheet.save(destination / "world-enrichment-120x120.png")
crop_path = source / "45x70-BoundaryCrop.response.json"
if crop_path.exists():
    data = json.loads(crop_path.read_text(encoding="utf-8-sig"))
    crop_sheet = Image.new("RGB", (760, 650), "#19232d")
    draw = ImageDraw.Draw(crop_sheet)
    draw.text((12, 8), "Awkward 45 x 70 boundary crop / offset (-1,-69)", fill="white", font=font)
    for column, frame in enumerate([data["preview"]["frames"][0], data["preview"]["frames"][-1]]):
        crop_sheet.paste(panel(data, frame, 8), (12 + column * 380, 45))
    draw.text((12, 614), "Natural Terrain                                          Final enrichment", fill="#a9bac8", font=small)
    crop_sheet.save(destination / "world-enrichment-boundary-crop.png")
    manifest.append({"case": "boundary-crop", "code": data["code"], "fingerprint": data["fingerprint"]})
for path in paths:
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    manifest.append({"case": path.stem, "code": data["code"], "fingerprint": data["fingerprint"]})
(destination / "world-enrichment-gallery.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
