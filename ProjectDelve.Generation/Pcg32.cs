using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ProjectDelve.Generation;

// PCG-XSH-RR 64/32, multiplier 6364136223846793005; reference two-step seeding.
public sealed class Pcg32
{
    private ulong state;
    private readonly ulong increment;
    public Pcg32(ulong initialState, ulong sequence)
    {
        increment = unchecked((sequence << 1) | 1);
        NextUInt(); state = unchecked(state + initialState); NextUInt();
    }
    public uint NextUInt()
    {
        var old = state;
        state = unchecked(old * 6364136223846793005UL + increment);
        var shifted = (uint)(((old >> 18) ^ old) >> 27);
        var rotation = (int)(old >> 59);
        return (shifted >> rotation) | (shifted << ((-rotation) & 31));
    }
    public int Next(int bound)
    {
        if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound));
        var threshold = unchecked(0u - (uint)bound) % (uint)bound;
        uint value;
        do { value = NextUInt(); } while (value < threshold);
        return (int)(value % (uint)bound);
    }
    public void Shuffle<T>(IList<T> values)
    {
        for (var i = values.Count - 1; i > 0; i--) { var j = Next(i + 1); (values[i], values[j]) = (values[j], values[i]); }
    }
    public static Pcg32 Stream(ulong seed, string domain, int attempt = 0)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"DNG1:{seed:X16}:{domain}:{attempt}")));
        return new(BinaryPrimitives.ReadUInt64LittleEndian(hash), BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(8)));
    }
}
