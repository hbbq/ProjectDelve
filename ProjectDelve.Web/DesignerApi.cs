using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record DesignerUnitType(string UnitTypeId, string DisplayName, int MaxHp,
    IReadOnlyList<Cell> FootprintOffsets, int CellSpan);
public sealed record DesignerError(string Message, string? Path);
public sealed record DesignerValidation(bool Valid, IReadOnlyList<DesignerError> Errors);
public sealed record DesignerImportRequest(string Transport);
public sealed class DesignerBoardLimitException() : ArgumentException(
    $"This host supports boards up to {DesignerApi.MaxBoardSize} × {DesignerApi.MaxBoardSize} (editor policy).");

// Stateless authoring operations. The engine still owns all scenario legality.
public static class DesignerApi
{
    public const int MaxBoardSize = 50; // Web/editor policy, never a Delve rule.

    public static void CheckHostLimits(ScenarioDefinition definition)
    {
        if (definition.Board is { } board && (board.Width > MaxBoardSize || board.Height > MaxBoardSize))
            throw new DesignerBoardLimitException();
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/designer/catalog", () => Results.Ok(new
        {
            maxBoardSize = MaxBoardSize,
            unitTypes = CanonicalUnitTypes.All.Select(type =>
            {
                var offsets = FootprintGeometry.OccupiedCells(type.Footprint, new(0, 0));
                return new DesignerUnitType(type.Id, type.DisplayName ?? type.Id, type.Hp,
                    offsets, offsets.Max(cell => cell.X) + 1);
            }).ToArray(),
            terrains = Enum.GetNames<TerrainKind>(),
            edgeKinds = Enum.GetNames<EdgeKind>(),
            postures = Enum.GetNames<Posture>(),
            controllers = Enum.GetNames<ControllerKind>()
        }));
        app.MapPost("/api/designer/import", (DesignerImportRequest request) => Execute(() =>
        {
            var definition = ScenarioDefinitionTransport.Decode(request.Transport, CheckHostLimits);
            return Results.Text(ScenarioDefinitionJson.ToJson(definition), "application/json");
        }));
        // JsonElement preserves the scenario JSON contract: no independent HTTP enum/model parser.
        app.MapPost("/api/designer/validate", (JsonElement draft) => Execute(() =>
        {
            _ = ScenarioDefinitionJson.FromJson(draft.GetRawText(), CheckHostLimits);
            return Results.Ok(new DesignerValidation(true, []));
        }, validation: true));
        app.MapPost("/api/designer/export", (JsonElement draft) => Execute(() =>
        {
            var definition = ScenarioDefinitionJson.FromJson(draft.GetRawText(), CheckHostLimits);
            return Results.Ok(new { transport = ScenarioDefinitionTransport.Encode(definition) });
        }));
    }

    private static IResult Execute(Func<IResult> operation, bool validation = false)
    {
        try { return operation(); }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidDataException or JsonException)
        {
            var path = ScenarioValidationContext.PathOf(error) ?? (error is DesignerBoardLimitException ? "board"
                : error is JsonException json ? json.Path : null);
            return Results.Json(new DesignerValidation(false, [new(error.Message, path)]),
                statusCode: validation ? 200 : 400);
        }
    }
}
