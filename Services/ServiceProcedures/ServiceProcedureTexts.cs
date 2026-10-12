namespace CarScanner.Services.ServiceProcedures;

/// <summary>Wording for the Service page (Georgian / English), kept out of the view model so it can be tested.</summary>
public static class ServiceProcedureTexts
{
    public static string Kind(ServiceKind kind, bool en) => kind switch
    {
        ServiceKind.OilService => en ? "Oil service" : "ზეთის სერვისი",
        ServiceKind.MaintenanceReset => en ? "Maintenance reset" : "სერვისის განულება",
        ServiceKind.Inspection => en ? "Inspection" : "ინსპექცია",
        ServiceKind.TirePressure => en ? "Tire pressure" : "საბურავის წნევა",
        ServiceKind.Battery => en ? "Battery" : "აკუმულატორი",
        ServiceKind.ParkingBrake => en ? "Parking brake" : "ხელის მუხრუჭი",
        _ => en ? "Other" : "სხვა"
    };

    public static string Status(ServiceCompatibilityStatus status, bool en) => status switch
    {
        ServiceCompatibilityStatus.Supported => en ? "Supported" : "მხარდაჭერილია",
        ServiceCompatibilityStatus.ManualProcedureOnly => en ? "Manual procedure" : "ხელით პროცედურა",
        ServiceCompatibilityStatus.PartiallySupported => en ? "Partially supported" : "ნაწილობრივ",
        ServiceCompatibilityStatus.CompatibilityUnknown => en ? "Compatibility unknown" : "თავსებადობა უცნობია",
        _ => en ? "Not supported" : "არ არის მხარდაჭერილი"
    };

    public static string StatusColor(ServiceCompatibilityStatus status) => status switch
    {
        ServiceCompatibilityStatus.Supported => "#1F8A4C",
        ServiceCompatibilityStatus.ManualProcedureOnly => "#1D5BD6",
        ServiceCompatibilityStatus.PartiallySupported => "#C98A1A",
        ServiceCompatibilityStatus.CompatibilityUnknown => "#7A8594",
        _ => "#B3361F"
    };

    public static string Method(ServiceMethod method, bool en) => method switch
    {
        ServiceMethod.Automatic => en ? "Automatic — the app runs it" : "ავტომატური — აპი ასრულებს",
        ServiceMethod.Manual => en ? "Manual — buttons / menu, nothing is sent to the car" : "ხელით — ღილაკები / მენიუ, მანქანას არაფერი ეგზავნება",
        _ => en ? "Needs a workshop tool" : "საჭიროა სერვისის ხელსაწყო"
    };

    public static string MethodShort(ServiceMethod method, bool en) => method switch
    {
        ServiceMethod.Automatic => en ? "Automatic" : "ავტომატური",
        ServiceMethod.Manual => en ? "Manual" : "ხელით",
        _ => en ? "Tool needed" : "ხელსაწყო"
    };

    public static string Reason(ServiceReason reason, bool en) => reason switch
    {
        ServiceReason.ExactModelMatch => en ? "Model and model year match this entry." : "მოდელი და წელი ემთხვევა ამ ჩანაწერს.",
        ServiceReason.BrandGenerationMatch => en
            ? "A guide for this brand and model-year range; your exact model is not listed separately."
            : "ინსტრუქცია ამ მარკისა და წლების დიაპაზონისთვისაა; შენი მოდელი ცალკე არ არის ჩამოთვლილი.",
        ServiceReason.UniversalFallback => en ? "General guide; not specific to your car." : "ზოგადი ინსტრუქციაა, შენი მანქანისთვის სპეციფიკური არ არის.",
        ServiceReason.BrandUnknown => en ? "The manufacturer is not known yet." : "მწარმოებელი ჯერ უცნობია.",
        ServiceReason.ModelUnknown => en ? "The model is not known yet." : "მოდელი ჯერ უცნობია.",
        ServiceReason.YearUnknown => en ? "The model year is not known yet." : "გამოშვების წელი ჯერ უცნობია.",
        ServiceReason.EngineUnknown => en ? "The engine is not known yet." : "ძრავა ჯერ უცნობია.",
        ServiceReason.FuelUnknown => en ? "The fuel type is not known yet." : "საწვავის ტიპი ჯერ უცნობია.",
        ServiceReason.BrandMismatch => en ? "For a different manufacturer." : "სხვა მწარმოებლისთვისაა.",
        ServiceReason.ModelMismatch => en ? "For a different model." : "სხვა მოდელისთვისაა.",
        ServiceReason.YearOutOfRange => en ? "Your model year is outside this entry's range." : "შენი გამოშვების წელი ამ ჩანაწერის დიაპაზონს გარეთაა.",
        ServiceReason.EngineMismatch => en ? "For a different engine." : "სხვა ძრავისთვისაა.",
        ServiceReason.FuelMismatch => en ? "For a different fuel type." : "სხვა საწვავის ტიპისთვისაა.",
        ServiceReason.AdapterCapabilityMissing => en ? "The connected adapter lacks a required capability." : "მიერთებულ ადაპტერს საჭირო შესაძლებლობა არ აქვს.",
        ServiceReason.ExternalToolRequired => en
            ? "No owner method is documented; a workshop or manufacturer tool is needed. OBD360 cannot do this."
            : "მფლობელის მეთოდი დოკუმენტირებული არ არის; საჭიროა სერვისის ან მწარმოებლის ხელსაწყო. OBD360 ამას ვერ ასრულებს.",
        ServiceReason.NoAutomaticPath => en
            ? "OBD360 has no reviewed automatic method for this car, so only the documented manual steps are offered."
            : "OBD360-ს ამ მანქანისთვის შემოწმებული ავტომატური მეთოდი არ აქვს, ამიტომ მხოლოდ ხელით ნაბიჯებია.",
        _ => en ? "No steps are documented." : "ნაბიჯები დოკუმენტირებული არ არის."
    };

    public static string Verification(ServiceVerification verification, bool en) => verification switch
    {
        ServiceVerification.VehicleVerified => en ? "Tested on a real vehicle" : "შემოწმებულია რეალურ მანქანაზე",
        ServiceVerification.SourceDocumented => en
            ? "From a named source — not tested on a real vehicle by OBD360"
            : "დასახელებული წყაროდან — OBD360-ს რეალურ მანქანაზე არ შეუმოწმებია",
        _ => en ? "No source recorded" : "წყარო მითითებული არ არის"
    };

    public static string VerificationColor(ServiceVerification verification) => verification switch
    {
        ServiceVerification.VehicleVerified => "#1F8A4C",
        ServiceVerification.SourceDocumented => "#7A8594",
        _ => "#C98A1A"
    };

    /// <summary>Always-shown preconditions for a manual guide (the guide's own first steps add the specifics).</summary>
    public static string ManualPrerequisites(bool en) => en
        ? "Park safely. Follow step 1 for the ignition state. Nothing is sent to the car, and the app cannot check the result — confirm it on the dashboard."
        : "გააჩერე მანქანა უსაფრთხოდ. ანთების მდგომარეობა პირველ ნაბიჯშია. მანქანას არაფერი ეგზავნება და აპი შედეგს ვერ ამოწმებს — დარწმუნდი დაფაზე.";

    public static string AutomaticPrerequisites(bool en) => en
        ? "Ignition on, car stationary in P, battery voltage at least 12.0 V. The app checks speed and voltage first and sends nothing if they cannot be read."
        : "ანთება ჩართული, მანქანა გაჩერებული P-ზე, ძაბვა მინიმუმ 12.0 V. აპი ჯერ სიჩქარესა და ძაბვას ამოწმებს და თუ ვერ წაიკითხა, არაფერს აგზავნის.";

    public static string DashboardVerification(bool en) => en
        ? "Check on the dashboard: the message disappears or a new interval is shown."
        : "შეამოწმე დაფაზე: შეტყობინება ქრება ან ახალი ინტერვალი ჩნდება.";

    public static string ReadBackVerification(bool en) => en
        ? "The app reads the value back from the car and reports it as confirmed only if it really changed."
        : "აპი მნიშვნელობას მანქანიდან თავიდან კითხულობს და დადასტურებულად მხოლოდ რეალური ცვლილებისას აცხადებს.";
}
