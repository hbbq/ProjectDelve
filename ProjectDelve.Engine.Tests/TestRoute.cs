using System.Collections.Immutable;

namespace ProjectDelve.Engine.Tests;

// Test-owned authoring helper: production Orders never carry area copies.
internal sealed record TestRoute(ImmutableArray<ObjectiveArea> Waypoints, RouteMode Mode = RouteMode.Once,
    int CurrentIndex = 0, int Direction = 1)
{
    internal List<Waypoint> Definitions() => Waypoints.Select((a, i) => new Waypoint($"w{i}", a)).ToList();
    internal Route Definition() => new("test-route", [.. Waypoints.Select((_, i) => $"w{i}")], Mode);
    internal FollowRoute Progress() => new("test-route", CurrentIndex, Direction);
    internal TestRoute? Next() => RouteRules.Next(Progress(), Definition()) is { } next
        ? this with { CurrentIndex = next.CurrentIndex, Direction = next.Direction } : null;
}
