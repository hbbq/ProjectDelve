using ProjectDelve.Engine;

namespace ProjectDelve.Web.Tests;

// Test-owned content; behavioral assertions do not depend on a shipped scenario.
internal static class OwnedAbilityWebFixture
{
    internal static ScenarioDefinition Definition(bool fog = false) => new()
    {
        Board = new() { Width = 20, Height = 4 },
        UnitTypeIds = ["woven-keeper", "injured-friend", "silent-target", "concealed-keeper"],
        UnitTypes =
        [
            new("woven-keeper", 2, 1, 0, 1, 3)
            {
                DisplayName = "Woven Keeper", Unique = true, Actions = UnitAction.None,
                CanHaveEquipment = true, CanHaveItems = true,
                LocalAbilities =
                [
                    new("pulse", "Silver Stitch", AbilityTiming.Action, new HealMechanic(2)) { Usage = new(2) },
                    new("shade", "Woven Shade", AbilityTiming.Passive, new AdjacentModifiersMechanic([new(Stat.Def, 2)]))
                ]
            },
            new("injured-friend", 0, 0, 0, 1, 5) { DisplayName = "Injured Friend", Unique = true, Actions = UnitAction.None },
            new("silent-target", 0, 0, 0, 0, 1) { DisplayName = "Silent Target", Actions = UnitAction.None },
            new("concealed-keeper", 0, 0, 0, 0, 1)
            {
                DisplayName = "Concealed Keeper", Actions = UnitAction.None, CanHaveEquipment = true, CanHaveItems = true,
                LocalAbilities = [new("pulse", "Hidden Native Whisper", AbilityTiming.Action, new HolyWaveMechanic())]
            }
        ],
        EquipmentTypes =
        [
            new("hollow-lantern", "Hollow Lantern")
            { LocalAbilities = [new("pulse", "Lantern Hush", AbilityTiming.Action, new HolyWaveMechanic())] },
            new("concealed-lantern", "Concealed Lantern")
            { LocalAbilities = [new("pulse", "Hidden Lantern Whisper", AbilityTiming.Action, new HolyWaveMechanic())] }
        ],
        ItemTypes =
        [
            new("folded-remedy", "Folded Remedy")
            { LocalAbilities = [new("pulse", "Paper Stitch", AbilityTiming.Action, new HealMechanic(3))] },
            new("spare-moment", "Spare Moment")
            { LocalAbilities = [new("pulse", "Borrow a Moment", AbilityTiming.BonusAction, new ExtraActionsMechanic(1))] },
            new("concealed-paper", "Concealed Paper")
            { LocalAbilities = [new("pulse", "Hidden Paper Whisper", AbilityTiming.Action, new HolyWaveMechanic())] }
        ],
        Units =
        [
            new("woven-keeper", "blue", new(1, 1), UnitId: "actor")
            {
                Inventory = [new("lantern-one", "hollow-lantern"),
                    new("remedy-one") { ItemTypeId = "folded-remedy" }, new("remedy-two") { ItemTypeId = "folded-remedy" },
                    new("moment-one") { ItemTypeId = "spare-moment" }, new("moment-two") { ItemTypeId = "spare-moment" }]
            },
            new("injured-friend", "blue", new(1, 2), InitialHp: 1, UnitId: "friend"),
            new("silent-target", "red", new(2, 1), UnitId: "target"),
            new("concealed-keeper", "red", new(19, 1), UnitId: "hidden")
            {
                Inventory = [new("hidden-lantern", "concealed-lantern"), new("hidden-paper") { ItemTypeId = "concealed-paper" }]
            }
        ],
        Agency =
        [
            new("woven-keeper", "blue", ControllerKind.Human),
            new("injured-friend", "blue", ControllerKind.Human),
            new("silent-target", "red", ControllerKind.Human),
            new("concealed-keeper", "red", ControllerKind.Human)
        ],
        Objectives = [new("blue", ScenarioObjective.NoObjective), new("red", ScenarioObjective.NoObjective)],
        FogOfWarSides = fog ? ["blue"] : [],
        GroundObjects = [new(new("hidden-ground-paper") { ItemTypeId = "concealed-paper" }, new(19, 2))],
        Loot = LootSettings.Empty
    };

    internal static GameState State(bool fog = false) => GameEngine.CreateGame(Definition(fog));
}
