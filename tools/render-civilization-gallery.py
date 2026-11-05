"""Deterministic top-down before/after and Site detail sheets. Actual UI inspected separately in Edge."""
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

source, output = map(Path, sys.argv[1:3])
output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 17)
small = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 13)
colors = {"grass": "#809a54", "dirt": "#967555", "tree": "#294e39", "solid-rock": "#626b76",
          "water": "#347f9d", "stone-floor": "#c6b7a3", "gravel": "#9d9b8e", "stone-floor-with-table": "#c6b7a3", "campfire": "#809a54"}


def panel(data, frame, pixels=5, crop=None):
    w, h = data["window"]["width"], data["window"]["height"]
    x0, y0, cw, ch = crop or (0, 0, w, h)
    image = Image.new("RGB", (cw * pixels, ch * pixels), "#19232d")
    draw = ImageDraw.Draw(image)
    palette = data["preview"]["terrainPalette"]
    for y in range(y0, y0 + ch):
        for x in range(x0, x0 + cw):
            kind = palette[ord(frame["terrain"][y * w + x]) - 65]
            px, py = (x - x0) * pixels, (y - y0) * pixels
            draw.rectangle((px, py, px + pixels - 1, py + pixels - 1), fill=colors.get(kind, "magenta"))
            if pixels >= 8:
                draw.rectangle((px, py, px + pixels - 1, py + pixels - 1), outline="#667363")
            if kind == "tree" and pixels >= 5:
                draw.ellipse((px + 1, py + 1, px + pixels - 2, py + pixels - 2), outline="#527249")
            if kind in ("campfire", "stone-floor-with-table"):
                inset = max(1, pixels // 4)
                draw.rectangle((px + inset, py + inset, px + pixels - inset - 1, py + pixels - inset - 1), fill="#ff942e" if kind == "campfire" else "#6b4226")
    for edge in frame.get("edges", []):
        x, y = edge["position"]["x"], edge["position"]["y"]
        px, py = (x - x0) * pixels, (y - y0) * pixels
        xy = (px + pixels, py, px + pixels, py + pixels) if edge["direction"] == "Right" else (px, py + pixels, px + pixels, py + pixels)
        draw.line(xy, fill="#e5a14a" if "Door" in edge["kind"] else "#17212b", width=max(2, pixels // 5))
    return image


manifest = []
names = ["120x100-Grassland", "DenseWoodland", "ExcavatedRock", "ArtificialIslands", "Mixed", "NarrowEdge"]
for name in names:
    data = json.loads((source / (name + ".response.json")).read_text(encoding="utf-8-sig"))
    frames = data["preview"]["frames"]
    index = next(i for i, frame in enumerate(frames) if frame["stage"] == "Civilization")
    w, h = data["window"]["width"], data["window"]["height"]
    scale = 5 if w >= 100 else 8
    canvas = Image.new("RGB", (w * scale * 2 + 42, h * scale + 108), "#19232d")
    draw = ImageDraw.Draw(canvas)
    draw.text((12, 8), f"{name} / before Civilization and final accepted Sites", fill="white", font=font)
    for column, frame in enumerate([frames[index - 1], frames[-1]]):
        canvas.paste(panel(data, frame, scale), (12 + column * (w * scale + 18), 40))
    stats = frames[-1].get("civilizationStats") or {}
    draw.text((12, h * scale + 50), f"Sites {stats.get('sites', 0)} / Buildings {stats.get('buildings', 0)} / Rooms {stats.get('rooms', 0)} / Attempts {stats.get('attempts', 0)}", fill="white", font=small)
    draw.text((12, h * scale + 74), "Dark lines: Walls / amber: Doors / brown: tables / orange: Campfire. Water-separated Sites require later connections.", fill="#bccbd4", font=small)
    image_name = f"world-civilization-{name.lower()}.png"
    canvas.save(output / image_name)
    manifest.append({"name": name, "image": image_name, "code": data["code"], "fingerprint": data["fingerprint"], "stats": stats})

# Independent details across deterministic seeds/landscapes expose room and Door geometry.
details = []
for name in names:
    data = json.loads((source / (name + ".response.json")).read_text(encoding="utf-8-sig"))
    for site in data["preview"]["frames"][-1].get("sites", []):
        details.append((name, data, site))
sheet = Image.new("RGB", (1260, 550 * ((len(details) + 2) // 3) + 40), "#19232d")
draw = ImageDraw.Draw(sheet)
draw.text((12, 6), "Accepted Site details / procedural partitions, entrances and preparation / same fixed seeds as comparisons", fill="white", font=font)
for i, (name, data, site) in enumerate(details):
    b = site["footprint"]["bounds"]
    w, h = data["window"]["width"], data["window"]["height"]
    x, y = max(0, b["x"] - 2), max(0, b["y"] - 2)
    cw, ch = min(w - x, b["width"] + 4), min(h - y, b["height"] + 4)
    pixels = min(18, 390 // cw, 455 // ch)
    left, top = 12 + i % 3 * 420, 40 + i // 3 * 550
    draw.text((left, top), f"{name} / {site['kind']}", fill="white", font=small)
    sheet.paste(panel(data, data["preview"]["frames"][-1], pixels, (x, y, cw, ch)), (left, top + 25))
    draw.text((left, top + 488), site["id"].split("/")[-1] + " / preparation " + str(site["preparation"]["cost"]), fill="#bccbd4", font=small)
sheet.save(output / "world-civilization-site-details.png")
(output / "world-civilization-gallery.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
