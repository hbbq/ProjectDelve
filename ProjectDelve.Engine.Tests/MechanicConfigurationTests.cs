using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class MechanicConfigurationTests
{
    [Fact]
    public void SerializedLocalConfigurationsKeepCanonicalParametersAndUsageSeparate()
    {
        Assert.Equal(new Fury(), JsonSerializer.Deserialize<Fury>("{}"));
        Assert.Equal(new Backstab(), JsonSerializer.Deserialize<Backstab>("{}"));
        Assert.Equal(new Cleave(), JsonSerializer.Deserialize<Cleave>("{}"));
        var heal = UnitType.Cleric().Ability<HealMechanic>()!;
        Assert.Equal(new HealMechanic(2), heal.Mechanic);
        Assert.Equal(2, heal.Usage.MaxUses);
        Assert.Equal(new Fury(), UnitType.Barbarian().Mechanic<Fury>());
        Assert.Equal(new Cleave(), UnitType.Barbarian().Mechanic<Cleave>());
        Assert.Equal(new Backstab(), UnitType.Rogue().Mechanic<Backstab>());
        var restored = JsonSerializer.Deserialize<UnitType>(JsonSerializer.Serialize(UnitType.Barbarian()))!;
        Assert.Equal(2, restored.Ability<Cleave>()!.Usage.MaxUses);
        Assert.Equal(new Cleave(), restored.Mechanic<Cleave>());
    }

    [Theory]
    [InlineData("heal", 0)]
    [InlineData("heal", -1)]
    [InlineData("cleave-trigger", 0)]
    [InlineData("cleave-trigger", -1)]
    [InlineData("cleave-damage", 0)]
    [InlineData("cleave-damage", -1)]
    [InlineData("fury-threshold", 0)]
    [InlineData("fury-threshold", -1)]
    public void NonpositiveHealingDamageAndEnemyThresholdsAreRejected(string parameter, int value)
    {
        AbilityMechanic mechanic = parameter switch
        {
            "heal" => new HealMechanic(value),
            "cleave-trigger" => new Cleave { TriggerDamage = value },
            "cleave-damage" => new Cleave { Damage = value },
            "fury-threshold" => new Fury { AdjacentEnemyThreshold = value },
            _ => throw new ArgumentOutOfRangeException(nameof(parameter))
        };
        var timing = mechanic is Cleave ? AbilityTiming.AfterAttack : mechanic is Fury ? AbilityTiming.Passive : AbilityTiming.Action;
        var type = new UnitType("configured", 0, 0, 0, 0, 1)
        {
            LocalAbilities = [new("configured", "Configured", timing, mechanic) { Usage = new(mechanic is Fury ? null : 2) }]
        };
        var state = new GameState
        {
            Physical = new(new Board(1, 1, []), [new("actor", new(0, 0))]),
            Types = [type], Units = [type.CreateUnit("actor", "side")]
        };
        Assert.Throws<ArgumentException>(() => TestGame.StartRound(state, new Dice()));
    }

    private sealed class Dice : IRandomProvider
    {
        public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[0];
        public AttackFace RollAttackDie() => throw new InvalidOperationException();
        public DefenceFace RollDefenceDie() => throw new InvalidOperationException();
        public int RollD6() => throw new InvalidOperationException();
    }
}
