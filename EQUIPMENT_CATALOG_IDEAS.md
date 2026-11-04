# Project Delve — Equipment & Item Catalog Ideas v0.1

Ideation backlog, not approved rules or implemented content. Proposed names, effects and Type IDs may be refined. Categories: Classic, O-Classic, Funny, World Bending. Items and Immediate Loot are included where appropriate.

## Classic

| Name | Kind | Effect | Created |
|---|---|---|---|
| Boots of Haste | Equipment | Passive MOV +1 | Yes, as Lucky Boots
| Bracers of Strength | Equipment | Passive ATK +1 | Yes
| Iron Shield | Equipment | Passive DEF +1 | Yes
| Eagle Eye | Equipment | Passive RNG +2 | Yes
| Banner of Fury | Equipment | Passive: adjacent friendly Units ATK +1 | Yes
| Guardian Standard | Equipment | Passive: adjacent friendly Units DEF +1 | Yes
| Berserker Ring | Equipment | Bonus Action: ATK +2 for current activation; 2 uses | Yes
| Seven-League Boots | Equipment | Bonus Action: MOV +2 for current activation; 2 uses | Yes
| Scope of Precision | Equipment | Bonus Action: RNG +2 for current activation; 2 uses | Yes
| Healer's Staff | Equipment | Action: heal adjacent damaged friendly Unit by 2 HP; 2 uses | Yes
| Holy Boots | Equipment | Action: Holy Wave; 2 uses | Yes
| Ring of Command | Equipment | Bonus Action: +1 Action this activation; 2 uses | Yes

## O-Classic

| Name | Kind | Effect | Created |
|---|---|---|---|
| Phase Cloak | Equipment | Passive: Phase | Yes, as One-Size-Fits-Most Bedsheet
| Amulet of Undying | Equipment | Passive: Undying (equipment trigger support to verify) | No, undying not selectable in equipments
| Displacer Gloves | Equipment | Bonus Action: Swap; unlimited | Yes
| Displacer Boots | Equipment | Bonus Action: Displace maxMove 1; unlimited | Yes
| Goblin Whistle | Equipment | Action: summon one Lying Goblin on legal adjacent Cell; 2 uses | Yes
| Knightly Boots | Equipment | Action: summon one Lying Hobby Knight on legal adjacent Cell; 2 uses | Yes
| Telekinetic Gloves | Equipment | Action: Telekinesis; unlimited | Yes
| Rogue's Cape | Equipment | Passive: Backstab | Yes
| Fireball Slippers | Equipment | Free Action: Fireball; 2 uses | Yes
| Doorbreakers | Equipment | Action: Try Open Door with 4 successes; unlimited | Yes

## Funny

| Name | Kind | Effect | Created |
|---|---|---|---|
| Banana Boots | Equipment | After each successful one-Cell Move step, attempt to place Banana Peel on departed Cell; unlimited | No, mechanic missing
| Banana Boots Deluxe | Equipment | Passive MOV +1 and Banana Trail | No, banana trail mechanic mossing
| Emergency Goat Bell | Equipment | Existing Goat Bell Action | Yes
| Coward's Medal | Equipment | Passive DEF +2, ATK -1 | Yes
| Heroic Paper Crown | Equipment | Passive ATK +2, DEF -2 | Yes
| Wrong-Way Boots | Equipment | Passive MOV +2; reverse selected Move displacement if legal, otherwise Stay | No, unclear how it should work
| Pocket Holy Wave | Immediate Loot | On reveal, Holy Wave from collector without spending Action | No, mechanic not selectable for loot card and possibly "lay down self" missing
| Emergency Lunch | Immediate Loot | Heal collector by 3 HP, capped at maximum | Yes
| Last Resort Fireball | Single Use Item | Action Fireball, consumed when used | Yes
| Self-Destruct Button | Single Use Item | Explosion damage 1 centered on user, then consume Item | No, mechanic missing for Items

## World Bending

| Name | Kind | Effect | Created |
|---|---|---|---|
| Portable Campfire | Equipment | Action: replace legal adjacent Floor Cell with Grass Campfire assembly; 2 uses | No, mechanic missing
| Architect's Pencil | Equipment | Action: change one visible Cell's Terrain to validated type; 2 uses | No, mechanic missing
| Lava Lantern | Equipment | Action: replace legal adjacent Floor Cell with ordinary Lava; 2 uses | No, mechanic missing
| Aura of Doubt | Equipment | Passive: adjacent enemy Units DEF -1 | Yes
| Hostile Encouragement | Equipment | Passive: adjacent enemy Units ATK +2 | Yes
| Remote Blessing | Equipment | Action: friendly Unit within RNG and LOS gains persistent ATK +1 until scenario end; 2 uses | No, mechanic missing
| Bag Cruncher | Equipment | Action: add one token per friendly Unit Type + Side group to current activation bag; 1 use | No, mechanic missing
| Token Thief | Equipment | Action: remove one random enemy-group token from current bag, if any; 2 uses | No, mechanic missing
| Phase Inverter | Equipment | Action: reverse Move and Action order for target Unit's next activation; 1 use | No, mechanic missing
| Rollback Ring | Equipment | Free Action: undo current activation except spending this Ability use; 1 use | No, mechanic missing
| Scramble Gloves | Equipment | Action: permute eligible numeric fields of target Unit definition; commit only if valid; 1 use | No, mechanic missing
| Quality Stamp | Equipment | Action: validate target Unit definition and display diagnostics; unlimited | No, mechanic missing
| Virus Vial | Single Use Item | Mutate one eligible character in target Unit definition value fields; apply only if valid | No, mechanic missing
| Forget-Me-Not | Equipment | Action: suppress target Unit Type from active scenario catalog until scenario end; dependent references need rules; 1 use | No, mechanic missing
| Budget Barbarian Kit | Single Use Item | Summon Lying Budget Barbarian adjacent: MOV 3, RNG 1, ATK 4, DEF 3, HP 2, Fury and Rage but no Cleave | Yes, and Budget Barbarian created

## Other candidates and open questions

- Lay down one enemy within RNG and LOS as an unlimited Action.
- Bonus Action: +1 separate Move opportunity this activation; 2 uses (not MOV +1).
- Configurable aura recipients: Friendlies, Enemies, Neutral, All.
- Persistent friendly buff within RNG and LOS, possibly represented by an Equipment instance.
- More terrain, position and activation bag manipulation; combinations of multiple mechanics.
- Holy Wave could potentially be generalized as AdjacentPostureChange(Enemies | Self, Lying), preserving current rules and ordering.
- Banana Trail should attempt placement per departed Cell. Multiple sources resolve in deterministic order against current Game State; failed placement does nothing.
- Immediate Loot requiring player choices or Attacks remains deferred; Single Use Items are an alternative.
- World Bending needs structural and runtime validation, deterministic ordering and clear snapshot/continuation semantics.
- Verify each entry against current Mechanic Catalog before implementation. No balance or feasibility status is guaranteed.

## Suggested later workflow

Refine names and precise effects; create ordinary Equipment and linked Loot Cards in Content Library; test using the dynamic All preset; collect missing parameters and mechanics for a focused Engine expansion. Keep experimental World Bending entries opt-in.
