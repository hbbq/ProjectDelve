"""Verify the registered transparent exports against their immutable originals.

Run: python -m unittest discover -s tools -p test_prepare_artwork.py
Requires the same Pillow/WebP environment as prepare-artwork.py.
"""
import importlib.util
from io import BytesIO
from pathlib import Path
import unittest
import tempfile
import re
import xml.etree.ElementTree as ET
from urllib.parse import unquote
import railway_geometry as railway

from PIL import Image, ImageCms, ImageOps

spec = importlib.util.spec_from_file_location("prepare_artwork", Path(__file__).with_name("prepare-artwork.py"))
pipeline = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pipeline)


class CompositeArtworkTests(unittest.TestCase):
    def test_track_generation_reproduces_committed_assets(self):
        with tempfile.TemporaryDirectory() as directory:
            generated = Path(directory)
            railway.export_tracks(pipeline.SOURCE, generated)
            for name in ['fallbacks.css', *[f'{shape}.svg' for shape in railway.SHAPES]]:
                self.assertEqual((generated/name).read_bytes(), (pipeline.OUTPUT/'railway'/name).read_bytes())

    def test_gravel_and_existing_runtime_decoding(self):
        for source, destination, size in pipeline.ASSETS:
            with self.subTest(destination=destination), Image.open(pipeline.OUTPUT / destination) as image:
                image.load()
                self.assertEqual(image.size, size if isinstance(size, tuple) else (size, size))
                self.assertEqual(ImageCms.getProfileName(ImageCms.ImageCmsProfile(BytesIO(image.info['icc_profile']))).strip(), 'sRGB built-in')
                if source.startswith('terrain/') and not source.startswith('terrain/objects/'):
                    self.assertEqual(image.convert('RGBA').getchannel('A').getextrema(), (255,255))
        for slot in range(1,4):
            self.assertIn((f'terrain/gravel-{slot:02}.png', f'terrain/gravel/{slot:02}.webp',256), pipeline.ASSETS)

    def test_rail_exports_embed_real_transparent_sleeper_and_share_exact_ports(self):
        ns = {'s':'http://www.w3.org/2000/svg'}
        css = (pipeline.OUTPUT / 'railway/fallbacks.css').read_text()
        for shape, sides in railway.SHAPES.items():
            with self.subTest(shape=shape):
                svg = ET.parse(pipeline.OUTPUT / f'railway/{shape}.svg').getroot()
                self.assertEqual(svg.attrib['viewBox'], '0 0 256 256')
                encoded = svg.find('s:defs/s:image', ns).attrib['href'].split(',')[1]
                import base64
                with Image.open(BytesIO(base64.b64decode(encoded))) as tie:
                    tie.load()
                    self.assertEqual(tie.getchannel('A').getextrema(), (0,255))
                    self.assertIn('icc_profile', tie.info)
                paths = [p.attrib['d'] for p in svg.findall('s:path',ns) if p.attrib['stroke-width'] == str(railway.RAIL_WIDTH)]
                self.assertEqual(paths, railway.geometry(shape)[0])
                fallback = re.search(r'data-rail-shape="'+shape+r'".*?data:image/svg\+xml,([^";]+)',css).group(1)
                self.assertEqual([p.attrib['d'] for p in ET.fromstring(unquote(fallback)).findall('s:path',ns)], paths)
                endpoints = set()
                for path in paths:
                    parts = path.split()
                    start = (float(parts[1]),float(parts[2]))
                    if parts[3] == 'V': end = (start[0],float(parts[4]))
                    elif parts[3] == 'H': end = (float(parts[4]),start[1])
                    else:
                        end = (float(parts[-2]),float(parts[-1]))
                        # Circular rails share corner centers, with exact tangent ports.
                        self.assertEqual(float(parts[4]),float(parts[5]))
                        self.assertEqual(parts[6:9], ['0','0','0'])
                    endpoints.update((start,end))
                expected = {point for side in sides for point in (
                    [(p,0) for p in railway.PORTS] if side == 0 else
                    [(256,p) for p in railway.PORTS] if side == 1 else
                    [(p,256) for p in railway.PORTS] if side == 2 else
                    [(0,p) for p in railway.PORTS])}
                self.assertEqual(endpoints,expected)
                # Every rotation still exposes exactly the same two port positions.
                for turn in range(4):
                    rotated = endpoints
                    for _ in range(turn): rotated = {(256-y,x) for x,y in rotated}
                    for side in [(s+turn)%4 for s in sides]:
                        port = {(p,0) for p in railway.PORTS} if side == 0 else {(256,p) for p in railway.PORTS} if side == 1 else {(p,256) for p in railway.PORTS} if side == 2 else {(0,p) for p in railway.PORTS}
                        self.assertTrue(port <= rotated)

    def test_transparent_object_registrations_and_lossless_resized_alpha(self):
        registrations = {source: (destination, size) for source, destination, size in pipeline.ASSETS}
        for name in ("tree", "table"):
            for slot in range(1, 4):
                source = f"terrain/objects/{name}-{slot:02}.png"
                with self.subTest(source=source):
                    destination, size = registrations[source]
                    self.assertEqual(destination, f"terrain/objects/{name}/{slot:02}.webp")
                    self.assertEqual(size, 256)
                    with Image.open(pipeline.SOURCE / source) as original:
                        original.load()
                        self.assertEqual(original.format, "PNG")
                        self.assertEqual(original.mode, "RGBA")
                        expected = ImageOps.exif_transpose(original).resize((256, 256), Image.Resampling.LANCZOS)
                    with Image.open(pipeline.OUTPUT / destination) as exported:
                        exported.load()
                        self.assertEqual(exported.format, "WEBP")
                        self.assertEqual(exported.size, (256, 256))
                        alpha = exported.getchannel("A")
                        self.assertEqual(alpha.tobytes(), expected.getchannel("A").tobytes())
                        self.assertEqual(alpha.getextrema()[0], 0)
                        self.assertGreater(alpha.getextrema()[1], 0)
                        self.assertEqual(ImageCms.getProfileName(ImageCms.ImageCmsProfile(
                            BytesIO(exported.info['icc_profile']))).strip(), 'sRGB built-in')


if __name__ == "__main__":
    unittest.main()
