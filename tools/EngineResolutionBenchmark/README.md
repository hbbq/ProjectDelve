# Engine resolution benchmark

Run from the repository root using .NET 10, Release, default tiered JIT and
workstation GC:

```powershell
dotnet run --project tools/EngineResolutionBenchmark -c Release --artifacts-path .playwright-cli
dotnet run --project tools/EngineResolutionBenchmark -c Release --artifacts-path .playwright-cli -- --obstacles
dotnet run --project tools/EngineResolutionBenchmark -c Release --artifacts-path .playwright-cli -- --stronghold --warmups 3 --samples 5
```

The default is the original 30×30, ten-monster fixture. `--obstacles` adds its
deterministic rock distribution. `--stronghold` independently regenerates
`DNG2-OS-50x50-O2-T2-N3-L3-B2-OR2-F1-QAll-S6DCCA420DCFB55FB` using compiled
canonical content. It does not access a running host or save changes to scenarios.

To measure an effective content snapshot instead, pass `--state path/to/state.json`.
This must be a complete authoritative serialized `GameState`, not a FoW-filtered
Web response, ready to start a round (initial setup or a completed round). Both
numeric and named enums are accepted. Use a detached export or
isolated scenario creation; never advance a live game for profiling. The snapshot
captures its own definitions, Orders, exploration and other gameplay state.

Every run resolves a copy with RNG seed 7241. The original fixture retains its
red-first token schedule. Generated/snapshot fixtures choose fixed-seed random
automated tokens before human tokens, stopping at the first human decision. The
tool measures Engine work and provider time, not HTTP, rendering or playback.
Fixture construction, cold timing, warmups, the one-second JIT settling interval
and final trace serialization are outside measured samples. Allocation is measured
on the synchronous resolution thread; GC counts span all measured samples.

The output includes median/range, allocation, GC collections, activation/event/RNG
counts and a SHA-256 digest over full results, pending candidates and event-time
state snapshots. Compare digests only with identical fixture content, runtime
serialization and token scheduling. Derived vision caches are absent from saved
snapshots and are rebuilt on the first refresh after loading.

## Milestone 4 measurements

Measured on Windows/.NET 10.0.12, Release, default tiered JIT, workstation GC,
three warmups plus one second settling and five measured samples. Independently
generated Stronghold fingerprint `0EC9201373404D1F`, using detached copies of the
local Canonical publication and Workspace. This was not the running game. It had
19 Units (four Heroes, fifteen hidden automated monsters), nine Hold, four Normal
Ghosts, two Protect Bomb Imps, Hero-only FoW, and 2,500 explicit terrain entries.
The same deserialized snapshot was used for all three builds; initial cache setup
was included in each resolution. Content snapshot files were temporary.

| Measurement | Unchanged baseline | Order dispatch (A) | Dispatch + FoW reuse (B) |
| --- | ---: | ---: | ---: |
| Engine median, ms | 2,814 | 2,074 | 411 |
| Engine range, ms | 2,737–2,885 | 2,057–2,093 | 384–462 |
| Provider median, ms | 1,134 | 380 | 380 |
| Allocated bytes/run | 2,656,065,504 | 771,910,728 | 757,684,088 |
| GC collections, gen 0/1/2 across five samples | 1597/80/17 | 465/29/5 | 456/25/5 |
| Approach fields | 15 | 4 | 4 |
| Approach BFS calls, forward + reverse | 46 + 15 | 46 + 4 | 46 + 4 |
| Approach BFS cells visited | 47,974 | 33,069 | 33,069 |
| Hypothetical attack evaluations | 59,316 | 16,193 | 16,193 |
| GameState copies | 59,104 | 16,025 | 15,999 |
| Refresh calls | 152 | 152 | 141 |
| Source vision calculations (four sources per full refresh) | 608 | 608 | 4 |
| FoW geometric LOS checks | 36,480 | 36,480 | 240 |
| Total geometric LOS checks | 37,216 | 36,544 | 304 |
| Total terrain-definition resolutions | 91,751,466 | 90,020,411 | 818,218 |

Operation counts came from separate temporary instrumented source copies, never
from the timed builds. One warm diagnostic sample spent approximately 1,788 / 1,790 /
27 ms inside Refresh respectively; these inclusive diagnostic timings are not
sample medians. Of B's 141 refresh calls, only the first recomputed vision; the
others compared dependencies and accumulated cached sight. Moving a non-FoW Unit
still refreshes once for preceding edits, but creates no swept transit snapshots.
Vision-source movement retains the canonical transit sweeps.

All builds produced 26 advances, fifteen completed monster activations, 52 events,
five RNG calls, and identical full result digest
`ba67635dc9e5a0dd988ef0c4d64559b7fe85d7d613f1e644278dc5caea6387aa`.
The earlier investigation's approximately 2,673 ms baseline is consistent but
comes from a separate run; the comparison above uses the freshly measured baseline.

The next measured cost is Normal Ghost planning: four approach fields still perform
16,193 hypothetical attack evaluations and nearly 16,000 state copies. That work
remains outside this milestone. No pathfinding, attack rules or content changes
were included.

Verification: solution build succeeded; all 2,345 Engine tests and 890
JavaScript/security tests passed. Web tests: 655 passed, twelve existing
`knightly-boots` conflicting-identity failures remained. No SSE timeout occurred.
The 30 added tests include a frozen-provider matrix of 324 Order/Behavior/request/
candidate-order combinations, six seeded full-round traces, and a frozen FoW
recomputation/sweep oracle with dependency, copy, save and event-time checks.
Independent review found no production defects. Isolated Edge desktop/mobile
rendering and round-start checks reported no console or request errors. The live
5080 host and original content were left untouched; profiling source copies,
instrumentation and content snapshots were removed afterward.
