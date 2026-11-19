using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static class FixedComponentRoster
{
    public static ImmutableArray<FixedComponentDefinition> All { get; } =
    [
        new("table", "Table", true, false) { Presentation = new("Table") },
        new("tree", "Tree", true, true) { BlocksVision = false, Presentation = new("Tree") },
        new("campfire", "Campfire", true, false) { Presentation = new("Campfire") },
        new("landmark-marker", "Marker", true, false) { Presentation = new("Marker") },
        new("landmark-statue", "Statue", true, true) { Presentation = new("Statue") },
        new("landmark-obelisk", "Obelisk", true, true) { Presentation = new("Obelisk") },
        new("well", "Well", true, false) { Presentation = new("Well") }
    ];
}
