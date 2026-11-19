# Economy Phase 4 — Unit and Equipment Levels

## Physical state and authoring

A Unit, its initial placement, and each physical Equipment object carry an integer `level`. Missing means Level 1. The old Type declaration is the complete Level 1 configuration. Optional `additionalLevels` are ordered from Level 2. Each entry supplies its complete configuration. Its list order fixes the Level number. Unit Levels require MOV, RNG, ATK, DEF and HP. Omitting an optional field gives that field's ordinary default, including an empty Ability list. Equipment entries can contain any supported Equipment Abilities and signed maximum HP and activation-count modifiers. Single Use Items retain their existing semantics.

Unit movement capabilities, pattern, restrictions, sight, available Attack, attack ranking, activation counts and local Abilities resolve at the selected Level. Type identity, footprint, Unique, ownership permissions, shared storage, controller policy, random defeat rewards and Sell Value are shared across Levels. Ability source and accounting IDs still identify the acting Unit or physical Equipment copy. Resolving a Level does not draw from the RNG.

| Provisional content | Level 1 | Level 2 |
| --- | --- | --- |
| Barbarian | 5 HP, Friendly Swap as an Action, Rage twice per game | 6 HP, Friendly Swap as a Bonus Action, Rage three times per game. Retains Open Door, Operate, Fury and Cleave. |
| Lucky Boots | +1 MOV | +2 MOV, +1 maximum HP and Second Step, a once-per-game Bonus Action granting one extra Move. Both Levels sell for 4 Credits. |

## Transitions and Effective Max HP

`GameState.UnitConfiguration` and `EquipmentConfiguration` select the effective definitions used by Engine queries. `EffectiveMaxHpOf` uses the selected Unit's base HP plus each owned Equipment instance's current maximum HP modifier. The minimum is 1. Non-Unique Units always have a maximum of 1, even while owning such Equipment. Posture does not affect maximum HP.

`GameEngine.UpgradeUnit` advances a living Unit to the next defined Level, restores HP to its new effective maximum and refreshes all native uses. `UpgradeEquipment` advances one owned or ground instance and refreshes its own uses. A change in ownership or Equipment Level can clamp HP silently. Gaining capacity through Equipment leaves current HP intact. Unrelated counters, instance IDs, Posture and activation spending survive. The helpers evaluate detached state and return it only if the bearer can occupy its current footprint. They perform no future route search. They reject an undefined next Level and cannot be used to refresh an instance already at its highest Level.

## Save, replay and Expedition

Level 1 is omitted from serialized instances. An absent Level ladder and zero base modifiers introduce no new serialized Type fields. Explicit higher Levels and definitions participate in JSON, DELVE1, content digests, detached snapshots and checkpoints. Old checkpoints use their saved definitions when opened. Region handoff restores the five originals at their retained Levels with the same physical Equipment IDs and Levels. Newly selected content must support each carried Level before the successor can be published. A retry reuses the existing atomic handoff. Existing source checkpoints remain historical records.

Cards, Equipment descriptions, decisions, Station previews and controller observations use the current configurations. The client displays the Engine's effective maximum and the Level of each observable instance. Visible defeat contexts retain the Unit Level. Phase 4 has no player upgrade command, so a standalone purchase event or new RNG stream is unnecessary.

## Verification

The .NET 10 solution built with zero errors. NuGet emitted NU1900 because the cached build could not reach the vulnerability feed. The Engine suite, including generation, passed all 3,862 tests. The client suite passed 1,160 tests and skipped the optional Beacon benchmark. All seven browser-dispatcher checks passed. [Build log](.playwright-cli/phase4-final-build.log) · [Engine run](.playwright-cli/phase4-verification.log) · [Client run](.playwright-cli/levels4-client.log) · [Dispatcher run](.playwright-cli/phase4-dispatcher.log)

The complete Web suite passed all 900 tests with xUnit test collections run serially. Earlier parallel runs timed out in localhost generation and streaming tests. An earlier serial run saw one connection reset during an oversized cover upload. That test passed on its isolated retry and in the final full run. [Final Web run](.playwright-cli/phase4-web-verified.log) · [Cover-upload retry](.playwright-cli/phase4-cover-retry.log)

Dedicated Engine tests cover complete Ability replacement and timing, distinct same-Type instances, remaining uses, explicit movement and Attack restrictions, all three activation budgets, legacy MoveDone-only saves, HP restoration and clamping, permitted non-Unique Equipment, immediate occupancy, healing, defeat and ground drops. Station tests replay an operation opened by a level-specific Operate Ability and verify source charging, transfer, sale, zero RNG draws and HP normalization. Web tests cover typed authoring, validation, digests, authorized observations, reopen, region handoff, incompatible refresh and retry.

**Browser verification.** In Edge at 1440 and 390 pixels wide, a copied Barbarian's Level 2 MOV was edited to 5 and validated. The Engine preview showed Bonus Action Friendly Swap and three Rage uses. In Scenario Designer, a new Level 2 Barbarian validated and entered play at 6/6 HP. Another draft independently assigned Level 2 to its Lucky Boots, saved to the library, recovered after reload and entered play at 7/7 HP. Confirming a Station transfer of the boots left the Barbarian at 6/6 and the injured Cleric at 2/5. Desktop and mobile screenshots were inspected. All four completed walkthroughs recorded zero console errors and zero failed requests. Local evidence includes the [Content Editor preview](.playwright-cli/browser-qa/inspection-k82nEj/editor-desktop.png), [mobile placement](.playwright-cli/browser-qa/inspection-KWPgC0/placement-mobile.png), [recovered Equipment](.playwright-cli/browser-qa/inspection-GeZ9WT/recovered.png) and [confirmed transfer](.playwright-cli/browser-qa/inspection-6WGi8H/confirmed.png).

## Current limits and Phase 5 integration

Barbarian and Lucky Boots are the two canonical Types with a second Level. Scenario authors can define further Levels or place instances directly at any defined Level. Advancing during normal play awaits Phase 5.

The Station still offers transfers and sales. The Engine's Level operations are available to trusted callers at stable boundaries. Phase 5 can build a detached final loadout with its purchases, then perform the current Station's live validation, source charging and one atomic Confirm. The helpers deliberately reject an already pending Station or nested combat sequence. Phase 5 must coordinate HP restoration with the final retained Equipment and validate the combined proposal, including its Credits cost.
