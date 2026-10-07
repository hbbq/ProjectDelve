using ProjectDelve.Engine;

namespace ProjectDelve.ConsoleHost;

internal sealed class ConsoleDecisionProvider : IDecisionProvider
{
    public string? Choose(DecisionRequest request, IGameplayQueries queries)
    {
        Console.WriteLine($"Pending: {request.Kind} | Type: {request.TypeId} | Unit: {request.UnitId ?? "(choose a Unit)"}");
        if (request.Roll is { } roll) Console.WriteLine($"Roll {roll.Count} {roll.Family} dice for {roll.OwnerUnitId}: {roll.Purpose}");
        for (var i = 0; i < request.Candidates.Count; i++)
        {
            var candidate = request.Candidates[i];
            var path = candidate.Path is null ? "" :
                $" | Path: {string.Join(" -> ", candidate.Path.Select(c => $"({c.X},{c.Y})"))}";
            var label = candidate.Kind == ActivationChoiceKind.RollDice ? "Roll Dice" :
                candidate.Kind == ActivationChoiceKind.Stay ? "Stay here" :
                candidate.Kind == ActivationChoiceKind.EndTurn ? "End Turn" :
                candidate.Kind == ActivationChoiceKind.Cleave ? $"Cleave {candidate.TargetId}" :
                candidate.BonusAction is { } ability ? $"{ability.DisplayName ?? ability.Name} (Bonus Action)" +
                    (candidate.TargetId is null ? "" : $" → {candidate.TargetId}") +
                    (candidate.Destination is null ? "" : $" to ({candidate.Destination.X},{candidate.Destination.Y})") :
                candidate.FreeAction == UnitFreeAction.OpenDoor ?
                    $"Open Door ({candidate.Door!.A.X},{candidate.Door.A.Y}) <-> ({candidate.Door.B.X},{candidate.Door.B.Y}) (Free Action)" : candidate.Action switch
            {
                UnitAction.NormalAttack => $"Attack {candidate.TargetId}",
                _ => candidate.Key
            };
            Console.WriteLine($"  {i + 1}. {label}{path}");
        }
        if (request.AllowsNone)
            Console.WriteLine(request.Kind == DecisionKind.Move ? "  0. Stay here" :
                request.Kind == DecisionKind.Cleave ? "  0. Decline Cleave" : "  0. Take no action");

        while (true)
        {
            Console.Write("Choose a number (q to quit): ");
            var input = Console.ReadLine();
            if (input is null || input.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
                throw new OperationCanceledException();
            if (int.TryParse(input, out var number))
            {
                if (number == 0 && request.AllowsNone) return null;
                if (number >= 1 && number <= request.Candidates.Count)
                    return request.Candidates[number - 1].Key;
            }
            Console.WriteLine("Enter one of the listed choices.");
        }
    }
}
