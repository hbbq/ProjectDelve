# The Bell Beneath Salt

An experimental five-scenario Project Delve campaign. At low tide, a bell sounds beneath an abandoned saltworks. Each expedition follows its echo further inland and down into the buried tower. The bell, rising tide and choir are narrative framing: they introduce no timers, interactions or extra rules.

## Play

The campaign is installed in this checkout's local library. Open **Home → The Bell Beneath Salt → Choose scenario**, then any **Play scenario** button. Every entry is independently available. Blue Heroes are Human-controlled and Red enemies Automated. Every start restores the authored party, full HP and normal ability uses. There are no unlocks, carryover, World Cards or Fog of War.

Open **Tools · Objectives & Orders** to inspect the authoritative mission. Scenarios I and IV require all starting Heroes in the marked Area simultaneously; losing even one Hero ends those missions in defeat. Scenarios III and V require only the named Shaman targets, not their summons or guards. In II, III and V, Red wins by eliminating the Heroes.

`library.json` is the portable, version-1 campaign library source. The installed, editable copy lives in the application's ignored `App_Data/library`, separate from bundled content. To install on another checkout with this artwork and a running local host, from the repository root:

```powershell
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File campaigns/the-bell-beneath-salt/install.ps1
```

The default host is `http://127.0.0.1:5080`. Use `-BaseUrl http://127.0.0.1:5081` for an isolated QA host. Repeat installation preserves local edits. Explicit `-Update` replaces the matching campaign/scenarios from this portable source using library revision tokens. Unrelated entries are preserved. The installer prints the resulting campaign link; library-generated IDs differ between machines. Individual scenarios can also be opened in Designer and exported as ordinary DELVE1.

## Expeditions and design intent

### I · The Drowned Quay — crossing together

*The landing stones vanish into green water. Beyond them, a dry stair climbs toward the first lantern. The two scouts must reach it together.*

Barbarian and Rogue face three Grunts on a 10×8 board. Reach the eastern two-column strip with both Heroes alive. Two broad crossings offer a direct northern approach and a longer southern approach. Water blocks movement but not sight. Use the Barbarian to engage while the Rogue preserves room to move and exploit Backstab; dashing ahead exposes the weaker Hero without achieving the shared Objective.

Intended lesson: decide when to fight and when to cross, rather than race one fast figure to an exit. Known weakness: clearing the three Grunts first is usually the safer solution, and the final walk then has little tension. This is deliberately the short introductory mission; there is no invented turn limit.

### II · The Lantern Gallery — opening a firing lane

*The saltworkers once hung their lamps behind stone shutters. Now those dark apertures watch the gallery. Take the room before descending to the well.*

Barbarian, Rogue and Wizard must eliminate five enemies on an 11×9 board. Three Skeleton Archers Hold Position behind a wall with a central opening and two Closed Doors. Two Grunts contest the approach. Staggered stone buttresses provide cover; the table blocks movement but not LOS. The Wizard can contest ranged lanes while a faster Hero takes a door flank. Opening a Door is permanent exposure, not a temporary peek.

Intended lesson: choose which firing lane to open, and coordinate ranged pressure with a flank. Iteration moved the southern buttress west so the southern Door has a usable approach. Known weakness: cautious players can lure out the Grunts and then dismantle held Archers one at a time; there is no reinforcement clock. Losing one Hero does not automatically lose this mission.

### III · The Choir Well — pursue the source

*Below the gallery, a voice answers the bell. Each answer brings another shape out of the dark. Find the singer; do not fight the whole echo.*

Barbarian, Rogue and Wizard enter a 12×10 ring of stone around an open well. Defeat the Shaman with Unit ID `cantor`. A Troll and two Zombies impede the pursuit; summoned Goblins are not Objective targets. The well blocks walking but allows ranged fire. Northern and southern routes let the party close from different directions rather than chase the fleeing Shaman in a single file.

Intended lesson: distinguish the Objective from nearby distractions. Telekinesis can interrupt a standing enemy, including the Troll; it does not move figures. A Troll is not defeated merely by triggering Undying. Known weakness: informed target-focused play can finish quickly, while a player who insists on clearing every summon can make the same map much harder. That contrast is intentional, but means difficulty depends heavily on recognizing the Objective.

### IV · The Parting Tide — regroup before extraction

*A collapsed passage has divided the company. Across the cistern, the northern landing is still dry. Nobody is to be left below.*

All four Heroes begin in two separated pairs on a 12×11 board. Bring all four into the northern 4×2 landing simultaneously. Two held Skeleton Archers overlook the approaches, a Troll guards the landing, and two Bomb Imps pressure the lower rooms. The Wizard sets the pace; decide whether to reunite in the south or advance as pairs around the cistern. Cleric Aura and Heal reward contact, while Bomb Imp Explosion punishes crowding during a kill.

Intended lesson: coordinate a whole party under a no-casualty Objective. Iteration removed a rear Zombie so regrouping in the south is a credible option. Known weakness: this is the sharpest difficulty step; adverse activation order or an exposed Wizard can end the mission early. It is not a speed puzzle, and eliminating immediate threats before approaching the landing is valid.

### V · The Bell Beneath Salt — two voices, one company

*The stair ends beneath the tower. Two cantors lie prostrate on opposite sides of the chamber. When they rise, the buried choir will answer again. Silence both voices.*

All four Heroes enter a 13×12 chamber. Defeat both Shamans, IDs `west-cantor` and `east-cantor`; guards and Goblins need not be cleared. The central stone mass splits the approach, while upper and lower connecting lanes allow regrouping. A held Archer covers the east wing, Bomb Imps make clustered fighting costly, and a Troll occupies the upper crossing. Both Shamans start Lying: their first activation stands them up and ends, buying approach time using ordinary Posture rules. Iteration removed the western Archer to create a more open first approach and reduce ranged attrition.

Intended lesson: balance concentrated force against leaving the other summoner active. Splitting preserves tempo but divides Cleric support; concentrating is safer locally but gives the other wing time to accumulate Goblins. Known weakness: the finale remains deliberately hard and sensitive to activation order. Greedy target chasing is poor at protecting the Wizard and Cleric. Automated victories establish possible winning play, not reliable human balance.

## Physical setup and scope

All content uses existing canonical Unit Types, full-health initial figures, terrain, Doors, Orders and Objectives. No custom abilities or Engine/rules changes. Coordinates in JSON are zero-based, x east and y south. Default terrain is Stone Floor; `cells` override it. A `Right` edge separates `(x,y)` from `(x+1,y)`. Every listed Unit occupies its `anchor`. Place the finale's Shaman figures Lying and distinguish them by their printed IDs. Keep the published Hold Position Orders visible beside the relevant Skeleton Archer group. Narrative titles do not change the canonical Unit cards.

The shared [cover](../../ProjectDelve.Web/wwwroot/art/campaigns/the-bell-beneath-salt.png) deliberately reveals no map, enemies or ending. [Artwork provenance and prompt](artwork.md) describe its generation.

## Evidence and limitations

See [playtest results](PLAYTEST.md) and the raw simulation results for policies, seeds, iterations and failure modes. The simulation tool is an authoring aid and is not a fun score or a regression test for the exact layouts. The automated catalog test only checks loading, engine validity, reference resolution and JSON/DELVE1 roundtrips, so future editing of this campaign does not freeze its design.

Edge inspection covered the actual campaign selection flow and all five Enhanced 2D maps, including both Door approaches, exit outlines and initial formations. Tactical review found all passable cells connected and sufficient simultaneous exit capacity. Further human sessions are needed to judge pacing, repetition and the difficulty jump in the final two missions.

Saved review images (map spoilers): [selection](review/campaign.png), [I](review/map-1.png), [II](review/map-2.png), [III](review/map-3.png), [IV](review/map-4.png), [V](review/map-5.png).
