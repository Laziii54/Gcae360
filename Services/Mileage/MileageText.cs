namespace CarScanner.Services.Mileage;

public static class MileageText
{
    public static string NoData(bool english) =>
        english ? "Unavailable" : "მიუწვდომელია";

    public static string OneSource(bool english) =>
        english
            ? "One verified source"
            : "ერთი დადასტურებული წყარო";

    public static string SourcesAgree(bool english) =>
        english
            ? "Sources agree"
            : "წყაროები ემთხვევა";

    public static string Mismatch(bool english) =>
        english
            ? "Mileage mismatch"
            : "გარბენის შეუსაბამობა";

    public static string ProFeature(bool english) =>
        english
            ? "Mileage Verification is a PRO feature."
            : "გარბენის გადამოწმება PRO ფუნქციაა.";

    public static string ScanDetails(bool english) =>
        english
            ? "Checking all available verified mileage sources..."
            : "გარბენის ყველა ხელმისაწვდომი დადასტურებული წყაროს შემოწმება...";

    public static string Confidence(bool english, double value) =>
        english
            ? $"Confidence: {value:P0}"
            : $"სანდოობა: {value:P0}";
}
