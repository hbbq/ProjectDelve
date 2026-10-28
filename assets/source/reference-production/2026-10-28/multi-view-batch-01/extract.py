"""Reviewed Batch 1 crops. Run from any directory; originals are read-only."""
import hashlib
import json
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[4]
CANVAS = (1024, 1024)
PADDING_FADE = 64
ORIENTATIONS = ('front', 'side', 'back', 'three-quarter')
# Coordinates are source pixels. Crop right/bottom are exclusive.
# The shared vertical extent retains native scale and the sheet's ground alignment.
PLANS = {
    'goblin': [0, 420, 735, 1135, 1536],
    'barbarian': [0, 430, 735, 1135, 1536],
    'cleric': [0, 420, 745, 1140, 1536],
    'hobby-knight': [0, 410, 780, 1135, 1536],
    'worker': [0, 425, 740, 1150, 1536],
}
# Front hand and side sack overlap in X but not in image space. Follow their
# neutral background gap; no subject pixels are reconstructed or retouched.
WORKER_SEAM = [(0, 400), (220, 400), (320, 385), (430, 390),
               (510, 415), (620, 425), (1023, 425)]  # (y, x)
NOTES = {
    'goblin': ['Front is near-frontal; three-quarter is relatively shallow.',
               'Small patch, strap and finger variations in the sheet are preserved.'],
    'barbarian': ['Three-quarter view is shallow.',
                  'Axe angle and minor cloth/fur details vary between source views; preserved without correction.'],
    'cleric': ['Staff height/position and fine robe embroidery vary slightly between source views; preserved.',
               'Rear braid and partly occluded staff are the authored rear view, not missing extractions.'],
    'hobby-knight': ['Front is near-frontal; three-quarter rotation is shallow.',
                     'Toy horse and stick are naturally obscured from behind; no replacement geometry added.',
                     'Stick angle, cape folds and small armor details vary in source; preserved.'],
    'worker': ['Front and three-quarter are both shallow front angles, not exact orthographic views.',
               'Sack, rock arrangement and tool details vary slightly between source views; preserved.',
               'First/second figures require a shaped boundary to exclude neighbors without clipping the hand or sack.'],
}

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def worker_edge(y):
    for (y0, x0), (y1, x1) in zip(WORKER_SEAM, WORKER_SEAM[1:]):
        if y0 <= y <= y1:
            return round(x0 + (x1 - x0) * (y - y0) / (y1 - y0))
    raise ValueError(y)

def main():
    manifest_path = HERE / 'manifest.json'
    targets = [ROOT / f'assets/source/units/{slug}/reference/views/view-{i+1:02}-{view}.png'
               for slug in PLANS for i, view in enumerate(ORIENTATIONS)]
    assert not manifest_path.exists(), 'Preserving existing manifest'
    assert not any(path.exists() for path in targets), 'Preserving existing view output'
    protected = {path: sha(path) for folder in ('assets/source', 'assets/previews', 'ProjectDelve.Web/wwwroot/art')
                 for path in (ROOT / folder).rglob('*') if path.is_file()}
    manifest = dict(schemaVersion=1, batch='Tripo multi-view Batch 1',
                    operation='Conventional crop and neutral-background padding only',
                    tool='Pillow', canvas=list(CANVAS), scale=1, mirrored=False,
                    coordinateConvention='source pixels; left/top inclusive, right/bottom exclusive',
                    paddingMethod='Feather boundary background RGB over 64 padding pixels to the neutral source left-edge row color; retained pixels stay unchanged',
                    paddingFadePixels=PADDING_FADE,
                    status='extracted-awaiting-visual-review', units=[])
    for slug, boundaries in PLANS.items():
        source = ROOT / f'assets/source/units/{slug}/reference/character-sheet.png'
        full = source.with_name('full-body.png')
        portrait = ROOT / f'assets/source/units/{slug}.png'
        unit = dict(slug=slug, unitTypeId=slug+'-type', source=source.relative_to(ROOT).as_posix(),
                    sourceSha256=sha(source), preservedFullBody=dict(path=full.relative_to(ROOT).as_posix(), sha256=sha(full)),
                    preservedPortrait=dict(path=portrait.relative_to(ROOT).as_posix(), sha256=sha(portrait)),
                    limitations=NOTES[slug], missingOrientations=[], views=[])
        with Image.open(source) as original:
            original.load()
            assert original.size == (1536, 1024) and original.mode == 'RGB'
            assert original.getexif().get(274) in (None, 1)
            unit['sourceDimensions'] = list(original.size)
            for i, orientation in enumerate(ORIENTATIONS):
                left, right = boundaries[i:i+2]
                if slug == 'worker' and i == 1:
                    left = min(x for y, x in WORKER_SEAM)
                width = right-left
                offset = (CANVAS[0]-width)//2
                result = Image.new('RGB', CANVAS)
                source_rows = []
                for y in range(CANVAS[1]):
                    row_left, row_right = left, right
                    if slug == 'worker' and i == 0:
                        row_right = worker_edge(y)
                    elif slug == 'worker' and i == 1:
                        row_left = worker_edge(y)
                    start, end = offset+row_left-left, offset+row_right-left
                    # Fade only added background padding, preventing floor shadows
                    # from stretching into long bars. All retained pixels stay 1:1.
                    neutral = original.getpixel((0, y))
                    result.paste(neutral, (0, y, CANVAS[0], y+1))
                    for edge_x, direction, edge_color in (
                        (start-1, -1, original.getpixel((row_left, y))),
                        (end, 1, original.getpixel((row_right-1, y))),
                    ):
                        for distance in range(PADDING_FADE):
                            x = edge_x + direction * distance
                            if 0 <= x < CANVAS[0]:
                                weight = (PADDING_FADE-distance)/PADDING_FADE
                                color = tuple(round(a*weight+b*(1-weight)) for a,b in zip(edge_color,neutral))
                                result.putpixel((x,y),color)
                    result.paste(original.crop((row_left, y, row_right, y+1)), (start, y))
                    source_rows.append((row_left, row_right, start, end))
                target = ROOT / f'assets/source/units/{slug}/reference/views/view-{i+1:02}-{orientation}.png'
                target.parent.mkdir(parents=True, exist_ok=True)
                with target.open('xb') as output:
                    result.save(output, format='PNG', **({'icc_profile': original.info['icc_profile']} if original.info.get('icc_profile') else {}))
                with Image.open(target) as decoded:
                    decoded.load()
                    assert decoded.size == CANVAS and decoded.mode == 'RGB'
                    assert decoded.tobytes() == result.tobytes()
                    # Every retained pixel, including silhouette details, matches its source.
                    for y, (row_left, row_right, start, end) in enumerate(source_rows):
                        assert decoded.crop((start,y,end,y+1)).tobytes() == original.crop((row_left,y,row_right,y+1)).tobytes()
                view = dict(orientation=orientation, sideDirection='faces image-right' if orientation=='side' and slug!='cleric' else 'faces image-left' if orientation=='side' else None,
                            sourcePanel=i+1, crop=[left,0,right,1024], pasteOffset=[offset,0],
                            padding=dict(left=offset,right=CANVAS[0]-width-offset,top=0,bottom=0),
                            output=target.relative_to(ROOT).as_posix(), sha256=sha(target), dimensions=list(CANVAS),
                            nativeSourcePixelsPreserved=True, visualReview='pending')
                if slug=='worker' and i in (0,1):
                    view['shapedBoundary'] = dict(pointsYx=WORKER_SEAM, interpolation='piecewise linear; round to nearest integer using Python round',
                        retain='left of boundary' if i==0 else 'right of boundary', extraPadding='Row-dependent outside shaped boundary')
                unit['views'].append(view)
        manifest['units'].append(unit)
    assert all(path.exists() and sha(path)==digest for path,digest in protected.items()), 'An existing file changed'
    manifest['preservation'] = dict(preExistingFilesVerified=len(protected), allUnchanged=True,
                                    characterSheetsAndFullBodiesUnchanged=True)
    manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(units=len(PLANS),outputs=len(targets),canvas=CANVAS,protectedUnchanged=len(protected))))

if __name__ == '__main__':
    main()
