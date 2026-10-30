using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static class FixedComponentRoster
{
    public static ImmutableArray<FixedComponentDefinition> All { get; } =
    [
        new("table", "Table", true, false) { Presentation = new("Table") },
        new("tree", "Tree", true, true) { Presentation = new("Tree") },
        new("campfire", "Campfire", true, false) { Presentation = new("Campfire") }
    ];
}
