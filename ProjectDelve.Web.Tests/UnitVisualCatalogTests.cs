using System.Reflection;
using System.Text.Json;
using ProjectDelve.Engine;
using Xunit;

namespace ProjectDelve.Web.Tests;

public sealed class UnitVisualCatalogTests
{
    [Fact]
    public void VisualCatalogExactlyCoversTheCompleteCanonicalRegistryAndRoster()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "ProjectDelve.Web/wwwroot/art/models/unit-visuals.json")));
        var catalog = document.RootElement;
        Assert.Equal(1, catalog.GetProperty("schemaVersion").GetInt32());
        var types = CanonicalUnitTypes.All.ToArray();
        var units = catalog.GetProperty("units").EnumerateArray().ToArray();
        var ids = units.Select(u => u.GetProperty("typeId").GetString()).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal(types.Select(t => t.Id).Order(), ids.Order());
        var constants = typeof(UnitTypeIds).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!);
        Assert.Equal(constants.Order(), ids.Order());
        // Catch a new authored factory forgotten by the assembled canonical registry too.
        var factories = typeof(UnitRoster).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(UnitType));
        var authored = factories.Select(m => (UnitType)m.Invoke(null, m.GetParameters().Select(_ => Type.Missing).ToArray())!).Select(t => t.Id);
        Assert.Equal(authored.Order(), ids.Order());
        var models = catalog.GetProperty("models").EnumerateArray().ToArray();
        var modelIds = models.Select(m => m.GetProperty("id").GetString()).ToArray();
        Assert.Equal(modelIds.Length, modelIds.Distinct().Count());
        foreach (var m in models)
        {
            Assert.True(m.GetProperty("approved").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(m.GetProperty("version").GetString()));
            var url = m.GetProperty("url").GetString()!;
            Assert.StartsWith("./art/models/", url);
            Assert.EndsWith(".glb", url);
            Assert.DoesNotContain("..", url[2..]);
            var runtime = Path.Combine(root, "ProjectDelve.Web/wwwroot", url[2..]);
            Assert.True(File.Exists(runtime), "Approved model must exist: " + url);
        }
        foreach (var u in units)
        {
            var height = u.GetProperty("targetHeight").GetDouble();
            var radius = u.GetProperty("maximumRadius").GetDouble();
            Assert.True(double.IsFinite(height) && height > 0);
            Assert.True(double.IsFinite(radius) && radius > 0);
            var baseRadius = u.GetProperty("baseRadius").GetDouble();
            Assert.True(double.IsFinite(baseRadius) && baseRadius > 0);
            var cells = FootprintGeometry.OccupiedCells(types.Single(t => t.Id == u.GetProperty("typeId").GetString()).Footprint, new(0, 0));
            var width = cells.Max(c => c.X) - cells.Min(c => c.X) + 1;
            var depth = cells.Max(c => c.Y) - cells.Min(c => c.Y) + 1;
            Assert.True(radius <= (width == 2 && depth == 2 ? 1.25 : .92));
            Assert.True(baseRadius <= .45 * Math.Min(width, depth));
            if (u.TryGetProperty("model", out var model)) Assert.Contains(model.GetString(), modelIds);
        }
    }
}
