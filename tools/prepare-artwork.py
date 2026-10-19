"""Export the first local artwork pack. Build-time dependency: Pillow with WebP.

Run from any directory: python tools/prepare-artwork.py
Sources are read only. Preview is for composition review, not a browser screenshot.
"""
from hashlib import sha256
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageCms, ImageDraw, ImageOps, features

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets/source"
OUTPUT = ROOT / "ProjectDelve.Web/wwwroot/art"
ASSETS = [
    *[(f"units/{name}.png", f"units/{name}.webp", 512)
      for name in ("barbarian", "zombie", "worker", "geomancer")],
    *[(f"terrain/stone-floor-{i:02}.png", f"terrain/stone-floor/{i:02}.webp", 256)
      for i in range(1, 4)],
]


def main():
    assert features.check("webp"), "Pillow requires WebP support"
    hashes = {name: sha256((SOURCE / name).read_bytes()).digest() for name, _, _ in ASSETS}
    # Inspect every original before exporting anything; require review if composition changes.
    for name, _, _ in ASSETS:
        with Image.open(SOURCE / name) as original:
            print(f"Source {name}: {original.size}, {original.mode}, {original.format}")
            assert original.width == original.height, f"Review non-square source: {name}"
            assert getattr(original, "n_frames", 1) == 1, f"Review animated source: {name}"

    srgb = ImageCms.createProfile("sRGB")
    profile = ImageCms.ImageCmsProfile(srgb).tobytes()
    decoded = []
    for name, destination, size in ASSETS:
        with Image.open(SOURCE / name) as original:
            image = ImageOps.exif_transpose(original).convert("RGBA")
            if original.info.get("icc_profile"):
                image = ImageCms.profileToProfile(image, ImageCms.ImageCmsProfile(
                    BytesIO(original.info["icc_profile"])), srgb, outputMode="RGBA")
            # Untagged generated sources are treated as sRGB. No crop or retouching.
            image = image.resize((size, size), Image.Resampling.LANCZOS)
            if image.getchannel("A").getextrema() == (255, 255):
                image = image.convert("RGB")
            target = OUTPUT / destination
            target.parent.mkdir(parents=True, exist_ok=True)
            image.save(target, "WEBP", quality=85, method=6, alpha_quality=100,
                       exact=True, icc_profile=profile)
        with Image.open(target) as result:
            result.load()  # Verify complete decoding, dimensions and alpha after encoding.
            assert result.size == (size, size)
            if image.mode == "RGBA":
                assert result.getchannel("A").tobytes() == image.getchannel("A").tobytes()
            decoded.append(result.convert("RGB"))
        print(f"Export {destination}: {size}x{size}, {target.stat().st_size:,} bytes")

    # Roughly 50px portraits (circular UI clip) and 60px Cells, plus larger references.
    sheet = Image.new("RGB", (720, 350), "#202630")
    draw = ImageDraw.Draw(sheet)
    draw.text((16, 12), "Artwork pack 01 | 50px portrait / 60px Cell + larger references", fill="white")
    for i, ((_, destination, _), image) in enumerate(zip(ASSETS, decoded)):
        x, y = 16 + (i % 4) * 176, 40 + (i // 4) * 150
        draw.text((x, y), Path(destination).stem, fill="white")
        sheet.paste(image.resize((100, 100), Image.Resampling.LANCZOS), (x, y + 20))
        thumb = image.resize((50, 50) if i < 4 else (60, 60), Image.Resampling.LANCZOS)
        mask = None
        if i < 4:
            mask = Image.new("L", (200, 200))
            ImageDraw.Draw(mask).ellipse((0, 0, 199, 199), fill=255)
            mask = mask.resize((50, 50), Image.Resampling.LANCZOS)
        sheet.paste(thumb, (x + 106, y + 40), mask)
    preview = ROOT / "assets/previews/artwork-pack-01.png"
    preview.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(preview, optimize=True)
    assert all(sha256((SOURCE / name).read_bytes()).digest() == digest
               for name, digest in hashes.items()), "Source images changed"


if __name__ == "__main__":
    main()
