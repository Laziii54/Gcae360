using CarScanner.Models;
using CarScanner.Services.OBD;
namespace CarScanner.Services;
public sealed class AdvancedDiagnosticService
{
    private readonly OBDService _obd;
    public AdvancedDiagnosticService(OBDService obd)=>_obd=obd;
    public async Task<IReadOnlyList<EcuIdentifier>> ReadIdentifiersAsync(CancellationToken token=default)
    {
        _obd.EnsureInitialized(); var list=new List<EcuIdentifier>();
        var items=new[]{(2,"VIN კოდი","მანქანის უნიკალური საიდენტიფიკაციო ნომერი"),(4,"კალიბრაციის ID","ECU-ს პროგრამული ვერსიის იდენტიფიკატორი"),(6,"CVN","ECU პროგრამის შემოწმების ნომერი"),(10,"ECU-ს სახელი","კონტროლერის/მოდულის სახელი")};
        foreach(var item in items)
        {
            try{var raw=await _obd.ReadMode09QuickAsync(item.Item1,token);var text=item.Item1==6?ObdResponseParser.DecodeHexFromMode09(raw,(byte)item.Item1):ObdResponseParser.DecodeAsciiFromMode09(raw,(byte)item.Item1);list.Add(new EcuIdentifier{Name=item.Item2,Value=string.IsNullOrWhiteSpace(text)?"მიუწვდომელია":text,Explanation=item.Item3});}
            catch{list.Add(new EcuIdentifier{Name=item.Item2,Value="მიუწვდომელია",Explanation=item.Item3});}
        }
        list.Add(new EcuIdentifier{Name="კავშირის პროტოკოლი",Value=_obd.CurrentProtocolDescription,Explanation="გზა, რომლითაც აპი ECU-სგან მონაცემებს იღებს."});
        return list;
    }
}
