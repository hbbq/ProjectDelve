# JSON content packages

Canonical providers feed the same typed `ContentBundle` and validated effective
catalog described in [CONTENT_COMPOSITION.md](CONTENT_COMPOSITION.md). Packages
contain declarations, never executable hooks. Gameplay reads its captured game
snapshot, never package files.

## Format 1

```json
{
  "formatVersion": 1,
  "id": "example",
  "title": "Example content",
  "description": "Optional authoring description",
  "dependencies": [],
  "definitions": {
    "equipmentTypes": [{
      "id": "example-bell",
      "name": "Example Bell",
      "localAbilities": [{
        "key": "encourage",
        "name": "Encourage",
        "timing": "Action",
        "usage": { "maxUses": 2 },
        "mechanic": { "kind": "standAdjacentFriendly" }
      }]
    }],
    "lootCardDefinitions": [{
      "id": "example-gift",
      "name": "Example Gift",
      "equipmentTypeId": "example-bell"
    }]
  }
}
```

Definition arrays: `unitTypes`, `equipmentTypes`, `itemTypes`,
`lootCardDefinitions`, `lootPresets`, `worldCardDefinitions`, `defaultWorldDeck`,
`terrainTypes`, `terrainAssemblies`, `fixedComponentTypes`, `resources`.
Omitted arrays are empty. These use existing typed models and mechanic JSON
discriminators; see [ABILITY_CONTENT.md](ABILITY_CONTENT.md),
[MECHANIC_CATALOG.md](MECHANIC_CATALOG.md) and
[CONTENT_VALIDATION.md](CONTENT_VALIDATION.md). Export existing definitions for
complete serialized examples.

Dependencies name other supplied package IDs, including `canonical-csharp` for
the C# provider. Direct cross-provider references require declaring their owner.
Declarations do not fetch files or pin versions. All declared packages must be
supplied; self-dependencies and duplicate declarations are invalid. Mutually
dependent packages and mutually summoning Units are legal. Terrain composition
restrictions still apply. Dynamic Loot `All` resolves against the assembled
catalog without requiring a fixed dependency on every available card.

Unsupported versions, unknown/duplicate JSON fields, duplicate package IDs,
duplicate family/Type IDs (even identical payloads), invalid parameters and
unresolved references fail validation. Moving an identity to JSON requires
removing its original C# registration in the same change. Scenario-local Unit
overrides remain a separate composition operation.

## APIs and commands

`ContentPackageJson.FromJson` / `ToJson` read and deterministically write typed
packages. Serialization preserves arrays, Ability order, parameters and deck
composition, using camel case, string enums and LF line endings.
`ContentPackageCatalog.Compose(documents, optionalCSharpBundle)` validates all
providers together without a scenario or GameState. It returns `EffectiveContent`
with the typed dependency graph. Documents carry source paths outside gameplay
definitions. Definition errors use existing `ContentDiagnostic` owner, field,
source and dependency identities. JSON/header exceptions expose their document
through `ContentPackageJson.SourceOf`; `ScenarioValidationContext.PathOf` supplies
a field path where available.

`ReadDirectory` reads recursive `*.json` files in ordinal relative-path order;
composition orders packages by ordinal ID. Missing directories fail. An existing
empty directory explicitly selects C# content alone. `LoadCanonical(directory)`
supplies the C# roster bundle plus its packages.

`ContentPackageExport.Selected(id, catalog, identities)` exports selections and
their typed transitive closure, preserving source declaration order. With
`includeDependencies: false`, callers supply external package dependencies and
validate the combined catalog. Export never renames identities. Embedded Terrain
composition stays embedded in resolved Terrain exports.

From the repository root:

```powershell
dotnet run --project ProjectDelve.Console -- content validate ProjectDelve.Web/Content/Canonical
dotnet run --project ProjectDelve.Console -- content validate my-packages --with-csharp
dotnet run --project ProjectDelve.Console -- content load --directory ProjectDelve.Web/Content/Canonical
dotnet run --project ProjectDelve.Console -- content export --directory ProjectDelve.Web/Content/Canonical --id exported-example --select LootCard:json-example-encouragement-gift --output example.json
dotnet run --project ProjectDelve.Console -- content validate example.json
```

Validation exits nonzero with diagnostics on failure. Validate a directory to
check cross-package references together. A closure export is portable but cannot
be loaded beside the original identities. Thin export also accepts
`--without-dependencies --depends package-a,canonical-csharp`.

## Web host and example

The host loads `Content/Canonical` beneath its content root at startup. Override
with `--CanonicalContent:Directory <directory>` or equivalent .NET configuration.
Packages are copied into build/publish output. Invalid content fails startup;
there is no hot reload. One host-owned canonical bundle serves Content Browser,
Designer, scenario import, Playtest and new-game creation. Editable drafts retain
references; game creation captures definitions and dynamic deck composition.

`json-provider-example.json` adds clearly labelled JSON Example Encouragement
Bell Equipment and its Loot Card. It uses the existing Stand Adjacent Friendly
mechanic and physical two-use counter without C# roster changes. Browser and
Designer show both as canonical. Fixed Loot presets and generator selection
pools remain unchanged; dynamic `All` includes the card. Direct Engine callers
must explicitly load/pass a provider catalog: `ContentBundle.Canonical()` still
means the existing C# source alone.

## Remaining boundaries

No persistent library, revision pinning, remote registry, hooks, inheritance,
asset management or promotion workflow is introduced. Existing C# rosters are
not migrated. Resources support named production lookup and existing embedded
payloads; same-ID inline Resource/Machine variants retain their payloads and need
an independent reference/variant model before becoming general library
definitions. Machines and Cell Markers gain no new authoring family. Package
provenance stays outside gameplay definitions and runtime saves.
