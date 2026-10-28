# Unit reference artwork production - 2026-10-28

Completed 30 missing Unit sets: 30 character sheets and 30 full-body references. All 32 canonical Unit Types now have both files. Zombie and Troll were already complete and remain byte-for-byte unchanged.

Outputs: `assets/source/units/<slug>/reference/character-sheet.png` and `full-body.png`. These are source references for manual Tripo generation, Standees and illustration; no runtime registration or GLB generation was performed.

The machine-readable [manifest](manifest.json) gives every canonical ID, portrait, optional concept, output path, status and visual issue. [Verification](verification.json) records decoding/dimensions and preservation checks. `records/` contains the exact prompt set, input and output SHA-256 values, generator source path and visual inspection for each deliverable. No owner approval or new license clearance is implied.

Used the installed ProjectDelve artwork and imagegen skills with built-in `image_gen.imagegen`, one image per call, catalog IDs sorted ascending. Each portrait was inspected, then its sheet generated and inspected, then the full-body generated using both portrait and sheet. All 60 new images were inspected as generated. One rejected Locomotive draft gained an extra wheel; one targeted edit corrected it before final save. No successful outputs were overwritten. No generation failures or missing Types were skipped.

All 177 pre-existing source, runtime-art and preview files retain their initial SHA-256 values; 162 pre-existing raster images and all 64 reference outputs decoded fully. Canonical IDs were checked against the actual factory registry. Every new full-body provenance record contains its portrait and sheet as inputs. No gameplay/build/browser tests were run, per task scope.

Generated canvas dimensions: 1024 x 1536, 1254 x 1254, 1536 x 1024, 1672 x 941, 1774 x 887, 1983 x 793 pixels. Original PNG bytes were copied without image edits/resizing. All new backgrounds are neutral gray; no alpha conversion was needed.

[Representative saved-output review](review-contact-sheet.png) covers humanoid, vehicle, spectral, toy-horse, locomotive, dragon, mounted and laborer subjects. This contact sheet is QA only, not a character deliverable.

Visual notes: Ghost retains spectral translucence and fine torn cloth, which may require interpretation in Tripo. Barbarian, Bomb Imp, Goblin Peasant and Goblin King use shallow front three-quarter angles; their sheets provide the clearer side/rear views. Fine embroidery, feathers, jewelry and skeletal fingers may be interpreted differently by image-to-3D. Existing Zombie/Troll sheet bases, labels and scale comparisons were preserved as requested. No Tripo models or reconstruction checks were produced.

Tool limitations: the built-in generator does not expose model/version, seed or exact size controls here. Canvas sizes varied. Unseen anatomy, clothing backs and mechanical backs were conservatively extrapolated; the references do not establish gameplay rules or make optional concepts canon.

| Unit Type ID | Portrait | Character sheet | Full body |
| --- | --- | --- | --- |
| barbarian-type | `assets/source/units/barbarian.png` | generated-reviewed | generated-reviewed |
| bomb-imp-type | `assets/source/units/bomb-imp.png` | generated-reviewed | generated-reviewed |
| castle-guard-type | `assets/source/units/castle-guard.png` | generated-reviewed | generated-reviewed |
| chief-mechanic-type | `assets/source/units/chief-mechanic.png` | generated-reviewed | generated-reviewed |
| cleric-type | `assets/source/units/cleric.png` | generated-reviewed | generated-reviewed |
| diamond-kart-type | `assets/source/units/diamond-kart.png` | generated-reviewed | generated-reviewed |
| displacer-demon-type | `assets/source/units/displacer-demon.png` | generated-reviewed | generated-reviewed |
| geomancer-type | `assets/source/units/geomancer.png` | generated-reviewed | generated-reviewed |
| ghost-type | `assets/source/units/ghost.png` | generated-reviewed | generated-reviewed |
| goblin-bishop-type | `assets/source/units/goblin-bishop.png` | generated-reviewed | generated-reviewed |
| goblin-king-type | `assets/source/units/goblin-king.png` | generated-reviewed | generated-reviewed |
| goblin-peasant-type | `assets/source/units/goblin-peasant.png` | generated-reviewed | generated-reviewed |
| goblin-type | `assets/source/units/goblin.png` | generated-reviewed | generated-reviewed |
| grunt-type | `assets/source/units/grunt.png` | generated-reviewed | generated-reviewed |
| hobby-knight-type | `assets/source/units/hobby-knight.png` | generated-reviewed | generated-reviewed |
| labyrinth-shaper-type | `assets/source/units/labyrinth-shaper.png` | generated-reviewed | generated-reviewed |
| locomotive-type | `assets/source/units/locomotive.png` | generated-reviewed | generated-reviewed |
| red-dragon-type | `assets/source/units/red-dragon.png` | generated-reviewed | generated-reviewed |
| rogue-type | `assets/source/units/rogue.png` | generated-reviewed | generated-reviewed |
| royal-bishop-type | `assets/source/units/royal-bishop.png` | generated-reviewed | generated-reviewed |
| royal-king-type | `assets/source/units/royal-king.png` | generated-reviewed | generated-reviewed |
| royal-knight-type | `assets/source/units/royal-knight.png` | generated-reviewed | generated-reviewed |
| royal-peasant-type | `assets/source/units/royal-peasant.png` | generated-reviewed | generated-reviewed |
| royal-queen-type | `assets/source/units/royal-queen.png` | generated-reviewed | generated-reviewed |
| shaman-type | `assets/source/units/shaman.png` | generated-reviewed | generated-reviewed |
| skeleton-archer-type | `assets/source/units/skeleton-archer.png` | generated-reviewed | generated-reviewed |
| tower-troll-type | `assets/source/units/tower-troll.png` | generated-reviewed | generated-reviewed |
| troll-type | `assets/source/units/troll.png` | existing-preserved | existing-preserved |
| witch-queen-type | `assets/source/units/witch-queen.png` | generated-reviewed | generated-reviewed |
| wizard-type | `assets/source/units/wizard.png` | generated-reviewed | generated-reviewed |
| worker-type | `assets/source/units/worker.png` | generated-reviewed | generated-reviewed |
| zombie-type | `assets/source/units/zombie.png` | existing-preserved | existing-preserved |
