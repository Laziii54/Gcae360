using CarScanner.Models;

namespace CarScanner.Helpers;

public static class ErrorCodeDatabase
{
    private static readonly Dictionary<string, ErrorCode> Codes = new()
    {
        ["P0300"] = new()
        {
            Code = "P0300",
            TitleKa = "შემთხვევითი/მრავალჯერადი გაცდენა",
            TitleEn = "Random/Multiple Cylinder Misfire Detected",
            DescriptionKa = "ძრავა აღმოაჩენს შემთხვევით ან მრავალ ცილინდრზე გაცდენას.",
            DescriptionEn = "The engine detects random or multiple-cylinder misfire.",
            Severity = "საშუალო",
            PossibleCausesKa = "• აალების სისტემა\n• საწვავის ინჟექტორები\n• კომპრესია\n• ჰაერის გაჟონვა",
            PossibleCausesEn = "• Ignition system\n• Fuel injectors\n• Compression\n• Air leak"
        },
        ["P0301"] = new()
        {
            Code = "P0301",
            TitleKa = "ცილინდრი 1 – გაცდენა",
            TitleEn = "Cylinder 1 Misfire Detected",
            DescriptionKa = "პირველ ცილინდრზე გაცდენა აღმოჩენილია.",
            DescriptionEn = "Misfire detected on cylinder 1.",
            Severity = "საშუალო",
            PossibleCausesKa = "• სანთელი\n• კოჭი\n• ინჟექტორი\n• კომპრესია",
            PossibleCausesEn = "• Spark plug\n• Ignition coil\n• Injector\n• Compression"
        },
        ["P0171"] = new()
        {
            Code = "P0171",
            TitleKa = "სისტემა ზედმეტად ღარიბია (ბანკი 1)",
            TitleEn = "System Too Lean (Bank 1)",
            DescriptionKa = "ძრავის მართვის ერთეული აღმოაჩენს, რომ საწვავის ნაზავი ზედმეტად ღარიბია.",
            DescriptionEn = "The engine control system detects a mixture that is too lean.",
            Severity = "საშუალო",
            PossibleCausesKa = "• ჰაერის გაჟონვა\n• MAF სენსორი\n• საწვავის წნევა\n• O2 სენსორი",
            PossibleCausesEn = "• Air leak\n• MAF sensor\n• Fuel pressure\n• O2 sensor"
        },
        ["P0420"] = new()
        {
            Code = "P0420",
            TitleKa = "კატალიზატორის ეფექტურობა დაბალია (ბანკი 1)",
            TitleEn = "Catalyst System Efficiency Below Threshold (Bank 1)",
            DescriptionKa = "კატალიზატორის მუშაობა ნორმალურ მაჩვენებელს ვერ აღწევს.",
            DescriptionEn = "Catalyst efficiency is below the expected threshold.",
            Severity = "საშუალო",
            PossibleCausesKa = "• კატალიზატორი\n• ჟანგბადის სენსორი\n• გამონაბოლქვის გაჟონვა\n• ძრავის გაუმართაობა",
            PossibleCausesEn = "• Catalytic converter\n• Oxygen sensor\n• Exhaust leak\n• Engine fault"
        },
        ["P0442"] = new()
        {
            Code = "P0442",
            TitleKa = "EVAP სისტემის მცირე გაჟონვა",
            TitleEn = "EVAP System Small Leak Detected",
            DescriptionKa = "საწვავის ორთქლის რეცირკულაციის სისტემაში მცირე გაჟონვა აღმოჩენილია.",
            DescriptionEn = "A small leak has been detected in the EVAP system.",
            Severity = "კარგი",
            PossibleCausesKa = "• საწვავის თავსახური\n• EVAP შლანგები\n• კანისტერი",
            PossibleCausesEn = "• Fuel cap\n• EVAP hoses\n• Canister"
        },
        ["P0401"] = new()
        {
            Code = "P0401",
            TitleKa = "EGR ნაკადი არასაკმარისი",
            TitleEn = "Exhaust Gas Recirculation Flow Insufficient",
            DescriptionKa = "EGR სისტემის ნაკადი მოსალოდნელზე ნაკლებია.",
            DescriptionEn = "EGR flow is lower than expected.",
            Severity = "საშუალო",
            PossibleCausesKa = "• EGR სარქველი\n• EGR არხები\n• სენსორი",
            PossibleCausesEn = "• EGR valve\n• EGR passages\n• Sensor"
        },
        ["P0128"] = new()
        {
            Code = "P0128",
            TitleKa = "გაგრილების სითხის ტემპერატურა დაბალია",
            TitleEn = "Coolant Temperature Below Expected Threshold",
            DescriptionKa = "თერმოსტატი შესაძლოა ღია დარჩა.",
            DescriptionEn = "The thermostat may be stuck open.",
            Severity = "კარგი",
            PossibleCausesKa = "• თერმოსტატი\n• გაგრილების სენსორი",
            PossibleCausesEn = "• Thermostat\n• Coolant temperature sensor"
        },
        ["P0455"] = new()
        {
            Code = "P0455",
            TitleKa = "EVAP სისტემის დიდი გაჟონვა",
            TitleEn = "EVAP System Large Leak Detected",
            DescriptionKa = "საწვავის ორთქლის სისტემაში დიდი გაჟონვა.",
            DescriptionEn = "A large leak is detected in the EVAP system.",
            Severity = "საშუალო",
            PossibleCausesKa = "• საწვავის თავსახური\n• შლანგები\n• კანისტერი",
            PossibleCausesEn = "• Fuel cap\n• Hoses\n• Canister"
        }
    };

    public static ErrorCode? Get(string code)
    {
        code = code.Trim().ToUpperInvariant();
        return Codes.TryGetValue(code, out var ec) ? ec : null;
    }

    public static ErrorCode GetOrDefault(string code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (Get(normalized) is { } known) return known;

        var category = normalized.Length > 0 ? normalized[0] : '?';
        var title = category switch
        {
            'P' => "Powertrain / ძრავისა და ტრანსმისიის კოდი",
            'C' => "Chassis / შასის სისტემის კოდი",
            'B' => "Body / კორპუსის სისტემის კოდი",
            'U' => "Network / კომუნიკაციის კოდი",
            _ => "უცნობი დიაგნოსტიკური კოდი"
        };
        var causes = category switch
        {
            'P' => "კოდი დადასტურებულია, მაგრამ ამ კონკრეტული კოდის სრული კატალოგი არ არის ლოკალურ ბაზაში. შეამოწმე OEM მონაცემები, Freeze Frame და შესაბამისი Live Data.",
            'C' => "შასის/ABS/steering სისტემებთან დაკავშირებული მიზეზი შეიძლება იყოს. საჭიროა შესაბამისი ECU-ს OEM დიაგნოსტიკა.",
            'B' => "Body/BCM/SRS/კომფორტის სისტემასთან დაკავშირებული მიზეზი შეიძლება იყოს. საჭიროა შესაბამისი მოდულის სკანირება.",
            'U' => "ECU-ებს შორის კომუნიკაციის პრობლემა შეიძლება იყოს. შეამოწმე კვება, ground, CAN და შესაბამისი მოდულების კომუნიკაცია.",
            _ => "კოდი ECU-მ დააბრუნა, მაგრამ ფორმატი/კატეგორია ვერ განისაზღვრა. საჭიროა raw response-ისა და OEM მონაცემების შემოწმება."
        };
        return new ErrorCode
        {
            Code = normalized,
            TitleKa = title,
            TitleEn = category switch { 'P' => "Powertrain diagnostic trouble code", 'C' => "Chassis diagnostic trouble code", 'B' => "Body diagnostic trouble code", 'U' => "Network diagnostic trouble code", _ => "Unknown diagnostic trouble code" },
            DescriptionKa = "კოდი რეალურად წაკითხულია ECU-დან, მაგრამ მისი სრული აღწერა ლოკალურ კატალოგში ჯერ არ არის.",
            DescriptionEn = "The ECU reported this code, but a full local catalog description is not available.",
            PossibleCausesEn = "The actual cause must be confirmed using freeze-frame data, live data and the appropriate OEM diagnostic procedure.",
            Severity = "უცნობი",
            PossibleCausesKa = causes
        };
    }

    public static IEnumerable<ErrorCode> GetAll() => Codes.Values;
}