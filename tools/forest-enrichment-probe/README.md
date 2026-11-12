# Forest enrichment measurement

Run from the repository root, with a distinct output path for each revision:

```powershell
dotnet build tools/forest-enrichment-probe/ForestEnrichmentProbe.csproj --artifacts-path .playwright-cli
$env:DOTNET_TieredCompilation='0'
dotnet .playwright-cli/bin/ForestEnrichmentProbe/debug/ForestEnrichmentProbe.dll .tmp/forest-phase2/before.jsonl
# Rebuild after the production change, then execute with after.jsonl.
python.exe tools/forest-enrichment-probe/compare.py .tmp/forest-phase2/before.jsonl .tmp/forest-phase2/after.jsonl --output .tmp/forest-phase2/comparison.json --verify-evidence tools/forest-enrichment-probe/evidence.jsonl
```

The executable refuses to overwrite output. It uses only public production APIs:
45 paired Session B worlds (three sizes, five styles, three seed/offset pairs),
each at disabled/low/medium/high intensity. Forest is the only enabled enrichment
family; Civilization, routes and Population are absent. Every configuration has
two warmups and five measured generations. Each production generation performs
authoritative Engine validation. Scenario fingerprints and feature, entrance and
opportunity metadata must agree across repetitions. Full generation timings and
current-thread allocations include validation, preview and accessibility audit;
stage measurements are also recorded. Compare the same configuration/build mode
and runtime. Timings are diagnostic and machine dependent.

Anchored access follows the component containing the top-left Cell of the
original largest cardinal ordinary-land component (component order breaks ties).
New traversable Cells count original Trees changed to ordinary land; previously
existing ground connected excludes that original component. No origin yields no
anchored improvement. These topology measurements do not claim Unit-specific
legal Moves, exploration, actual start placement, or encounter success.

Feature usefulness is deliberately explicit: an anchor in that retained component
with at least four Cells of reported usable space. Per-feature raw metadata is
retained so other interpretations can be assessed. Spatial coverage divides each
original eligible formation's bounds into 4×4 bins, reports occupied and edited
bins, and does not treat an edited bin as wholly accessible. Depth and exact
remaining masses use production terrain analysis. Blocking Cells beyond five
cardinal steps of anchored ground are a distance proxy, not Engine visibility.
Budget is the existing nominal `CoreArea * (3 + intensity * 5) / 100` integer cap
per eligible original Forest; recorded utilization is original Trees edited/cap.
The harness retains per-formation data rather than averaging percentages.

`compare.py` uses only Python's standard library. It validates complete paired
coverage and unchanged disabled fingerprints, then reports aggregate intensity,
size and style summaries plus high-intensity per-world paired outcomes. Run it
with just the first input to summarize a baseline before the change is ready.
If a Python launcher cannot locate an interpreter, resolve one with
`Get-Command python.exe -All` and invoke that executable directly.

The repository `evidence.jsonl` retains all 180 paired scenarios' stable scalar
outcomes, fingerprints, feature/coverage counts, and measured generation costs.
`--verify-evidence` checks its stable outcomes against freshly generated raw
inputs; machine-dependent cost fields are excluded from that equality check.
Use `--evidence-output path.jsonl` to create a new compact evidence file.

Optional visual QA (three named 120×120 examples; Pillow required):

```powershell
dotnet .playwright-cli/bin/ForestEnrichmentProbe/debug/ForestEnrichmentProbe.dll .tmp/forest-phase2/maps.jsonl --maps
python.exe tools/forest-enrichment-probe/render-maps.py .tmp/forest-phase2/maps.jsonl .tmp/formation-enrichment-investigation/session-B/results --output .tmp/forest-phase2/forest-comparison.png
```

The renderer restores all original non-Tree Cells from Session B's combined maps,
isolating the old Forest mask from Rock excavation. It verifies old edit counts
and both revisions' anchored new-Cell counts against repository evidence. This
optional historical-mask comparison requires the archived Session B map files;
the main measurement and evidence verification do not.
