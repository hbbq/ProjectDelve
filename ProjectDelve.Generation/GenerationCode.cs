using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ProjectDelve.Generation;

// DNG1 is an output contract. Output-affecting changes belong to a new version.
public sealed record GenerationCode(int Width = 30, int Height = 20, int Rooms = 8,
    int Connectivity = 2, int Doors = 2, int Density = 2, int Difficulty = 3,
    string Objective = "OR2", bool FogOfWar = true, ulong? Seed = null)
{
    public int RequiredHeroes => Objective == "ORA" ? 4 : Objective == "OEE" ? 0 : Objective[2] - '0';
    public GenerationCode Validate()
    {
        if (Width is < 14 or > 50 || Height is < 14 or > 50)
            throw new ArgumentException("DNG1 board dimensions must each be 14..50.");
        if (Rooms is < 2 or > 24) throw new ArgumentException("Room count must be 2..24.");
        if (Rooms > (Width - 2) / 6 * ((Height - 2) / 6))
            throw new ArgumentException("The requested rooms do not fit: each BSP region needs at least 6 × 6 Cells inside the outer rock border. Increase dimensions or reduce rooms.");
        if (Connectivity is < 0 or > 3) throw new ArgumentException("Connectivity must be 0..3 additional connections.");
        if (Doors is < 0 or > 3) throw new ArgumentException("Door density must be 0..3.");
        if (Density is < 1 or > 3) throw new ArgumentException("Monster density must be 1..3.");
        if (Difficulty is < 1 or > 5) throw new ArgumentException("Difficulty must be 1..5.");
        if (Objective is not ("OEE" or "ORA" or "OR1" or "OR2" or "OR3" or "OR4"))
            throw new ArgumentException("Objective must be OEE, ORA or OR1..OR4.");
        return Objective == "OR4" ? this with { Objective = "ORA" } : this;
    }

    public GenerationCode Complete() => Seed.HasValue ? Validate() : Validate() with
    { Seed = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8)) };

    public string Prefix
    {
        get
        {
            var c = Validate();
            return FormattableString.Invariant($"DNG1-HVM-{c.Width}x{c.Height}-R{c.Rooms}-C{c.Connectivity}-D{c.Doors}-N{c.Density}-L{c.Difficulty}-{c.Objective}-F{(c.FogOfWar ? 1 : 0)}");
        }
    }
    public override string ToString() => Prefix + (Seed is { } seed ? $"-S{seed:X16}" : "");

    public static GenerationCode Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new FormatException("Enter a DNG1 configuration prefix or complete code.");
        if (input.Length > 180) throw new FormatException("Generation code is too long.");
        var code = input.Trim();
        if (!code.StartsWith("DNG1-", StringComparison.Ordinal)) throw new FormatException("Unsupported generator version. This host supports DNG1.");
        var match = Regex.Match(code, @"\ADNG1-HVM-([1-9][0-9]?)x([1-9][0-9]?)-R([1-9][0-9]?)-C([0-3])-D([0-3])-N([1-3])-L([1-5])-(OEE|ORA|OR[1-4])-F([01])(?:-S([0-9A-Fa-f]{16}))?\z", RegexOptions.CultureInvariant);
        if (!match.Success) throw new FormatException("Expected DNG1-HVM-30x20-R8-C2-D2-N2-L3-OR2-F1, optionally followed by -S and exactly 16 hexadecimal seed digits. Fields and profile are strict and ordered.");
        int Number(int i) => int.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture);
        return new GenerationCode(Number(1), Number(2), Number(3), Number(4), Number(5), Number(6), Number(7),
            match.Groups[8].Value, Number(9) == 1,
            match.Groups[10].Success ? ulong.Parse(match.Groups[10].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null).Validate();
    }
}
