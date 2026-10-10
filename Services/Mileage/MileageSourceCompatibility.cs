namespace CarScanner.Services.Mileage;

public enum MileageSourceFamily
{
    /// <summary>Mode 01 PID A6 (optional, advertised in the supported-PID bitmaps).</summary>
    StandardOdometerPid,

    /// <summary>Mode 01 PIDs 21 / 31 / 4D / 4E — context values, never the odometer.</summary>
    StandardContextPid,

    /// <summary>Read-only UDS / KWP request taken from the public OBDb catalog for this exact model.</summary>
    CatalogUdsRead,

    /// <summary>Passive: the car's own broadcast message decoded with a public definition.</summary>
    CanBroadcast,

    /// <summary>A place found earlier on this phone.</summary>
    LearnedLocation
}

public enum MileageSourceStatus
{
    Supported,
    PartiallySupported,
    Unsupported,
    CompatibilityUnknown
}

public enum MileageSourceReason
{
    PidAdvertised,
    PidNotAdvertised,
    AdvertisedListUnavailable,
    AdapterNotReady,
    NotCanProtocol,
    ManufacturerUnknown,
    ModelUnknown,
    ExactModelDefinition,
    NoExactModelDefinition,
    BrandLevelDefinition,
    NoDefinitionForBrand,
    LearnedHere,
    NoLearnedPlace
}

/// <summary>What is known about the connected car and adapter when deciding whether to try a source.</summary>
public sealed record MileageSourceFacts(
    bool ManufacturerKnown,
    bool ModelKnown,
    bool AdapterReady,
    bool IsCan,
    IReadOnlySet<int>? AdvertisedPids,
    int ExactModelCatalogSignals,
    bool BroadcastDefinitionExists,
    int LearnedPlaces);

public sealed record MileageSourceAssessment(
    MileageSourceFamily Family,
    MileageSourceStatus Status,
    MileageSourceReason Reason)
{
    /// <summary>Requests (or listening) happen only when this is true — an unsupported or unknown source is never tried.</summary>
    public bool ShouldTry => Status is MileageSourceStatus.Supported or MileageSourceStatus.PartiallySupported;
}

/// <summary>
/// Decides per source — before anything is sent — whether it is worth trying on this car. Nothing here is
/// "Supported" because of the brand alone: a community definition or a brand-level CAN frame is at most
/// "partially supported", and only an optional PID that the ECU itself advertises is "supported".
/// </summary>
public static class MileageSourceCompatibility
{
    public static MileageSourceAssessment Evaluate(MileageSourceFamily family, MileageSourceFacts facts, int pid = StandardMileageDecoder.OdometerPid)
    {
        MileageSourceAssessment Result(MileageSourceStatus status, MileageSourceReason reason) => new(family, status, reason);

        switch (family)
        {
            case MileageSourceFamily.StandardOdometerPid:
            case MileageSourceFamily.StandardContextPid:
                if (!facts.AdapterReady)
                    return Result(MileageSourceStatus.CompatibilityUnknown, MileageSourceReason.AdapterNotReady);
                if (facts.AdvertisedPids is null)
                    return Result(MileageSourceStatus.CompatibilityUnknown, MileageSourceReason.AdvertisedListUnavailable);
                return facts.AdvertisedPids.Contains(pid)
                    ? Result(MileageSourceStatus.Supported, MileageSourceReason.PidAdvertised)
                    : Result(MileageSourceStatus.Unsupported, MileageSourceReason.PidNotAdvertised);

            case MileageSourceFamily.CatalogUdsRead:
                if (!facts.AdapterReady)
                    return Result(MileageSourceStatus.CompatibilityUnknown, MileageSourceReason.AdapterNotReady);
                if (!facts.IsCan)
                    return Result(MileageSourceStatus.Unsupported, MileageSourceReason.NotCanProtocol);
                if (!facts.ManufacturerKnown)
                    return Result(MileageSourceStatus.CompatibilityUnknown, MileageSourceReason.ManufacturerUnknown);
                if (!facts.ModelKnown)
                    return Result(MileageSourceStatus.CompatibilityUnknown, MileageSourceReason.ModelUnknown);
                return facts.ExactModelCatalogSignals > 0
                    ? Result(MileageSourceStatus.PartiallySupported, MileageSourceReason.ExactModelDefinition)
                    : Result(MileageSourceStatus.Unsupported, MileageSourceReason.NoExactModelDefinition);

            case MileageSourceFamily.CanBroadcast:
                if (!facts.AdapterReady)
                    return Result(MileageSourceStatus.CompatibilityUnknown, MileageSourceReason.AdapterNotReady);
                if (!facts.IsCan)
                    return Result(MileageSourceStatus.Unsupported, MileageSourceReason.NotCanProtocol);
                if (!facts.ManufacturerKnown)
                    return Result(MileageSourceStatus.CompatibilityUnknown, MileageSourceReason.ManufacturerUnknown);
                return facts.BroadcastDefinitionExists
                    ? Result(MileageSourceStatus.PartiallySupported, MileageSourceReason.BrandLevelDefinition)
                    : Result(MileageSourceStatus.Unsupported, MileageSourceReason.NoDefinitionForBrand);

            default:
                if (!facts.AdapterReady)
                    return Result(MileageSourceStatus.CompatibilityUnknown, MileageSourceReason.AdapterNotReady);
                if (!facts.IsCan)
                    return Result(MileageSourceStatus.Unsupported, MileageSourceReason.NotCanProtocol);
                return facts.LearnedPlaces > 0
                    ? Result(MileageSourceStatus.PartiallySupported, MileageSourceReason.LearnedHere)
                    : Result(MileageSourceStatus.Unsupported, MileageSourceReason.NoLearnedPlace);
        }
    }
}
