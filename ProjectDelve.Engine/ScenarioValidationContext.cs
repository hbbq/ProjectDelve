namespace ProjectDelve.Engine;

// Optional authoring location on the existing exceptions; messages and exception types stay intact.
public static class ScenarioValidationContext
{
    private const string PathKey = "ProjectDelve.ScenarioPath";
    public static string? PathOf(Exception error) => error.Data[PathKey] as string;

    internal static ArgumentException At(ArgumentException error, string path)
    {
        error.Data[PathKey] = path;
        return error;
    }
}
