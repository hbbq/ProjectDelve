Passage quality investigation
=============================

Run the detached benchmark sequentially (outside the solution):

```powershell
dotnet run --project tools/WorldGenerationBenchmark --artifacts-path .world-benchmark -- --passage-quality --self-test
dotnet run --project tools/WorldGenerationBenchmark --artifacts-path .world-benchmark -- --passage-quality .tmp/passage-quality-after
```

The corpus contains 48 complete WLD1 recipes: Woodland, RockyWilds,
LakeCountry and RiverValley; 30x24 and 60x50 windows at (-17,-13);
seeds 1, 2, 3, 13, 42 and 99; preset generation settings and Classic Loot.
`manifest.json` records every recipe, runtime, loaded Engine/Generation assembly
hashes and measurement definitions. `samples.jsonl` retains one result or full
exception for every recipe. Results overwrite previous outputs in the chosen
directory. Execution time is diagnostic, not a performance benchmark.

For paired before/after results, capture the baseline Engine and Generation DLLs
before changing the generator. Build this same benchmark source against those
DLLs in an isolated project/output directory, or replace its copied runtime DLLs
with that captured pair. Run once per pair, preserving both output directories.
Check the manifest hashes; an old executable loading newly built generation
DLLs is not a baseline. Use the same benchmark source for both runs.

Compare completed runs with the package-free Python helper:

```powershell
py tools/WorldGenerationBenchmark/compare-passage-quality.py .tmp/passage-quality-baseline .tmp/passage-quality-after
```

It requires identical recipe sets and writes `comparison.json` to the after
directory. Per-preset/size and overall paired totals include only recipes that
succeeded in both runs; the separate whole-run summaries and full error lists
retain failures rather than hiding them in paired averages. Clearance totals
count observations, so overlapping route corridors or Building exterior rings
can count a Cell more than once.

Geometry is measured through Engine static ordinary 1x1 Land occupancy and
cardinal passable Edges, with Closed Doors opened only on a detached measurement
board. Figures are ignored, Machines block. Thin means no legal 2x2 square
containing the Cell, including turns. Long thin chains contain at least eight
connected thin Cells of full-graph degree two. A substantial articulation
separates at least two pieces of at least twenty Cells each. Long substantial
bottleneck runs use the same degree-two chain definition restricted to substantial
articulations. Thus a short doorway is distinct from a long narrow corridor.
The iterative articulation analysis handles all connected components independently.

Clearance observations report passable, thin, 2x2-participating and substantial
Cells in route corridors, cardinal exterior rings of Buildings, Site exteriors
and outside portal Cells. These measures are advisory. Natural disturbance
compares original samples with final materialized terrain, grouped by original
terrain type, and includes all producers. Forest and Rock enrichment-stage
changes are also counted separately.
The existing internal mandatory-repair report is serialized via reflection only
for authoring diagnostics. Site/dungeon/room counts and planning omission reasons
help identify gains obtained by dropping content. Every successful row passes
Engine creation, explicit PlayReady validation and published-setup Quick Play
preparation and creation.

Self-tests use dedicated synthetic geometry: open rooms, a long corridor,
rooms joined by a corridor, a cycle and an isolated passable Cell. They check
2x2 detection, chain endpoints and substantial cut thresholds independently
of mutable authored scenarios.
