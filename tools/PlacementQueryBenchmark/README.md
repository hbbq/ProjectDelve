# Placement query benchmark

See [the Phase 1 implementation and measurement report](../../PERFORMANCE_PHASE1.md)
and [retained aggregate results](phase1-results.json) for the initial comparison.

Run from the repository root using .NET 10 and Release. This harness measures
synchronous Engine and in-process Web work, not HTTP, browser rendering, animation
or end-user latency. It never connects to a running game or changes scenario files.

```powershell
dotnet run --project tools/PlacementQueryBenchmark -c Release --artifacts-path .placement-benchmark -- --fixture small --mode all --warmups 3 --samples 30
dotnet run --project tools/PlacementQueryBenchmark -c Release --artifacts-path .placement-benchmark -- --fixture distant100 --mode summon --warmups 3 --samples 30
dotnet run --project tools/PlacementQueryBenchmark -c Release --artifacts-path .placement-benchmark -- --fixture distant442 --mode dormant --warmups 3 --samples 30
```

Defaults: `--fixture small --mode summon --warmups 1 --samples 5`.
Fixture values are `small`, `distant100`, `distant442`, or `all`.
Mode values are `create`, `summon`, `candidates`, `dormant`, `quick`, or `all`.
Use `--help` to print the command syntax. Redirect stdout to retain JSONL results;
build separately and execute the resulting DLL to keep build messages out of JSONL.

The synthetic definitions are benchmark-owned and independent of mutable authored
scenarios. `small` is a 12x12 open board with no distant Units; the other two are
120x120 with 100 or 442 distant Grunts. All place Beastmaster at (2,2), a Dormant
Grunt at (8,8), and distant Dormant Grunts at (15 + i % 100, 15 + i / 100).
Both blue and red controllers are human, blue has FoW, objectives are NoObjective,
and Loot is empty. Dormant mode substitutes Rogue for Beastmaster. The deterministic
provider chooses blue tokens first and misses dice. Quick mode prepares this same
definition with `WorldQuickPlay`, then imports the encoded result into fresh
`PlaytestGame` instances. Thus `--fixture small --mode quick` is the reproducible
small Quick Play workload.

Summon mode sets up an actor activation, measures Advance selecting Stay, enumerates
all Summon candidates, then measures Advance selecting the first Raptor candidate.
`summonCandidates` invokes the existing internal two-argument candidate method via
reflection and materializes its result. This is an isolated Summon candidate-query
allocation/time measurement, including enumeration/reflection overhead; it is not
an instrumented count or timing of individual placement predicate calls.
Each Advance starts from the same input state; fixture construction and initial
choice preparation are outside these stages. `create` measures full CreateGame.
`candidates` runs only the isolated `summonCandidates` stage on a freshly created
fixture with `MoveDone = true`, without calling Advance or RefreshChoices. Use it
to collect candidate-query allocations without repeating full baseline activation
work. It is explicitly selected and is not an additional stage in `all` mode.

Dormant mode measures quiet Rogue Round Start and the isolated proximity query.
It then moves the actor to (14,15), adjacent to the distant group, and measures
Alert propagation and Round Start with Alert. Small uses (7,8), adjacent to the
near Grunt, because it has no distant group. The internal proximity query is also
invoked through reflection. These stages are controls for unrelated dormant work.

Quick stages separately measure parse/validation, preparation (including canonical
bundle construction), encoding, CreateGame, Web import, browser projection and JSON
serialization. Host construction/disposal is outside Web import samples. Each
import uses a fresh host. Projection uses a separately imported host, initialized
outside that stage. Serialization reuses one options instance, as the server does. Optional
`--mode quick --delve path/to/scenario.delve` runs the same workflow on a saved
definition instead; retain that file and its hash for before/after comparisons.
Saved authored content is optional input, never a behavioral test fixture.

Every stage runs its configured warmups followed by measured samples. Output has
environment metadata (including loaded Engine path and module ID), every raw
sample's wall-clock milliseconds, process-wide allocated bytes and GC collection
deltas, then nearest-rank p50/p95/min/max for milliseconds and allocated bytes.
Percentiles with few samples are coarse; with five samples p95 is the maximum.
JSON output, setup and disposal occur outside the measured intervals. Allocation
uses `GC.GetTotalAllocatedBytes(true)` and can include background process activity.
No forced GC or tiered-JIT settling delay is applied. The tool does not record CPU
time or control OS scheduling. Use the same runtime, GC/JIT settings, build and
otherwise idle machine for comparison. Thirty warmed samples are useful for small
stages; retain raw distributions rather than treating p95 as precise with few runs.

`--warmups 0 --samples 1` in a new process measures the first invocation of each
stage, not application startup. Earlier stages and fixture setup can already warm
shared code; this is not a globally cold runtime. Use separate processes per mode
for first-invocation comparisons. Warm results include the state/cache behavior of
repeating each operation on the same input; no derived caches are manually cleared.

For a frozen pre-change build, the optional MSBuild property
`FrozenAssemblyDirectory` replaces project references with DLL references. Supply
an absolute directory containing the matching Engine, Generation and Web DLLs.
Use a separate artifacts directory so baseline and current binaries never overwrite
one another. The framework reference supplies ASP.NET runtime dependencies. Keep
the DLL set together and confirm the reported Engine module ID differs as expected.

```powershell
dotnet build tools/PlacementQueryBenchmark -c Release --artifacts-path .placement-before -p:FrozenAssemblyDirectory=D:/sc/bq/ProjectDelve/.phase1-baseline/bin/Probe/release
dotnet .placement-before/bin/PlacementQueryBenchmark/release/PlacementQueryBenchmark.dll --fixture distant100 --mode candidates --warmups 0 --samples 3
dotnet .placement-before/bin/PlacementQueryBenchmark/release/PlacementQueryBenchmark.dll --fixture small --mode quick --warmups 3 --samples 30
dotnet build tools/PlacementQueryBenchmark -c Release --artifacts-path .placement-after
dotnet .placement-after/bin/PlacementQueryBenchmark/release/PlacementQueryBenchmark.dll --fixture distant100 --mode summon --warmups 3 --samples 30
```

The original 442-distant Summon query is extremely expensive. Prefer retained
pre-change measurements for that case, and use small/distant100 for reproducible
baseline reruns. Requesting `all` explicitly opts into the expensive combination;
there is no timeout or automatic skip. Do not run a benchmark alongside builds,
tests or another benchmark when collecting comparison numbers.
