# WLD1 Passage Quality B1

Primary through routes try a continuous 2 × 2 footprint using the existing
route planner. Settlement and Mountain Dungeon branches, plus explicitly
required Site connections, can try the same footprint outside their existing
access port. The square includes the outward Cell and uses the original port
grant. The actual entrance can still be a single-Cell Door. The wider attempt
has a separate bounded work allowance and must pass the regular cumulative
disturbance limits and completion preflight. The width-one attempt retains its
original allowance. Route intents record why a preferred width was skipped.
Ordinary Campsite branches and exploration links keep their existing width policy.

Forest and Rock Enrichment look for runs of at least eight thin degree-two
Cells along the tentative feature paths. They try up to three local passing
spaces per feature. A straight stretch can gain a three-Cell shoulder with
a bypass around the center Cell. A smaller 2 × 2 pocket can fit at a bend.
These edits share the existing per-formation budget with the trail, cave
and room. Forest uses eligible original Trees. Rock uses eligible interior
Solid Rock away from original Water and checks the existing dungeon-opportunity
guard before accepting each bay. Feature diagnostics count added and skipped
spaces. Short narrows and the original entrance mouths are retained.

Settlement candidates rank clearance near building shells and public
approaches alongside existing preparation costs. After validating the draft's
access, the planner can clear up to four adjoining natural Cells for passing
space within the remaining Settlement preparation allowance. It uses open
Edges around already prepared exterior Cells and leaves the new ground outside
the protected footprint. Protected Cells, existing Walls and Doors, declared
ports and required access keep their original constraints. Layout diagnostics
record when narrow exterior Cells lack a permitted local passing space.

These policies are optional construction preferences. PlayReady still validates
protected deployment and ordinary geographic reachability with the existing
complete permitted-graph fallback. Passage diagnostics use ordinary potential
access through Open and Closed Doors. The benchmark method, recipe corpus and
commands are documented in [Passage quality benchmark](../tools/WorldGenerationBenchmark/PASSAGE_QUALITY.md).

## Paired results

The 48 recipes use six seeds at 30 × 24 and 60 × 50 for each of four presets.
The baseline is the WLD1 generator at `1f24135`.
Thin percentages divide thin Cells by all traversable Cells over paired maps.
The longest runs are measured in Cells.

| Preset | Thin Cells | Chains ≥8 | Longest thin chain | Substantial cuts | Substantial runs ≥8 |
|---|---:|---:|---:|---:|---:|
| Woodland | 17.28% → 11.90% | 58 → 37 | 32 → 32 | 230 → 174 | 7 → 3 |
| Rocky Wilds | 14.86% → 9.82% | 55 → 34 | 31 → 28 | 380 → 385 | 6 → 3 |
| Lake Country | 10.91% → 9.92% | 38 → 34 | 34 → 33 | 241 → 264 | 2 → 1 |
| River Valley | 8.47% → 5.96% | 28 → 18 | 22 → 16 | 216 → 197 | 3 → 2 |
| All | 12.52% → 9.17% | 179 → 123 | 34 → 33 | 1,067 → 1,020 | 18 → 9 |

The longest substantial run went from 14 to 13 Cells. Nine maps still had
a substantial run of at least eight Cells, down from fourteen. Lake Country
seed 42 at 60 × 50 retained a 33-Cell thin chain. The 30 × 24 River Valley
seed 1 produced a 13-Cell substantial run. Monitor narrow river and lake
approaches and protected port interfaces as separate cases when tuning.

| 2 × 2 clearance observations | Before | After |
|---|---:|---:|
| Through Route corridor Cells | 69.5% | 99.7% |
| All route corridor Cells | 73.0% | 96.8% |
| Building exterior ring Cells | 58.8% | 63.7% |
| Building approach Cells | 79.0% | 96.6% |
| Site exterior Cells | 74.2% | 76.4% |
| Site approach Cells | 76.3% | 80.7% |

Of 54 accepted Through Routes, 52 used width two, up from zero of 53.
Site Connections used it on 25 of 70 accepted branches, up from zero of 78.
Building approach clearance improved while substantial cuts at those positions
rose from 171 to 180. A passing space outside a doorway can offer maneuvering
room while the doorway remains a single point of access.

| Disturbance, content and validation | Before | After |
|---|---:|---:|
| Final terrain Cells different from original samples | 27,442 | 30,120 |
| Original Tree or Fruit Tree Cells changed by all stages | 8,963 | 9,871 |
| Original Solid Rock Cells changed by all stages | 6,559 | 6,964 |
| Forest Enrichment stage changes | 2,613 | 2,663 |
| Rock Enrichment stage changes | 1,247 | 1,251 |
| Sites | 197 | 189 |
| Buildings | 295 | 292 |
| Mountain Dungeons | 33 | 33 |
| Mountain Dungeon rooms | 88 | 84 |
| Worlds with mandatory repair conversions | 7 | 5 |
| Mandatory repair conversions | 50 | 33 |
| Exhaustive fallback invocations | 0 | 0 |
| Valid PlayReady worlds | 48 | 48 |
| Generation failures | 0 | 0 |

Final terrain disturbance rose 9.8% across all producers under the existing
ceilings. Optional Campsite reservation conflicts increased from 27 to 35 and
account for the drop in Sites. Settlement and Mountain Dungeon omission totals
held steady. A follow-up could compare earlier Campsite alternatives around
wide routes with the gain from retaining their preferred path footprints.
The focused repair fixture exercises the exhaustive fallback through a
protected single-Cell Door after a width-two attempt fails.
