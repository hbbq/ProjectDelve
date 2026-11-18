using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// SplitMix64. The version-1 checkpoint stores the exact state before the next draw.
internal sealed class ExpeditionRandom(ulong state) : IRandomProvider
{
    internal ulong State { get; private set; } = state;

    public ActivationToken DrawToken(IReadOnlyList<ActivationToken> bag) => bag[Next(bag.Count)];
    public AttackFace RollAttackDie() => Next(6) < 3 ? AttackFace.Hit : AttackFace.Miss;
    public DefenceFace RollDefenceDie() => Next(6) < 2 ? DefenceFace.Block : DefenceFace.Miss;
    public int RollD6() => 1 + Next(6);
    public int RollD8() => 1 + Next(8);
    public int WorldShuffleIndex(int exclusiveMax) => Next(exclusiveMax);
    // Keep IRandomProvider's physical d6 Loot sampling. The d6 source above is checkpointed.

    private int Next(int bound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bound);
        var threshold = unchecked(0UL - (ulong)bound) % (ulong)bound;
        ulong value;
        do
        {
            unchecked
            {
                State += 0x9E3779B97F4A7C15UL;
                value = (State ^ (State >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= value >> 31;
            }
        } while (value < threshold);
        return (int)(value % (ulong)bound);
    }
}
