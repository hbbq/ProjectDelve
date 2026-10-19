"""Verify the registered transparent exports against their immutable originals.

Run: python -m unittest discover -s tools -p test_prepare_artwork.py
Requires the same Pillow/WebP environment as prepare-artwork.py.
"""
import importlib.util
from io import BytesIO
from pathlib import Path
import unittest

from PIL import Image, ImageCms, ImageOps

spec = importlib.util.spec_from_file_location("prepare_artwork", Path(__file__).with_name("prepare-artwork.py"))
pipeline = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pipeline)


class CompositeArtworkTests(unittest.TestCase):
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
