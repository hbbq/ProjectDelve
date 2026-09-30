using ProjectDelve.Engine;

namespace ProjectDelve.ConsoleHost;

internal sealed class ConsoleRandomProvider : IRandomProvider
{
    public string DrawToken(IReadOnlyList<string> bag) => bag[Random.Shared.Next(bag.Count)];
    public AttackFace RollAttackDie() => Random.Shared.Next(6) < 3 ? AttackFace.Hit : AttackFace.Miss;
    public DefenceFace RollDefenceDie() => Random.Shared.Next(6) < 2 ? DefenceFace.Block : DefenceFace.Miss;
}
