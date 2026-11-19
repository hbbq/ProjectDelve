namespace ProjectDelve.Engine;

// A physical ground pile, separate from Resources, Loot cards and owned objects.
public sealed record CreditsPile(Cell Position, int Amount);

internal static class CreditsRules
{
    internal static (Dictionary<string, int> Balances, List<CreditsPile> Piles) Create(
        ScenarioDefinition definition, HashSet<string> sides)
    {
        Dictionary<string, int> balances = new(StringComparer.Ordinal);
        foreach (var (side, amount) in definition.SideCredits ?? [])
        {
            if (string.IsNullOrWhiteSpace(side) || !sides.Contains(side) || amount < 0)
                throw ScenarioValidationContext.At(new ArgumentException("Credits balances require known Sides and nonnegative amounts."), "sideCredits");
            balances[side] = amount;
        }
        return (balances, MergePiles(definition.CreditsPiles ?? [], balances, definition.Board.Width, definition.Board.Height));
    }

    internal static List<CreditsPile> MergePiles(IReadOnlyList<CreditsPile> entries,
        Dictionary<string, int> balances, int width, int height)
    {
        Dictionary<Cell, long> merged = [];
        for (var index = 0; index < entries.Count; index++)
        {
            var pile = entries[index];
            if (pile is null || pile.Position is not { } cell || pile.Amount <= 0 ||
                cell.X < 0 || cell.Y < 0 || cell.X >= width || cell.Y >= height)
                throw ScenarioValidationContext.At(new ArgumentException("Credits piles require a positive amount and an in-bounds Cell."), $"creditsPiles[{index}]");
            var total = merged.GetValueOrDefault(cell) + pile.Amount;
            if (total > int.MaxValue)
                throw ScenarioValidationContext.At(new ArgumentException("Merged Credits pile exceeds the supported balance range."), $"creditsPiles[{index}]");
            merged[cell] = total;
        }
        var piles = merged.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X)
            .Select(p => new CreditsPile(p.Key, (int)p.Value)).ToList();
        ValidateTotals(balances, piles);
        return piles;
    }

    internal static void Validate(GameState state)
    {
        if (state.SideCredits is { Count: 0 } && state.CreditsPiles is { Count: 0 }) return;
        var sides = state.Controllers.Select(c => c.Token.SideId).Concat(state.Units.Select(u => u.SideId))
            .Concat(state.Objectives.Select(o => o.SideId))
            .ToHashSet(StringComparer.Ordinal);
        if (state.SideCredits is null || state.CreditsPiles is null ||
            state.SideCredits.Any(p => string.IsNullOrWhiteSpace(p.Key) || !sides.Contains(p.Key) || p.Value < 0))
            throw new ArgumentException("Credits balances require known Sides and nonnegative amounts.");
        if (state.CreditsPiles.Any(p => p is null || p.Position is null || p.Amount <= 0 ||
            !MovementRules.Inside(state.Physical.Board, p.Position)) ||
            state.CreditsPiles.Select(p => p.Position).Distinct().Count() != state.CreditsPiles.Count)
            throw new ArgumentException("Credits piles require unique in-bounds Cells and positive amounts.");
        ValidateTotals(state.SideCredits, state.CreditsPiles);
    }

    private static void ValidateTotals(Dictionary<string, int> balances, List<CreditsPile> piles)
    {
        // Any Side can collect every remaining pile. Reserving that headroom at
        // validation prevents a later mandatory pickup from overflowing int.
        var outstanding = piles.Sum(p => (long)p.Amount);
        if (outstanding > int.MaxValue || balances.Values.Any(balance => balance + outstanding > int.MaxValue))
            throw new ArgumentException("Credits balances plus outstanding piles exceed the supported balance range.");
    }
}
