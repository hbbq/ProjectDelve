# Web publication benchmark

Run this standalone .NET 10 harness from the repository root in Release. It emits
JSONL raw samples (including warmups), nearest-rank p50/p95 elapsed time and
process-wide allocated bytes, and per-sample generation 0/1/2 collection deltas.
It never connects to an existing server or changes authored content.

See [PERFORMANCE_PHASE2.md](../../PERFORMANCE_PHASE2.md) for interpretation and
[phase2-results.json](phase2-results.json) for the captured before/after samples,
allocation/GC data, instrumentation and Edge results. The report distinguishes
warm, stage-first and fresh-host measurements and lists known test failures.

Build separately so build output does not pollute JSONL:

```powershell
dotnet build tools/WebPublicationBenchmark -c Release --artifacts-path .publication-after
dotnet .publication-after/bin/WebPublicationBenchmark/release/WebPublicationBenchmark.dll --fixture small --mode all --warmups 3 --samples 30 > .publication-after/small.jsonl
dotnet .publication-after/bin/WebPublicationBenchmark/release/WebPublicationBenchmark.dll --fixture distant442 --mode import --warmups 3 --samples 30 > .publication-after/import442.jsonl
```

The same source compiles against frozen assemblies without rebuilding production
projects. Supply an absolute assembly directory. For the Phase 1 baseline frozen
from commit `360e54a`, the existing directory is `.phase2-baseline/bin/Probe/release`:

```powershell
dotnet build tools/WebPublicationBenchmark -c Release --artifacts-path .publication-before -p:FrozenAssemblyDirectory=D:/sc/bq/ProjectDelve/.phase2-baseline/bin/Probe/release
dotnet .publication-before/bin/WebPublicationBenchmark/release/WebPublicationBenchmark.dll --fixture small --mode all --warmups 3 --samples 30 > .publication-before/small.jsonl
```

Use distinct artifact roots for before/after builds. Run sequentially on the same
machine with matching arguments and no concurrent builds/tests. The environment
record includes Engine/Web module IDs and loaded Engine location to verify the
assembly comparison. Defaults are `--fixture small --mode all --warmups 1 --samples 5`.
Use `--help` for syntax.

Fixtures are benchmark-owned. `small` has a 12x12 open board, Beastmaster at (2,2)
and a Dormant Grunt at (8,8). `distant100` and `distant442` have a 120x120 board
and add 100 or 442 Dormant Grunts at (15 + i % 100, 15 + i / 100). Both sides use
human control, blue has Fog of War, objectives are NoObjective and Loot is empty.
The deterministic provider chooses blue tokens first and rolls misses. `--fixture
all` runs all three sequentially. No authored scenario layout is a test contract.

Stages run independently and sequentially:

- `import`: combined Quick Play parse/validation/preparation (the handler's new
  Materialize + internal PrepareValidated path when available; frozen baseline
  falls back to FromJson + Prepare), transport encoding, legacy host import,
  streaming Accept-to-baseline import, BrowserUpdate projection and serialization.
  Canonical content construction and initial source JSON serialization are outside
  preparation timing. Reflection allows identical source to compile with both APIs.
- `preparation`: only Quick Play preparation, transport encoding and a separate
  `import.createPrepared` Engine materialization stage. Returns before host import
  to support larger warm sample sets without repeating expensive baseline host
  imports. Accepts `--delve`; excluded from `all`.
- `action`: streaming Stay and Raptor summon decisions from dedicated activation
  states. Each operation has a fresh host and prepared pending choice. Raptor's
  prerequisite Stay runs outside its measured stage. Internal host construction
  uses replay capacity 1024 to accommodate event streams.
- `pipeline`: explicit Stay/Raptor combined command service, BrowserUpdate projection
  and UTF-8 serialization measurement. Fresh host and subscription setup remain
  outside timing, as does disposal/runner joining. Each sample first collects the
  actual stream through its final boundary, then projects and serializes every
  collected update sequentially. Total elapsed time and allocations cover that
  complete sequence; details report publication count, bytes and service timing.
  This is a conservative sequential in-process pipeline, not actual concurrent SSE
  execution, HTTP transport or browser rendering. Excluded from `all`.
- `observation`: raw PlaytestGame.Detach(GameState), FogOfWar.Observe and observed
  state JSON serialization, measured separately on a pre-created game. These are
  useful attribution controls; observation serialization is not a browser payload.
- `dormant`: optional quiet Rogue Round Start Engine control, with a fresh state
  per invocation. This mode is explicitly selected and is excluded from `all`.

Host creation, disposal, subscriptions, initial-baseline draining, caller snapshot
and decision selection are outside stream timing/allocation intervals. Streaming
samples include admission, host execution, publication and reader consumption.
Their detail reports admissionMs, acceptedToReadyMs, totalServiceMs, publication
count/kinds and final status. Completion requires the matching command ID and
revision + 1 baseline (import) or boundary (action); faults and two-minute timeouts
fail the run. Initial subscriptions are drained before submission. Each measured
command uses a fresh host to prevent stale sessions/decisions.

After measuring service-to-ready, cleanup disposes the host and joins its command
runner outside timing. This excludes any unused aggregate construction performed
after the ready publication while preventing that work from overlapping a later
sample. Such post-ready aggregate cost requires separate profiling. Projection
hosts are also stopped and joined before projection measurements; BrowserUpdate
uses the retained publication data and remains callable after host disposal.

Projection stages consume actual collected GameUpdates on a separately initialized
host. They invoke the same internal BrowserUpdate used by SSE, then serialize all
projected updates with shared Web JSON options and string enums. Serialization
detail reports total UTF-8 bytes across updates, excluding SSE framing. Projection
and serialization are independent measurements, outside command service timing;
the harness does not include HTTP, sockets, animation, rendering or end-user latency.
Reflection overhead remains in relevant stages on both sides of the comparison.

`firstInvocation` identifies the first invocation of each stage, including its first
warmup. `--warmups 0` retains that first invocation among measured samples. This
is a stage-first control, not process startup or a guaranteed cold CLR: setup and
earlier stages can warm shared paths. Use fresh processes and compare warm samples
separately. Allocation is process-wide to include runner work; unrelated host
background activity can contribute noise. No forced collection occurs.

An optional saved transport can be measured without relying on its mutable content:

```powershell
dotnet .publication-after/bin/WebPublicationBenchmark/release/WebPublicationBenchmark.dll --mode import --delve path/to/saved442.delve --warmups 3 --samples 30 --export .publication-after/browser-response.json
```

`--delve` loads a transport before timing and requires `--mode import` or
`--mode preparation`. For preparation attribution, use the same command with
`--mode preparation --warmups 3 --samples 30` and omit `--export`.
`--export` writes the actual imported browser GameResponse outside timing for
before/after contract comparison. It requires `--mode import` and one fixture.
Normalize session IDs before
comparing outputs. Measurements intentionally contain no production instrumentation;
CreateGame/query/JSON call-count attribution is a separate investigation.
