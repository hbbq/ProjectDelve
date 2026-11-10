# Pathfinding benchmark

Run sequentially from the repository root with .NET 10. Timing runs must not overlap
builds, tests, another benchmark or browser measurements.

```powershell
dotnet run --project tools/PathfindingBenchmark -c Release --artifacts-path .phase3-after -- --worlds --samples 10 --warmups 3 --export .tmp/phase3/states
```

The 18 benchmark-owned scenarios use generated natural terrain at 30x24, 60x50 and
120x120, with fixed seed `0x123456789ABCDEF0`, one automated actor and one Human
target. They cover nearby, distant and disconnected targets, for route following
and Normal pursuit. Route controls use Follow Route to isolate waypoint distance
from Patrol's combat override. Disconnected controls add a solid-rock separator.
Nearby and distant placements are in the same independently discovered terrain
component. Generation, materialization and serialization are outside timing.

The exact saved 80x80 world is a separate input, never regenerated:

```powershell
dotnet run --project tools/PathfindingBenchmark -c Release --artifacts-path .phase3-after -- --delve .tmp/world80-investigation/original.delve --canonical .tmp/world80-investigation/canonical --workspace .tmp/world80-investigation/workspace --samples 10 --warmups 3 --export .tmp/phase3/states
```

Use detached Canonical/Workspace directories. The DELVE1 fixture uses the live
`All` Loot preset; compiled C# definitions alone need not supply the same deck.
The harness prints the transport SHA256. It creates original Patrol and Normal
diagnostic states, replacing only Patrol Orders for the latter.

`--states DIRECTORY` loads previously exported authoritative `*.state.json`
snapshots so all phases use exactly the same definitions and terrain. `--filter`
selects a name substring; `--seed` controls the injected random-index draw and
dice implementation (default 1). `--trace-only` skips timed samples. All choices
come from authoritative candidate lists; the exact world's Human sequence is
Stay, End Turn, then Start Round if the first round completed. Seed 0 exercises
Beastmaster-first draws; seed 1 exercises Patrol-first draws. Other fixtures stop
at the first Human decision. The elapsed metric is this entire Engine sequence,
not HTTP or client playback, and not just the initial Start Round call.

Each timed sample records total Engine elapsed time, aggregate provider time,
synchronous allocated bytes, GC collections and selected keys. Three default
warmups and one second JIT settling precede samples. Full traces run separately:
their SHA256 covers every EngineResult (including candidates, paths, events and
event-time snapshots), event-time FoW observations, provider/Human choices and
all RNG calls. A second trace serializes/deserializes state at every decision
boundary and must match the uninterrupted trace. Trace serialization is never
included in elapsed samples.

To compare a frozen build without modifying source or the running host:

```powershell
dotnet build tools/PathfindingBenchmark -c Release --artifacts-path .phase3-before -p:FrozenAssemblyDirectory=D:/path/to/frozen/assemblies
dotnet .phase3-before/bin/PathfindingBenchmark/release/PathfindingBenchmark.dll --states .tmp/phase3/states --samples 3 --warmups 1
```

The frozen directory must contain matching Engine, Generation and Web assemblies.
The tool is outside the solution and has no new package dependencies. Method
call/visited-cell profiles use separate temporary instrumented source copies;
those timings must not be treated as uninstrumented benchmark timings.

See [PERFORMANCE_PHASE3.md](../../PERFORMANCE_PHASE3.md) and
[phase3-results.json](phase3-results.json) for measured results and limitations.
