using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

namespace ProjectDelve.Web;

internal sealed record ExpeditionHero(string InstanceId, string TypeId);

// A handoff declares a new ordinary Scenario. Only the explicit Party and its
// actually owned physical objects cross the boundary; no resolution state does.
internal static class ExpeditionParty
{
    internal const string SideId = HeroesVsMonsters.Heroes;
    internal const int InitialCapacity = 6;
    private const string GroupId = "expedition-originals";
    internal static IReadOnlyList<ExpeditionHero> Originals { get; } = ImmutableArray.Create(
        new ExpeditionHero("expedition-barbarian", UnitTypeIds.Barbarian),
        new ExpeditionHero("expedition-rogue", UnitTypeIds.Rogue),
        new ExpeditionHero("expedition-wizard", UnitTypeIds.Wizard),
        new ExpeditionHero("expedition-cleric", UnitTypeIds.Cleric),
        new ExpeditionHero("expedition-beastmaster", UnitTypeIds.Beastmaster));

    internal static ExpeditionPartyRoster Founders => new(SideId, InitialCapacity, [.. Originals.Select(hero => hero.InstanceId)]);

    internal static IReadOnlyList<ExpeditionHero> Members(GameState? state)
    {
        if (state?.Party is not { } party) return Originals;
        return party.MemberIds.Select(id =>
        {
            var unit = state.Units.SingleOrDefault(u => u.Id == id)
                ?? throw new ArgumentException($"Party member '{id}' is absent from the previous region.");
            return new ExpeditionHero(unit.Id, unit.TypeId);
        }).ToArray();
    }

    internal static ContentBundle ResolveContent(ContentBundle latest, GameState? previous, IReadOnlyList<ExpeditionHero> heroes)
    {
        ValidateRoster(heroes, previous);
        var selected = Definitions(latest).ToDictionary(ContentDefinitions.Identity);
        var saved = previous is null ? new Dictionary<ContentIdentity, object>() : Definitions(new ContentBundle
        {
            Units = [.. previous.Types], Equipment = [.. previous.EquipmentTypes], Items = [.. previous.ItemTypes],
            LootCards = [.. previous.LootCardDefinitions], WorldCards = [.. previous.WorldCardDefinitions],
            Terrain = previous.Physical.Board.TerrainTypes, Resources = [.. previous.ResourceDefinitions]
        }).ToDictionary(ContentDefinitions.Identity);
        var visited = new HashSet<ContentIdentity>();
        var embedded = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Dependencies(object definition)
        {
            foreach (var dependency in ContentDefinitions.Dependencies(definition))
                if (dependency.EmbeddedDefinition is { } value)
                {
                    if (embedded.Add(value)) Dependencies(value);
                }
                else Require(dependency.Target);
        }
        void Require(ContentIdentity identity)
        {
            if (!visited.Add(identity)) return;
            if (!selected.TryGetValue(identity, out var definition))
            {
                if (!saved.TryGetValue(identity, out definition))
                    throw new ArgumentException($"Expedition requires missing {identity.Family} '{identity.TypeId}'; neither current Content nor the previous region supplies it.");
                selected.Add(identity, definition);
            }
            Dependencies(definition);
        }
        foreach (var hero in heroes) Require(new(ContentFamily.Unit, hero.TypeId));
        if (previous is not null)
            foreach (var obj in Inventories(previous, heroes).SelectMany(entry => entry.Inventory))
            {
                if (obj.EquipmentTypeId is { } equipment) Require(new(ContentFamily.Equipment, equipment));
                if (obj.ItemTypeId is { } item) Require(new(ContentFamily.Item, item));
                if (obj.LootCardId is { } cardId)
                {
                    Require(new(ContentFamily.LootCard, cardId));
                    var card = (LootCardDefinition)selected[new(ContentFamily.LootCard, cardId)];
                    if (card.EquipmentTypeId != obj.EquipmentTypeId || card.ItemTypeId != obj.ItemTypeId)
                        throw new ArgumentException($"Expedition cannot refresh object '{obj.Id}': current Loot Card '{cardId}' grants a different object Type. Restore its original reward or resolve its provenance before continuing; retained gear was not discarded.");
                }
            }
        // Current declarations remain whole. Missing dependencies may use a saved
        // definition, while a present but incompatible declaration never falls back.
        foreach (var identity in selected.Keys.ToArray()) Require(identity);
        var values = selected.Values.ToArray();
        var effective = latest with
        {
            Units = values.OfType<UnitType>().ToImmutableArray(), Equipment = values.OfType<EquipmentDefinition>().ToImmutableArray(),
            Items = values.OfType<ItemDefinition>().ToImmutableArray(), LootCards = values.OfType<LootCardDefinition>().ToImmutableArray(),
            LootPresets = values.OfType<LootPreset>().ToImmutableArray(), WorldCards = values.OfType<WorldCardDefinition>().ToImmutableArray(),
            Terrain = values.OfType<TerrainDefinition>().ToImmutableArray(), Assemblies = values.OfType<TerrainAssemblyDefinition>().ToImmutableArray(),
            FixedComponents = values.OfType<FixedComponentDefinition>().ToImmutableArray(), Resources = values.OfType<ResourceType>().ToImmutableArray()
        };
        return ContentComposition.Compose([effective]).Definitions;
    }

    internal static ScenarioDefinition Prepare(ScenarioDefinition terrain, ContentBundle content, ObjectiveArea exitArea,
        IReadOnlyList<ExpeditionHero> heroes, GameState? previous)
    {
        ValidateRoster(heroes, previous);
        var setup = terrain.Setup ?? throw new ArgumentException("Expedition requires a PlayReady deployment declaration.");
        var existing = GameEngine.CreateGame(terrain, content);
        var validation = PlayReadyScenarioSetup.Validate(terrain, existing);
        if (!validation.Valid)
            throw new ArgumentException("Expedition PlayReady setup is invalid: " + string.Join("; ", validation.Diagnostics.Select(d => d.Path + ": " + d.Message)));
        if (setup.Deployment.Capacity < heroes.Count || setup.Deployment.PlacementOrder.Count < heroes.Count)
            throw new ArgumentException("Expedition deployment does not have capacity for every Party member.");
        if (terrain.Units.Any(u => u.SideId == SideId) || terrain.Agency.Any(a => a.SideId == SideId) ||
            (terrain.NeutralSides ?? []).Contains(SideId))
            throw new ArgumentException("Generated region already uses the reserved Expedition Side.");
        var reserved = existing.Units.Select(u => u.Id).Concat(existing.Physical.Board.Machines.Select(m => m.Id))
            .Concat(terrain.Units.Select(u => u.GroupId).OfType<string>())
            .Concat(terrain.Agency.Select(a => a.GroupId).OfType<string>()).ToHashSet(StringComparer.Ordinal);
        if (reserved.Contains(GroupId) || heroes.Any(h => reserved.Contains(h.InstanceId)))
            throw new ArgumentException("Generated region conflicts with reserved Party instance or Group identities.");
        var inventory = previous is null ? new Dictionary<string, List<OwnedObject>>() : Inventories(previous, heroes).ToDictionary(e => e.Hero.InstanceId, e => e.Inventory);
        var json = ContentPackageJson.CreateOptions();
        foreach (var hero in heroes)
        {
            var current = content.Units.Single(t => t.Id == hero.TypeId);
            if (current.Footprint != Footprint.OneByOne)
                throw new ArgumentException($"Expedition cannot deploy refreshed Hero '{hero.TypeId}': Phase 1 requires a 1 × 1 footprint.");
            var local = existing.Types.SingleOrDefault(t => t.Id == hero.TypeId);
            if (local is not null && JsonSerializer.Serialize(local, json) != JsonSerializer.Serialize(current, json))
                throw new ArgumentException($"Generated region conflicts with refreshed Hero definition '{hero.TypeId}'.");
            var gear = inventory.GetValueOrDefault(hero.InstanceId) ?? [];
            // Refresh definitions at the boundary, retaining physical Levels. An
            // incompatible refresh fails before the successor can be published.
            current.AtLevel(previous?.Units.Single(u => u.Id == hero.InstanceId).Level ?? 1);
            foreach (var obj in gear.Where(o => o.EquipmentTypeId is not null))
                content.Equipment.Single(t => t.Id == obj.EquipmentTypeId).AtLevel(obj.Level);
            if (gear.Any(o => o.EquipmentTypeId is not null && !current.CanHaveEquipment || o.ItemTypeId is not null && !current.CanHaveItems))
                throw new ArgumentException($"Expedition cannot refresh Hero '{hero.TypeId}': current ownership capabilities reject retained gear.");
        }
        var party = heroes.Select((hero, index) => new UnitPlacement(hero.TypeId, SideId, setup.Deployment.PlacementOrder[index],
            UnitId: hero.InstanceId, GroupId: GroupId)
        {
            Level = previous?.Units.Single(u => u.Id == hero.InstanceId).Level ?? 1,
            Inventory = inventory.GetValueOrDefault(hero.InstanceId) ?? []
        }).ToList();
        var regionalSides = existing.Objectives.Select(o => o.SideId).Append(SideId).ToHashSet(StringComparer.Ordinal);
        // Only the bank crosses the seam. A Side can retain Credits even in a
        // region with no deployment or controller for that Side.
        var balances = previous is null ? terrain.SideCredits : previous.SideCredits;
        var absentSides = (balances?.Keys.AsEnumerable() ?? []).Except(regionalSides, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var sides = regionalSides.Concat(absentSides);
        var prepared = terrain with
        {
            Party = new(SideId, previous?.Party?.Capacity ?? InitialCapacity, [.. heroes.Select(h => h.InstanceId)]),
            SideCredits = balances is { Count: > 0 } ? new(balances, StringComparer.Ordinal) : null,
            AdditionalSideIds = absentSides.Count == 0 ? terrain.AdditionalSideIds : [.. terrain.AdditionalSideIds ?? [], .. absentSides],
            // Ownership can be assigned only once the Expedition Side exists. The
            // generation pipeline validates its detached terrain before adding Heroes.
            Machines = terrain.Machines?.Select(machine => machine.Id == ExpeditionRegionGeometry.ArrivalStationId
                ? machine with { OwnerSide = SideId } : machine).ToList(),
            UnitTypeIds = terrain.UnitTypeIds.Concat(heroes.Select(h => h.TypeId)).Distinct(StringComparer.Ordinal).ToList(),
            Units = [.. terrain.Units, .. party],
            Agency = [.. terrain.Agency, .. heroes.Select(h => h.TypeId).Distinct(StringComparer.Ordinal)
                .Select(type => new AgencyAssignment(type, SideId, ControllerKind.Human, GroupId))],
            FogOfWarSides = (terrain.FogOfWarSides ?? []).Append(SideId).Distinct(StringComparer.Ordinal).ToList(),
            Objectives = sides.Select(side => side == SideId
                ? new SideObjective(side, ScenarioObjective.ReachArea) { Count = 2, Area = exitArea, Selectors = [UnitSelector.Party(side)] }
                : new SideObjective(side, ScenarioObjective.NoObjective)).ToList()
        };
        if (prepared.Machines?.Any(m => m.Id == ExpeditionRegionGeometry.ArrivalStationId) != true) return prepared;
        var state = GameEngine.CreateGame(prepared, content);
        if (heroes.Any(hero => GameEngine.CanOperateMachineWithAction(state, hero.InstanceId,
                ExpeditionRegionGeometry.ArrivalStationId))) return prepared;
        // Refreshed definitions may move the usable Operate source to another member.
        // Try only complete Engine-validated placements, keeping every physical ID.
        var first = party[0];
        foreach (var hero in party.Skip(1))
        {
            var candidate = prepared with { Units = prepared.Units.Select(unit =>
                unit.UnitId == hero.UnitId ? unit with { Anchor = first.Anchor } :
                unit.UnitId == first.UnitId ? unit with { Anchor = hero.Anchor } : unit).ToList() };
            GameState candidateState;
            try { candidateState = GameEngine.CreateGame(candidate, content); }
            catch (ArgumentException) { continue; }
            if (GameEngine.CanOperateMachineWithAction(candidateState, hero.UnitId!, ExpeditionRegionGeometry.ArrivalStationId))
                return candidate;
        }
        throw new ArgumentException("Expedition refreshed Party cannot immediately Operate the arrival Equipment Station from a legal deployment placement.");
    }

    private static void ValidateRoster(IReadOnlyList<ExpeditionHero> heroes, GameState? previous)
    {
        if (heroes.Count == 0 || heroes.Count > (previous?.Party?.Capacity ?? InitialCapacity) ||
            heroes.Any(h => string.IsNullOrWhiteSpace(h.InstanceId) || string.IsNullOrWhiteSpace(h.TypeId)) ||
            heroes.Select(h => h.InstanceId).Distinct(StringComparer.Ordinal).Count() != heroes.Count)
            throw new ArgumentException("Expedition requires distinct Party instance identities within its configured capacity.");
        if (previous is null) return;
        if (previous.Party is { } party && !party.MemberIds.SequenceEqual(heroes.Select(h => h.InstanceId)))
            throw new ArgumentException("The handoff roster must preserve every Party member in its recorded order.");
        foreach (var hero in heroes)
            if (previous.Units.SingleOrDefault(u => u.Id == hero.InstanceId) is not { } unit ||
                unit.TypeId != hero.TypeId || unit.SideId != SideId)
                throw new ArgumentException($"Previous region does not contain Party member '{hero.InstanceId}' with its recorded Type and Side.");
    }

    private static IEnumerable<(ExpeditionHero Hero, List<OwnedObject> Inventory)> Inventories(GameState previous, IReadOnlyList<ExpeditionHero> heroes)
    {
        foreach (var hero in heroes)
        {
            var unit = previous.Units.Single(u => u.Id == hero.InstanceId);
            yield return (hero, unit.Inventory.Select(obj => obj with
            {
                EquipmentTypeId = previous.EquipmentTypeId(obj), ItemTypeId = previous.ItemTypeId(obj),
                AbilityUseCounts = ImmutableDictionary<string, int>.Empty
            }).ToList());
        }
    }

    private static IEnumerable<object> Definitions(ContentBundle content) => content.Units.Cast<object>().Concat(content.Equipment)
        .Concat(content.Items).Concat(content.LootCards).Concat(content.LootPresets).Concat(content.WorldCards).Concat(content.Terrain)
        .Concat(content.Assemblies).Concat(content.FixedComponents).Concat(content.Resources);
}
