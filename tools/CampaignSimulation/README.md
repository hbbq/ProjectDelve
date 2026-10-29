# Campaign simulation

This optional authoring tool loads ordinary library content and submits decisions
to the existing engine. It does not change scenario content, rules, or controllers.
It is deliberately outside the solution's regression tests: mutable campaign
layouts and win rates are not test contracts.

From the repository root:

```powershell
dotnet build tools/CampaignSimulation/CampaignSimulation.csproj --artifacts-path .playwright-cli
dotnet .playwright-cli/bin/CampaignSimulation/debug/CampaignSimulation.dll campaigns/the-bell-beneath-salt/library.json the-bell-beneath-salt 8 25 all > .playwright-cli/bell-simulation.json
```

Arguments are library file, campaign ID, seed count (default 8), round limit
(default 25), policy (`all`, `aggressive`, `objective`, or `rush`), and optional
scenario ID. Seeds begin at 20261029. JSON goes to stdout; progress goes to stderr.
The campaign's player Side is assumed to be `side-1`, its enemy Side `side-2`.

All monster decisions use the established Order-aware automated provider. Hero
policies share simple healing, available attack bonuses and door opening. They
exclude empty area attacks and known friendly fire, including fallback choices:

- `aggressive` attacks and approaches enemies using ordinary automated behavior,
  then seeks the exit after all enemies have been defeated.
- `objective` prioritizes designated defeat targets and routes toward the Reach
  Area, but attacks before spending movement whenever an attack is available.
- `rush` uses the same objective policy but moves toward a Reach Area before
  attacking. It deliberately tests whether ignoring combat trivializes an exit.

Legal candidates and engine queries supply all movement and targeting legality.
Heroes already inside a Reach Area seek distinct standing cells to leave its
entrance open for their companions.
The tool reports actual engine completion, rounds, casualties, HP, remaining
enemies and timeouts. It never infers victory from a position mid-activation.
The fixed random source uses the normal attack/defence die face probabilities.

Limitations: these greedy policies do not coordinate party movement, preserve
safe lanes, time door opening, use all abilities well, or plan several activations
ahead. They use initial scenario information and are intended for campaigns
without Fog of War. A timeout is an unresolved run, not proof of impossibility;
a win is evidence of one playable trajectory, not proof of balance or enjoyment.
Compare multiple policies, inspect actual play, and document the limitations.
