using System.Collections.Immutable;

namespace ProjectDelve.Engine;

public static partial class MechanicCatalog
{
    // Explicit registration only. Building contracts never reads authored rosters
    // or creates GameState, so catalog inspection cannot initialize gameplay content.
    private static readonly ImmutableArray<MechanicRegistration> Registrations = Build();
    public static ImmutableArray<MechanicDescriptor> All { get; } = Registrations.Select(r => r.Descriptor).ToImmutableArray();

    private static ImmutableArray<MechanicRegistration> Build()
    {
        var registrations = AbilityRegistrations().Concat(WorldAndEnvironmentRegistrations())
            .Concat(SpecializedRegistrations()).ToImmutableArray();
        if (registrations.Select(r => r.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != registrations.Length ||
            registrations.Select(r => r.PayloadType).Distinct().Count() != registrations.Length)
            throw new InvalidOperationException("Mechanic registrations require distinct IDs and payload types.");
        return registrations;
    }
    private static partial IEnumerable<MechanicRegistration> AbilityRegistrations();
    private static partial IEnumerable<MechanicRegistration> WorldAndEnvironmentRegistrations();
    private static partial IEnumerable<MechanicRegistration> SpecializedRegistrations();

    public static MechanicDescriptor? Find(string id) => All.SingleOrDefault(d => d.Id == id);
    public static MechanicDescriptor? FindByPayload(object? payload) => payload is null ? null
        : Registrations.SingleOrDefault(r => r.PayloadType == payload.GetType())?.Descriptor;

    public static ImmutableArray<MechanicDiagnostic> Validate(object? payload,
        MechanicValidationContext? context = null, string path = "mechanic")
    {
        List<MechanicDiagnostic> errors = [];
        var registration = payload is null ? null : Registrations.SingleOrDefault(r => r.PayloadType == payload.GetType());
        if (registration is null)
        {
            MechanicChecks.Error(errors, path, "unknownMechanic", "A supported typed mechanic is required.");
            return [.. errors];
        }
        registration.Validate(payload!, path, errors);
        if (context is not null) ValidateProfile(registration.Descriptor, context, path, errors);
        return [.. errors];
    }

    public static void ThrowIfInvalid(object? payload, MechanicValidationContext? context = null, string path = "mechanic")
    {
        var diagnostics = Validate(payload, context, path);
        if (!diagnostics.IsEmpty) throw new ArgumentException(string.Join(" ", diagnostics.Select(d => $"{d.Path}: {d.Message}")));
    }

    private static void ValidateProfile(MechanicDescriptor descriptor, MechanicValidationContext context,
        string path, List<MechanicDiagnostic> errors)
    {
        var profile = descriptor.Profiles.SingleOrDefault(p => p.Source == context.Source && p.Timing == context.Timing);
        if (profile is null)
            MechanicChecks.Error(errors, path, "unsupportedContext", "This mechanic does not support the requested source and timing.");
        else if (context.MaxUses is < 1 || (profile.Usage switch
        {
            MechanicUsageSupport.UnlimitedOnly or MechanicUsageSupport.NotApplicable => context.MaxUses is not null,
            MechanicUsageSupport.LimitedOnly => context.MaxUses is null,
            _ => false
        }))
            MechanicChecks.Error(errors, path + ".usage", "unsupportedUsage", "This mechanic does not support the requested use restriction.");
        if (!context.HasUnitContext && descriptor.Context is MechanicContext.ActingUnit or MechanicContext.BearingUnit or MechanicContext.CollectorUnit)
            MechanicChecks.Error(errors, path, "unitContextRequired", "This mechanic requires its declared Unit context.");
    }
}

internal sealed record MechanicRegistration(MechanicDescriptor Descriptor, Type PayloadType,
    Action<object, string, List<MechanicDiagnostic>> Validate)
{
    internal static MechanicRegistration Create<T>(MechanicDescriptor descriptor,
        Action<T, string, List<MechanicDiagnostic>> validate) =>
        new(descriptor, typeof(T), (payload, path, errors) => validate((T)payload, path, errors));
}

internal static class MechanicChecks
{
    internal static void Error(List<MechanicDiagnostic> errors, string path, string code, string message) =>
        errors.Add(new(path, code, message));
    internal static void Integer(List<MechanicDiagnostic> errors, MechanicParameter parameter, int value, string path)
    {
        if (parameter.Minimum is { } min && value < min)
            Error(errors, path, "minimum", $"Must be at least {min}.");
        if (parameter.Maximum is { } max && value > max)
            Error(errors, path, "maximum", $"Must be at most {max}.");
    }
    internal static void Enum<T>(List<MechanicDiagnostic> errors, MechanicParameter parameter, T value, string path) where T : struct, System.Enum
    {
        if (!parameter.AllowedValues.Contains(value.ToString(), StringComparer.Ordinal))
            Error(errors, path, "enum", "Must be one of the declared values.");
    }
    internal static bool List(List<MechanicDiagnostic> errors, MechanicParameter parameter, bool missing, int count, string path)
    {
        if (missing)
        {
            if (!parameter.AllowNull) Error(errors, path, "required", "A list is required.");
            return false;
        }
        if (parameter.MinimumItems is { } min && count < min)
            Error(errors, path, "minimumItems", $"Must contain at least {min} entries.");
        if (parameter.MaximumItems is { } max && count > max)
            Error(errors, path, "maximumItems", $"Must contain at most {max} entries.");
        return true;
    }
    internal static void Reference(List<MechanicDiagnostic> errors, MechanicParameter parameter, string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (parameter.Required) Error(errors, path, "required", "A reference is required.");
            return;
        }
        if (parameter.ReferenceCatalog == "resources" && !ResourceTypes.All.Any(r => r.Id == value))
            Error(errors, path, "unknownReference", "The resource reference does not resolve.");
    }
    internal static void Description(List<MechanicDiagnostic> errors, MechanicDescription policy, string text,
        string path, IReadOnlySet<string>? nonemptyLists = null)
    {
        if (!policy.AllowsOverride) { Error(errors, path, "overrideUnsupported", "This content does not accept a description override."); return; }
        if (policy.RejectsBlankOverride && string.IsNullOrWhiteSpace(text))
            Error(errors, path, "blankOverride", "The description override must not be blank.");
        if (policy.RejectsNumericOverrideLiterals && text.Any(char.IsDigit))
            Error(errors, path, "numericOverrideLiteral", "Insert authoritative parameter placeholders instead of numeric literals.");
        bool Applicable(string name) => !policy.NonemptyListOverridePlaceholders.Contains(name) || nonemptyLists?.Contains(name) == true;
        foreach (var name in policy.RequiredOverridePlaceholders.Where(Applicable))
            if (!text.Contains("{" + name + "}", StringComparison.Ordinal))
                Error(errors, path, "requiredPlaceholder", $"The description must include {{{name}}}.");
        if (policy.RejectsUnknownOverridePlaceholders)
        {
            var remainder = text;
            foreach (var name in policy.AllowedOverridePlaceholders.Where(Applicable))
                remainder = remainder.Replace("{" + name + "}", "", StringComparison.Ordinal);
            if (remainder.Contains('{') || remainder.Contains('}'))
                Error(errors, path, "unknownPlaceholder", "The description contains an unsupported or inapplicable placeholder.");
        }
    }
}
