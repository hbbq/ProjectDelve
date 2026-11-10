using ProjectDelve.Engine;

namespace ProjectDelve.Web;

// Raw ownership boundary. In particular, copying a response must not evaluate
// Presentation or resolve effective stats on an executable GameState.
internal sealed class PublicationSnapshot
{
    private readonly Dictionary<GameState, GameState> states = new(ReferenceEqualityComparer.Instance);

    internal GameState State(GameState source)
    {
        if (!states.TryGetValue(source, out var copy)) states.Add(source, copy = source.DetachedCopy());
        return copy;
    }

    internal static DecisionRequest? Decision(DecisionRequest? source) => source is null ? null : source with
    { Candidates = source.Candidates.Select(c => c with { Path = c.Path?.ToList() }).ToList() };

    internal static RulesEvent Event(RulesEvent source) => source with
    {
        Path = source.Path?.ToList(),
        ObservedMovement = source.ObservedMovement?.Select(path => path.ToList()).ToArray()
    };

    internal EngineResult Result(EngineResult source) => source with
    {
        State = State(source.State), Events = source.Events.Select(Event).ToList(),
        NextInput = Decision(source.NextInput),
        ResolutionSteps = source.ResolutionSteps.Select(s => s with { StateAfter = State(s.StateAfter) }).ToList()
    };

    internal GameResponse Response(GameResponse source) => source with
    {
        Result = Result(source.Result),
        Commentaries = source.Commentaries.ToArray(),
        Scenarios = source.Scenarios.Select(s => s with
        {
            SideHues = s.SideHues.ToDictionary(p => p.Key, p => p.Value),
            Controllers = s.Controllers.ToDictionary(p => p.Key, p => p.Value),
            ControllerAssignments = s.ControllerAssignments.ToArray(),
            UnitTypeNames = s.UnitTypeNames.ToDictionary(p => p.Key, p => p.Value),
            InitiallyPresentTokens = s.InitiallyPresentTokens.ToArray(),
            SideIds = s.SideIds.ToArray()
        }).ToArray(),
        Controllers = source.Controllers.ToDictionary(p => p.Key, p => p.Value),
        ControllerAssignments = source.ControllerAssignments.ToArray(),
        ControllerStatus = source.ControllerStatus,
        PresentationOverride = PlaytestGame.DetachDto(source.StoredPresentation)
    };

    internal GameUpdate Update(GameUpdate source) => source with { Snapshot = Response(source.Snapshot) };
}
