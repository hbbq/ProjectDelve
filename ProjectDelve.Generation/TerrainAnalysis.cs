using ProjectDelve.Engine;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ProjectDelve.Generation;

public enum NaturalFormationKind { Forest, RockMass, WaterBody, OpenLand, Other }
public sealed record CellBounds(int X, int Y, int Width, int Height);
public sealed record NaturalFormation(string Id, NaturalFormationKind Kind, CellBounds Bounds,
    int CoreArea, int EnvelopeArea, double Density, int MaxDepth, bool TouchesBoundary,
    IReadOnlyList<int> AdjacentFormations, double Forest, double Rock, double Openness, double Soil, int BlocksLos);
public sealed record EnrichmentFeature(string Id, string StepId, string Kind, string FormationId,
    CellBounds Bounds, int ChangedCells, IReadOnlyList<int> EntranceIndices);
public sealed record EntranceCandidate(string Id, string FeatureId, int InsideIndex, int OutsideIndex, int Width);
public sealed record TerrainOpportunity(string Id, string Kind, int AnchorIndex, CellBounds Bounds,
    int UsableArea, string? FormationId, string? FeatureId, int WalkableComponent, bool RequiresConnection)
{
    public int? OtherBankIndex { get; init; }
    public int WaterSpan { get; init; }
}
public sealed record ConnectivitySummary(int WalkableArea, int ComponentCount,
    IReadOnlyList<int> ComponentAreas, int LargestComponentArea);
public sealed record NarrowPassage(string Id, int AnchorIndex, CellBounds Bounds, int Width,
    int CellCount, int WalkableComponent, string Axis);
/// <summary>Detached authoring analysis. Arrays use row-major local cell indices; no input is retained or changed.</summary>
public sealed record TerrainAnalysisSnapshot(WorldWindow Window, int Revision, int[] FormationLabels,
    int[] ExactComponentLabels, int[] Depth, int[] Clearance, int[] WalkableLabels, bool[] Boundary,
    int[] Chokepoints, IReadOnlyList<NaturalFormation> Formations,
    IReadOnlyList<TerrainOpportunity> Opportunities, ConnectivitySummary Connectivity)
{
    public string Version => "TAN1";
    public string SourceFingerprint { get; init; } = "";
    public IReadOnlyList<NarrowPassage> NarrowPassages { get; init; } = [];
}

public static class TerrainAnalyzer
{
    public static TerrainAnalysisSnapshot Analyze(WorldWindow window, IReadOnlyList<NaturalTerrainSample> samples,
        ContentBundle? content = null, int revision = 0, IReadOnlyList<EdgeDefinition>? edges = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(samples);
        window.Validate();
        var w = window.Width; var h = window.Height; var n = w * h;
        if (samples.Count != n) throw new ArgumentException("Supply exactly one sample per window cell.", nameof(samples));
        var definitions = (content ?? ContentBundle.Canonical()).Terrain.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var kinds = new NaturalFormationKind[n]; var passable = new bool[n]; var los = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var sample = samples[i] ?? throw new ArgumentException("Samples cannot contain null.", nameof(samples));
            if (!definitions.TryGetValue(sample.TerrainTypeId, out var definition))
                throw new ArgumentException($"Unknown terrain '{sample.TerrainTypeId}'.", nameof(samples));
            passable[i] = definition.Passable; los[i] = definition.BlocksLos;
            // These are sampler landscape families, never gameplay legality or behavior dispatch.
            kinds[i] = sample.TerrainTypeId switch
            {
                "tree" or "fruit-tree" => NaturalFormationKind.Forest,
                "solid-rock" => NaturalFormationKind.RockMass,
                "water" => NaturalFormationKind.WaterBody,
                "grass" or "dirt" => NaturalFormationKind.OpenLand,
                _ => NaturalFormationKind.Other
            };
        }
        int Neighbor(int i, int d) => d switch
        {
            0 => i % w > 0 ? i - 1 : -1,
            1 => i % w + 1 < w ? i + 1 : -1,
            2 => i >= w ? i - w : -1,
            _ => i + w < n ? i + w : -1
        };
        var walls = new HashSet<(int, int)>();
        foreach (var edge in edges ?? [])
        {
            if (edge is null || edge.Position is null || !Enum.IsDefined(edge.Direction) || !Enum.IsDefined(edge.Kind))
                throw new ArgumentException("Edges require valid positions, directions and kinds.", nameof(edges));
            var x = edge.Position.X; var y = edge.Position.Y;
            if (x < 0 || y < 0 || x >= w || y >= h ||
                edge.Direction == EdgeDirection.Right && x == w - 1 || edge.Direction == EdgeDirection.Down && y == h - 1)
                throw new ArgumentException("Edges must join two cells in the window.", nameof(edges));
            var nx = x + (edge.Direction == EdgeDirection.Right ? 1 : 0);
            var ny = y + (edge.Direction == EdgeDirection.Down ? 1 : 0);
            if (edge.Kind is EdgeKind.ClosedDoor or EdgeKind.OpenDoor or EdgeKind.None) continue;
            var a = y * w + x; var b = ny * w + nx; walls.Add((Math.Min(a, b), Math.Max(a, b)));
        }
        // Stable source identity, independent of runtime hash randomization, culture and edge ordering.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bytes = new byte[34];
        for (var i = 0; i < n; i++)
        {
            var name = Encoding.UTF8.GetBytes(samples[i].TerrainTypeId);
            BinaryPrimitives.WriteInt32LittleEndian(bytes, name.Length); hash.AppendData(bytes, 0, 4); hash.AppendData(name);
            BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(samples[i].Forest));
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), BitConverter.DoubleToInt64Bits(samples[i].Rock));
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(16), BitConverter.DoubleToInt64Bits(samples[i].Openness));
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), BitConverter.DoubleToInt64Bits(samples[i].Soil));
            bytes[32] = passable[i] ? (byte)1 : (byte)0; bytes[33] = los[i] ? (byte)1 : (byte)0;
            hash.AppendData(bytes);
        }
        foreach (var wall in walls.OrderBy(e => e.Item1).ThenBy(e => e.Item2))
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes, wall.Item1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), wall.Item2); hash.AppendData(bytes, 0, 8);
        }
        var fingerprint = Convert.ToHexString(hash.GetHashAndReset())[..24];
        bool Linked(int a, int b) => !walls.Contains((Math.Min(a, b), Math.Max(a, b)));
        var exact = Enumerable.Repeat(-1, n).ToArray(); var queue = new Queue<int>(); var componentCount = 0;
        for (var start = 0; start < n; start++)
        {
            if (exact[start] >= 0) continue;
            exact[start] = componentCount++; queue.Enqueue(start);
            while (queue.TryDequeue(out var i))
                for (var d = 0; d < 4; d++)
                {
                    var j = Neighbor(i, d);
                    if (j < 0 || exact[j] >= 0 || samples[j].TerrainTypeId != samples[i].TerrainTypeId) continue;
                    exact[j] = exact[i]; queue.Enqueue(j);
                }
        }
        var parent = Enumerable.Range(0, componentCount).ToArray();
        int Root(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        void Union(int a, int b) { a = Root(a); b = Root(b); parent[Math.Max(a, b)] = Math.Min(a, b); }
        bool SameFamily(int a, int b) => kinds[a] == kinds[b] &&
            (kinds[a] != NaturalFormationKind.Other || samples[a].TerrainTypeId == samples[b].TerrainTypeId);
        var gaps = new List<(int Gap, int Core)>();
        for (var i = 0; i < n; i++)
        {
            for (var d = 0; d < 4; d++)
            {
                var j = Neighbor(i, d);
                if (j >= 0 && SameFamily(i, j)) Union(exact[i], exact[j]);
            }
            if (kinds[i] != NaturalFormationKind.OpenLand) continue;
            for (var axis = 0; axis < 2; axis++)
            {
                var a = Neighbor(i, axis * 2); var b = Neighbor(i, axis * 2 + 1);
                if (a < 0 || b < 0 || !SameFamily(a, b) ||
                    kinds[a] is not (NaturalFormationKind.Forest or NaturalFormationKind.RockMass)) continue;
                Union(exact[a], exact[b]); gaps.Add((i, a));
            }
        }
        var labels = new int[n]; var roots = new Dictionary<int, int>(); var cells = new List<List<int>>();
        for (var i = 0; i < n; i++)
        {
            var root = Root(exact[i]);
            if (!roots.TryGetValue(root, out var label)) { label = roots.Count; roots.Add(root, label); cells.Add([]); }
            labels[i] = label; cells[label].Add(i);
        }
        var boundary = new bool[n]; var depth = new int[n]; var clearance = new int[n];
        for (var i = 0; i < n; i++)
        {
            for (var d = 0; d < 4; d++)
            {
                var j = Neighbor(i, d);
                if (j < 0 || !SameFamily(i, j)) boundary[i] = true;
            }
            if (boundary[i]) { depth[i] = 1; queue.Enqueue(i); }
        }
        while (queue.TryDequeue(out var i))
            for (var d = 0; d < 4; d++)
            {
                var j = Neighbor(i, d);
                if (j < 0 || depth[j] != 0 || !SameFamily(i, j)) continue;
                depth[j] = depth[i] + 1; queue.Enqueue(j);
            }
        for (var i = 0; i < n; i++)
        {
            if (!passable[i]) { queue.Enqueue(i); continue; }
            if (i % w == 0 || i % w == w - 1 || i < w || i >= n - w ||
                Enumerable.Range(0, 4).Any(d => Neighbor(i, d) is var j && j >= 0 && !Linked(i, j)))
            { clearance[i] = 1; queue.Enqueue(i); }
            else clearance[i] = int.MaxValue;
        }
        while (queue.TryDequeue(out var i))
            for (var d = 0; d < 4; d++)
            {
                var j = Neighbor(i, d);
                if (j < 0 || !Linked(i, j) || clearance[j] <= clearance[i] + 1) continue;
                clearance[j] = clearance[i] + 1; queue.Enqueue(j);
            }
        var walk = Enumerable.Repeat(-1, n).ToArray(); var areas = new List<int>();
        for (var start = 0; start < n; start++)
        {
            if (!passable[start] || walk[start] >= 0) continue;
            var area = 0; walk[start] = areas.Count; queue.Enqueue(start);
            while (queue.TryDequeue(out var i))
            {
                area++;
                for (var d = 0; d < 4; d++)
                {
                    var j = Neighbor(i, d);
                    if (j < 0 || !passable[j] || walk[j] >= 0 || !Linked(i, j)) continue;
                    walk[j] = walk[i]; queue.Enqueue(j);
                }
            }
            areas.Add(area);
        }
        // Iterative Tarjan traversal keeps even a 14,400-cell path off the call stack.
        var discovered = new int[n]; var low = new int[n]; var predecessors = Enumerable.Repeat(-1, n).ToArray();
        var next = new int[n]; var children = new int[n]; var articulation = new bool[n]; var time = 0;
        var stack = new Stack<int>();
        for (var start = 0; start < n; start++)
        {
            if (!passable[start] || discovered[start] != 0) continue;
            discovered[start] = low[start] = ++time; stack.Push(start);
            while (stack.TryPeek(out var i))
            {
                if (next[i] < 4)
                {
                    var j = Neighbor(i, next[i]++);
                    if (j < 0 || !passable[j] || !Linked(i, j)) continue;
                    if (discovered[j] == 0)
                    {
                        predecessors[j] = i; children[i]++; discovered[j] = low[j] = ++time; stack.Push(j);
                    }
                    else if (j != predecessors[i]) low[i] = Math.Min(low[i], discovered[j]);
                    continue;
                }
                stack.Pop(); var p = predecessors[i];
                if (p < 0) articulation[i] = children[i] > 1;
                else
                {
                    low[p] = Math.Min(low[p], low[i]);
                    if (predecessors[p] >= 0 && low[i] >= discovered[p]) articulation[p] = true;
                }
            }
        }
        var adjacent = cells.Select(_ => new SortedSet<int>()).ToArray();
        for (var i = 0; i < n; i++)
            for (var d = 0; d < 4; d++)
            { var j = Neighbor(i, d); if (j >= 0 && labels[j] != labels[i]) adjacent[labels[i]].Add(labels[j]); }
        var envelopes = cells.Select(c => c.Count).ToArray();
        foreach (var gap in gaps.Select(g => (g.Gap, Formation: labels[g.Core])).Distinct()) envelopes[gap.Formation]++;
        var formations = new List<NaturalFormation>(); var opportunities = new List<TerrainOpportunity>();
        for (var f = 0; f < cells.Count; f++)
        {
            var core = cells[f]; var anchor = core[0];
            var minX = core.Min(i => i % w); var maxX = core.Max(i => i % w);
            var minY = core.Min(i => i / w); var maxY = core.Max(i => i / w);
            var bounds = new CellBounds(minX, minY, maxX - minX + 1, maxY - minY + 1);
            var touches = core.Any(i => i % w == 0 || i % w == w - 1 || i < w || i >= n - w);
            var id = FormattableString.Invariant($"TAN1:{fingerprint}:{window.OffsetX},{window.OffsetY}:{w}x{h}:{kinds[anchor]}:{(long)window.OffsetX + anchor % w},{(long)window.OffsetY + anchor / w}");
            formations.Add(new(id, kinds[anchor], bounds, core.Count, envelopes[f], (double)core.Count / envelopes[f],
                core.Max(i => depth[i]), touches, adjacent[f].ToArray(), core.Average(i => samples[i].Forest),
                core.Average(i => samples[i].Rock), core.Average(i => samples[i].Openness), core.Average(i => samples[i].Soil), core.Count(i => los[i])));
            if (kinds[anchor] != NaturalFormationKind.OpenLand) continue;
            // One compact opportunity per actual passable subcomponent; never imply custom blocked ground is usable.
            foreach (var group in core.Where(i => passable[i]).GroupBy(i => walk[i]).OrderBy(g => g.Key))
            {
                var kind = !touches && adjacent[f].Count > 0 && adjacent[f].All(a => kinds[cells[a][0]] == NaturalFormationKind.WaterBody)
                    ? "island" : !touches && adjacent[f].Any(a => kinds[cells[a][0]] == NaturalFormationKind.Forest) ? "clearing" : "open-area";
                var usable = group.ToArray();
                var left = usable.Min(i => i % w); var top = usable.Min(i => i / w);
                var usableBounds = new CellBounds(left, top, usable.Max(i => i % w) - left + 1, usable.Max(i => i / w) - top + 1);
                opportunities.Add(new(id + ":" + kind + ":" + group.Key, kind, usable.OrderByDescending(i => clearance[i]).ThenBy(i => i).First(),
                    usableBounds, usable.Length, id, null, group.Key, kind == "island"));
            }
        }
        var passages = new List<NarrowPassage>();
        for (var axis = 0; axis < 2; axis++)
        {
            var widths = new int[n];
            for (var i = 0; i < n; i++)
            {
                if (!passable[i]) continue;
                var width = 1; var enclosed = true;
                for (var side = 0; side < 2; side++)
                {
                    var current = i;
                    while (true)
                    {
                        var j = Neighbor(current, axis * 2 + side);
                        if (j < 0) { enclosed = false; break; } // A crop boundary is not an obstacle.
                        if (!passable[j] || !Linked(current, j)) break;
                        width++; current = j;
                        if (width > 2) { enclosed = false; break; }
                    }
                    if (!enclosed) break;
                }
                if (enclosed && width <= 2) widths[i] = width;
            }
            var visited = new bool[n];
            for (var start = 0; start < n; start++)
            {
                if (widths[start] == 0 || visited[start]) continue;
                visited[start] = true; queue.Enqueue(start);
                var count = 0; var width = 0; var minX = w; var maxX = 0; var minY = h; var maxY = 0;
                while (queue.TryDequeue(out var i))
                {
                    count++; width = Math.Max(width, widths[i]);
                    minX = Math.Min(minX, i % w); maxX = Math.Max(maxX, i % w);
                    minY = Math.Min(minY, i / w); maxY = Math.Max(maxY, i / w);
                    for (var d = 0; d < 4; d++)
                    {
                        var j = Neighbor(i, d);
                        if (j < 0 || widths[j] == 0 || visited[j] || !Linked(i, j)) continue;
                        visited[j] = true; queue.Enqueue(j);
                    }
                }
                var direction = axis == 0 ? "vertical" : "horizontal";
                var id = FormattableString.Invariant($"TAN1:{fingerprint}:{window.OffsetX},{window.OffsetY}:{w}x{h}:passage:{direction}:{start}");
                passages.Add(new(id, start, new(minX, minY, maxX - minX + 1, maxY - minY + 1), width, count, walk[start], direction));
            }
        }
        return new(window, revision, labels, exact, depth, clearance, walk, boundary,
            Enumerable.Range(0, n).Where(i => articulation[i]).ToArray(), formations.AsReadOnly(), opportunities.AsReadOnly(),
            new(areas.Sum(), areas.Count, areas.AsReadOnly(), areas.Count == 0 ? 0 : areas.Max()))
            { SourceFingerprint = fingerprint, NarrowPassages = passages.AsReadOnly() };
    }
}
