# Six character portrait and sheet production — 2026-11-09

Twelve derived PNGs are ready for user review: one square portrait and one landscape four-view character sheet for Beastmaster, Jackalope, Giant Toad, Dire Bear, Raptor and Light Elemental. The user-approved full-body masters remain authoritative and unchanged. No runtime integration, GLB import, gameplay change or change to the original prompt document was performed by this batch.

## Review

- [Local review gallery](review.html): each approved master beside its portrait, 50/100px circular-token previews and sheet. Click any image for full resolution. All files are local; the gallery has no scripts or remote dependencies.
- [Contact sheet of all twelve outputs](review-contact-sheet.png).
- [Manifest](manifest.json): explicit canonical UnitTypeId, slug, approved master hash, output paths, dimensions and record paths.
- [Verification](verification.json): complete PNG decoding, aspect ratios and preservation checks.

| Character / canonical ID | Portrait | Character sheet | Unchanged approved master |
| --- | --- | --- | --- |
| Beastmaster / `beastmaster-type` | [portrait](../../units/beastmaster.png) | [sheet](../../units/beastmaster/reference/character-sheet.png) | [master](../../units/beastmaster/reference/full-body.png) |
| Jackalope / `jackalope-type` | [portrait](../../units/jackalope.png) | [sheet](../../units/jackalope/reference/character-sheet.png) | [master](../../units/jackalope/reference/full-body.png) |
| Giant Toad / `giant-toad-type` | [portrait](../../units/giant-toad.png) | [sheet](../../units/giant-toad/reference/character-sheet.png) | [master](../../units/giant-toad/reference/full-body.png) |
| Dire Bear / `dire-bear-type` | [portrait](../../units/dire-bear.png) | [sheet](../../units/dire-bear/reference/character-sheet.png) | [master](../../units/dire-bear/reference/full-body.png) |
| Raptor / `raptor-type` | [portrait](../../units/raptor.png) | [sheet](../../units/raptor/reference/character-sheet.png) | [master](../../units/raptor/reference/full-body.png) |
| Light Elemental / `light-elemental-type` | [portrait](../../units/light-elemental.png) | [sheet](../../units/light-elemental/reference/character-sheet.png) | [master](../../units/light-elemental/reference/full-body.png) |

## Conventions reused

Used the installed imagegen skill and built-in `image_gen.imagegen`, one image per call. Each approved full-body PNG was fully decoded and visually inspected before generation. The master alone was supplied as the primary identity reference for both derivatives; existing portrait/sheet examples and production records supplied the established composition and painterly style instructions. No secondary image was needed. The old portrait-first identity hierarchy was intentionally reversed for this approved full-body-first batch.

Portraits use the existing hand-painted premium fantasy tabletop style, subdued gray backgrounds, soft upper-left lighting and circular-token composition adapted for creature anatomy. All six are 1254 × 1254. Sheets show front, side, rear and front three-quarter views in that order, in neutral poses on a plain background. Sheet canvases are 1774 × 887 for Beastmaster, Jackalope, Dire Bear and Light Elemental, and 2172 × 724 for Giant Toad and Raptor.

All generated original bytes were copied to the requested source paths without cropping, resizing, retouching or recompression. Contact-sheet thumbnails and circular crops are separate QA derivatives only. No portrait was registered in `board-visuals.js` or `prepare-artwork.py`; `unit-visuals.json` and the GLB importer remain unchanged. Any inbox GLBs arriving during production were left untouched.

## Inspection notes and retries

Fourteen generation calls produced twelve selected outputs. There were no generation-tool failures or skipped characters. Two drafts were rejected and regenerated from the same approved master:

- Jackalope portrait: first draft placed antlers too close to square edges. The selected second draft gives more circular-crop clearance. Keeping complete antlers makes the face smaller at token scale; review both 50px and 100px previews.
- Giant Toad portrait: first draft repeated the full seated body. The selected second draft concentrates on the broad head, both eyes and throat.

No obvious extra limbs, equipment or major identity mismatch was accepted in the selected outputs. Supporting sheets retain small generative variations: Beastmaster clasp/strap details; Jackalope antler surfaces and a near-profile side view; Toad toes and skin bumps; Bear fur clumps and natural far-leg occlusion; Raptor fine stripes/claws and a foreshortened rear tail; Elemental crystal facets and rear surface continuation. Unseen rear details are inferred reference material, not additions to the approved design. The master resolves any conflict. These are review notes, not a claim that every surface matches exactly across views.

An independent read-only visual review inspected all six masters, all twelve full-resolution derivatives and the circular previews. It found no material identity/anatomy mismatch, missing view, cropped critical portrait feature or unexpected equipment, and requested no further retries. Optional review note: Raptor's portrait includes more torso, making its face comparatively small at 50px; its head silhouette and eye remain legible. See [independent review](independent-review.json).

User approval of the derived artwork remains pending. No Tripo reconstruction, geometry, physical support or printability validation has been performed. Token previews are local visual checks, not actual-board/browser approval.

## Provenance and verification

`records/<slug>-portrait.json` and `records/<slug>-characterSheet.json` store the exact final prompt, approved reference path and SHA-256, generator output source path, saved output SHA-256/size/dimensions/format and visual review. Rejected portrait attempts include their original prompt, source path, hash and rejection reason. Generation model/version, seed and exact canvas controls are not exposed by this built-in tool; no values were invented. The user selected the full-body masters; the derived outputs are agent-inspected and awaiting user review. No new rights or licensing clearance is implied.

`baseline.json` records SHA-256 values for 547 pre-existing source, runtime-art, preview and relevant mapping/rules/tool files, including all six approved masters. All remain unchanged. All twelve saved outputs fully decode as single-frame PNGs, with square portraits and landscape sheets.

The batch-local `manage.py` follows the existing dated reference-production helper convention: it records prompts/provenance, performs exclusive byte-preserving copies, checks preservation and builds QA previews. It never calls a generator or registers assets. The completed `init` operation must not be rerun. With an existing Python/Pillow installation:

```text
python assets/source/reference-production/2026-11-09/manage.py verify
python assets/source/reference-production/2026-11-09/manage.py previews
```

The system `py` launcher was unavailable; production used the existing Python 3.14 installation with Pillow 13.0.0. No dependency installation was needed. No gameplay/build/browser tests were run for this source-art review task.

Next: review the twelve derivatives, request any corrections, then authorize the existing artwork integration workflow to add the six portrait export/catalog registrations and prepare runtime WebPs. Character sheets remain source references; GLB review/import is a separate task.
