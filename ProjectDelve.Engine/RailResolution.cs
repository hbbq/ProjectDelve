namespace ProjectDelve.Engine;

public static partial class GameEngine
{
    private static void ResolveRunRails(GameState state, string unitId, RunRails rails, ResolutionEvents events)
    {
        for (var step = 0; step < rails.Steps; step++)
        {
            var figure = state.Physical.Figures.SingleOrDefault(f => f.Id == unitId);
            if (figure is not { Posture: Posture.Upright }) break;
            var board = state.Physical.Board;
            var tile = board.Terrain.SingleOrDefault(t => t.Position == figure.Position);
            if (tile is not { Kind: TerrainKind.Rail, Direction: { } direction }) break;
            var destination = direction switch
            {
                RailDirection.North => new Cell(figure.Position.X, figure.Position.Y - 1),
                RailDirection.East => new Cell(figure.Position.X + 1, figure.Position.Y),
                RailDirection.South => new Cell(figure.Position.X, figure.Position.Y + 1),
                RailDirection.West => new Cell(figure.Position.X - 1, figure.Position.Y),
                _ => throw new InvalidOperationException("Invalid Rail direction.")
            };
            if (board.TerrainAt(destination) != TerrainKind.Rail ||
                !MovementRules.CanStep(board, Footprint.OneByOne, figure.Position, destination, default)) break;

            var occupant = FootprintGeometry.UnitAtCell(state, destination);
            if (occupant is not null)
            {
                if (FootprintGeometry.FootprintOf(state, occupant) != Footprint.OneByOne) break;
                if (state.IsUpright(occupant))
                {
                    ExternalMovementRules.SwapPlaces(state, unitId, occupant);
                    events.Add(new RulesEvent("PlacesSwapped", unitId, occupant) { ActionId = "run-rails" });
                    ResolveArrival(state, unitId, events);
                    ResolveArrival(state, occupant, events);
                    ChangePosture(state, occupant, Posture.Lying, events, unitId, "run-rails");
                }
                else
                {
                    events.Add(ResolveDefeat(state, occupant, unitId, "run-rails"));
                    MoveRailStep();
                }
            }
            else MoveRailStep();
            ResolveExplosions(state, events);

            void MoveRailStep()
            {
                var index = state.Physical.Figures.FindIndex(f => f.Id == unitId);
                state.Physical.Figures[index] = figure with { Position = destination };
                events.Add(new RulesEvent("UnitRepositioned", unitId)
                    { ActionId = "run-rails", Path = [figure.Position, destination] });
                ResolveArrival(state, unitId, events);
            }
        }
    }
}
