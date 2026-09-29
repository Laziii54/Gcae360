namespace CarScanner.Services.Mileage;

public sealed class VerifiedMileageProfileRegistry
{
    private readonly List<VerifiedUdsMileageProfile> _uds = new();
    private readonly List<VerifiedCanMileageProfile> _can = new();

    public IReadOnlyList<VerifiedUdsMileageProfile> UdsProfiles => _uds;
    public IReadOnlyList<VerifiedCanMileageProfile> CanProfiles => _can;

    public void RegisterUds(VerifiedUdsMileageProfile profile) { ValidateUds(profile); _uds.Add(profile); }
    public void RegisterCan(VerifiedCanMileageProfile profile) { ValidateCan(profile); _can.Add(profile); }

    public IReadOnlyList<VerifiedUdsMileageProfile> FindUds(string? manufacturer, string? model, string? year) =>
        _uds.Where(x => Same(x.Brand, manufacturer) && ProfileMatches(x.VehicleProfile, model, year)).ToArray();

    public IReadOnlyList<VerifiedCanMileageProfile> FindCan(string? manufacturer, string? model, string? year) =>
        _can.Where(x => Same(x.Brand, manufacturer) && ProfileMatches(x.VehicleProfile, model, year)).ToArray();

    private static bool ProfileMatches(string profile, string? model, string? year)
    {
        if (profile == "*") return true;
        var m = model?.Trim() ?? string.Empty;
        var y = year?.Trim() ?? string.Empty;
        return string.Equals(profile, $"{m} {y}".Trim(), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(profile, m, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Same(string a, string? b) =>
        !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void ValidateUds(VerifiedUdsMileageProfile p)
    {
        if (string.IsNullOrWhiteSpace(p.Brand) || string.IsNullOrWhiteSpace(p.VehicleProfile) ||
            string.IsNullOrWhiteSpace(p.Ecu) || string.IsNullOrWhiteSpace(p.Source) ||
            string.IsNullOrWhiteSpace(p.ByteOrder) || string.IsNullOrWhiteSpace(p.NumericEncoding))
            throw new ArgumentException("UDS mileage profile is incomplete.");
        var maxCan = p.Extended29Bit ? 0x1FFFFFFF : 0x7FF;
        if (p.TxCanId < 0 || p.RxCanId < 0 || p.TxCanId > maxCan || p.RxCanId > maxCan)
            throw new ArgumentOutOfRangeException(nameof(p), "Invalid UDS CAN identifiers.");
        if (p.DataOffset < 0 || p.DataLength <= 0 || p.DataLength > 8)
            throw new ArgumentOutOfRangeException(nameof(p), "Invalid UDS data range.");
        if (p.MinKm < 0 || p.MaxKm <= p.MinKm || p.Confidence <= 0 || p.Confidence > 1 ||
            !double.IsFinite(p.Scale) || !double.IsFinite(p.Offset))
            throw new ArgumentOutOfRangeException(nameof(p), "Invalid UDS mileage profile.");
    }

    private static void ValidateCan(VerifiedCanMileageProfile p)
    {
        if (string.IsNullOrWhiteSpace(p.Brand) || string.IsNullOrWhiteSpace(p.VehicleProfile) ||
            string.IsNullOrWhiteSpace(p.Ecu) || string.IsNullOrWhiteSpace(p.Source))
            throw new ArgumentException("CAN mileage profile is incomplete.");
        var maxCan = p.Extended29Bit ? 0x1FFFFFFF : 0x7FF;
        if (p.CanId > maxCan || p.StartBit < 0 || p.BitLength <= 0 || p.BitLength > 64 || p.StartBit + p.BitLength > 64)
            throw new ArgumentOutOfRangeException(nameof(p), "Invalid CAN mileage signal definition.");
        if (p.MinKm < 0 || p.MaxKm <= p.MinKm || p.Confidence <= 0 || p.Confidence > 1 ||
            !double.IsFinite(p.Factor) || !double.IsFinite(p.Offset))
            throw new ArgumentOutOfRangeException(nameof(p), "Invalid CAN mileage profile.");
    }
}
