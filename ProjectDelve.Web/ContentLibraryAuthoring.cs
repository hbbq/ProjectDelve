using System.Collections.Immutable;
using System.Text.Json;
using ProjectDelve.Engine;

namespace ProjectDelve.Web;

public sealed record ValidateLibraryContent(SaveContentLibraryPackage Draft, ContentEditorRequest Edit);

// Destination adapter only: typed parsing and Engine composition supply all content semantics.
public static class ContentLibraryAuthoring
{
    public static ContentBrowserResponse Catalog(ContentLibraryInspection inspection)
    {
        var body = inspection.Content.Definitions;
        var catalog = ContentBrowserCatalog.Definitions(body.Equipment, body.Items, body.LootCards, body.Units, canonical: body);
        var owners = inspection.Revisions.SelectMany(revision => revision.Package.Definitions.Units.Select(d => ("units", d.Id))
            .Concat(revision.Package.Definitions.Equipment.Select(d => ("equipment", d.Id)))
            .Concat(revision.Package.Definitions.Items.Select(d => ("items", d.Id)))
            .Concat(revision.Package.Definitions.LootCards.Select(d => ("lootCards", d.Id)))
            .Select(identity => (identity, revision))).ToDictionary(row => row.identity, row => row.revision);
        return catalog with { Context = new("library", inspection.Root.Package.Title ?? inspection.Root.Package.Id), Entries = catalog.Entries.Select(entry =>
            owners.TryGetValue((entry.Family, entry.Id), out var owner) ? entry with
            {
                Origin = owner.Package.Id == inspection.Root.Package.Id ? "library-local" : "library-dependency",
                PackageId = owner.Package.Id, PackageRevision = owner.Revision
            } : entry).ToArray() };
    }

    public static ContentEditorResponse Validate(ContentLibrary library, ValidateLibraryContent request)
    {
        if (request?.Draft?.Package is null || request.Edit is null)
            return new(false, [new("request", "required", "A package workspace and definition are required.")]);
        try
        {
            var candidate = ContentEditorApi.ParseDefinition(request.Edit);
            var body = request.Draft.Package.Definitions;
            ImmutableArray<T> Apply<T>(ImmutableArray<T> rows, T value, Func<T, string> id)
            {
                if (request.Edit.ExistingId is not { } existing) return rows.Add(value);
                if (id(value) != existing) throw new ArgumentException("Existing Type IDs stay fixed; use Create Copy for a new identity.");
                var index = Array.FindIndex(rows.ToArray(), row => id(row) == existing);
                if (index < 0) throw new ArgumentException("Only definitions owned by the current package can be edited.");
                return rows.SetItem(index, value);
            }
            body = candidate switch
            {
                EquipmentDefinition equipment => body with { Equipment = Apply(body.Equipment, equipment, d => d.Id) },
                LootCardDefinition card => body with { LootCards = Apply(body.LootCards, card, d => d.Id) },
                _ => throw new ArgumentException("Unsupported content family.")
            };
            var draft = request.Draft with { Package = request.Draft.Package with { Definitions = body } };
            var catalog = Catalog(library.Inspect(draft));
            var identity = ContentDefinitions.Identity(candidate);
            return new(true, []) { Definition = JsonSerializer.SerializeToElement(candidate, candidate.GetType(), ContentPackageJson.CreateOptions()),
                Entry = catalog.Entries.Single(e => e.Family == request.Edit.Family && e.Id == identity.TypeId), Catalog = catalog };
        }
        catch (ContentCompositionException error)
        {
            return new(false, [.. error.Diagnostics.Select(d => new ContentEditorDiagnostic(
                d.Owner.TypeId == request.Edit.Definition.GetProperty("id").GetString() && d.Owner.Family.ToString() == (request.Edit.Family == "equipment" ? "Equipment" : "LootCard")
                    ? "definition" + (d.Path.Length == 0 ? "" : "." + d.Path) : (d.Source?.Path ?? "package") + "." + d.Path,
                d.Code, d.Message))]);
        }
        catch (JsonException error) { return new(false, [new("definition" + (error.Path?.TrimStart('$') ?? ""), "json", error.Message)]); }
        catch (ArgumentException error) { return new(false, [new(ScenarioValidationContext.PathOf(error) ?? "definition", "validation", error.Message)]); }
    }
}
