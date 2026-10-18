using CarScanner.Models;
using CarScanner.Services.OBD;
namespace CarScanner.Services;
public sealed class AdvancedDiagnosticService
{
    private readonly OBDService _obd;
    public AdvancedDiagnosticService(OBDService obd)=>_obd=obd;
    /// <param name="includeVin">False skips Mode 09 PID 02 (VIN) entirely — used where the VIN must never appear
    /// in the returned list, e.g. before feeding it anywhere that is not the vehicle's own already-known VIN.</param>
    public async Task<IReadOnlyList<EcuIdentifier>> ReadIdentifiersAsync(bool includeVin=true, CancellationToken token=default)
    {
        _obd.EnsureInitialized(); var list=new List<EcuIdentifier>();
        var items=new[]{(2,"VIN კოდი","მანქანის უნიკალური საიდენტიფიკაციო ნომერი"),(4,"კალიბრაციის ID","ECU-ს პროგრამული ვერსიის იდენტიფიკატორი"),(6,"CVN","ECU პროგრამის შემოწმების ნომერი"),(10,"ECU-ს სახელი","კონტროლერის/მოდულის სახელი")}
            .Where(item=>includeVin||item.Item1!=2);
        foreach(var item in items)
        {
            try{var raw=await _obd.ReadMode09QuickAsync(item.Item1,token);var text=item.Item1 is 4 or 6 or 10?ObdResponseParser.FormatMode09(raw,(byte)item.Item1):ObdResponseParser.DecodeAsciiFromMode09(raw,(byte)item.Item1);list.Add(new EcuIdentifier{Name=item.Item2,Value=string.IsNullOrWhiteSpace(text)?"მიუწვდომელია":text,Explanation=item.Item3});}
            catch{list.Add(new EcuIdentifier{Name=item.Item2,Value="მიუწვდომელია",Explanation=item.Item3});}
        }
        list.Add(new EcuIdentifier{Name="კავშირის პროტოკოლი",Value=_obd.CurrentProtocolDescription,Explanation="გზა, რომლითაც აპი ECU-სგან მონაცემებს იღებს."});
        return list;
    }
}
