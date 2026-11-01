using ProjectDelve.Engine;

namespace ProjectDelve.ConsoleHost;

internal static class ContentCommands
{
    public static int Run(string[] args)
    {
        try
        {
            string Option(string name, string? fallback = null)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback ?? throw new ArgumentException($"Missing {name}.");
            }
            var command = args.ElementAtOrDefault(0);
            if (command == "validate")
            {
                var path = args.ElementAtOrDefault(1) ?? throw new ArgumentException("Specify a package file or directory.");
                var packages = Directory.Exists(path) ? ContentPackageCatalog.ReadDirectory(path) :
                    [new ContentPackageDocument(ContentPackageJson.FromJson(File.ReadAllText(path), path), path)];
                var catalog = ContentPackageCatalog.Compose(packages, args.Contains("--with-csharp") ? ContentBundle.Canonical() : null);
                System.Console.WriteLine($"Valid: {packages.Count} package(s), {catalog.Definitions.Units.Length} Units, {catalog.Definitions.Equipment.Length} Equipment, {catalog.Definitions.LootCards.Length} Loot Cards.");
            }
            else if (command is "load" or "export")
            {
                var directory = Option("--directory", "ProjectDelve.Web/Content/Canonical");
                var catalog = ContentPackageCatalog.LoadCanonical(directory);
                if (command == "load") System.Console.WriteLine($"Canonical catalog valid: {catalog.Definitions.Equipment.Length} Equipment, {catalog.Definitions.LootCards.Length} Loot Cards.");
                else
                {
                    var selections = Option("--select").Split(',').Select(text =>
                    {
                        var parts = text.Split(':', 2);
                        if (parts.Length != 2 || !Enum.TryParse<ContentFamily>(parts[0], true, out var family) || !Enum.IsDefined(family))
                            throw new ArgumentException($"Invalid selection '{text}'; use Family:TypeID.");
                        return new ContentIdentity(family, parts[1]);
                    });
                    var package = ContentPackageExport.Selected(Option("--id"), catalog, selections,
                        !args.Contains("--without-dependencies"), Option("--depends", "").Split(',', StringSplitOptions.RemoveEmptyEntries));
                    File.WriteAllText(Option("--output"), ContentPackageJson.ToJson(package));
                    System.Console.WriteLine($"Exported package '{package.Id}'.");
                }
            }
            else throw new ArgumentException("Use content validate <file-or-directory> [--with-csharp], content load --directory <directory>, or content export --directory <directory> --id <id> --select <Family:TypeID,...> --output <file>.");
            return 0;
        }
        catch (ContentCompositionException error)
        {
            foreach (var diagnostic in error.Diagnostics)
                System.Console.Error.WriteLine($"{diagnostic.Source?.Document} [{diagnostic.Source?.Package}] {diagnostic.Source?.Path}.{diagnostic.Path}: {diagnostic.Code}: {diagnostic.Owner.Family} '{diagnostic.Owner.TypeId}': {diagnostic.Message}");
            return 1;
        }
        catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException or IOException)
        {
            System.Console.Error.WriteLine($"{ContentPackageJson.SourceOf(error)?.Document} {ScenarioValidationContext.PathOf(error)}: {error.Message}");
            return 1;
        }
    }
}
