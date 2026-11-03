using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;

namespace ProjectDelve.Engine;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LayDownSelf), "layDownSelf")]
[JsonDerivedType(typeof(SpendResource), "spendResource")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record AbilityCost;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LayDownSelf : AbilityCost;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SpendResource(
    [property: JsonRequired] string ResourceTypeId,
    [property: JsonRequired] int Amount) : AbilityCost;

// These contracts describe the shared active Ability payment vocabulary.
public sealed record AbilityCostDescriptor(string Id, string Name)
{
    public ImmutableArray<MechanicParameter> Parameters { get; init; } = [];
    public ImmutableArray<MechanicSupportProfile> Profiles { get; init; } = [];
    public string SerializedBinding { get; init; } = "";
    public string DescriptionTemplate { get; init; } = "";
    public MechanicContext Context => MechanicContext.ActingUnit;
    public string ContextRequirements => "Requires an acting Unit and a supported Action, Bonus Action or Free Action from a Unit, Equipment or Item.";
    public bool ExecutionAvailable => true;
}

public static class CostCatalog
{
    private static readonly ImmutableArray<MechanicSupportProfile> ActiveProfiles =
        (from source in new[] { MechanicSource.Unit, MechanicSource.Equipment, MechanicSource.Item }
         from timing in new[] { MechanicTiming.Action, MechanicTiming.BonusAction, MechanicTiming.FreeAction }
         select new MechanicSupportProfile(source, timing,
             source == MechanicSource.Item ? MechanicUsageSupport.UnlimitedOnly : MechanicUsageSupport.LimitedOrUnlimited)).ToImmutableArray();
    public static ImmutableArray<AbilityCostDescriptor> All { get; } =
    [
        new("layDownSelf", "Lay Down Self") { Profiles = ActiveProfiles, SerializedBinding = "layDownSelf", DescriptionTemplate = "Lay down this Unit." },
        new("spendResource", "Spend Resource")
        {
            Profiles = ActiveProfiles, SerializedBinding = "spendResource",
            Parameters = [new("resourceTypeId", MechanicParameterKind.Reference) { ReferenceCatalog = "resources" },
                new("amount", MechanicParameterKind.Integer) { Minimum = 1 }],
            DescriptionTemplate = "Spend {amount} {resourceTypeId} from this Unit's shared Side + Unit Type Resource Storage."
        }
    ];

    public static AbilityCostDescriptor? Find(string id) => All.SingleOrDefault(d => d.Id == id);
    public static AbilityCostDescriptor? FindByPayload(AbilityCost? cost) => cost switch
    {
        LayDownSelf => Find("layDownSelf"), SpendResource => Find("spendResource"), _ => null
    };

    public static ImmutableArray<MechanicDiagnostic> Validate(ImmutableArray<AbilityCost> costs,
        MechanicValidationContext? context = null, string path = "costs", IReadOnlySet<string>? availableResourceIds = null)
    {
        List<MechanicDiagnostic> errors = [];
        if (costs.IsDefault) return [new(path, "required", "Ability costs must be a list.")];
        bool laidDown = false;
        for (int i = 0; i < costs.Length; i++)
        {
            var cost = costs[i];
            var costPath = $"{path}[{i}]";
            var descriptor = FindByPayload(cost);
            if (descriptor is null)
            {
                errors.Add(new(costPath, "unknownCost", "A supported typed Ability cost is required."));
                continue;
            }
            if (cost is LayDownSelf)
            {
                if (laidDown) errors.Add(new(costPath, "duplicateCost", "LayDownSelf may occur only once per Ability."));
                laidDown = true;
            }
            if (cost is SpendResource resource)
            {
                MechanicChecks.Reference(errors, descriptor.Parameters[0], resource.ResourceTypeId, costPath + ".resourceTypeId");
                MechanicChecks.Integer(errors, descriptor.Parameters[1], resource.Amount, costPath + ".amount");
                if (!string.IsNullOrWhiteSpace(resource.ResourceTypeId) && availableResourceIds is not null && !availableResourceIds.Contains(resource.ResourceTypeId))
                    errors.Add(new(costPath + ".resourceTypeId", "unknownReference", $"Unknown Resource Type ID '{resource.ResourceTypeId}'."));
            }
            if (context is null) continue;
            var profile = descriptor.Profiles.SingleOrDefault(p => p.Source == context.Source && p.Timing == context.Timing);
            if (profile is null)
                errors.Add(new(costPath, "unsupportedContext", "Ability costs support only Unit, Equipment or Item Action, Bonus Action or Free Action envelopes."));
            if (!context.HasUnitContext)
                errors.Add(new(costPath, "unitContextRequired", "Ability costs require the acting Unit context."));
            if (context.MaxUses is < 1 || context.Source == MechanicSource.Item && context.MaxUses is not null)
                errors.Add(new(costPath, "unsupportedUsage", "Ability use limits must be positive; Single Use Items use physical consumption instead of counters."));
        }
        return [.. errors];
    }

    public static string Describe(AbilityCost cost) => cost switch
    {
        LayDownSelf => Find("layDownSelf")!.DescriptionTemplate,
        SpendResource resource => Find("spendResource")!.DescriptionTemplate
            .Replace("{amount}", resource.Amount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{resourceTypeId}", resource.ResourceTypeId, StringComparison.Ordinal),
        _ => throw new ArgumentException("A supported typed Ability cost is required.", nameof(cost))
    };
}
