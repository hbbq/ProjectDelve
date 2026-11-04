using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ProjectDelve.Engine;
using ProjectDelve.Web;

// Run from the repository root:
// dotnet run --project tools/ScenarioMetadataBenchmark -c Release --artifacts-path .playwright-cli
// This compares the original metadata getters with the captured response metadata.
// The legacy scenarios field intentionally describes compiled C# content. Workspace,
// Canonical JSON and editable library content do not affect it; changing that authority
// or removing the field is a separate API/architecture decision.
var state = new GameState
{
    Physical = new(new Board(30, 30, []), []),
    Types = [new("benchmark-unit", 3, 1, 1, 0, 1)], Units = []
};
for (var i = 0; i < 11; i++)
{
    state.Units.Add(new("unit-" + i, "benchmark-unit", i == 0 ? "blue" : "red", 1));
    state.Physical.Figures.Add(new("unit-" + i, new(i * 2, 20)));
}
var cold = Stopwatch.StartNew();
var response = new GameResponse(0, new(state, [], null));
var captured = Options();
var capturedJson = JsonSerializer.Serialize(response, captured);
cold.Stop();
var legacy = Options();
var resolver = new DefaultJsonTypeInfoResolver();
var metadataGameRequests = 0;
resolver.Modifiers.Add(type =>
{
    if (type.Type != typeof(ScenarioDescription)) return;
    foreach (var property in type.Properties)
        property.Get = property.Name switch
        {
            "sideHues" => value => ((ScenarioDescription)value).Id == "genombrottet"
                ? new Dictionary<string, int> { ["side-1"] = 210, ["side-2"] = 0 } : new Dictionary<string, int>(),
            "controllers" => value => Definition(value).Agency.GroupBy(a => a.SideId).ToDictionary(g => g.Key,
                g => g.All(a => a.Controller == ControllerKind.Human) ? "Human" : "Default Behavior"),
            "controllerAssignments" => value => Definition(value).Agency.Select(a => new ControllerSelection(
                a.UnitTypeId, a.SideId, a.Controller == ControllerKind.Human ? "Human" : "Default Behavior")).ToArray(),
            "unitTypeNames" => value =>
            {
                metadataGameRequests++;
                return PlaytestScenarios.Create(((ScenarioDescription)value).Id).Types
                    .ToDictionary(t => t.Id, t => t.DisplayName ?? t.Id);
            },
            "initiallyPresentTokens" => value => Definition(value).Units
                .Select(u => new ActivationToken(u.UnitTypeId, u.SideId)).Distinct().ToArray(),
            "sideIds" => value => Definition(value).Agency.Select(a => a.SideId).Distinct().ToArray(),
            _ => property.Get
        };
});
legacy.TypeInfoResolver = resolver;
if (capturedJson != JsonSerializer.Serialize(response, legacy))
    throw new InvalidOperationException("Captured and legacy response JSON differ.");
Console.WriteLine($"Runtime={Environment.Version}; serverGC={System.Runtime.GCSettings.IsServerGC}; cold capture+serialization={cold.Elapsed.TotalMilliseconds:F3} ms; exact JSON match; chars={capturedJson.Length}");
Measure("Legacy getters", legacy);
Measure("Captured metadata", captured);

void Measure(string label, JsonSerializerOptions options)
{
    const int warmups = 5, samples = 15;
    for (var i = 0; i < warmups; i++) JsonSerializer.Serialize(response, options);
    var times = new List<double>();
    var allocations = new List<long>();
    metadataGameRequests = 0;
    for (var i = 0; i < samples; i++)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        JsonSerializer.Serialize(response, options);
        watch.Stop();
        allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        times.Add(watch.Elapsed.TotalMilliseconds);
    }
    times.Sort(); allocations.Sort();
    Console.WriteLine($"{label}: median={times[samples / 2]:F3} ms; min={times[0]:F3}; max={times[^1]:F3}; allocated={allocations[samples / 2]} bytes; metadata GameState requests/sample={metadataGameRequests / samples}");
}
static ScenarioDefinition Definition(object value) => PlaytestScenarios.Definition(((ScenarioDescription)value).Id);
static JsonSerializerOptions Options() => new(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() }
};
