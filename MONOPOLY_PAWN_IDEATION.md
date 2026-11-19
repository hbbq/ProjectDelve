# The Tycoon / Monopoly Pawn — design notes (not decided rules)

Status: **ideation only**. Do not add to CONSTRAINTS.md or implement as part of the Economy phases without a separate decision.

## Character and presentation

A recruitable, persistent Expedition Party Hero inspired by a classic family-board-game pawn, possibly a simple plastic pawn or metal token. Unlike the ordinary static Delve figures, it visibly hops from Cell to Cell along the actual movement path, with a little landing click, like a player moving a pawn by hand. An oversized hand physically picking it up and setting it down is an amusing optional presentation idea, **not required**. Presentation never overrides legal movement/pathfinding. If Lying, the pawn simply tips over. Its abilities should evoke its source board game while remaining ordinary Delve Engine mechanics.

Role: mobile Loot/Credits collector and economic support, weak-to-moderate combat. Proposed name: **The Tycoon** (working name).

## Core activation mechanic — most important idea

Base **MOV 0**. A proposed general-purpose **Start of Activation** trigger (new mechanic, if not already supported) resolves before normal Bonus / Move / Act phases:

1. Roll **two independent d6**.
2. On doubles, increment this Unit's persistent **equals-counter**.
3. If the counter reaches **3**, reset it to 0, apply **Lying** (thematic 'Go to Jail'), and **end this activation immediately**. No ordinary Move or Act from this activation; do not award an extra activation for the third doubles.
4. Otherwise, if doubles, grant **one Extra Activation**, to occur after the current activation.
5. Apply a **temporary MOV modifier equal to the sum of the two dice** for the current activation, then run the normal movement rules (up to the available MOV, not an exact distance). On third doubles the activation has already ended, so no movement.

**Open design question:** When should equals-counter reset after a non-double? For fidelity to Monopoly, proposal is reset to zero on a non-double, so only *consecutive* doubles count. This was suggested in discussion but not explicitly confirmed. Also decide what happens when activation sequences are interrupted or resumed across rounds/saves.

Extra Activation means a **full new activation** (new start trigger and ordinary Bonus / Move / Act), **not** an extra Action or simply more movement. Proposed timing: immediately after the current activation, before the next token-bag draw. Each new activation rerolls 2d6. The third doubles ends the current activation Lying and must not leave a dangling extra activation from that roll. If the engine already supports a similar mechanic under a different name, reuse it rather than duplicating.

## Extra Activation as a generic mechanic

This is useful far beyond the pawn: Single Use Items, Equipment Actions, Unit Abilities and World Cards could grant Extra Activation. Physically, place a small **Extra Activation token** beside the Unit, then remove it when the extra activation starts. Digital engine should use deterministic, serializable pending activation grants; ensure replay, save/resume, token-bag order, defeat/removal, and multiple grants are well-defined. This should not be pawn-specific. Exact stacking/queue semantics and whether extra activations can grant further extra activations are to be designed separately.

## Other proposed abilities

- **Bonus Action: Draw Chance Card** — draw from a thematic Chance deck; outcomes could grant or remove Credits, reposition a Unit, add a Cell Marker, etc. Prefer existing card/effect primitives if feasible. Exact deck, draws, timing and balance undecided.
- **Action: Collect Rent** — transfer a small amount of Credits from an **adjacent enemy Side** to the pawn's Side. Since Credits are shared by Side, the enemy Unit need not hold Credits itself. Suggested cap: up to 5 Credits, bounded by the enemy Side balance. Amount and constraints are provisional.
- Potential upgrades/levels could change these abilities; no levels have been settled.

## Boundaries and decisions

- This is **not** Budget Barbarian. Budget Barbarian remains a deliberately unmodeled 'BB' token summoned by a discoverable Equipment; it should **not** become the ordinary sixth recruit by default.
- The pawn is only a candidate for the sixth Party Hero, not selected or implemented yet.
- Do not silently turn 'Go to Jail' into a teleport to Expedition spawn: ordinary scenarios may lack a spawn area; **Lying** was the preferred cross-scenario effect.
- Existing general Action/Bonus Action modifiers are separate from Extra Activation.
- Keep mechanics engine-owned, deterministic and compatible with physical play.
