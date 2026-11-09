"""Batch-local review records and byte-preserving saves; never generates images."""
import hashlib
import json
import shutil
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageOps

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
SLUGS = ('beastmaster', 'jackalope', 'giant-toad', 'dire-bear', 'raptor', 'light-elemental')
NAMES = ('Beastmaster', 'Jackalope', 'Giant Toad', 'Dire Bear', 'Raptor', 'Light Elemental')
DETAILS = {
    'beastmaster': 'The SAME human woman: light olive skin, oval face, dark eyebrows and hazel-brown eyes, dark brown hair swept back into the visible long braid; plum tunic, charcoal-green hooded short mantle with geometric gold edging, brass circular geometric clasp, brown leather straps, bracers, pouches, vial holder and boots. Preserve the exact dark carved wooden staff and inset brass circular star motif; no new focus or ornaments.',
    'jackalope': 'The SAME tawny rabbit-like Jackalope: amber-brown eyes with pale eye surrounds, cream muzzle/chest, gray-brown and tawny fur, two long pink-lined ears, two substantial branching ivory-brown antlers, strong folded hind legs, forepaws and short cream-tipped tail. Match the master antler beam and branch arrangement; do not add horns, branches or accessories.',
    'giant-toad': 'The SAME broad squat toad: very wide closed mouth, raised widely spaced gold-amber eyes with horizontal black pupils, olive/ochre/umber mottled bumpy skin and pale cream speckled throat/belly. Two forelegs and two powerful folded hind legs, four substantial amphibian feet. Preserve its head width, eye placement, skin bumps and visible toe construction; no tongue, tail, fangs or equipment.',
    'dire-bear': 'The SAME massive brown bear: broad forehead and muzzle, small rounded ears, amber-brown eyes, dark nose, pale tan muzzle/bridge, dark umber fur with the distinctive warmer russet shoulder hump and dark chest/legs. Four large planted paws and short bear tail, no clothing, armor, ornaments or spikes. Match facial proportions, mass and broad fur clumps.',
    'raptor': 'The SAME featherless predatory dinosaur: long narrow rounded snout and closed mouth, golden eyes, slate/moss-green scaled hide, ochre irregular stripes from head through torso and tail, pale cream-gray throat and belly, two compact three-fingered forelimbs, two planted clawed hind feet with raised sickle claws, one long gently curved balancing tail. Preserve the exact head anatomy, limb construction and distinctive markings; no feathers, horns, new spikes or equipment.',
    'light-elemental': 'The SAME small benevolent humanoid-like solid elemental: ivory face, almond golden eyes, subtle smile, amber diamond forehead inset, a connected crown of thick amber/ivory flame-shaped crystals, broad rounded faceted shoulders, two substantial arms and hands held close across the chest, continuous twisting ivory/gold/amber lower taper with broad flattened ground contact. Preserve the opaque painted luminous-looking material, exact silhouette and integrated lower form; no legs, new ornaments, detached crystals, halos or light effects.',
}
STYLE = 'Premium fantasy tabletop board game artwork. Hand-painted digital illustration with rich but controlled colors, detailed textures, subtle brushwork, and a cohesive classic fantasy aesthetic. Clean, readable premium board-game presentation. Soft directional lighting from the upper left and a simple subdued neutral gray background. Preserve the approved reference material colors and painting; no style redesign.'


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inspect(path):
    with Image.open(path) as im:
        im.load()
        return dict(dimensions=list(im.size), format=im.format, mode=im.mode,
                    frames=getattr(im, 'n_frames', 1), exifOrientation=im.getexif().get(274),
                    embeddedColorProfile=bool(im.info.get('icc_profile')),
                    alphaExtrema=list(im.getchannel('A').getextrema()) if 'A' in im.getbands() else None)


def record_path(slug, kind):
    return HERE / 'records' / f'{slug}-{kind}.json'


def init():
    assert not (HERE / 'manifest.json').exists(), 'Preserve existing batch'
    units = []
    for slug, name in zip(SLUGS, NAMES):
        master = f'assets/source/units/{slug}/reference/full-body.png'
        info = inspect(ROOT / master)
        assert info['format'] == 'PNG' and info['frames'] == 1
        unit = dict(unitTypeId=slug + '-type', slug=slug, name=name,
                    approvedFullBody=dict(path=master, sha256=digest(ROOT / master), **info))
        for kind, output in [('portrait', f'assets/source/units/{slug}.png'),
                             ('characterSheet', f'assets/source/units/{slug}/reference/character-sheet.png')]:
            assert not (ROOT / output).exists(), f'Preserve existing output: {output}'
            if kind == 'portrait':
                task = ('Create ONE new square 1:1 portrait of exactly this character. Center the recognizable head and upper body; adapt the crop naturally for this creature. Fit all identity-critical features inside the central 75 percent, including complete ears, antlers or crystal crown where present, with generous circular-token crop margins. The face and silhouette must read clearly at small token size. For Beastmaster retain visible collar, mantle, clasp and a hint of the existing staff without covering the face. For the Jackalope frame the full antlers and ears; for the toad retain broad head width and both eyes. This is a portrait, not a full-body view or sheet. Do not draw the circular crop or frame.')
            else:
                task = ('Create ONE new landscape character reference sheet with exactly FOUR clearly separated complete full-body views in one horizontal row: front, side, rear, and front three-quarter, in that order. Same character in every view, neutral resting pose, identical anatomy/proportions/colors/materials/equipment, consistent scale and ground line, generous space between figures and margins around all extremities. Use near-orthographic views with minimal perspective distortion. Preserve the master pose as far as each orientation permits. Infer unseen rear construction only in the simplest consistent way, continuing existing forms and colors; no elaborate new features. Include complete antlers, ears, hands, feet, tails, staff and integrated ground-contact form as applicable. The sheet is supporting reference and must not override the approved full body. No portrait panels or decorative layout.')
            prompt = ('Use case: stylized-concept. Asset type: Project Delve ' + ('token portrait' if kind == 'portrait' else 'four-view character reference sheet') + '.\n'
                      'Input Image 1: approved full-body master, PRIMARY and authoritative identity/design reference, not an edit target. Generate a separate derived image; never change or replace the master.\n'
                      + STYLE + '\n' + task + '\nSubject: ' + DETAILS[slug] + '\n'
                      'Preserve identity, face, anatomy, proportions, palette, clothing, materials, accessories and equipment exactly as visible in Image 1. Do not substitute the earlier text concept for the approved image. No added equipment, limbs, horns, ornaments or magical effects. No text, labels, measurements, letters, borders, token frames, faction rings, health markers, UI, logos or watermarks. No scenery, pedestal, miniature base, additional character designs, motion blur, detached effects, bloom or transparency-dependent anatomy.')
            record = dict(slug=slug, kind=kind, unitTypeId=slug + '-type', prompt=prompt,
                          references=[master], inputs=[dict(path=master, sha256=digest(ROOT / master))],
                          outputPath=output, status='pending', tool='built-in image_gen.imagegen',
                          model='not exposed by tool', seed='not exposed by tool', date='2026-11-09',
                          approval='Approved full body supplied by user; derived output awaits user review')
            write(record_path(slug, kind), record)
            unit[kind] = dict(path=output, record=record_path(slug, kind).relative_to(ROOT).as_posix())
        units.append(unit)
    protected = []
    for folder in ('assets/source', 'assets/previews', 'ProjectDelve.Web/wwwroot/art'):
        for path in (ROOT / folder).rglob('*'):
            if path.is_file() and not path.is_relative_to(HERE):
                protected.append(dict(path=path.relative_to(ROOT).as_posix(), sha256=digest(path)))
    for name in ('ASSET_PROMPTS.md', 'CONSTRAINTS.md', 'ProjectDelve.Engine/UnitContent.cs',
                 'ProjectDelve.Engine/UnitRoster.cs', 'ProjectDelve.Web/wwwroot/board-visuals.js',
                 'tools/prepare-artwork.py', 'tools/import-unit-models.cjs'):
        protected.append(dict(path=name, sha256=digest(ROOT / name)))
    write(HERE / 'baseline.json', protected)
    write(HERE / 'manifest.json', dict(schemaVersion=1, date='2026-11-09',
          purpose='Six full-body-first derived portrait/sheet sets; source artwork for review only',
          tool='built-in image_gen.imagegen', runtimeIntegration=False, units=units))
    print(json.dumps(dict(validMasters=len(units), protectedFiles=len(protected), pendingOutputs=12)))


def save(slug, kind, source):
    rp = record_path(slug, kind)
    record = json.loads(rp.read_text(encoding='utf-8'))
    target = ROOT / record['outputPath']
    assert not target.exists(), f'Preserve existing output: {target}'
    source = Path(source)
    info = inspect(source)
    assert info['format'] == 'PNG' and info['frames'] == 1
    width, height = info['dimensions']
    assert width == height if kind == 'portrait' else width > height
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open('xb') as out, source.open('rb') as original:
        shutil.copyfileobj(original, out)
    assert digest(target) == digest(source)
    record.update(generatedSource=str(source), status='generated-awaiting-agent-review',
                  output=dict(path=record['outputPath'], sha256=digest(target), bytes=target.stat().st_size, **info))
    write(rp, record)
    print(json.dumps(record['output']))


def review(slug, kind, note):
    rp = record_path(slug, kind)
    record = json.loads(rp.read_text(encoding='utf-8'))
    assert record.get('output') and note
    record.update(status='agent-visually-reviewed-awaiting-user-review', visualReview=note,
                  issues=[], approval='Agent visually inspected; user review of derived output pending; no Tripo or print validation')
    write(rp, record)


def retry(slug, kind, source, instruction, reason):
    rp = record_path(slug, kind)
    record = json.loads(rp.read_text(encoding='utf-8'))
    attempts = record.setdefault('rejectedAttempts', [])
    attempts.append(dict(prompt=record['prompt'], references=record['references'],
                         generatedSource=source, sha256=digest(Path(source)),
                         image=inspect(Path(source)), reason=reason))
    record['prompt'] += '\n' + instruction
    write(rp, record)


def verify():
    baseline = json.loads((HERE / 'baseline.json').read_text(encoding='utf-8'))
    changed = [entry['path'] for entry in baseline if not (ROOT / entry['path']).is_file() or digest(ROOT / entry['path']) != entry['sha256']]
    assert not changed, f'Protected originals or files changed: {changed}'
    outputs = []
    manifest = json.loads((HERE / 'manifest.json').read_text(encoding='utf-8'))
    for slug in SLUGS:
        for kind in ('portrait', 'characterSheet'):
            record = json.loads(record_path(slug, kind).read_text(encoding='utf-8'))
            assert record['status'] == 'agent-visually-reviewed-awaiting-user-review'
            output = ROOT / record['outputPath']
            assert digest(output) == record['output']['sha256']
            for ref in record['inputs']:
                assert digest(ROOT / ref['path']) == ref['sha256']
            outputs.append(dict(slug=slug, kind=kind, path=record['outputPath'], **inspect(output)))
            unit = next(u for u in manifest['units'] if u['slug'] == slug)
            unit[kind].update(status=record['status'], sha256=digest(output),
                              dimensions=record['output']['dimensions'])
    manifest.update(status='complete-awaiting-user-review', generatedOutputs=len(outputs),
                    approvedMastersPreserved=True)
    write(HERE / 'manifest.json', manifest)
    write(HERE / 'verification.json', dict(protectedFiles=len(baseline), protectedFilesUnchanged=True,
          approvedMastersUnchanged=True, generatedOutputs=len(outputs), outputs=outputs,
          runtimeIntegration=False, userApproval='pending'))
    print(json.dumps(dict(protectedFilesUnchanged=len(baseline), validOutputs=len(outputs))))


def previews():
    # QA derivatives only; no delivered artwork or approved reference is edited.
    canvas = Image.new('RGB', (1800, 2040), '#25272a')
    draw = ImageDraw.Draw(canvas)
    draw.text((20, 12), 'Project Delve | derived artwork review | portrait, 50/100px circular checks, four-view sheet', fill='white')
    cards = []
    for i, (slug, name) in enumerate(zip(SLUGS, NAMES)):
        y = 44 + i * 330
        draw.text((20, y), name, fill='white')
        portrait = ROOT / f'assets/source/units/{slug}.png'
        sheet = ROOT / f'assets/source/units/{slug}/reference/character-sheet.png'
        master = ROOT / f'assets/source/units/{slug}/reference/full-body.png'
        with Image.open(portrait) as im:
            square = ImageOps.contain(im.convert('RGB'), (285, 285), Image.Resampling.LANCZOS)
            canvas.paste(square, (20, y + 22))
            for size, x in ((50, 330), (100, 405)):
                thumb = im.convert('RGB').resize((size, size), Image.Resampling.LANCZOS)
                mask = Image.new('L', (size * 4, size * 4))
                ImageDraw.Draw(mask).ellipse((0, 0, size * 4 - 1, size * 4 - 1), fill=255)
                mask = mask.resize((size, size), Image.Resampling.LANCZOS)
                canvas.paste(thumb, (x, y + 110), mask)
        with Image.open(sheet) as im:
            thumb = ImageOps.contain(im.convert('RGB'), (1240, 285), Image.Resampling.LANCZOS)
            canvas.paste(thumb, (540, y + 22 + (285 - thumb.height) // 2))
        rel = lambda p: p.relative_to(ROOT).as_posix()
        prefix = '../../../../'
        cards.append(f'<section><h2>{name}</h2><div class="row"><figure><a href="{prefix}{rel(master)}"><img class="master" src="{prefix}{rel(master)}" alt="Approved {name} full body"></a><figcaption>Approved master (unchanged)</figcaption></figure><figure><a href="{prefix}{rel(portrait)}"><img class="portrait" src="{prefix}{rel(portrait)}" alt="{name} portrait"></a><figcaption>Portrait</figcaption><div class="tokens"><img width="50" height="50" src="{prefix}{rel(portrait)}" alt="50 pixel token"><img width="100" height="100" src="{prefix}{rel(portrait)}" alt="100 pixel token"></div></figure><figure class="sheet"><a href="{prefix}{rel(sheet)}"><img src="{prefix}{rel(sheet)}" alt="{name} four-view sheet"></a><figcaption>Front / side / rear / three-quarter</figcaption></figure></div></section>')
    canvas.save(HERE / 'review-contact-sheet.png')
    html = '<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Project Delve - Six character artwork review</title><style>body{margin:24px;background:#25272a;color:#eee;font:16px system-ui}h1{font-size:26px}h2{font-size:20px}section{border-top:1px solid #666;padding:16px 0}.row{display:flex;gap:20px;align-items:center;flex-wrap:wrap}figure{margin:0}img{max-width:100%;height:auto}.master{width:180px;max-height:300px;object-fit:contain}.portrait{width:250px}.sheet{flex:1;min-width:400px}.sheet img{width:100%;max-height:420px;object-fit:contain}.tokens{display:flex;align-items:center;gap:16px;margin-top:12px}.tokens img{border-radius:50%}figcaption{margin-top:8px;color:#ccc}a{color:#e2c17c}@media(max-width:650px){.sheet{min-width:0;width:100%;flex-basis:100%}}</style><h1>Six character artwork review</h1><p>Approved full-body masters remain authoritative. Click an image to inspect its full resolution. Derived portraits and sheets await user review; no runtime integration has occurred.</p>'
    (HERE / 'review.html').write_text(html + ''.join(cards) + '</html>\n', encoding='utf-8')
    print('Saved review.html and review-contact-sheet.png')


if __name__ == '__main__':
    action = sys.argv[1]
    if action == 'init':
        init()
    elif action == 'save':
        save(*sys.argv[2:5])
    elif action == 'review':
        review(*sys.argv[2:5])
    elif action == 'retry':
        retry(*sys.argv[2:7])
    elif action == 'verify':
        verify()
    elif action == 'previews':
        previews()
