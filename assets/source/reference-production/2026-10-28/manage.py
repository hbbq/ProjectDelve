"""Copy reviewed generator originals without image edits; record reproducible provenance."""
import hashlib
import json
import re
import shutil
import sys
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def inspect(path):
    with Image.open(path) as image:
        image.load()
        return dict(dimensions=list(image.size), format=image.format, mode=image.mode,
                    frames=getattr(image, 'n_frames', 1),
                    alphaExtrema=list(image.getchannel('A').getextrema()) if 'A' in image.getbands() else None,
                    exifOrientation=image.getexif().get(274), embeddedColorProfile=bool(image.info.get('icc_profile')))

def write(path, data):
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')

manifest_path = HERE / 'manifest.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
if sys.argv[1] == 'save':
    request_path = Path(sys.argv[2])
    request = json.loads(request_path.read_text(encoding='utf-8'))
    unit = next(unit for unit in manifest['units'] if unit['slug'] == request['slug'])
    asset = unit[request['kind']]
    target = ROOT / asset['path']
    assert not target.exists(), f'Preserving existing output: {target}'
    source = Path(request['generatedSource'])
    metadata = inspect(source)
    assert metadata['format'] == 'PNG' and metadata['frames'] == 1
    assert min(metadata['dimensions']) >= 768 and max(metadata['dimensions']) >= 1024
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open('xb') as output, source.open('rb') as original:
        shutil.copyfileobj(original, output)
    assert digest(source) == digest(target)
    request['output'] = dict(path=asset['path'], sha256=digest(target), bytes=target.stat().st_size, **metadata)
    request['inputs'] = [dict(path=path, sha256=digest(ROOT / path)) for path in request['references']]
    request['tool'] = manifest['tool']
    request['model'] = manifest['generationModel']
    request['date'] = manifest['date']
    request['approval'] = 'agent visually inspected; not a claim of owner approval or Tripo validation'
    write(request_path, request)
    asset.update(status='generated-reviewed', sha256=digest(target), dimensions=metadata['dimensions'], provenance=request_path.relative_to(ROOT).as_posix())
    unit['visualIssues'].extend(request.get('issues', []))
    write(manifest_path, manifest)
    print(f"Saved {asset['path']} {metadata['dimensions']}")
elif sys.argv[1] == 'verify':
    baseline = json.loads((HERE / 'baseline.json').read_text(encoding='utf-8-sig'))
    changed = [entry['path'] for entry in baseline if not (ROOT / entry['path']).is_file() or digest(ROOT / entry['path']) != entry['sha256']]
    assert not changed, f'Protected files changed: {changed}'
    catalog = (ROOT / manifest['canonicalCatalog']).read_text(encoding='utf-8-sig')
    constants = dict(re.findall(r'public const string (\w+) = "([a-z-]+-type)";', catalog))
    factory_names = re.findall(r'\[UnitTypeIds\.(\w+)\] =', catalog)
    canonical_ids = {constants[name] for name in factory_names}
    assert canonical_ids == {unit['unitTypeId'] for unit in manifest['units']}
    baseline_images = []
    for entry in baseline:
        if Path(entry['path']).suffix.lower() in ('.png', '.webp', '.jpg', '.jpeg'):
            baseline_images.append(dict(path=entry['path'], **inspect(ROOT / entry['path'])))
    outputs = []
    for unit in manifest['units']:
        for kind in ('characterSheet', 'fullBody'):
            asset = unit[kind]
            path = ROOT / asset['path']
            if path.exists():
                if asset['status'] == 'generated-reviewed':
                    record = json.loads((ROOT / asset['provenance']).read_text(encoding='utf-8'))
                    assert record['output']['sha256'] == digest(path) == asset['sha256']
                    assert record['visualReview'] and record['prompt']
                    assert unit['portraitSource'] in record['references']
                    if kind == 'fullBody':
                        assert unit['characterSheet']['path'] in record['references']
                    for reference in record['inputs']:
                        assert digest(ROOT / reference['path']) == reference['sha256']
                    assert inspect(path)['dimensions'] == asset['dimensions']
                outputs.append(dict(unitTypeId=unit['unitTypeId'], kind=kind, path=asset['path'], **inspect(path)))
    report = dict(canonicalTypes=len(canonical_ids), protectedFiles=len(baseline), protectedFilesUnchanged=True,
                  existingImagesDecoded=len(baseline_images), outputImagesDecoded=len(outputs), outputs=outputs,
                  missing=[unit['unitTypeId'] + ':' + kind for unit in manifest['units'] for kind in ('characterSheet', 'fullBody') if not (ROOT / unit[kind]['path']).is_file()])
    write(HERE / 'verification.json', report)
    print(json.dumps({key:value for key,value in report.items() if key != 'outputs'}, indent=2))
elif sys.argv[1] == 'overview':
    # QA contact sheet only; source and delivered reference images are read-only.
    from PIL import ImageDraw, ImageOps
    samples = ['barbarian', 'diamond-kart', 'ghost', 'hobby-knight', 'locomotive', 'red-dragon', 'royal-knight', 'worker']
    canvas = Image.new('RGB', (1600, 1000), '#373737')
    draw = ImageDraw.Draw(canvas)
    for index, slug in enumerate(samples):
        x, y = (index % 4) * 400, (index // 4) * 500
        unit = next(unit for unit in manifest['units'] if unit['slug'] == slug)
        with Image.open(ROOT / unit['fullBody']['path']) as original:
            preview = ImageOps.contain(original.convert('RGB'), (384, 460), Image.Resampling.LANCZOS)
            canvas.paste(preview, (x + (400-preview.width)//2, y + 28 + (460-preview.height)//2))
        draw.text((x+12, y+8), slug + ' | saved full-body', fill='white')
    target = HERE / 'review-contact-sheet.png'
    assert not target.exists()
    canvas.save(target)
    print(target)
elif sys.argv[1] == 'finalize':
    verification = json.loads((HERE / 'verification.json').read_text(encoding='utf-8'))
    assert not verification['missing'] and verification['protectedFilesUnchanged']
    assert len(manifest['units']) == verification['canonicalTypes'] == 32
    generated_units = [unit for unit in manifest['units'] if unit['characterSheet']['status'] == 'generated-reviewed']
    assert len(generated_units) == 30
    for unit in manifest['units']:
        if unit['slug'] in ('barbarian', 'bomb-imp', 'goblin-peasant', 'goblin-king'):
            note = 'Full-body camera is a shallow front three-quarter angle; side/rear geometry is clearer in the character sheet.'
            if note not in unit['visualIssues']:
                unit['visualIssues'].append(note)
        if unit['slug'] == 'zombie':
            unit['visualIssues'] = ['Existing sheet includes miniature bases and labels; explicitly preserved per task. Existing full-body is base-free.']
        if unit['slug'] == 'troll':
            unit['visualIssues'] = ['Existing sheet includes a labeled scale-comparison character; explicitly preserved per task. Existing full-body contains one base-free Troll.']
    manifest['status'] = 'complete-with-documented-visual-notes'
    manifest['summary'] = dict(canonicalTypes=32, alreadyComplete=2, processedTypes=30,
                               characterSheetsGenerated=30, fullBodyReferencesGenerated=30,
                               failedTypes=[], skippedMissingTypes=[], preservedTypes=['troll-type', 'zombie-type'],
                               generatorCalls=61, rejectedDrafts=1, targetedCorrections=1,
                               protectedFilesUnchanged=177, referenceOutputs=64)
    manifest['toolLimitations'] = ['Built-in tool does not expose generation model/version, seed or exact canvas-size controls in this session.',
                                   'Generated PNG originals have varying canvas dimensions and are preserved without resampling.',
                                   'Not validated by Tripo; unseen body surfaces are conservative visual extrapolations, not new game canon.',
                                   'Some camera angles are shallow; Ghost translucence and fine ornaments may need manual reconstruction interpretation.']
    manifest['verification'] = 'verification.json'
    manifest['representativeReview'] = dict(path='review-contact-sheet.png',
        units=['barbarian-type', 'diamond-kart-type', 'ghost-type', 'hobby-knight-type', 'locomotive-type', 'red-dragon-type', 'royal-knight-type', 'worker-type'],
        finding='Saved copies reviewed together; identity, complete silhouettes and source style coherent. Locomotive wheel correction verified. Ghost and shallow camera notes retained.',
        additionalOutlier='goblin-peasant/reference/full-body.png inspected at full size; shallow angle documented.')
    write(manifest_path, manifest)
    generated = [entry for entry in verification['outputs'] if entry['unitTypeId'] not in ('troll-type', 'zombie-type')]
    dimensions = sorted({tuple(entry['dimensions']) for entry in generated})
    lines = ['# Unit reference artwork production - 2026-10-28', '',
             'Completed 30 missing Unit sets: 30 character sheets and 30 full-body references. All 32 canonical Unit Types now have both files. Zombie and Troll were already complete and remain byte-for-byte unchanged.', '',
             'Outputs: `assets/source/units/<slug>/reference/character-sheet.png` and `full-body.png`. These are source references for manual Tripo generation, Standees and illustration; no runtime registration or GLB generation was performed.', '',
             'The machine-readable [manifest](manifest.json) gives every canonical ID, portrait, optional concept, output path, status and visual issue. [Verification](verification.json) records decoding/dimensions and preservation checks. `records/` contains the exact prompt set, input and output SHA-256 values, generator source path and visual inspection for each deliverable. No owner approval or new license clearance is implied.', '',
             'Used the installed ProjectDelve artwork and imagegen skills with built-in `image_gen.imagegen`, one image per call, catalog IDs sorted ascending. Each portrait was inspected, then its sheet generated and inspected, then the full-body generated using both portrait and sheet. All 60 new images were inspected as generated. One rejected Locomotive draft gained an extra wheel; one targeted edit corrected it before final save. No successful outputs were overwritten. No generation failures or missing Types were skipped.', '',
             'All 177 pre-existing source, runtime-art and preview files retain their initial SHA-256 values; 162 pre-existing raster images and all 64 reference outputs decoded fully. Canonical IDs were checked against the actual factory registry. Every new full-body provenance record contains its portrait and sheet as inputs. No gameplay/build/browser tests were run, per task scope.', '',
             'Generated canvas dimensions: ' + ', '.join(f'{w} x {h}' for w,h in dimensions) + ' pixels. Original PNG bytes were copied without image edits/resizing. All new backgrounds are neutral gray; no alpha conversion was needed.', '',
             '[Representative saved-output review](review-contact-sheet.png) covers humanoid, vehicle, spectral, toy-horse, locomotive, dragon, mounted and laborer subjects. This contact sheet is QA only, not a character deliverable.', '',
             'Visual notes: Ghost retains spectral translucence and fine torn cloth, which may require interpretation in Tripo. Barbarian, Bomb Imp, Goblin Peasant and Goblin King use shallow front three-quarter angles; their sheets provide the clearer side/rear views. Fine embroidery, feathers, jewelry and skeletal fingers may be interpreted differently by image-to-3D. Existing Zombie/Troll sheet bases, labels and scale comparisons were preserved as requested. No Tripo models or reconstruction checks were produced.', '',
             'Tool limitations: the built-in generator does not expose model/version, seed or exact size controls here. Canvas sizes varied. Unseen anatomy, clothing backs and mechanical backs were conservatively extrapolated; the references do not establish gameplay rules or make optional concepts canon.', '',
             '| Unit Type ID | Portrait | Character sheet | Full body |',
             '| --- | --- | --- | --- |']
    for unit in manifest['units']:
        lines.append(f"| {unit['unitTypeId']} | `{unit['portraitSource']}` | {unit['characterSheet']['status']} | {unit['fullBody']['status']} |")
    write_target = HERE / 'README.md'
    write_target.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print(json.dumps(manifest['summary'], indent=2))
