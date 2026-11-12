using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectDelve.Engine;
using ProjectDelve.Generation;

// Advisory authoring measurements, never game legality or acceptance policy.
if (args.Length is <1 or >2 || args.Length==2 && args[1]!="--maps") throw new ArgumentException("Supply a new output JSONL path, optionally followed by --maps.");
var output = Path.GetFullPath(args[0]);
if (File.Exists(output)) throw new ArgumentException("Output already exists; use a new path.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var content = ContentBundle.Canonical();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
var styles = new (string Name, NaturalTerrainSettings Settings)[] {
    ("Woodland", new(60,8,8)), ("RockyWilds", new(15,8,50,FeatureScale.Broad,WaterForm.Mixed)),
    ("DenseForest", new(85,0,5,FeatureScale.Broad)), ("DenseRock", new(5,0,85,FeatureScale.Broad)),
    ("FineMixed", new(35,20,35,FeatureScale.Fine,WaterForm.Mixed)) };
int number = 0;
using var writer = new StreamWriter(output);
foreach (var (w,h) in new[] { (30,24), (60,50), (120,120) })
foreach (var style in styles)
foreach (var (seed,x,y) in new[] { (1UL,0,0), (42UL,-17,-13), (0x123456789ABCDEF0UL,37,-29) })
{
    var key = $"{w}x{h}-{style.Name}-{seed:X16}";
    for (int intensity = 0; intensity <= 3; intensity++)
    {
        var recipe = new WorldGenerationRecipe(new(x,y,w,h), style.Settings, seed,
            new(Enabled:intensity>0, Intensity:Math.Max(1,intensity), Forest:true, Rock:false, Water:false, Grassland:false));
        if(args.Length==2)
        {
            // Small, named visual QA subset; ordinary runs retain the full fixed corpus.
            if(intensity!=3 || w!=120 || !(style.Name=="Woodland"&&seed==42 || style.Name=="DenseForest"&&seed==0x123456789ABCDEF0UL || style.Name=="FineMixed"&&seed==42)) continue;
            var map=WorldGenerator.Generate(recipe,content);
            writer.WriteLine(JsonSerializer.Serialize(new {key,map.Code,map.Fingerprint,w,h,
                original=map.Context.OriginalSamples.Select(s=>s.TerrainTypeId),terrain=map.Context.Samples.Select(s=>s.TerrainTypeId),
                originalLabels=map.Context.OriginalAnalysis!.WalkableLabels,currentLabels=map.Context.CurrentAnalysis!.WalkableLabels},json));
            writer.Flush();continue;
        }
        // Two warmups and five samples keep JIT and one-off pauses out of the median.
        for (int run=0;run<2;run++) _=WorldGenerator.Generate(recipe,content);
        List<double> times=[]; List<long> allocations=[]; List<GeneratedWorld> samples=[];
        for (int run=0;run<5;run++) {
            var allocated=GC.GetAllocatedBytesForCurrentThread(); var timer=Stopwatch.StartNew();
            var world=WorldGenerator.Generate(recipe,content); times.Add(timer.Elapsed.TotalMilliseconds);
            allocations.Add(GC.GetAllocatedBytesForCurrentThread()-allocated); samples.Add(world);
        }
        var last=samples[^1];
        var signatures=samples.Select(v=>JsonSerializer.Serialize(new {v.Fingerprint,v.Context.Features,v.Context.Entrances,v.Context.FeatureOpportunities},json)).Distinct().Count();
        if(signatures!=1)throw new InvalidOperationException("Nondeterministic: "+key);
        // Generate performs authoritative GameEngine.CreateGame validation on every sample.
        var record=new {key,w,h,style=style.Name,seed=seed.ToString("X16"),x,y,intensity,last.Code,last.Fingerprint,
            deterministic=true,engineValid=true,runtime=Environment.Version.ToString(),warmups=2,samples=5,
            medianMs=times.Order().ElementAt(2),minMs=times.Min(),maxMs=times.Max(),allocatedMedian=allocations.Order().ElementAt(2),
            stages=last.Metrics.Stages.Select(s=>new {s.StepId,medianMs=samples.Select(v=>v.Metrics.Stages.Single(t=>t.StepId==s.StepId).Milliseconds).Order().ElementAt(2),
                allocatedMedian=samples.Select(v=>v.Metrics.Stages.Single(t=>t.StepId==s.StepId).AllocatedBytes).Order().ElementAt(2)}),
            metrics=Measure(last.Context,intensity)};
        writer.WriteLine(JsonSerializer.Serialize(record,json)); writer.Flush();
    }
    Console.WriteLine($"{++number}/45 {key}");
}

object Measure(WorldGenerationContext c,int intensity)
{
    var o=c.OriginalAnalysis!;var a=c.CurrentAnalysis!;int n=c.Samples.Length,w=c.Window.Width;
    int Main(TerrainAnalysisSnapshot t)=>t.Connectivity.ComponentAreas.Count==0?-2:Enumerable.Range(0,t.Connectivity.ComponentAreas.Count).OrderByDescending(i=>t.Connectivity.ComponentAreas[i]).ThenBy(i=>i).First();
    var originalMain=Main(o);var main=Main(a);var anchor=Array.FindIndex(o.WalkableLabels,l=>l==originalMain);
    var anchoredLabel=anchor<0?-2:a.WalkableLabels[anchor];
    bool Anchored(int i)=>anchoredLabel>=0&&a.WalkableLabels[i]==anchoredLabel;
    var tree=Enumerable.Range(0,n).Where(i=>c.OriginalSamples[i].TerrainTypeId=="tree").ToArray();
    var changed=tree.Where(i=>c.Samples[i].TerrainTypeId!="tree").ToArray();
    var remaining=tree.Except(changed).ToArray();
    var distance=Enumerable.Repeat(-1,n).ToArray();Queue<int> queue=new();
    for(int i=0;i<n;i++)if(Anchored(i)){distance[i]=0;queue.Enqueue(i);}
    while(queue.TryDequeue(out var i))foreach(var j in Neighbors(i,w,n))if(distance[j]<0){distance[j]=distance[i]+1;queue.Enqueue(j);}
    var eligible=o.Formations.Where(f=>f.Kind==NaturalFormationKind.Forest&&f.CoreArea>=64&&f.MaxDepth>=2).ToArray();
    var formations=eligible.Select(f=>{
        var label=Array.FindIndex(o.Formations.ToArray(),v=>v.Id==f.Id);
        var cells=tree.Where(i=>o.FormationLabels[i]==label).ToArray();
        var edits=cells.Intersect(changed).ToArray();
        var budget=intensity==0?0:f.CoreArea*(3+intensity*5)/100;
        var touched=edits.Select(i=>(X:(i%w-f.Bounds.X)*4/Math.Max(1,f.Bounds.Width),Y:(i/w-f.Bounds.Y)*4/Math.Max(1,f.Bounds.Height))).Distinct().Count();
        var occupied=cells.Select(i=>(X:(i%w-f.Bounds.X)*4/Math.Max(1,f.Bounds.Width),Y:(i/w-f.Bounds.Y)*4/Math.Max(1,f.Bounds.Height))).Distinct().Count();
        return new {f.Id,f.CoreArea,f.MaxDepth,f.Bounds,budget,used=edits.Length,anchoredEdits=edits.Count(Anchored),
            budgetUtilization=budget==0?0:edits.Length/(double)budget,occupiedSpatialBins=occupied,touchedSpatialBins=touched,
            features=c.Features.Count(v=>v.FormationId==f.Id)};
    }).ToArray();
    var features=c.Features.Select(f=>{
        var op=c.FeatureOpportunities.Single(v=>v.FeatureId==f.Id);
        var label=a.WalkableLabels[op.AnchorIndex];
        return new {f.Id,f.Kind,f.FormationId,f.ChangedCells,op.AnchorIndex,op.UsableArea,
            anchored=Anchored(op.AnchorIndex),principal=label>=0&&label==main,
            usefulAnchoredSpace=Anchored(op.AnchorIndex)&&op.UsableArea>=4,
            componentArea=label<0?0:a.Connectivity.ComponentAreas[label],entrances=f.EntranceIndices.Count};
    }).ToArray();
    return new {originalMainAnchor=anchor,originalMainArea=originalMain<0?0:o.Connectivity.ComponentAreas[originalMain],
        anchoredArea=anchoredLabel<0?0:a.Connectivity.ComponentAreas[anchoredLabel],a.Connectivity,
        beforeTreeCells=tree.Length,afterTreeCells=remaining.Length,newlyTraversable=changed.Count(i=>a.WalkableLabels[i]>=0),
        anchoredNewCells=changed.Count(Anchored),previousGroundConnected=Enumerable.Range(0,n).Count(i=>o.WalkableLabels[i]>=0&&o.WalkableLabels[i]!=originalMain&&Anchored(i)),
        beforeMaxDepth=tree.Select(i=>o.Depth[i]).DefaultIfEmpty().Max(),afterMaxDepth=remaining.Select(i=>a.Depth[i]).DefaultIfEmpty().Max(),
        deepBlockingCells=remaining.Count(i=>a.Depth[i]>=6),blockingBeyondMainSightRange=remaining.Count(i=>distance[i]<0||distance[i]>5),
        largestRemainingExactMass=remaining.GroupBy(i=>a.ExactComponentLabels[i]).Select(g=>g.Count()).DefaultIfEmpty().Max(),
        nonForestEdits=Enumerable.Range(0,n).Count(i=>c.OriginalSamples[i].TerrainTypeId!="tree"&&c.Samples[i]!=c.OriginalSamples[i]),
        c.RoutingSearches,c.RoutingVisitedCells,formations,features,
        naturalClearings=o.Opportunities.Where(v=>v.Kind=="clearing").Select(v=>new {v.AnchorIndex,v.UsableArea,beforeAnchored=o.WalkableLabels[v.AnchorIndex]==originalMain,afterAnchored=Anchored(v.AnchorIndex)})};
}

static IEnumerable<int> Neighbors(int i,int w,int n)
{if(i>=w)yield return i-w;if(i%w>0)yield return i-1;if(i%w+1<w)yield return i+1;if(i+w<n)yield return i+w;}
