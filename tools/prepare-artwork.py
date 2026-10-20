"""Export the local artwork catalog. Build-time dependency: Pillow with WebP.

Run from any directory: python tools/prepare-artwork.py
Sources are read only. Preview is for composition review, not a browser screenshot.
"""
from hashlib import sha256
import argparse
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageCms, ImageDraw, ImageOps, features
from railway_geometry import export_tracks, preview_tracks

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets/source"
OUTPUT = ROOT / "ProjectDelve.Web/wwwroot/art"
# Reviewed source rectangles (left, top, right, bottom). Outside these bounds
# there are only empty margins and stray alpha < 8/255 pixels, not object detail.
EDGE_CROPS = {
    "wall": (15, 325, 1766, 555),
    "door-closed": (16, 299, 1764, 573),
    "door-open": (16, 295, 1763, 569),
}
ASSETS = [
    *[(f"units/{name}.png", f"units/{name}.webp", 512)
      for name in ("barbarian", "zombie", "worker", "chief-mechanic", "diamond-kart", "geomancer",
                   "wizard", "rogue", "grunt", "skeleton-archer", "goblin", "troll",
                   "cleric", "shaman", "ghost", "bomb-imp", "displacer-demon",
                   "labyrinth-shaper", "red-dragon", "locomotive")],
    *[(f"terrain/{name}-{i:02}.png", f"terrain/{name}/{i:02}.webp", 256)
      for name in ("stone-floor", "grass", "water", "lava", "gravel", "asphalt") for i in range(1, 4)],
    *[(f"terrain/objects/{name}-{i:02}.png", f"terrain/objects/{name}/{i:02}.webp", 256)
      for name in ("tree", "table", "fruit-tree") for i in range(1, 4)],
    *[(f"resources/{name}.png", f"resources/{name}.webp", 128) for name in ("coal", "diamond", "fruit", "banana", "mushroom", "shell")],
    ("markers/banana-peel.png", "markers/banana-peel.webp", 128),
    ("machines/diamond-press.png", "machines/diamond-press.webp", 256),
    ("machines/slot-machine.png", "machines/slot-machine.webp", 256),
    *[(f"edges/{name}.png", f"edges/{name}.webp", (512, 80)) for name in EDGE_CROPS],
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--edges-only", action="store_true", help="Export only the reviewed Edge batch")
    args = parser.parse_args()
    assets = [asset for asset in ASSETS if not args.edges_only or asset[0].startswith("edges/")]
    assert features.check("webp"), "Pillow requires WebP support"
    hashes = {path: sha256(path.read_bytes()).digest()
              for path in SOURCE.rglob("*") if path.is_file()}
    if not args.edges_only:
        for name in ("sleeper", "rail"):
            with Image.open(SOURCE / f"railway/{name}.png") as original:
                original.load()
                assert original.width == original.height and original.mode == "RGBA"
                assert original.getchannel("A").getextrema() == (0, 255)
                assert getattr(original, "n_frames", 1) == 1
    # Inspect every original before exporting anything; require review if composition changes.
    for name, _, _ in assets:
        with Image.open(SOURCE / name) as original:
            original.load()
            print(f"Source {name}: {original.size}, {original.mode}, {original.format}")
            if not name.startswith("edges/"):
                assert original.width == original.height, f"Review non-square source: {name}"
                if name.startswith("terrain/") and not name.startswith("terrain/objects/"):
                    assert original.convert("RGBA").getchannel("A").getextrema() == (255, 255), f"Review transparent terrain: {name}"
            else:
                assert original.size == (1774, 887) and original.mode == "RGBA", f"Review changed Edge source: {name}"
                bounds = original.getchannel("A").point(lambda a: 255 if a >= 8 else 0).getbbox()
                assert bounds == EDGE_CROPS[Path(name).stem], f"Review changed Edge composition: {name}"
            if name.startswith(("terrain/objects/", "resources/", "machines/", "markers/")):
                alpha = original.convert("RGBA").getchannel("A")
                assert alpha.getextrema()[0] == 0 and alpha.getextrema()[1] > 0, f"Review object alpha: {name}"
            assert getattr(original, "n_frames", 1) == 1, f"Review animated source: {name}"

    srgb = ImageCms.createProfile("sRGB")
    profile = ImageCms.ImageCmsProfile(srgb).tobytes()
    decoded = []
    for name, destination, size in assets:
        with Image.open(SOURCE / name) as original:
            image = ImageOps.exif_transpose(original).convert("RGBA")
            if original.info.get("icc_profile"):
                image = ImageCms.profileToProfile(image, ImageCms.ImageCmsProfile(
                    BytesIO(original.info["icc_profile"])), srgb, outputMode="RGBA")
            # Untagged generated sources are treated as sRGB.
            dimensions = size if isinstance(size, tuple) else (size, size)
            if name.startswith("edges/"):
                # Preserve the full visible object and its aspect ratio. The 80px
                # canvas accommodates the Doors without cutting off posts/leaves.
                image = ImageOps.contain(image.crop(EDGE_CROPS[Path(name).stem]),
                                         dimensions, Image.Resampling.LANCZOS)
                canvas = Image.new("RGBA", dimensions)
                canvas.paste(image, ((dimensions[0] - image.width) // 2,
                                     (dimensions[1] - image.height) // 2))
                image = canvas
            else:
                image = image.resize(dimensions, Image.Resampling.LANCZOS)
            if image.getchannel("A").getextrema() == (255, 255):
                image = image.convert("RGB")
            target = OUTPUT / destination
            target.parent.mkdir(parents=True, exist_ok=True)
            image.save(target, "WEBP", quality=85, method=6, alpha_quality=100,
                       exact=True, icc_profile=profile)
        with Image.open(target) as result:
            result.load()  # Verify complete decoding, dimensions and alpha after encoding.
            assert result.format == "WEBP"
            assert result.size == dimensions
            assert result.info.get("icc_profile") == profile
            if name.startswith("terrain/") and not name.startswith("terrain/objects/"):
                assert result.convert("RGBA").getchannel("A").getextrema() == (255, 255)
            if image.mode == "RGBA":
                assert result.getchannel("A").tobytes() == image.getchannel("A").tobytes()
            if name.startswith("edges/"):
                assert result.getchannel("A").getextrema() == (0, 255)
                assert target.stat().st_size < 100_000, f"Review oversized Edge: {destination}"
                if Path(name).stem == "door-open":
                    # The middle of the passage must remain transparent, with
                    # neither a painted background nor a fallback bar beneath it.
                    gap = result.getchannel("A").crop((200, 28, 312, 52))
                    assert gap.getextrema()[1] < 8, "Review Open Door central opening"
            decoded.append(result.convert("RGBA"))
        print(f"Export {destination}: {dimensions[0]}x{dimensions[1]}, {target.stat().st_size:,} bytes")

    # Roughly 50px portraits (circular UI clip) and 60px Cells, plus larger references.
    edge_preview([image for asset, image in zip(assets, decoded) if asset[0].startswith("edges/")])
    if not args.edges_only:
        composite_preview(assets, decoded)
        object_preview(assets, decoded)
        marker_preview(assets, decoded)
        export_tracks(SOURCE, OUTPUT / "railway")
        preview_tracks(SOURCE, OUTPUT, ROOT / "assets/previews/railway.png")
    assert all(sha256(path.read_bytes()).digest() == digest
               for path, digest in hashes.items()), "Source images changed"
    if args.edges_only:
        return
    square_assets = [(asset, image) for asset, image in zip(assets, decoded) if not asset[0].startswith("edges/")]
    sheet = Image.new("RGB", (720, 50 + ((len(square_assets) + 3) // 4) * 150), "#202630")
    draw = ImageDraw.Draw(sheet)
    draw.text((16, 12), "Artwork catalog | 50px portrait / 60px Cell + larger references", fill="white")
    for i, ((_, destination, _), image) in enumerate(square_assets):
        x, y = 16 + (i % 4) * 176, 40 + (i // 4) * 150
        label = Path(destination).with_suffix("").as_posix().replace("terrain/", "").replace("units/", "")
        draw.text((x, y), label, fill="white")
        reference = image.resize((100, 100), Image.Resampling.LANCZOS)
        sheet.paste(reference, (x, y + 20), reference)
        is_portrait = Path(destination).parts[0] == "units"
        thumb = image.resize((50, 50) if is_portrait else (60, 60), Image.Resampling.LANCZOS)
        mask = thumb
        if is_portrait:
            mask = Image.new("L", (200, 200))
            ImageDraw.Draw(mask).ellipse((0, 0, 199, 199), fill=255)
            mask = mask.resize((50, 50), Image.Resampling.LANCZOS)
        sheet.paste(thumb, (x + 106, y + 40), mask)
    preview = ROOT / "assets/previews/artwork-pack-01.png"
    preview.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(preview, optimize=True)


def composite_preview(assets, images):
    """Test-owned samples at minimum Cell scale, all base/object slots and turns."""
    catalog = {asset[1]: image for asset, image in zip(assets, images)}
    sheet = Image.new("RGB", (768, 752), "#202630")
    draw = ImageDraw.Draw(sheet)
    draw.text((12, 8), "Composite terrain | 60px Cells | base/object variants + independent turns", fill="white")
    for row, (obj, base) in enumerate((("tree", "grass"), ("table", "stone-floor"), ("fruit-tree", "grass"))):
        for variant in range(3):
            for turn in range(4):
                x = 12 + (variant * 4 + turn) * 62
                y = 40 + row * 232
                tile = catalog[f"terrain/{base}/{variant + 1:02}.webp"].rotate(turn * 90)
                overlay = catalog[f"terrain/objects/{obj}/{(variant + turn) % 3 + 1:02}.webp"].rotate((turn + 1) * 90)
                tile = Image.alpha_composite(tile, overlay)
                sheet.paste(tile.resize((60, 60), Image.Resampling.LANCZOS), (x, y))
                if turn == 0:
                    sheet.paste(tile.resize((160, 160), Image.Resampling.LANCZOS), (12 + variant * 248, y + 64))
    sheet.save(ROOT / "assets/previews/composite-terrain.png", optimize=True)


def marker_preview(assets, images):
    """Marker size and alpha over three materials, without gameplay state."""
    catalog = {asset[1]: image for asset, image in zip(assets, images)}
    sheet = Image.new("RGB", (420, 210), "#202630")
    draw = ImageDraw.Draw(sheet)
    draw.text((12, 10), "Banana Peel | 60px and 100px Cells", fill="white")
    for col, base in enumerate(("stone-floor", "grass", "gravel")):
        for row, size in enumerate((60, 100)):
            tile = catalog[f"terrain/{base}/01.webp"].resize((size, size), Image.Resampling.LANCZOS)
            marker_size = round(size * .62)
            marker = catalog["markers/banana-peel.webp"].resize((marker_size, marker_size), Image.Resampling.LANCZOS)
            tile.alpha_composite(marker, ((size-marker_size)//2, (size-marker_size)//2))
            sheet.paste(tile, (12 + col*136, 34 + row*72))
    sheet.save(ROOT / "assets/previews/cell-markers.png", optimize=True)


def object_preview(assets, images):
    """Test-owned Machine/Resource samples at minimum and normal Cell sizes."""
    catalog = {asset[1]: image for asset, image in zip(assets, images)}
    sheet = Image.new("RGB", (720, 920), "#202630")
    draw = ImageDraw.Draw(sheet)
    draw.text((12, 10), "Machines / Resources | 24px markers | 60px + 100px Cells", fill="white")
    for size_index, size in enumerate((60, 100)):
        for base_index, base in enumerate(("stone-floor", "grass", "gravel")):
            for i, resource in enumerate(("coal", "diamond", "fruit", "banana", "mushroom", "shell")):
                x, y = 12 + i * 116, 46 + (size_index * 3 + base_index) * 116
                tile = catalog[f"terrain/{base}/01.webp"].resize((size, size), Image.Resampling.LANCZOS)
                marker = catalog[f"resources/{resource}.webp"].resize((24, 24), Image.Resampling.LANCZOS)
                tile.alpha_composite(marker, (size-25, 1))
                sheet.paste(tile, (x, y))
                draw.text((x, y+size+2), resource, fill="white")
    for i, (machine, name, resource) in enumerate((("diamond-press", "Press", "Coal"), ("slot-machine", "Slot", "Fruit"))):
        for j, size in enumerate((60, 100)):
            x, y = 12 + i * 350 + j * 150, 780
            tile = catalog["terrain/stone-floor/01.webp"].resize((size, size), Image.Resampling.LANCZOS)
            tile = Image.alpha_composite(tile, catalog[f"machines/{machine}.webp"].resize((size, size), Image.Resampling.LANCZOS))
            label = ImageDraw.Draw(tile)
            label.rectangle((1, size-27, size-1, size-1), fill="#18232cdd")
            for line, text in enumerate((name, "Blue", f"{resource}: 3/3")):
                label.text((2, size-27 + line*9), text, fill="white", font_size=8)
            sheet.paste(tile, (x, y))
            draw.text((x, y+size+2), machine, fill="white")
    sheet.save(ROOT / "assets/previews/machines-resources.png", optimize=True)


def edge_preview(images):
    """Both orientations over a checkerboard, plus a test-owned sample layout."""
    preview = ROOT / "assets/previews/edges.png"
    preview.parent.mkdir(parents=True, exist_ok=True)
    sheet = Image.new("RGBA", (1000, 760), "#303a42")
    draw = ImageDraw.Draw(sheet)
    for y in range(30, 760, 12):
        for x in range(0, 1000, 12):
            if (x // 12 + y // 12) % 2:
                draw.rectangle((x, y, x + 11, y + 11), fill="#46515a")
    draw.text((16, 8), "Edge masters | horizontal + clockwise vertical | transparency checkerboard", fill="white")
    for i, (name, image) in enumerate(zip(EDGE_CROPS, images)):
        draw.text((16, 44 + i * 120), f"Horizontal {name}", fill="white")
        sheet.alpha_composite(image, (16, 64 + i * 120))
        draw.text((550 + i * 146, 44), name, fill="white")
        sheet.alpha_composite(image.transpose(Image.Transpose.ROTATE_270), (570 + i * 146, 70))
    draw.text((16, 426), "Sample: connected walls / closed + open doors (120px Cells)", fill="white")
    for y in range(2):
        for x in range(4):
            draw.rectangle((20 + x * 120, 460 + y * 120, 140 + x * 120, 580 + y * 120),
                           fill="#666a68", outline="#293238")
    for x, image in enumerate([images[0], images[0], images[1], images[2]]):
        thumb = image.resize((120, 19), Image.Resampling.LANCZOS)
        sheet.alpha_composite(thumb, (20 + x * 120, 570))
    sheet.alpha_composite(images[0].resize((120, 19), Image.Resampling.LANCZOS)
                          .transpose(Image.Transpose.ROTATE_270), (251, 460))
    sheet.convert("RGB").save(preview, optimize=True)


if __name__ == "__main__":
    main()
