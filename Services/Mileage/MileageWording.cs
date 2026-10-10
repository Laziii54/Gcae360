namespace CarScanner.Services.Mileage;

/// <summary>
/// Wording for the Mileage page (Georgian / English). The strongest claim it ever makes is "reported by …":
/// diagnostic data cannot establish the historically authentic mileage, and no text here says it does.
/// </summary>
public static class MileageWording
{
    public static string Measurement(MileageMeasurement measurement, bool en) => measurement switch
    {
        MileageMeasurement.TotalOdometer => en ? "Total odometer" : "ჯამური ოდომეტრი",
        MileageMeasurement.DistanceSinceDtcClear => en ? "Distance since trouble codes were cleared (not the odometer)" : "მანძილი კოდების წაშლის შემდეგ (არა ოდომეტრი)",
        MileageMeasurement.DistanceWithMilOn => en ? "Distance driven with the check-engine light on (not the odometer)" : "მანძილი ჩართული Check Engine-ით (არა ოდომეტრი)",
        MileageMeasurement.TripDistance => en ? "Trip distance (not the odometer)" : "მოგზაურობის მანძილი (არა ოდომეტრი)",
        MileageMeasurement.EngineOperatingHours => en ? "Engine operating hours (not a distance)" : "ძრავის მუშაობის საათები (არა მანძილი)",
        MileageMeasurement.MinutesSinceDiagnosticEvent => en ? "Time since a diagnostic event (not a distance)" : "დრო დიაგნოსტიკური მოვლენიდან (არა მანძილი)",
        MileageMeasurement.ElectricDriveDistance => en ? "Electric drive distance (not the odometer)" : "ელექტრო მოძრაობის მანძილი (არა ოდომეტრი)",
        MileageMeasurement.EstimatedDistance => en ? "Estimated distance (a calculation, not a reading)" : "სავარაუდო მანძილი (გამოთვლა და არა ჩვენება)",
        _ => en ? "Other distance value (not the odometer)" : "სხვა მანძილის მნიშვნელობა (არა ოდომეტრი)"
    };

    public static string MeasurementShort(MileageMeasurement measurement, bool en) => measurement switch
    {
        MileageMeasurement.TotalOdometer => en ? "Odometer" : "ოდომეტრი",
        MileageMeasurement.DistanceSinceDtcClear => en ? "Since codes cleared" : "კოდების წაშლიდან",
        MileageMeasurement.DistanceWithMilOn => en ? "With MIL on" : "MIL-ით",
        MileageMeasurement.TripDistance => en ? "Trip" : "მოგზაურობა",
        MileageMeasurement.EngineOperatingHours => en ? "Engine hours" : "ძრავის საათები",
        MileageMeasurement.MinutesSinceDiagnosticEvent => en ? "Time" : "დრო",
        MileageMeasurement.ElectricDriveDistance => en ? "Electric drive" : "ელექტრო",
        MileageMeasurement.EstimatedDistance => en ? "Estimate" : "შეფასება",
        _ => en ? "Other" : "სხვა"
    };

    public static string SourceKind(MileageSourceKind kind, bool en) => kind switch
    {
        MileageSourceKind.StandardObd => en
            ? "Standard OBD-II PID (SAE J1979). Which ECU answered is not identified."
            : "სტანდარტული OBD-II PID (SAE J1979). რომელმა ECU-მ უპასუხა, ცნობილი არ არის.",
        MileageSourceKind.PublicDefinition => en
            ? "Public community definition (OBDb catalog) for this model — not tested by GCar360 on a real vehicle."
            : "საჯარო საზოგადოებრივი აღწერა (OBDb) ამ მოდელისთვის — GCar360-ს რეალურ მანქანაზე არ შეუმოწმებია.",
        MileageSourceKind.CanBroadcast => en
            ? "Message the car broadcasts, decoded with a public definition (opendbc / OVMS) — nothing was requested."
            : "მანქანის თვითნაკადი შეტყობინება, საჯარო აღწერით (opendbc / OVMS) — არაფერი მოუთხოვიათ.",
        _ => en
            ? "A place found earlier on this phone by matching a number you typed — unverified."
            : "ამ ტელეფონზე ადრე ნაპოვნი ადგილი შენს მიერ ჩაწერილი რიცხვის მიხედვით — დაუდასტურებელია."
    };

    public static string ReadStatus(MileageReadStatus status, bool en) => status switch
    {
        MileageReadStatus.Ok => en ? "Read" : "წაკითხულია",
        MileageReadStatus.Implausible => en ? "Implausible value — flagged for review, not used" : "არარეალური მნიშვნელობა — გადასამოწმებელია, არ გამოიყენება",
        _ => en ? "Malformed or incomplete reply — not used" : "დაზიანებული ან არასრული პასუხი — არ გამოიყენება"
    };

    public static string SourceStatus(MileageSourceStatus status, bool en) => status switch
    {
        MileageSourceStatus.Supported => en ? "Supported" : "მხარდაჭერილია",
        MileageSourceStatus.PartiallySupported => en ? "Partially supported" : "ნაწილობრივ",
        MileageSourceStatus.Unsupported => en ? "Unsupported" : "მხარდაუჭერელია",
        _ => en ? "Compatibility unknown" : "თავსებადობა უცნობია"
    };

    public static string SourceReason(MileageSourceReason reason, bool en) => reason switch
    {
        MileageSourceReason.PidAdvertised => en ? "The ECU lists this PID as supported." : "ECU ამ PID-ს მხარდაჭერილად აცხადებს.",
        MileageSourceReason.PidNotAdvertised => en ? "The ECU does not list this PID — nothing was requested." : "ECU ამ PID-ს არ აცხადებს — არაფერი მოუთხოვიათ.",
        MileageSourceReason.AdvertisedListUnavailable => en ? "The supported-PID list could not be read — nothing was requested." : "მხარდაჭერილი PID-ების სია ვერ წაიკითხა — არაფერი მოუთხოვიათ.",
        MileageSourceReason.AdapterNotReady => en ? "The adapter is not ready." : "ადაპტერი მზად არ არის.",
        MileageSourceReason.NotCanProtocol => en ? "Needs a CAN connection; this car uses another protocol." : "საჭიროა CAN კავშირი; ეს მანქანა სხვა პროტოკოლს იყენებს.",
        MileageSourceReason.ManufacturerUnknown => en ? "The manufacturer is not identified — nothing was requested." : "მწარმოებელი ამოცნობილი არ არის — არაფერი მოუთხოვიათ.",
        MileageSourceReason.ModelUnknown => en ? "The model is not identified, so no model-specific request can be chosen — nothing was requested." : "მოდელი ამოცნობილი არ არის, ამიტომ მოდელის მოთხოვნა ვერ შეირჩევა — არაფერი მოუთხოვიათ.",
        MileageSourceReason.ExactModelDefinition => en ? "A public definition exists for this exact model; it has not been verified on a real vehicle by GCar360." : "ამ ზუსტი მოდელისთვის საჯარო აღწერა არსებობს; GCar360-ს რეალურ მანქანაზე არ შეუმოწმებია.",
        MileageSourceReason.NoExactModelDefinition => en ? "No definition for this exact model — requests from other models are not used." : "ამ ზუსტი მოდელისთვის აღწერა არ არის — სხვა მოდელების მოთხოვნები არ გამოიყენება.",
        MileageSourceReason.BrandLevelDefinition => en ? "A brand-level message definition exists; your exact model is not confirmed." : "მარკის დონის შეტყობინების აღწერა არსებობს; შენი ზუსტი მოდელი დადასტურებული არ არის.",
        MileageSourceReason.NoDefinitionForBrand => en ? "No broadcast definition for this brand." : "ამ მარკისთვის თვითნაკადი შეტყობინების აღწერა არ არის.",
        MileageSourceReason.LearnedHere => en ? "A place found earlier on this phone for this model." : "ამ მოდელისთვის ამ ტელეფონზე ადრე ნაპოვნი ადგილი.",
        _ => en ? "No place saved on this phone for this model." : "ამ მოდელისთვის ამ ტელეფონზე შენახული ადგილი არ არის."
    };

    public static string Headline(MileageOutcome outcome, bool en) => outcome switch
    {
        MileageOutcome.NoReading => en ? "No odometer reading could be retrieved" : "ოდომეტრის მაჩვენებელი ვერ მოიძებნა",
        MileageOutcome.SingleSource => en ? "One module-reported odometer reading" : "მოდულის მიერ მოწოდებული ერთი ოდომეტრის მაჩვენებელი",
        MileageOutcome.Consistent => en ? "Readings are consistent with each other" : "მაჩვენებლები ერთმანეთს ეთანხმება",
        _ => en ? "Readings differ" : "მაჩვენებლები განსხვავდება"
    };

    public static string Explanation(MileageOutcome outcome, bool en) => outcome switch
    {
        MileageOutcome.NoReading => en
            ? "The car did not return a usable total odometer value through the methods that apply to it. That is not a finding about the car's mileage."
            : "მანქანამ მასზე მისადაგებული მეთოდებით გამოსადეგი ჯამური ოდომეტრი არ დააბრუნა. ეს მანქანის გარბენის შესახებ დასკვნა არ არის.",
        MileageOutcome.SingleSource => en
            ? "Only one source answered, so there is nothing to compare it with. It is the value that source reports, not a confirmation of the real mileage."
            : "მხოლოდ ერთმა წყარომ უპასუხა, ამიტომ შესადარებელი არაფერია. ეს არის ამ წყაროს ჩვენება და არა რეალური გარბენის დადასტურება.",
        MileageOutcome.Consistent => en
            ? "These readings agree within the tolerance. That is all it means: agreement between modules does not prove the mileage is authentic, because a change made in every module would also agree."
            : "ეს მაჩვენებლები დასაშვებ ფარგლებში ემთხვევა. მეტს ეს არაფერს ნიშნავს: მოდულების თანხვედრა გარბენის ნამდვილობას არ ადასტურებს, რადგან ყველა მოდულში შეტანილი ცვლილებაც თანხვდებოდა.",
        _ => en
            ? "Each reading is shown on its own and none is chosen as correct. Differences can have ordinary causes — a replaced or reprogrammed module, different update timing, a unit or decoding difference — and need further investigation (service records, a workshop check). This is not a finding of tampering."
            : "თითოეული მაჩვენებელი ცალკეა ნაჩვენები და არცერთი არ არის არჩეული სწორად. სხვაობას ჩვეულებრივი მიზეზებიც შეიძლება ჰქონდეს — შეცვლილი ან გადაპროგრამებული მოდული, განახლების დროის სხვაობა, ერთეულის ან გაშიფვრის განსხვავება — და საჭიროა დამატებითი შემოწმება (სერვისის ჩანაწერები, სერვისი). ეს არ არის გადახვევის დასკვნა."
    };

    public static string Disclaimer(bool en) => en
        ? "Diagnostic data shows what the car's modules report today. It cannot establish the vehicle's historically authentic mileage."
        : "დიაგნოსტიკური მონაცემი აჩვენებს, რას აცხადებს მანქანის მოდულები ახლა. ის მანქანის ისტორიულად ნამდვილ გარბენს ვერ ადასტურებს.";

    public static string ContextNote(bool en) => en
        ? "These values are shown for context. They are not odometer readings and are not compared with them."
        : "ეს მნიშვნელობები კონტექსტისთვისაა ნაჩვენები. ისინი ოდომეტრის მაჩვენებლები არ არის და მათთან არ შედარდება.";

    public static string PowertrainNote(bool electricOrHybrid, bool en) => !electricOrHybrid
        ? string.Empty
        : en
            ? "Electric / hybrid vehicle: only total-odometer sources are compared. Electric drive distance, trip counters and engine hours are different measurements and are not converted into mileage."
            : "ელექტრო / ჰიბრიდული მანქანა: შედარდება მხოლოდ ჯამური ოდომეტრის წყაროები. ელექტრო მოძრაობის მანძილი, მოგზაურობის მრიცხველები და ძრავის საათები სხვა სიდიდეებია და გარბენად არ გარდაიქმნება.";

    public static string FormatValue(MileageEvidence evidence, bool en)
    {
        var value = evidence.DisplayValue;
        var number = value.ToString(value % 1 == 0 ? "N0" : "N1", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');
        var unit = evidence.Unit switch
        {
            "mi" => en ? "mi" : "მილი",
            "h" => en ? "h" : "სთ",
            "min" => en ? "min" : "წთ",
            _ => en ? "km" : "კმ"
        };
        return $"{number} {unit}";
    }
}
