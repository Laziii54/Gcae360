namespace CarScanner.Services.OemDiagnostics;

public sealed record GmVoltEcuAddress(string EcuId,int TxId,int RxId,string VerificationSource,bool VerifiedForVehicle);

public sealed class GmVoltAddressProfile
{
    private readonly Dictionary<string,GmVoltEcuAddress> _addresses = new(StringComparer.OrdinalIgnoreCase);
    public void RegisterVerified(string ecuId,int txId,int rxId,string source)
    {
        if(txId is < 0 or > 0x7FF || rxId is < 0 or > 0x7FF) throw new ArgumentOutOfRangeException();
        if(string.IsNullOrWhiteSpace(source)) throw new ArgumentException("A verification source is required.",nameof(source));
        _addresses[ecuId]=new(ecuId,txId,rxId,source,true);
    }
    public bool TryGetVerified(string ecuId,out GmVoltEcuAddress address)=>_addresses.TryGetValue(ecuId,out address!) && address.VerifiedForVehicle;
    public IReadOnlyList<GmVoltEcuAddress> All => _addresses.Values.ToArray();
}
