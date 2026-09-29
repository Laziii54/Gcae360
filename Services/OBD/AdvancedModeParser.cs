using CarScanner.Models;
namespace CarScanner.Services.OBD;

public static class AdvancedModeParser
{
    public static IReadOnlyList<ObdTestItem> ParseMode05(string raw)=>ParseMode05Internal(raw);
    public static IReadOnlyList<Mode05MonitorResult> ParseMode05Detailed(string raw)
    {
        var payload = ObdResponseParser.ExtractPayload(raw, 0x45);
        if (payload.Length == 0)
            return Array.Empty<Mode05MonitorResult>();

        var result = new List<Mode05MonitorResult>();
        for (var index = 0; index + 5 < payload.Length; index += 6)
        {
            var testId = payload[index];
            var actual = (payload[index + 1] << 8) | payload[index + 2];
            var limit = (payload[index + 3] << 8) | payload[index + 4];
            result.Add(new Mode05MonitorResult(testId, actual, limit, payload[index + 5]));
        }
        return result;
    }
    public static IReadOnlyList<ObdTestItem> ParseMode06(string raw)=>ParseMode06Internal(raw);
    public static IReadOnlyList<Mode06MonitorResult> ParseMode06Detailed(string raw)
    {
        var payload = ObdResponseParser.ExtractPayload(raw, 0x46);
        if (payload.Length == 0)
            return Array.Empty<Mode06MonitorResult>();

        var result = new List<Mode06MonitorResult>();
        for (var i = 0; i + 9 < payload.Length; i += 10)
        {
            var testId = payload[i];
            var componentId = payload[i + 1];
            var minimum = (payload[i + 2] << 8) | payload[i + 3];
            var maximum = (payload[i + 4] << 8) | payload[i + 5];
            var actual = (payload[i + 6] << 8) | payload[i + 7];
            var auxiliary = (payload[i + 8] << 8) | payload[i + 9];
            result.Add(new Mode06MonitorResult(testId, componentId, minimum, maximum, actual, auxiliary));
        }

        return result;
    }

    private static IReadOnlyList<ObdTestItem> ParseMode05Internal(string raw)
    {
        var payload=ObdResponseParser.ExtractPayload(raw,0x45); if(payload.Length==0)return Array.Empty<ObdTestItem>();
        var result=new List<ObdTestItem>();
        for(int i=0;i+5<payload.Length;i+=6)
        {
            var id=payload[i]; var actual=(payload[i+1]<<8)|payload[i+2]; var limit=(payload[i+3]<<8)|payload[i+4]; var status=payload[i+5];
            var state=(status&0x01)!=0?"FAIL":"PASS/NOT-FLAGGED";
            result.Add(new("Mode 05",$"Sensor/Test 0x{id:X2}",$"Actual={actual}; Limit={limit}; Status=0x{status:X2}",state,"O2 monitor მონაცემი. ზუსტი ერთეული და scaling დამოკიდებულია ECU-ის test definition-ზე."));
        }
        return result;
    }

    private static IReadOnlyList<ObdTestItem> ParseMode06Internal(string raw)
    {
        var payload=ObdResponseParser.ExtractPayload(raw,0x46); if(payload.Length==0)return Array.Empty<ObdTestItem>();
        var result=new List<ObdTestItem>();
        for(int i=0;i+9<payload.Length;i+=10)
        {
            var tid=payload[i]; var cid=payload[i+1]; var min=(payload[i+2]<<8)|payload[i+3]; var max=(payload[i+4]<<8)|payload[i+5]; var actual=(payload[i+6]<<8)|payload[i+7]; var aux=(payload[i+8]<<8)|payload[i+9];
            var pass=actual>=min&&actual<=max;
            result.Add(new("Mode 06",$"TID 0x{tid:X2} / CID 0x{cid:X2}",$"MIN={min}; MAX={max}; ACTUAL={actual}; AUX={aux}",pass?"PASS_RANGE":"FAIL_RANGE","Generic monitor value. ერთეული/scaling უნდა განისაზღვროს კონკრეტული test definition-ით."));
        }
        return result;
    }
}
