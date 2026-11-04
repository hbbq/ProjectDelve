# Large Maps — Generator & Automated Awareness (Design Backlog)

Status: **idea only; not approved gameplay rules or implementation**. Revisit together when designing a new generator for larger maps. Do not interrupt the ongoing pathfinding performance work.

## Motivation

On large maps, default automated monsters pursue enemies across arbitrarily long reachable paths. This pulls distant monsters toward the heroes early, leaving unexplored regions empty. It also incurs costly movement/attack/approach analysis for monsters that are far away and hidden by FoW.

We want large maps to retain encounters in distant regions while avoiding unnecessary automation work.

## Candidate behavior policies

### Maximum Pursuit Distance

An automated Unit treats enemies farther than X steps away as unavailable for its default approach targeting. This limits *target acquisition*, not necessarily the whole activation. If all enemies exceed the threshold, normal fallback behavior must be specified. If a target later returns within range, pursuit may resume.

### Dormant Until Triggered (permanent awakening)

A Unit starts dormant and performs no automated actions until an enemy comes within distance X. Once awakened, it remains active for the rest of the scenario even if enemies move away.

Dormant Units should initially remain in the normal activation bag and resolve an empty activation; changing token-bag population is a separate rules decision. The wake state must be serialized for deterministic save/resume and replay.

### Guard Area / Leash (possible later extension)

A Unit defends an area or anchor point, pursuing only within a specified boundary or returning when the target leaves. This is a distinct policy, not simply a maximum target distance.

## Configuration scope

Prefer **scenario-specific behavior**, potentially as parameters of Standing Orders or a separate awareness/acquisition policy layered over Normal/Hold/MoveTo. Avoid hardcoding these settings in canonical Unit Types.

Possible future scope hierarchy: scenario now; campaign-specific settings later.

Illustrative configuration:

```text
Order: Normal
TargetAcquisition:
  maxPursuitDistance: 10
Awareness:
  dormantUntilEnemyWithin: 6
  permanentlyAwaken: true
```

No syntax or contract is decided.

## Distance and knowledge questions

- Pursuit distance: shortest *legal movement-path* distance is a candidate, especially if shared approach/distance-field optimization makes it cheap.
- Wake proximity: Manhattan distance is a cheap initial candidate; optionally LOS or path distance if scenario design calls for it.
- Distinguish geometric proximity from actual traversable distance.
- Define whether walls and closed doors affect waking, and whether waking represents hearing/proximity rather than vision.
- Respect each Side's information model; do not accidentally leak player FoW knowledge into automated decisions.
- Decide how multiple enemy Units are aggregated (nearest qualifying enemy).
- Define edge cases: X=0, unreachable enemies, no enemies, sleeping units being attacked or displaced, summons, and activation during wake-up.
- Preserve deterministic order, activation bag behavior, and replay.

## Generator for large maps

Design the awareness policies *together with* a new large-map generator, not as a standalone mechanic task.

The generator should support regional encounter placement and varying dormant/pursuit/guard behaviors so distant monsters remain in their own areas until the player approaches. Consider different policies by region or encounter rather than requiring manual configuration for every Unit.

Keep the existing generators and current rules unchanged until the new design is approved.

## Performance goals and measurement

Dormant Units should skip expensive automated movement/attack/approach planning, performing only a cheap wake check. Pursuit limits should reduce unnecessary distant target searches when exact legality permits it.

Benchmark large maps with many dormant and active monsters, measure decision time and allocations, and verify that behavior changes are deliberate rather than accidental optimizations. Maintain deterministic Engine transitions and FoW-safe observations.

## Next step

When starting the large-map generator investigation, review this backlog, choose distance semantics and policy placement, then design generator parameters, encounter distribution, and acceptance tests together.
