using System.Text.Json;
using Xunit;

namespace ProjectDelve.Engine.Tests;

public sealed class MechanicConfigurationTests
{
    [Fact]
    public void ExistingConstructorsAndSerializedConfigurationsKeepCanonicalDefaults()
    {
        Assert.Equal(3, new Heal(3).MaxUses);
        Assert.Equal(2, new Heal(3).Amount);
        Assert.Equal(new Heal(3), JsonSerializer.Deserialize<Heal>("{\"MaxUses\":3}"));
        Assert.Equal(new Cleave(3), JsonSerializer.Deserialize<Cleave>("{\"MaxUses\":3}"));
        Assert.Equal(new Fury(), JsonSerializer.Deserialize<Fury>("{}"));
        Assert.Equal(new Backstab(), JsonSerializer.Deserialize<Backstab>("{}"));
        Assert.Equal(new Heal(2), UnitType.Cleric().Heal);
        Assert.Equal(new Fury(), UnitType.Barbarian().Fury);
        Assert.Equal(new Cleave(2), UnitType.Barbarian().Cleave);
        Assert.Equal(new Backstab(), UnitType.Rogue().Backstab);
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
        var type = new UnitType("configured", 0, 0, 0, 0, 1);
        type = parameter switch
        {
            "heal" => type with { Heal = new() { Amount = value } },
            "cleave-trigger" => type with { Cleave = new() { TriggerDamage = value } },
            "cleave-damage" => type with { Cleave = new() { Damage = value } },
            "fury-threshold" => type with { Fury = new() { AdjacentEnemyThreshold = value } },
            _ => throw new ArgumentOutOfRangeException(nameof(parameter))
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
