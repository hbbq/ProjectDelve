using ProjectDelve.Engine;

namespace ProjectDelve.Web;

internal sealed class SystemRandomProvider : IRandomProvider
{
    public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[Random.Shared.Next(bag.Count)];
    public AttackFace RollAttackDie() => Random.Shared.Next(6) < 3 ? AttackFace.Hit : AttackFace.Miss;
    public int RollD6() => Random.Shared.Next(1, 7);
    public DefenceFace RollDefenceDie() => Random.Shared.Next(6) < 2 ? DefenceFace.Block : DefenceFace.Miss;
}
