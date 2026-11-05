using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// Host/editor resource policy, independent of physical game rules.
public sealed class AuthoringLimits
{
    public const int SupportedMaxBoardSize = 120;
    public const int MaxScenarioJsonBytes = ScenarioDefinitionTransport.MaxJsonBytes;
    public const long MaxLibraryRequestBytes = MaxScenarioJsonBytes + 64 * 1024;
    public static AuthoringLimits Default { get; } = new();
    public int MaxBoardSize { get; }

    public AuthoringLimits(int maxBoardSize = SupportedMaxBoardSize)
    {
        if (maxBoardSize is < 1 or > SupportedMaxBoardSize)
            throw new ArgumentOutOfRangeException(nameof(maxBoardSize), "Authoring:MaxBoardSize must be 1..120.");
        MaxBoardSize = maxBoardSize;
    }

    public void CheckDimensions(int width, int height)
    {
        if (width > MaxBoardSize || height > MaxBoardSize)
            throw new DesignerBoardLimitException(MaxBoardSize);
    }

    public void CheckScenario(ScenarioDefinition definition)
    {
        if (definition.Board is { } board) CheckDimensions(board.Width, board.Height);
    }
}
