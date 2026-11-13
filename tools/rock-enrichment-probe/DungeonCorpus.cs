using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

internal static class DungeonCorpus
{
    internal static void Run(string output)
    {
        if (File.Exists(output)) throw new ArgumentException("Use a new output path.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using var writer = new StreamWriter(output);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var recipes = new List<(string Group, WorldGenerationRecipe Recipe)>();
        foreach (var (w,h) in new[] { (30,24), (60,50), (120,120) })
        foreach (var seed in new[] { 1UL,2UL,3UL,42UL,12345UL,0x123456789ABCDEF0UL })
        foreach (var routes in new[] { false, true })
        foreach (var mode in new[] { "off", "forest-only", "rock-only", "both" })
            recipes.Add(("session-b", new(new(-17,-13,w,h), new(15,8,50,FeatureScale.Broad,WaterForm.Mixed),seed,
                new(Enabled:mode!="off",Intensity:3,Forest:mode is "forest-only" or "both",Rock:mode is "rock-only" or "both",Water:false,Grassland:false),
                new(),routes ? new(Profile:RouteProfileKind.RockPassage) : null)));
        foreach (var (w,h,seed,x,y) in new[] { (30,24,1UL,0,0), (60,50,42UL,-17,-13), (120,120,42UL,-17,-13), (60,50,0x123456789ABCDEF0UL,37,-29) })
        foreach (var mode in new[] { "off", "rock-only", "both" })
            recipes.Add(("opportunity",new(new(x,y,w,h),seed==0x123456789ABCDEF0UL ? new(15,8,50,FeatureScale.Broad,WaterForm.Mixed) : new(35,20,35,FeatureScale.Fine,WaterForm.Mixed),seed,
                new(Enabled:mode!="off",Intensity:3,Forest:mode=="both",Rock:mode!="off",Water:false,Grassland:false),new())));
        var number = 0;
        foreach (var (group, recipe) in recipes)
        {
            try
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
                var world = WorldGenerator.Generate(recipe); var elapsed = timer.Elapsed.TotalMilliseconds;
                var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                var repeat = WorldGenerator.Generate(recipe);
                if (world.Fingerprint != repeat.Fingerprint || JsonSerializer.Serialize(world.Context.Sites,json) != JsonSerializer.Serialize(repeat.Context.Sites,json))
                    throw new InvalidOperationException("Nondeterminism");
                var c = world.Context; var w = c.Window.Width;
                var before = world.Preview.Frames.TakeWhile(f => f.StepId != "civilization").Last();
                var terrain = c.OriginalSamples.Select((s,i) => s with { TerrainTypeId = world.Preview.TerrainPalette[before.Terrain[i]-'A'] }).ToArray();
                var early = Enumerable.Range(0,terrain.Length).Where(i => c.OriginalSamples[i].TerrainTypeId=="solid-rock" && terrain[i].TerrainTypeId=="stone-floor").ToHashSet();
                var dungeons = c.Sites.Where(s => s.MountainDungeon is not null).Select(s =>
                {
                    var d = s.MountainDungeon!; var a = c.CurrentAnalysis!;
                    var root = a.WalkableLabels[d.AccessRootIndex];
                    var access = root >= 0 && d.Rooms.SelectMany(r=>r.Usable.Indices(w)).Concat(s.Entrances.Select(e=>e.InsideIndex)).All(i=>a.WalkableLabels[i]==root);
                    var shell = d.ProtectedRock.Indices(w).All(i=>c.Samples[i].TerrainTypeId=="solid-rock");
                    if (!access || !shell) throw new InvalidOperationException("Invalid final dungeon geometry");
                    return new {d.Profile,d.AccessRootIndex,d.RockMassAnchor,d.RockMassArea,d.RoomCapacity,
                        rooms=d.Rooms.Count,usable=s.Access.UsableCells,
                        reused= d.Rooms.SelectMany(r=>r.Interior.Indices(w)).Concat(d.Corridors.Indices(w)).Distinct().Count(early.Contains),
                        shellIntact=shell,stableRootAccess=access};
                }).ToArray();
                writer.WriteLine(JsonSerializer.Serialize(new {group,world.Code,world.Fingerprint,deterministic=true,engineValid=true,
                    elapsedMs=elapsed,allocatedBytes=bytes,c.CivilizationStats,c.PlanningFailures,
                    survey=Survey(c.Window,terrain),earlyRockEdits=early.Count,dungeons,world.Metrics.Stages},json));
            }
            catch(Exception e) { writer.WriteLine(JsonSerializer.Serialize(new {group,code=recipe.ToString(),error=e.ToString()},json)); }
            writer.Flush(); Console.WriteLine($"{++number}/{recipes.Count} {recipe}");
        }
    }

    // Historical intact-rock opportunity metric, deliberately stricter than controlled absorption.
    internal static object[] Survey(WorldWindow window, IReadOnlyList<NaturalTerrainSample> samples)
    {
        var analysis = TerrainAnalyzer.Analyze(window,samples); var w=window.Width; var h=window.Height;
        var main=analysis.Connectivity.ComponentAreas.Count==0 ? -2 : Enumerable.Range(0,analysis.Connectivity.ComponentAreas.Count).OrderByDescending(i=>analysis.Connectivity.ComponentAreas[i]).ThenBy(i=>i).First();
        var pending=Enumerable.Range(0,samples.Count).Where(i=>samples[i].TerrainTypeId=="solid-rock").ToHashSet();
        List<object> records=[];
        foreach(var anchor in pending.Order().ToArray())
        {
            if(!pending.Remove(anchor))continue;
            HashSet<int> mass=[anchor]; Queue<int> q=new([anchor]);
            while(q.TryDequeue(out var i))foreach(var j in Neighbors(i))if(pending.Remove(j)){mass.Add(j);q.Enqueue(j);}
            var core=mass.Where(i=>Square(i,3).All(mass.Contains)).ToHashSet();
            var centers=core.Where(i=>Square(i,3).All(core.Contains)).Order().ToArray();
            HashSet<int> packed=[];var capacity=0;
            foreach(var center in centers)if(!Square(center,5).Any(packed.Contains)){packed.UnionWith(Square(center,3));capacity++;}
            var entrances=mass.Where(i=>!core.Contains(i)&&Neighbors(i).Any(core.Contains))
                .SelectMany(i=>Neighbors(i).Where(j=>analysis.WalkableLabels[j]==main).Select(j=>(Inside:i,Outside:j)))
                .Count(e=>Neighbors(e.Inside).Where(j=>!mass.Contains(j)).All(j=>j==e.Outside));
            records.Add(new {anchor,area=mass.Count,capacity,entrances,eligible=capacity>=2&&entrances>0});
        }
        return records.ToArray();
        IEnumerable<int> Neighbors(int i)
        { if(i>=w)yield return i-w;if(i%w>0)yield return i-1;if(i%w+1<w)yield return i+1;if(i+w<samples.Count)yield return i+w; }
        IEnumerable<int> Square(int i,int size)
        { for(var y=i/w-size/2;y<=i/w+size/2;y++)for(var x=i%w-size/2;x<=i%w+size/2;x++)yield return x<0||y<0||x>=w||y>=h ? -1 : y*w+x; }
    }
}

