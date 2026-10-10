using Microsoft.Maui.Controls;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CarScanner.Services.Localization;

public sealed partial class LocalizationService
{
    public const string LanguageKey = "language";

    public event EventHandler? LanguageChanged;

    public LocalizationService() => _current = this;

    // Preserve original XAML text. Without this, switching Georgian → English
    // → Georgian could gradually overwrite strings and, worse, static updates
    // could replace text bindings on view-model-driven controls.
    private readonly ConditionalWeakTable<Element, OriginalText> _originalTexts = new();

    private sealed class OriginalText
    {
        public string Value { get; set; } = string.Empty;

        /// <summary>Text this service last wrote; anything else came from code or a binding.</summary>
        public string? Applied { get; set; }
    }


    // =========================================================
    // GEORGIAN -> ENGLISH
    // =========================================================

    private static readonly Dictionary<string, string> MotoKaToEn =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ავტომობილის ტიპი"] = "Vehicle type",
            ["აირჩიე ავტომობილის ტიპი"] = "Choose vehicle type",
            ["მოტოციკლი"] = "Motorcycle",
            ["სკუტერი"] = "Scooter",
            ["მოპედი"] = "Moped",
            ["სხვა მსუბუქი ტრანსპორტი"] = "Other light vehicle",
            ["ტრანსპორტის ტიპი"] = "Vehicle type",
            ["შეცვლა"] = "Change",
            ["მოტო / სკუტერი / მოპედის დიაგნოსტიკა"] =
                "Motorcycle / Scooter / Moped diagnostics",
            ["მოპედის / მოტოციკლის სკანერი"] =
                "Motorcycle / Moped scanner",
            ["ავტომობილის პროფილი"] = "Vehicle profile",
            ["მწარმოებელი"] = "Manufacturer",
            ["მოდელი"] = "Model",
            ["წელი"] = "Year",
            ["მოცულობა"] = "Engine displacement",
            ["საწვავი"] = "Fuel type",
            ["ECU"] = "ECU",
            ["სრული სკანირება"] = "Full scan",
            ["სკანირების შედეგი"] = "Scan result",
            ["დიაგნოსტიკის შესაძლებლობები"] =
                "Diagnostic capabilities",
            ["ადაპტერის დაკავშირება"] = "Connect adapter",
            ["მხარდაჭერა არ არის / პროტოკოლი ვერიფიცირებული არ არის"] =
                "Unsupported / Protocol not verified",
            ["მხარდაჭერილია — Generic OBD-II"] =
                "Supported — Generic OBD-II",
            ["შეცდომის კოდები"] = "Trouble codes",
            ["ცოცხალი მონაცემები"] = "Live data",
            ["მხოლოდ ვერიფიცირებული პროტოკოლი, ადაპტერი და ECU ფუნქცია გაეშვება. უცნობი მხარდაჭერა = არ არის მხარდაჭერილი."] =
                "Only verified protocols, adapters and ECU functions are enabled. Unknown support = unsupported.",
            ["ECU ინფორმაცია"] = "ECU information",
            ["ტრანსპორტი"] = "Vehicle",
            ["ძრავი"] = "Engine",
            ["საწვავი"] = "Fuel",
            ["სტატუსი"] = "Status",
            ["სერვისის ფუნქციები"] = "Service functions",
            ["ანგარიშები / History"] = "Reports / History",
            ["მოტოციკლი / სკუტერი / მოპედი"] =
                "Motorcycle / Scooter / Moped",
            ["პროტოკოლი"] = "Protocol",
            ["კავშირის ტიპი"] = "Connection type"
        };


    private static readonly Dictionary<string, string> KaToEn =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["კალენდარი"] = "Calendar",
            ["სერვისის კალენდარი"] = "Service Calendar",
            ["შეხსენება მოვა ერთი დღით ადრე და იმავე დღეს"] =
                "You will be reminded one day before and on the due date",
            ["+ დამატება"] = "+ Add",
            ["ახალი ჩანაწერი"] = "New reminder",
            ["ტიპი"] = "Type",
            ["თარიღი"] = "Date",
            ["ინტერვალი (თვე)"] = "Interval (months)",
            ["ინტერვალი (კმ, სურვილისამებრ)"] = "Interval (km, optional)",
            ["შენახვა"] = "Save",
            ["ამ დღეს შესასრულებელი არაფერია"] = "Nothing is due on this day",
            ["გაკეთდა"] = "Completed",
            ["დაზღვევა"] = "Insurance",
            ["ტექდათვალიერება"] = "Technical inspection",
            ["ფილტრი"] = "Filter",
            ["საბურავები"] = "Tires",
            ["სხვა"] = "Other",
            ["ზეთი"] = "Oil",
            ["ორშ"] = "Mon",
            ["სამ"] = "Tue",
            ["ოთხ"] = "Wed",
            ["ხუთ"] = "Thu",
            ["პარ"] = "Fri",
            ["შაბ"] = "Sat",
            ["კვი"] = "Sun",
            ["მაგ.: დაზღვევის განახლება"] = "E.g. renew insurance",
            ["მაგ.: 10000"] = "E.g. 10000",
            ["გაზომე ავტომობილის აჩქარების დრო 0-დან 100 კმ/სთ-მდე."] =
                "Measure the vehicle acceleration time from 0 to 100 km/h.",
            ["ბოლო შედეგი"] = "Latest result",
            ["შედეგები"] = "Results",
            ["გაზომვების ისტორია"] = "Measurement history",
            ["უსაფრთხოება: გაზომვა ჩაატარე მხოლოდ უსაფრთხო, კანონით ნებადართულ ადგილზე. შედეგი ითვლება OBD სიჩქარის სენსორიდან და დამოკიდებულია მანქანასა და adapter-ზე."] =
                "Safety: measure only in a safe, legally permitted location. The result uses the OBD speed sensor and depends on the vehicle and adapter.",
            ["AI Diagnostic: backend-only, user API key არ არის საჭირო"] =
                "AI Diagnostic: backend-only; no user API key is required.",

            ["CSV ექსპორტი"] = "Export CSV",
            ["DTC კოდების წაშლა"] = "Clear DTC Codes",
            ["DTC წაკითხვა"] = "Read DTCs",
            ["ECU ინფორმაცია"] = "ECU Information",
            ["აპის შესახებ"] = "About",
            ["გაგზავნა"] = "Send",
            ["ჩატი"] = "Chat",
            ["AI დიაგნოსტიკის დაწყება"] = "Start AI Diagnostic",
            ["AI დიაგნოსტიკის დასაწყებად დააჭირე ღილაკს"] = "Tap the button to start AI Diagnostic",
            ["გათიშვა"] = "Disconnect",
            ["გათიშულია"] = "Disconnected",
            ["გამომიგზავნე შეტყობინება პირდაპირ დეველოპერთან."] =
                "Send a message directly to the developer.",
            ["განახლება"] = "Refresh",
            ["გარბენის გადამოწმება"] = "Mileage Verification",
            ["გარბენის გადამოწმება (PRO)"] =
                "Mileage Verification (PRO)",
            ["გარბენის შემოწმება"] = "Check Mileage",
            ["გასუფთავება"] = "Reset",
            ["გაჩერება"] = "Stop",
            ["დააჭირე „შეცდომების სკანირება“-ს"] =
                "Tap “Scan for Errors”",
            ["დაკავშირება"] = "Connect",
            ["არ არის დაკავშირებული"] = "Not connected",
            ["დაკავშირება საჭიროა რეალური მანქანის მონაცემებისთვის."] =
                "Connection is required for real vehicle data.",
            ["დაკავშირებულია"] = "Connected",
            ["დაუსვი კითხვა AI-ს…"] = "Ask AI a question…",
            ["დაწყება"] = "Start",
            ["განახლებისთვის დააჭირე ღილაკს"] = "Tap the button to refresh",
            ["პაუზა"] = "Pause",
            ["გაზომვის დაწყება"] = "Start measurement",
            ["გაუქმება"] = "Cancel",
            ["ჯერ არ არის გაზომილი"] = "Not measured yet",
            ["გაზომვისთვის დააკავშირე OBD2"] = "Connect OBD2 to measure",
            ["შედეგი OBD სიჩქარის სენსორიდან ითვლება; მისი სიზუსტე დამოკიდებულია ავტომობილსა და adapter-ზე."] =
                "The result is calculated from the OBD speed sensor; accuracy depends on the vehicle and adapter.",
            ["გააჩერე ავტომობილი და დაიწყე აჩქარება."] =
                "Stop the vehicle, then begin accelerating.",
            ["გაზომვის დასაწყებად სიჩქარე უნდა იყოს 0–3 km/h."] =
                "Speed must be 0–3 km/h to begin the measurement.",
            ["იზომება…"] = "Measuring…",
            ["გაზომვა გაუქმდა"] = "Measurement cancelled",
            ["შედეგი ინახება…"] = "Saving result…",
            ["iPhone მხარს უჭერს BLE OBD-II ადაპტერებს. Classic ELM327 iPhone-თან არ მუშაობს."] =
                "iPhone supports BLE OBD-II adapters. Classic ELM327 adapters do not work with iPhone.",
            ["Android მხარს უჭერს BLE და Bluetooth Classic OBD-II ადაპტერებს."] =
                "Android supports BLE and Bluetooth Classic OBD-II adapters.",
            ["დიაგნოსტიკა"] = "Diagnostics",
            ["დიაგნოსტიკის მოდულები"] = "Diagnostic Modules",
            ["ენა"] = "Language",
            ["თემა"] = "Theme",
            ["თუ რომელიმე მონაცემი მიუწვდომელია, ეს მანქანის/ECU-ს მხარდაჭერას ნიშნავს."] =
                "If data is unavailable, the vehicle/ECU may not support it.",
            ["თუ რომელიმე ტესტი მიუწვდომელია, ეს ნიშნავს, რომ კონკრეტული ECU ამ OBD რეჟიმს არ აბრუნებს — ეს ავტომატურად გაუმართაობას არ ნიშნავს."] =
                "If a test is unavailable, that ECU may not provide that OBD mode. It does not automatically mean there is a fault.",
            ["ინიციალიზაცია"] = "Initialize",
            ["ისტორია"] = "History",
            ["ისტორია ცარიელია"] = "History is empty",
            ["ისტორიის გასუფთავება"] = "Clear History",
            ["კავშირი არ არის"] = "Not connected",
            ["კავშირის სტატუსი"] = "Connection Status",
            ["კოდების წაშლა"] = "Clear Codes",
            ["კონტაქტი"] = "Contact",
            ["მანქანა"] = "Vehicle",
            ["მანქანის ინფორმაცია"] = "Vehicle Information",
            ["მთავარი"] = "Home",
            ["მოდელი"] = "Model",
            ["მომენტალური"] = "Instant",
            ["მუდმივი შეცდომები"] = "Permanent Faults",
            ["მწარმოებელი"] = "Manufacturer",
            ["მწვანე = ნორმალური დიაპაზონი • წითელი = შესამოწმებელი მნიშვნელობა"] =
                "Green = normal range • Red = value needs attention",
            ["მხარდაჭერილი PID"] = "Supported PIDs",
            ["მხარდაჭერილი სენსორები ვერ მოიძებნა"] =
                "No supported sensors found",
            ["პროტოკოლი"] = "Protocol",
            ["რას ნიშნავს"] = "What does it mean?",
            ["რეკომენდაცია"] = "Recommendation",
            ["რჩევა: ჩართე მანქანის კონტაქტი და შემდეგ ჩართე OBD-II ადაპტერი."] =
                "Tip: turn on the ignition, then power the OBD-II adapter.",
            ["საშუალო"] = "Average",
            ["საწვავის ხარჯი"] = "Fuel Consumption",
            ["საწვავის ხარჯი (PRO)"] =
                "Fuel Consumption (PRO)",
            ["სენსორები"] = "Sensors",
            ["სერვისები"] = "Services",
            ["სკანირების შემდეგ აქ გამოჩნდება."] =
                "Scan results will appear here.",
            ["სრული დიაგნოსტიკა"] = "Full Diagnostics",
            ["სრული სკანირება"] = "Full Scan",
            ["DTC კოდების წაშლა და გადამოწმება"] = "Clear and verify DTC codes",
            ["Freeze Frame — ხარვეზის მომენტის მონაცემები"] = "Freeze Frame — fault snapshot data",
            ["DTC კოდების წაკითხვა და სტატუსის შემოწმება"] = "Read DTC codes and check status",
            ["ტესტების შემოწმება"] = "Check Tests",
            ["უსაფრთხო რეჟიმი: უნივერსალური OBD-II-ით კოდის წაკითხვა/წაშლა და ტესტები მუშაობს. მწარმოებლის სპეციფიკური Coding/Write ოპერაციები ავტომატურად არ სრულდება, რადგან არასწორმა ბრძანებამ ECU შეიძლება დააზიანოს."] =
                "Safe mode: generic OBD-II code reading, clearing and tests are supported. Manufacturer-specific Coding/Write operations are not performed automatically because an incorrect command can damage the ECU.",
            ["ყველა სენსორი"] = "All Sensors",
            ["შედეგი"] = "Result",
            ["შენახული შეცდომები"] = "Stored Faults",
            ["შეტყობინებების ჩართვა"] = "Enable Notifications",
            ["შეტყობინების გაგზავნა"] = "Send Message",
            ["შეცდომების კოდები"] = "Trouble Codes",
            ["შეცდომების სკანირება"] = "Scan for Errors",
            ["შეცდომის კოდი არ არის"] = "No trouble code",
            ["ჩაწერე შენი შეტყობინება…"] =
                "Write your message…",
            ["ჩაწერის გაჩერება"] = "Stop Recording",
            ["ჩაწერის დაწყება"] = "Start Recording",
            ["ძებნა"] = "Scan",
            ["ძებნა შეჩერდა"] = "Scan stopped",
            ["ძირითადი დიაგნოსტიკა"] = "Basic Diagnostics",
            ["წაკითხვა"] = "Read",
            ["წაშლა"] = "Delete",
            ["წელი"] = "Year",
            ["„სკანირება“ ამოწმებს DTC კოდებს, ხოლო ეს გვერდი აჩვენებს კონკრეტულად ნაპოვნ შეცდომებს."] =
                "Scan checks DTC codes and this page shows the detected faults.",

            // Bluetooth
            ["Bluetooth OBD2"] = "Bluetooth OBD2",
            ["OBD კავშირი"] = "OBD Connection",
            ["Wi-Fi OBD ადაპტერი"] = "Wi-Fi OBD Adapter",
            ["IP მისამართი"] = "IP Address",
            ["TCP პორტი"] = "TCP Port",
            ["კავშირის სტატუსი"] = "Connection Status",
            ["კავშირის სტატუსი:"] = "Connection Status:",
            ["Bluetooth გამორთულია"] = "Bluetooth is off",
            ["Bluetooth ჩართულია"] = "Bluetooth is on",
            ["Bluetooth ჩართული არ არის"] =
                "Bluetooth is not turned on",
            ["Bluetooth ნებართვა"] = "Bluetooth permission",
            ["Bluetooth შეცდომა"] = "Bluetooth error",
            ["Bluetooth permission საჭიროა."] =
                "Bluetooth permission is required.",
            ["ჩართე Bluetooth"] = "Turn on Bluetooth",
            ["ჩართე Bluetooth და შემდეგ მოძებნე OBD-II ადაპტერი."] =
                "Turn on Bluetooth and then scan for the OBD-II adapter.",
            ["ჩართე Bluetooth, ჩართე მანქანის კონტაქტი და მოძებნე OBD-II ადაპტერი."] =
                "Turn on Bluetooth, turn on the vehicle ignition, and scan for the OBD-II adapter.",
            ["დაუკავშირე ავტომობილის OBD-II ადაპტერი რეალური დიაგნოსტიკისთვის."] =
                "Connect the vehicle's OBD-II adapter for real diagnostics.",
            ["ვეძებ OBD-II ადაპტერებს…"] =
                "Searching for OBD-II adapters…",
            ["OBD-II ადაპტერი ვერ მოიძებნა."] =
                "No OBD-II adapter found.",
            ["OBD-II ადაპტერი ვერ მოიძებნა. შეამოწმე ადაპტერი და მანქანის კონტაქტი."] =
                "No OBD-II adapter found. Check the adapter and vehicle ignition.",
            ["ნაპოვნია OBD-II ადაპტერი."] =
                "OBD-II adapter found.",
            ["OBD-II ადაპტერები ვერ მოიძებნა."] =
                "No OBD-II adapters found.",
            ["OBD-II კავშირი"] = "OBD-II connection",
            ["OBD კავშირი/ინიციალიზაცია..."] =
                "OBD connection/initialization...",
            ["ადაპტერი დაკავშირებულია. მიმდინარეობს OBD ინიციალიზაცია…"] =
                "Adapter connected. OBD initialization in progress…",
            ["OBD მზად არის. ვკითხულობ მანქანის ინფორმაციას…"] =
                "OBD is ready. Reading vehicle information…",
            ["OBD-II ადაპტერი დაკავშირებულია და მზად არის."] =
                "The OBD-II adapter is connected and ready.",
            ["OBD-II ადაპტერი გათიშულია."] =
                "OBD-II adapter disconnected.",
            ["OBD-II ადაპტერთან დაკავშირება ვერ მოხერხდა."] =
                "Could not connect to the OBD-II adapter.",
            ["კავშირი ვერ დამყარდა. შეამოწმე Bluetooth, მანქანის კონტაქტი და ადაპტერი."] =
                "Connection failed. Check Bluetooth, vehicle ignition, and the adapter.",
            ["მოწყობილობა ვერ მოიძებნა"] =
                "No device found",
            ["OBD-II მოწყობილობა ვერ მოიძებნა"] =
                "No OBD-II device found",
            ["OBD-II ადაპტერი ვერ მოიძებნა"] =
                "No OBD-II adapter found",
            ["დაკავშირება საჭიროა რეალური მანქანის მონაცემებისთვის."] =
                "Connection is required for real vehicle data.",
            ["ჩართე Bluetooth და დარწმუნდი, რომ OBD-II ადაპტერი შეერთებულია."] =
                "Turn on Bluetooth and make sure the OBD-II adapter is connected.",
            ["დარწმუნდი, რომ Bluetooth ჩართულია, მანქანის კონტაქტი ჩართულია და OBD-II ადაპტერი შეერთებულია."] =
                "Make sure Bluetooth and the vehicle ignition are on and the OBD-II adapter is connected.",
            ["სიგნალი"] = "Signal",
            ["OBD-II Adapter"] = "OBD-II Adapter",
            ["OBD-II ადაპტერი"] = "OBD-II Adapter",

            // Common dialog/error text
            ["OBD კავშირის შეცდომა"] = "OBD Connection Error",
            ["კავშირის შეცდომა"] = "Connection Error",
            ["კარგი"] = "OK",
            ["გინდა Bluetooth-ის პარამეტრების გახსნა?"] =
                "Would you like to open Bluetooth settings?",
            ["ჩართე Bluetooth და დაბრუნდი აპში. ძებნა ავტომატურად დაიწყება."] =
                "Turn on Bluetooth and return to the app. Scanning will start automatically.",
            ["Bluetooth ჩართული არ არის."] =
                "Bluetooth is not turned on.",
            ["Bluetooth ამ მოწყობილობაზე მიუწვდომელია."] =
                "Bluetooth is unavailable on this device.",
            ["OBD2 ადაპტერი არ არის დაკავშირებული."] =
                "The OBD2 adapter is not connected.",
            ["Bluetooth ძებნა ვერ შესრულდა."] =
                "Bluetooth scan failed."
        };

    // Long-form help text and the remaining mixed-language labels. Keeping
    // these separate makes the main command-label catalog easier to audit.
    private static readonly Dictionary<string, string> ExtendedKaToEn =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["მანქანის ანგარიში"] = "Vehicle report",
            ["ყველა ხელსაწყო"] = "All tools",
            ["15 ხელსაწყო · პარამეტრებიც აქ"] = "15 tools · settings too",
            ["დიაგნოსტიკა და მონაცემები"] = "Diagnostics and data",
            ["ყველა შემოწმება და ხელსაწყო ერთ ადგილას"] = "Every check and tool in one place",
            ["გარბენი, მგზავრობები, ECU · ტექსტური ფაილი"] = "Mileage, trips, ECU · text file",
            ["მოტოციკლი"] = "Motorcycle",
            ["ავტომობილი"] = "Car",
            ["სხვა მსუბუქი ტრანსპორტი"] = "Other light vehicle",
            ["მოპედი"] = "Moped",
            ["სკუტერი"] = "Scooter",
            ["₾ / ლიტრი"] = "GEL / liter",
            ["☆ Favorite"] = "☆ Favorite",
            ["✦ Car Scanner PRO"] = "✦ Car Scanner PRO",
            ["12V სისტემის მდგომარეობა"] = "12V system status",
            ["2. PRO ანალიტიკა"] = "2. PRO analytics",
            ["AI დიაგნოსტიკა · PRO"] = "AI diagnostics · PRO",
            ["ავტომობილის შესაძლებლობები"] = "Vehicle capabilities",
            ["აირჩიე მარკა"] = "Choose a make",
            ["აირჩიე სიჩქარის ინტერვალი"] = "Choose a speed interval",
            ["აქტიური ტესტები"] = "Active tests",
            ["აღმოჩენილი ტესტები"] = "Detected tests",
            ["აღწერა"] = "Description",
            ["ახალი სრული სკანი"] = "New full scan",
            ["ბენზინგასამართი"] = "Fuel station",
            ["გაზიარება"] = "Share",
            ["გაშვება"] = "Run",
            ["გახსნა"] = "Open",
            ["დაიწყე სრული სკანირება."] = "Start a full scan.",
            ["დახარჯული საწვავი"] = "Fuel used",
            ["დეტალური მონიტორები"] = "Detailed monitors",
            ["ემისია / მზადყოფნა"] = "Emissions / readiness",
            ["ტექდათვალიერების შემოწმება"] = "Technical inspection check",
            ["0–100 რეიტინგი"] = "0–100 ranking",
            ["QR ანგარიში"] = "QR report",
            ["ემისიის მზადყოფნა"] = "Emissions readiness",
            ["ერთ აპში მანქანა, მოტოციკლი, სკუტერი და მოპედი."] = "Cars, motorcycles, scooters and mopeds in one app.",
            ["კავშირის მდგომარეობა"] = "Connection status",
            ["კატეგორია"] = "Category",
            ["კოდების გასუფთავება"] = "Clear codes",
            ["კოდების წაკითხვა"] = "Read codes",
            ["კოდების წაშლა და გადამოწმება"] = "Clear and verify codes",
            ["კოდირება და ადაპტაცია"] = "Coding and adaptation",
            ["კოდირების დაწყება"] = "Start coding",
            ["ლიტრები"] = "Liters",
            ["მაგ: 182450"] = "Example: 182450",
            ["მაგალითად: რატომ მიწერს ეს კოდი?"] = "For example: Why am I getting this code?",
            ["მანქანის კონტექსტი"] = "Vehicle context",
            ["მანძილი"] = "Distance",
            ["მაქს. სიჩქარე"] = "Maximum speed",
            ["მაქსიმალური ძაბვა"] = "Maximum voltage",
            ["მინიმალური ძაბვა"] = "Minimum voltage",
            ["მნიშვნელობების დამრგვალება (2 ათწილადი)"] = "Round values (2 decimals)",
            ["მოდულების სკანირების შედეგები"] = "Module scan results",
            ["მოძებნე მარკა"] = "Search makes",
            ["მოძრაობის დრო"] = "Moving time",
            ["საწვავის დონე"] = "Fuel level",
            ["მაქსიმალური სიჩქარე"] = "Maximum speed",
            ["დაპაუზებულია"] = "Paused",
            ["მწარმოებლის სპეციალური ფუნქციები"] = "Manufacturer-specific functions",
            ["პარამეტრები"] = "Settings",
            ["რატომ არის ეს დაბლოკილი?"] = "Why is this locked?",
            ["რეალური კავშირის მიხედვით, მარკის არჩევით არა"] = "Based on the real connection, not the selected make",
            ["რეალური მონაცემების გრაფიკი"] = "Real-data graph",
            ["საბოლოო"] = "Target",
            ["საშ. სიჩქარე"] = "Average speed",
            ["საწვავის ტიპი"] = "Fuel type",
            ["საწყისი"] = "Start",
            ["სერვისი / კოდირება"] = "Service / coding",
            ["სერვისი და კოდირება"] = "Service and coding",
            ["სესიები"] = "Sessions",
            ["სიმპტომები"] = "Symptoms",
            ["სკანირება"] = "Scan",
            ["სკანირების დაწყება"] = "Start scan",
            ["სრული ავზი"] = "Full tank",
            ["სწრაფი სკანირება"] = "Quick scan",
            ["ტესტის არჩევა"] = "Select test",
            ["ტესტის გაშვება"] = "Run test",
            ["ფასი 1 ლიტრზე (მაგ: 2.85)"] = "Price per liter (example: 2.85)",
            ["ყველა გაფართოებული ფუნქცია ერთ გეგმაში"] = "All advanced features in one plan",
            ["შემოწმება"] = "Test",
            ["შენახული რეალური სკანების ანგარიში — მონაცემი არ იქმნება ხელოვნურად."] = "Reports from saved real scans — no data is fabricated.",
            ["შენახული სკანები"] = "Saved scans",
            ["შენიშვნა (არასავალდებულო)"] = "Notes (optional)",
            ["შესაძლო მიზეზები"] = "Possible causes",
            ["ყიდვისწინა შემოწმება · PRO"] = "Pre-purchase check · PRO",
            ["მოდულების ცოცხალი მონაცემები"] = "Module live data",
            ["ყიდვისწინა შემოწმება · PRO"] = "Pre-purchase check · PRO",
            ["ყიდვისწინა შემოწმება"] = "Pre-purchase check",
            ["მანქანის სრული შემოწმება ყიდვამდე — ერთი ღილაკით"] = "Full check of the car before buying — with one button",
            ["ჩართე კონტაქტი ან ძრავი"] = "Turn the ignition or engine on",
            ["ადაპტერი"] = "Adapter",
            ["რას შეამოწმებს"] = "What it checks",
            ["შეცდომები ყველა მოდულში (ძრავი, ABS, აირბაგი…)"] = "Fault codes in every module (engine, ABS, airbag…)",
            ["მოდულები, ვერსიები და ნომრები"] = "Modules, versions and part numbers",
            ["გარბენის შედარება მოდულებს შორის"] = "Mileage comparison between modules",
            ["ძრავის და სენსორების ცოცხალი მონაცემები"] = "Live engine and sensor data",
            ["ბატარეა და დამუხტვა"] = "Battery and charging",
            ["მზადყოფნა ტექდათვალიერებისთვის"] = "Readiness for the technical inspection",
            ["VIN ისტორია (ავარიები, აუქციონი)"] = "VIN history (accidents, auctions)",
            ["0–100 (არასავალდებულო)"] = "0–100 (optional)",
            ["დაახლოებით 1–2 წუთი · მანქანაში არაფერი იცვლება"] = "About 1–2 minutes · nothing is changed in the car",
            ["შეცდომები"] = "Fault codes",
            ["გარბენი"] = "Mileage",
            ["მდგომარეობა"] = "Condition",
            ["AI კონსულტაცია"] = "AI consultation",
            ["მოდულების ინფორმაცია"] = "Module information",
            ["PRO-ს გააქტიურება"] = "Get PRO",
            ["სკანირებაზე გადასვლა"] = "Go to scan",
            ["მწარმოებლის მონაცემები"] = "Manufacturer data",
            ["ABS · ბორბლები · საჭე · ბატარეა და უჯრედები · კოლოფი · საბურავები"] = "ABS · wheels · steering · battery and cells · transmission · tires",
            ["ბორბლები"] = "Wheels",
            ["საჭე"] = "Steering",
            ["ბატარეა და უჯრედები"] = "Battery and cells",
            ["კოლოფი"] = "Transmission",
            ["საბურავები"] = "Tires",
            ["მოდულების მონაცემები"] = "Module live data",
            ["დაბალი"] = "Low",
            ["რა შევამოწმოთ"] = "What to check",
            ["მოდულების ცოცხალი მონაცემები"] = "Module live data",
            ["ჩემი მანქანა"] = "My vehicle",
            ["წინასთან შედარება"] = "Compare with previous",
            ["ხარვეზის მომენტის მონაცემები"] = "Fault-time data",
            ["ხელმისაწვდომი ახლა"] = "Available now",
            ["ჯერ ანგარიში არ არის შენახული."] = "No report has been saved yet.",
            ["Core + 3 დამატებითი PID"] = "Core + 3 additional PIDs",
            ["Custom ტესტის გამოყენება"] = "Using a custom test",
            ["ECU / მონაცემის წყარო"] = "ECU / data source",
            ["Engine Live Data-ის გახსნა"] = "Open engine live data",
            ["Freeze Frame წაკითხვა"] = "Read freeze frame",
            ["Idle დრო"] = "Idle time",
            ["JSON იმპორტი"] = "Import JSON",
            ["Performance ისტორია"] = "Performance history",
            ["PRO ანგარიში"] = "PRO account",
            ["ანგარიშიდან გამოსვლა"] = "Sign out",
            ["ანგარიშის სრულად წაშლა"] = "Delete account permanently",
            ["პაროლი — მინიმუმ 10 სიმბოლო"] = "Password — at least 10 characters",
            ["შესვლა"] = "Sign in",
            ["რეგისტრაცია"] = "Register",
            ["Readiness წაკითხვა"] = "Read readiness",
            ["Severity:"] = "Severity:",
            ["აირჩიე ECU"] = "Select ECU",
            ["აღმოჩენილი raw test items"] = "Detected raw test items",
            ["გარბენის გადამოწმება · PRO"] = "Mileage verification · PRO",
            ["დიაგნოსტიკური ანგარიში · PRO"] = "Diagnostic report · PRO",
            ["ოდომეტრი (km)"] = "Odometer (km)",
            ["რატომ ფიქრობს ასე AI"] = "Why AI thinks this",
            ["რეალურ OBD მტკიცებულებაზე დაფუძნებული შეფასება"] = "Assessment based on real OBD evidence",
            ["რეალური Bluetooth, ELM327 და ECU კავშირის შემოწმება"] = "Real Bluetooth, ELM327 and ECU connection test",
            ["რეალური ECU-reported 12V Control Module Voltage (PID 0142)"] = "Real ECU-reported 12V control-module voltage (PID 0142)",
            ["რეალური emissions test data · generic OBD-II"] = "Real emissions test data · generic OBD-II",
            ["რეალური Live Data-ის გახსნა"] = "Open real live data",
            ["რეალური OBD სიჩქარე"] = "Real OBD speed",
            ["რეალური optional O₂ monitor data · generic OBD-II"] = "Real optional O₂ monitor data · generic OBD-II",
            ["რეალური Vehicle Speed (PID 01 0D)"] = "Real vehicle speed (PID 01 0D)",
            ["საშ. Engine Load"] = "Average engine load",
            ["საშ. RPM"] = "Average RPM",
            ["საწვავის ხარჯი · PRO"] = "Fuel consumption · PRO",
            ["სერვისი და Reset"] = "Service and reset",
            ["ტრასა (>60 km/h)"] = "Highway (>60 km/h)",
            ["ქალაქი (≤60 km/h)"] = "City (≤60 km/h)",
            ["ყველა მხარდაჭერილი PID"] = "All supported PIDs",
            ["შეცდომის კოდები (DTC)"] = "Trouble codes (DTC)",
            ["ჩემი სწრაფი Dashboard"] = "My quick dashboard",
            ["DTC კოდები"] = "DTC codes",
            ["აღმოჩენილი მოდულები"] = "Detected modules",
            ["ვეძებ OBD-II ადაპტერებს..."] = "Searching for OBD-II adapters...",
            ["სკანირების შემდეგ აქ გამოჩნდება პასუხმგებელი ECU-ები."] = "Responding ECUs will appear here after scanning.",
            ["ადამიანური განმარტება მხოლოდ რეალური OBD სკანის მტკიცებულებით"] = "Plain-language explanation using only real OBD scan evidence",
            ["Advanced live data • ECU identifiers • mileage verification • reports • fuel statistics • no ads"] = "Advanced live data • ECU identifiers • mileage verification • reports • fuel statistics • no ads",
            ["Advanced live data • ECU identifiers • mileage verification • reports • fuel statistics • no ads\nყოველთვიური ავტომატურად განახლებადი გამოწერა"] = "Advanced live data • ECU identifiers • mileage verification • reports • fuel statistics • no ads\nMonthly auto-renewable subscription",
            ["1. შეამოწმე შესაბამისი Live Data · 2. დაათვალიერე ჰაერის/ვაკუუმის გზები · 3. შეამოწმე სენსორი და გაყვანილობა · 4. შეადარე Freeze Frame მონაცემებს"] = "1. Review related live data · 2. Inspect air/vacuum paths · 3. Check the sensor and wiring · 4. Compare freeze-frame data",
            ["ABS, SRS, BCM, Coding და აქტიური ტესტები მხოლოდ დადასტურებული მარკა/მოდელი/ECU/მისამართით."] = "ABS, SRS, BCM, coding and active tests require a verified make/model, ECU and address.",
            ["Diagnostic path — მხოლოდ რეალური OBD პასუხი და verified definition-ით შემოწმებული ECU"] = "Diagnostic path — only real OBD responses and ECUs validated by a verified definition",
            ["Coding გამოჩნდება მხოლოდ exact reviewed OEM definition-ზე. აპი ჯერ ინახავს VIN-ზე მიბმულ original backup-ს, წერს მნიშვნელობას და შემდეგ ECU-დან ხელახლა კითხულობს დასადასტურებლად."] = "Coding appears only for an exact reviewed OEM definition. The app first saves a VIN-linked original backup, writes the value and reads it back from the ECU for verification.",
            ["PRO მხოლოდ მაშინ იხსნება, როდესაც საჭირო რეალური telemetry ხელმისაწვდომია."] = "PRO features open only when the required real telemetry is available.",
            ["Faults — რეალურად წაკითხული ECU პასუხები"] = "Faults — real responses read from the ECU",
            ["Generic OBD-II რეჟიმში გამოჩნდება მხოლოდ ECU, რომელმაც რეალურად დააბრუნა პასუხი."] = "In generic OBD-II mode, only an ECU that actually responded is shown.",
            ["Manufacturer-specific ECU Identification"] = "Manufacturer-specific ECU identification",
            ["MODE 05 — O₂ TESTS"] = "MODE 05 — O₂ TESTS",
            ["Mode 05 შედეგები შემოწმების შემდეგ გამოჩნდება."] = "Mode 05 results appear after the test.",
            ["Mode 06 შედეგები შემოწმების შემდეგ გამოჩნდება."] = "Mode 06 results appear after the test.",
            ["Original Coding Backup-ის აღდგენა"] = "Restore original coding backup",
            ["Reviewed vehicle/ECU definition და რეალური OEM პასუხი ჯერ ხელმისაწვდომი არ არის."] = "A reviewed vehicle/ECU definition and real OEM response are not available yet.",
            ["მხოლოდ ამ სესიაში ECU-დან წარმატებით მიღებული გაზომვები."] = "Only measurements successfully received from the ECU in this session.",
            ["რეალური OBD ფუნქციებისთვის საჭიროა Bluetooth ადაპტერი და მანქანის კონტაქტი."] = "Real OBD functions require a Bluetooth adapter and the vehicle ignition to be on.",
            ["ყველა OBD-II ავტომობილზე — მხოლოდ ECU-ის რეალური პასუხის საფუძველზე."] = "For all OBD-II vehicles — based only on real ECU responses.",
            ["ჩართე მანქანის კონტაქტი და დარწმუნდი, რომ OBD-II ადაპტერი შეერთებულია."] = "Turn on the ignition and make sure the OBD-II adapter is connected.",
            ["Freeze Frame აჩვენებს ECU-ის მიერ შენახულ მონაცემებს კოდის დაფიქსირების მომენტში — თუ მანქანა ამ snapshot-ს ინახავს."] = "Freeze Frame shows ECU data saved when the code was detected, if the vehicle stores that snapshot.",
            ["Idle და ~2500 RPM Fuel Trim-ის მოკლე შედარება. გამოიყენე მხოლოდ გაჩერებულ მანქანაზე და უსაფრთხო გარემოში."] = "A short fuel-trim comparison at idle and about 2500 RPM. Use only while stationary and in a safe setting.",
            ["Mode 05 არის read-only legacy O₂ monitor service. თანამედროვე ECU-ების უმეტესობა მას არ აბრუნებს; ასეთ შემთხვევაში გამოიყენე Mode 06 ან Live Data O₂/Lambda PID-ები."] = "Mode 05 is a read-only legacy O₂ monitor service. Most modern ECUs do not return it; use Mode 06 or live O₂/Lambda PIDs instead.",
            ["Mode 06 მხოლოდ კითხულობს ECU-ის self-test საზღვრებს. ეს გვერდი არაფერს ცვლის მანქანაში. მწარმოებლის სრული სახელები, ერთეულები და დამატებითი ტესტები დაემატება მხოლოდ დადასტურებული OEM definition pack-ით."] = "Mode 06 only reads ECU self-test limits and changes nothing in the vehicle. Full manufacturer labels, units and extra tests require a verified OEM definition pack.",
            ["Not Supported ნიშნავს, რომ Generic OBD-II ამ ECU-ს ცალკე ვერ კითხულობს. Not Available ნიშნავს, რომ მოთხოვნილი სტანდარტული მონაცემი ECU-მ არ დააბრუნა. ორივე სტატუსი ქულაში არ შედის."] = "Not Supported means generic OBD-II cannot query that ECU separately. Not Available means the ECU did not return the requested standard data. Neither status affects the score.",
            ["PID 0142 აჩვენებს ECU-ის Control Module Voltage-ს — ეს არის დამუხტვის სისტემის ინდიკატორი და არა აკუმულატორის capacity/health ტესტი."] = "PID 0142 reports ECU control-module voltage. It indicates charging-system voltage, not battery capacity or health.",
            ["Rolling ტესტი იწყება მხოლოდ მაშინ, როცა ECU სიჩქარე საწყის ნიშნულს ქვემოდან კვეთს."] = "A rolling test starts only when ECU speed crosses the starting threshold from below.",
            ["RPM · Speed · Coolant · Load · Throttle · trims · pressure · MAP · MAF · Voltage"] = "RPM · Speed · Coolant · Load · Throttle · trims · pressure · MAP · MAF · Voltage",
            ["ამოწმებს MIL/Check Engine სტატუსს და ECU-ის შენახული კოდების რაოდენობას. გამოიყენე ტექნიკურ ინსპექტირებამდე."] = "Checks MIL/Check Engine status and the ECU's stored-code count. Use before an emissions inspection.",
            ["ანგარიში ინახავს გამოწერის კავშირს და გაძლევს საშუალებას PRO აღადგინო აპის ხელახლა დაყენების ან ტელეფონის შეცვლის შემდეგ."] = "An account links your subscription so PRO can be restored after reinstalling the app or changing phones.",
            ["არჩეული სამი PID-მდე; თითო ხაზს საკუთარი მასშტაბი აქვს."] = "Up to three selected PIDs; each line has its own scale.",
            ["აქტიურია მხოლოდ იმ მოქმედებები, რომლებსაც მანქანა და Generic OBD-II adapter რეალურად ადასტურებს."] = "Only actions actually confirmed by the vehicle and generic OBD-II adapter are enabled.",
            ["გამოიყენე მხოლოდ მიზეზის მოგვარების შემდეგ. ეს გაასუფთავებს DTC-ებსა და Readiness მონიტორებს; აპი მოქმედებამდე თანხმობას მოგთხოვს."] = "Use only after fixing the cause. This clears DTCs and readiness monitors; the app asks for confirmation first.",
            ["გამოჩნდება მხოლოდ ზუსტად ამ მანქანა/ECU-ზე reviewed ბრძანება. გაშვებისას მოწმდება სიჩქარე, ძრავის მდგომარეობა და ძაბვა; STOP ავტომატურადაც იგზავნება."] = "Only a command reviewed for this exact vehicle/ECU is shown. Speed, engine state and voltage are checked before execution, and STOP is also sent automatically.",
            ["გამოწერა ავტომატურად განახლდება ყოველ თვე, სანამ Store-ის პარამეტრებიდან არ გააუქმებ. თანხას და საბოლოო ფასს ყიდვამდე App Store ან Google Play გაჩვენებს."] = "The subscription renews monthly until cancelled in store settings. App Store or Google Play shows the final price before purchase.",
            ["დასკვნა აღწერს „სავარაუდო“ და „შესაძლო“ მიზეზებს. რემონტამდე გადაამოწმე რეკომენდებული ნაბიჯები."] = "The conclusion describes likely and possible causes. Verify the recommended steps before repair.",
            ["ეს არის გარბენის თანხვედრის შემოწმება და არა „100% ორიგინალური გარბენის“ დაპირება. Generic OBD-II-ით მიღებული ერთი მნიშვნელობა მხოლოდ ECU-ის პასუხია; დადასტურებას სჭირდება რამდენიმე დამოუკიდებელი წყარო."] = "This checks mileage consistency; it does not promise 100% original mileage. One generic OBD-II value is only an ECU response and verification needs several independent sources.",
            ["ეს არის ხელით შეყვანილი dashboard მნიშვნელობა; აპი მას ECU-დან წაკითხულად არ მონიშნავს."] = "This dashboard value was entered manually; the app does not label it as ECU data.",
            ["ზეთის reset, EPB, აქტიური ტესტი და Coding განსხვავდება მანქანის მოდელისა და ECU-ის მიხედვით. მათთვის საჭიროა დადასტურებული OEM პროტოკოლი, შესაბამისი ECU მისამართი, ძაბვის კონტროლი და უსაფრთხოების ავტორიზაცია. ამ პირობების გარეშე აპი ბრძანებას არ აგზავნის."] = "Oil reset, EPB, active tests and coding vary by model and ECU. They require a verified OEM protocol, correct ECU address, voltage checks and safety authorization. The app sends no command without them.",
            ["ეს მონაცემი optional Mode 01 PID 41-იდან იკითხება; თუ ECU არ პასუხობს, სექცია ცარიელი დარჩება."] = "This optional value is read from Mode 01 PID 41. If the ECU does not respond, the section remains empty.",
            ["კითხულობს ძრავის ECU-ში შენახულ, მომლოდინე და მუდმივ კოდებს. გამოიყენე, როცა Check Engine ან სხვა სიმპტომი გაქვს."] = "Reads stored, pending and permanent engine-ECU codes. Use when Check Engine is on or another symptom exists.",
            ["მუშაობს ბენზინზე, დიზელზე, ჰიბრიდზე და სხვა OBD-II მხარდაჭერილ ავტომობილებზე. კონკრეტული PID მხოლოდ მაშინ გამოჩნდება, როცა ECU მას მხარს უჭერს."] = "Works with gasoline, diesel, hybrid and other OBD-II vehicles. A PID appears only when the ECU supports it.",
            ["მხოლოდ რეალური OBD Live Data ინახება ამ მოწყობილობაზე. GPS მდებარეობა ამ სესიებში არ იწერება."] = "Only real OBD live data is stored on this device. GPS location is not recorded in these sessions.",
            ["მოპედისა და მოტოციკლის დიაგნოსტიკა აქტიურდება მხოლოდ იმ შემთხვევაში, თუ კონკრეტული ტრანსპორტის პროტოკოლი/ადაპტერი/ECU პროფილი ვერიფიცირებულია. დაუდასტურებელ შემთხვევაში აპი აჩვენებს მხარდაჭერის სტატუსს და არ შექმნის ფეიკ მონაცემებს."] = "Motorcycle and moped diagnostics activate only when that vehicle's protocol, adapter and ECU profile are verified. Otherwise the app shows support status and creates no fake data.",
            ["ნაჩვენებია მხოლოდ ECU-ის მიერ მხარდაჭერილი და რეალურად წაკითხული PID-ები.&#10;მწვანე = ნორმა · წითელი = შესამოწმებელი მნიშვნელობა"] = "Only PIDs supported and actually read from the ECU are shown.&#10;Green = normal · Red = needs checking",
            ["პასუხი იყენებს ამ ეკრანზე წაკითხულ DTC, Live Data და მანქანის კონტექსტს."] = "The answer uses DTCs, live data and vehicle context read on this screen.",
            ["პრიორიტეტი ენიჭება ECU-ის პირდაპირ Fuel Rate PID 01 5E-ს. თუ ის მიუწვდომელია, MAF-ით გამოთვლილ შედეგს აპი ყოველთვის მონიშნავს როგორც გამოთვლილს."] = "Direct ECU Fuel Rate PID 01 5E is preferred. If unavailable, an MAF-derived result is always marked as calculated.",
            ["რემონტამდე შეინახე მიმდინარე რეალური სკანი. შემოწმებისას აპი მხოლოდ იმავე VIN-ის ახალ DTC/MIL/PID პასუხს ადარებს."] = "Save the current real scan before repair. Verification compares it only with new DTC/MIL/PID responses from the same VIN.",
            ["უსაფრთხოება: ეს გვერდი მხოლოდ read-only მონაცემებს კითხულობს. ABS, TCM, BMS, SRS და სხვა OEM მოდულები მხოლოდ დადასტურებული მარკა/მოდელის ECU პროფილის არსებობისას გახდება აქტიური."] = "Safety: this page reads only read-only data. ABS, TCM, BMS, SRS and other OEM modules activate only with a verified make/model ECU profile.",
            ["ფასი ითვლება ჩაწერილი საწვავის რაოდენობიდან; თანხა GEL-შია."] = "Cost is calculated from the entered fuel quantity and shown in GEL.",
            ["შედეგი OBD speed sensor-ით არის გამოთვლილი და დამოკიდებულია ავტომობილისა და adapter-ის განახლების სიხშირეზე."] = "The result uses the OBD speed sensor and depends on the vehicle and adapter refresh rate.",
            ["შენიშვნა: ინსპექციის წესები ქვეყნების/რეგიონების მიხედვით განსხვავდება; ეს ეკრანი აჩვენებს ECU-ის OBD readiness სტატუსს."] = "Note: inspection rules vary by country and region; this screen shows the ECU's OBD readiness status.",
            ["ხელით შეყვანილი ჩანაწერი. შენახვა მხოლოდ ამოცნობილ VIN-ზე ხდება, რომ სხვა მანქანების ხარჯი არ აირიოს."] = "Manually entered record. It is saved only for the identified VIN so another vehicle's costs are not mixed in.",
            ["ჯერ დააჭირე „შემოწმება“; შემდეგ უსაფრთხო, სტანდარტული OBD-II მოქმედებები ავტომატურად გააქტიურდება."] = "Tap Test first; safe standard OBD-II actions will then be enabled automatically."
        };


    // =========================================================
    // ENGLISH -> GEORGIAN
    // =========================================================

    private static readonly Dictionary<string, string> EnToKa =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ABS / Brakes"] = "ABS / მუხრუჭები",
            ["ADAS"] = "ADAS / მძღოლის დამხმარე სისტემები",
            ["AI history"] = "AI ისტორია",
            ["About"] = "აპის შესახებ",
            ["Actuator Test"] = "აქტუატორის ტესტი",
            ["Adapter"] = "ადაპტერი",
            ["Auto reconnect OBD adapter"] =
                "OBD ავტომატური დაკავშირება",
            ["BCM / Body"] = "BCM / კორპუსი",
            ["Battery / HV"] = "ბატარეა / მაღალი ძაბვა",
            ["Check Tests"] = "ტესტების შემოწმება",
            ["Clear DTC Codes"] = "DTC კოდების წაშლა",
            ["Clear History"] = "ისტორიის გასუფთავება",
            ["Cluster"] = "ინსტრუმენტების პანელი",
            ["Coding / Write"] = "კოდირება / ჩაწერა",
            ["Connected"] = "დაკავშირებულია",
            ["Connection Status"] = "კავშირის სტატუსი",
            ["Contact"] = "კონტაქტი",
            ["DTC"] = "შეცდომის კოდი",
            ["Delete AI history"] = "AI ისტორიის წაშლა",
            ["Delete All Vehicle Data"] =
                "მანქანის ყველა მონაცემის წაშლა",
            ["Detailed Scan Sessions"] =
                "დეტალური სკანირების სესიები",
            ["Diagnostic Modules"] =
                "დიაგნოსტიკის მოდულები",
            ["Diagnostic session is not valid for this operation."] =
                "ამ ოპერაციისთვის დიაგნოსტიკური სესია ვალიდური არ არის.",
            ["Diagnostics"] = "დიაგნოსტიკა",
            ["Disconnected"] = "გათიშულია",
            ["ECU Identifiers"] =
                "ECU იდენტიფიკატორები",
            ["ECU did not respond"] =
                "ECU-მ პასუხი არ გასცა",
            ["ECU identification is required before this operation."] =
                "ამ ოპერაციამდე ECU-ს იდენტიფიკაცია აუცილებელია.",
            ["Electric Steering"] = "ელექტრო საჭე",
            ["Emission / Readiness"] =
                "ემისიები / Readiness",
            ["Enable Notifications"] =
                "შეტყობინებების ჩართვა",
            ["Export CSV"] = "CSV ექსპორტი",
            ["Export HTML"] = "HTML ექსპორტი",
            ["Export JSON"] = "JSON ექსპორტი",
            ["Export My Data"] =
                "ჩემი მონაცემების ექსპორტი",
            ["Export PDF"] = "PDF ექსპორტი",
            ["FAIL"] = "ვერ გაიარა",
            ["FULL VEHICLE DIAGNOSTICS"] =
                "სრული ავტომობილის დიაგნოსტიკა",
            ["Full Vehicle Diagnostics"] =
                "სრული ავტომობილის დიაგნოსტიკა",
            ["Full Vehicle Scan"] =
                "სრული ავტომობილის სკანირება",
            ["Generic OBD-II + OEM diagnostics where the vehicle profile supports them"] =
                "Generic OBD-II + OEM დიაგნოსტიკა, სადაც მანქანის პროფილი მხარს უჭერს.",
            ["HVAC"] = "კლიმატკონტროლი / HVAC",
            ["History"] = "ისტორია",
            ["Hybrid / HV"] = "ჰიბრიდი / მაღალი ძაბვა",
            ["Invalid response"] = "არასწორი პასუხი",
            ["Language"] = "ენა",
            ["Legacy Scan History"] =
                "ძველი სკანირების ისტორია",
            ["Live Data polling"] =
                "Live Data-ის განახლება",
            ["Low voltage"] = "დაბალი ძაბვა",
            ["Needs Attention"] =
                "საჭიროებს შემოწმებას",
            ["No DTCs reported by responding ECU."] =
                "პასუხის გამცემ ECU-ზე შეცდომის კოდი არ დაფიქსირდა.",
            ["No OEM diagnostics profile matches this vehicle."] =
                "ამ მანქანას შესაბამისი OEM დიაგნოსტიკის პროფილი ვერ მოეძებნა.",
            ["No supported sensors found"] =
                "მხარდაჭერილი სენსორები ვერ მოიძებნა",
            ["Normal"] = "ნორმალური",
            ["Not applicable"] = "არ ვრცელდება",
            ["Not read yet"] = "ჯერ არ წაკითხულა",
            ["PASS"] = "გავლილია",
            ["PRO / Payments"] = "PRO / გადახდები",
            ["Pending"] = "მოლოდინში",
            ["Permanent"] = "მუდმივი",
            ["Powertrain"] = "ძრავი / Powertrain",
            ["Programming"] = "პროგრამირება",
            ["Protocol"] = "პროტოკოლი",
            ["Protocol error"] =
                "პროტოკოლის შეცდომა",
            ["Read DTCs"] = "DTC-ების წაკითხვა",
            ["Read-only"] = "მხოლოდ წაკითხვა",
            ["Restore Purchase"] =
                "შეძენის აღდგენა",
            ["SRS / Airbag"] = "SRS / აირბაგი",
            ["Safety preconditions passed."] =
                "უსაფრთხოების ყველა წინაპირობა გავლილია.",
            ["Scan results will appear here."] =
                "სკანირების შედეგები აქ გამოჩნდება.",
            ["Security access is required and has not been granted."] =
                "საჭიროა Security Access, რომელიც ჯერ არ არის მინიჭებული.",
            ["Send Message"] =
                "შეტყობინების გაგზავნა",
            ["Send a message directly to the developer."] =
                "გამომიგზავნე შეტყობინება პირდაპირ დეველოპერთან.",
            ["Service"] = "სერვისი",
            ["Service / Coding"] =
                "სერვისი / კოდირება",
            ["Settings"] = "პარამეტრები",
            ["Start Recording"] =
                "ჩაწერის დაწყება",
            ["Stop Recording"] =
                "ჩაწერის გაჩერება",
            ["Stored"] = "შენახული",
            ["Stored Faults"] =
                "შენახული შეცდომები",
            ["Subject"] = "თემა",
            ["Supported"] = "მხარდაჭერილი",
            ["TPMS"] =
                "TPMS / საბურავების წნევა",
            ["The monthly subscription is handled through Apple App Store / Google Play Billing. Store products must be created with the same product ID before release."] =
                "თვიური გამოწერა მუშავდება Apple App Store / Google Play Billing-ის მეშვეობით. გამოშვებამდე Store-ში იგივე Product ID უნდა შეიქმნას.",
            ["The requested OEM capability is not defined for this ECU profile."] =
                "მოთხოვნილი OEM ფუნქცია ამ ECU პროფილში განსაზღვრული არ არის.",
            ["Theme"] = "თემა",
            ["Start measurement"] = "გაზომვის დაწყება",
            ["Cancel"] = "გაუქმება",
            ["Not measured yet"] = "ჯერ არ არის გაზომილი",
            ["Connect OBD2 to measure"] = "გაზომვისთვის დააკავშირე OBD2",
            ["The result is calculated from the OBD speed sensor; accuracy depends on the vehicle and adapter."] =
                "შედეგი OBD სიჩქარის სენსორიდან ითვლება; მისი სიზუსტე დამოკიდებულია ავტომობილსა და adapter-ზე.",
            ["Stop the vehicle, then begin accelerating."] =
                "გააჩერე ავტომობილი და დაიწყე აჩქარება.",
            ["Speed must be 0–3 km/h to begin the measurement."] =
                "გაზომვის დასაწყებად სიჩქარე უნდა იყოს 0–3 km/h.",
            ["Measuring…"] = "იზომება…",
            ["Measurement cancelled"] = "გაზომვა გაუქმდა",
            ["Saving result…"] = "შედეგი ინახება…",
            ["iPhone supports BLE OBD-II adapters. Classic ELM327 adapters do not work with iPhone."] =
                "iPhone მხარს უჭერს BLE OBD-II ადაპტერებს. Classic ELM327 iPhone-თან არ მუშაობს.",
            ["Android supports BLE and Bluetooth Classic OBD-II adapters."] =
                "Android მხარს უჭერს BLE და Bluetooth Classic OBD-II ადაპტერებს.",
            ["Timeout"] = "დროის ამოწურვა",
            ["Transmission"] = "ტრანსმისია",
            ["Transport error"] =
                "კავშირის ტრანსპორტის შეცდომა",
            ["Unavailable"] = "მიუწვდომელია",
            ["Units"] = "ერთეულები",
            ["Unsupported"] = "მხარდაჭერის გარეშე",
            ["Vehicle not detected."] =
                "მანქანა ვერ დაფიქსირდა.",
            ["Vehicle/adapter voltage is not within a safe range."] =
                "მანქანის/ადაპტერის ძაბვა უსაფრთხო დიაპაზონში არ არის.",
            ["Voltage"] = "ძაბვა",
            ["Write your message…"] =
                "ჩაწერე შენი შეტყობინება…",

            // Shared page titles and compact ECU-style section labels.
            ["Adapter status"] = "ადაპტერის სტატუსი",
            ["AI Diagnostic"] = "AI დიაგნოსტიკა",
            ["Battery / Charging"] = "ბატარეა / დამუხტვა",
            ["BATTERY / CHARGING"] = "ბატარეა / დამუხტვა",
            ["Before / After"] = "მანამდე / შემდეგ",
            ["Bluetooth / OBD2"] = "Bluetooth / OBD2",
            ["Car Scanner PRO"] = "Car Scanner PRO",
            ["Connection Diagnostics"] = "კავშირის დიაგნოსტიკა",
            ["Continuous Monitors"] = "უწყვეტი მონიტორები",
            ["Current Drive Cycle"] = "მიმდინარე მოძრაობის ციკლი",
            ["Data Recording"] = "მონაცემების ჩაწერა",
            ["DATA RECORDING"] = "მონაცემების ჩაწერა",
            ["Diagnostic Report"] = "დიაგნოსტიკური ანგარიში",
            ["DIAGNOSTIC REPORT"] = "დიაგნოსტიკური ანგარიში",
            ["Distance since clear"] = "მანძილი გასუფთავების შემდეგ",
            ["Distance with MIL ON"] = "მანძილი ანთებული MIL-ით",
            ["DTC / Fault Summary"] = "DTC / შეცდომების შეჯამება",
            ["DTC Center"] = "DTC ცენტრი",
            ["ECM Identification"] = "ECM იდენტიფიკაცია",
            ["ECU Explorer"] = "ECU ინფორმაცია",
            ["ECU EXPLORER"] = "ECU ინფორმაცია",
            ["Email"] = "ელფოსტა",
            ["Emissions / Readiness"] = "ემისია / მზადყოფნა",
            ["Freeze Frame"] = "Freeze Frame",
            ["Fuel Rate"] = "საწვავის სიჩქარე",
            ["FULL HEALTH SCAN"] = "სრული მდგომარეობის სკანირება",
            ["Georgian Car Scanner"] = "Georgian Car Scanner",
            ["Graph Studio"] = "გრაფიკი",
            ["High Voltage Battery"] = "მაღალი ძაბვის ბატარეა",
            ["Inspection Readiness"] = "ინსპექტირების მზადყოფნა",
            ["INSPECTION READINESS"] = "ინსპექტირების მზადყოფნა",
            ["Live Dashboard"] = "ცოცხალი მონაცემები",
            ["Live Data"] = "ცოცხალი მონაცემები",
            ["Manage Subscription"] = "გამოწერის მართვა",
            ["Mode 05"] = "Mode 05",
            ["Mode 06"] = "Mode 06",
            ["MODE 06 MONITORS"] = "MODE 06 მონიტორები",
            ["Non-continuous Monitors"] = "არაუწყვეტი მონიტორები",
            ["OBD Adapter Center"] = "OBD ადაპტერის ცენტრი",
            ["OBD ADAPTER CENTER"] = "OBD ადაპტერის ცენტრი",
            ["Open Related Live Data"] = "შესაბამისი ცოცხალი მონაცემების გახსნა",
            ["Performance Lab"] = "აჩქარების გაზომვა",
            ["PERFORMANCE LAB"] = "აჩქარების გაზომვა",
            ["Privacy Policy"] = "კონფიდენციალურობის პოლიტიკა",
            ["Purchase PRO"] = "PRO-ს შეძენა",
            ["Recommended checks"] = "რეკომენდებული შემოწმებები",
            ["Reset"] = "განულება",
            ["Response Benchmark"] = "პასუხის სიჩქარე",
            ["Scan Summary"] = "სკანირების შეჯამება",
            ["Since DTC Clear"] = "DTC-ის გასუფთავების შემდეგ",
            ["Speed vs Time"] = "სიჩქარე / დრო",
            ["STOP ACTIVE TEST"] = "აქტიური ტესტის გაჩერება",
            ["STOP TEST"] = "ტესტის გაჩერება",
            ["Supported PIDs"] = "მხარდაჭერილი PID-ები",
            ["Terms of Use"] = "გამოყენების პირობები",
            ["Test Results"] = "ტესტის შედეგები",
            ["Throttle"] = "Throttle",
            ["Time since DTC clear"] = "დრო DTC-ის გასუფთავების შემდეგ",
            ["Time with MIL ON"] = "დრო ანთებული MIL-ით",
            ["Trip Analytics"] = "მგზავრობის ანალიტიკა",
            ["TROUBLE CODES — DTC CENTER"] = "შეცდომის კოდები — DTC ცენტრი",
            ["Vehicle"] = "ტრანსპორტი",
            ["Vehicle Capabilities"] = "ავტომობილის შესაძლებლობები",
            ["VEHICLE CAPABILITIES"] = "ავტომობილის შესაძლებლობები",
            ["Vehicle Health"] = "ავტომობილის მდგომარეობა",
            ["VEHICLE HEALTH"] = "ავტომობილის მდგომარეობა",
            ["VEHICLE HEALTH SCORE"] = "ავტომობილის მდგომარეობის ქულა",
            ["DETECTED ISSUES"] = "აღმოჩენილი პრობლემები",
            ["Voltage History"] = "ძაბვის ისტორია",
            ["Warm-ups since clear"] = "გახურებები გასუფთავების შემდეგ",

            // Bluetooth
            ["Bluetooth OBD2"] = "Bluetooth OBD2",
            ["OBD Connection"] = "OBD კავშირი",
            ["Wi-Fi OBD Adapter"] = "Wi-Fi OBD ადაპტერი",
            ["IP Address"] = "IP მისამართი",
            ["TCP Port"] = "TCP პორტი",
            ["Scan"] = "ძებნა",
            ["Disconnect"] = "გათიშვა",
            ["Connect"] = "დაკავშირება",
            ["Connection Status"] =
                "კავშირის სტატუსი",
            ["Connected"] = "დაკავშირებულია",
            ["Disconnected"] = "გათიშულია",
            ["Bluetooth is off"] =
                "Bluetooth გამორთულია",
            ["Bluetooth is on"] =
                "Bluetooth ჩართულია",
            ["Bluetooth is not turned on"] =
                "Bluetooth ჩართული არ არის",
            ["Bluetooth permission"] =
                "Bluetooth ნებართვა",
            ["Bluetooth error"] =
                "Bluetooth შეცდომა",
            ["Turn on Bluetooth"] =
                "ჩართე Bluetooth",
            ["Turn on Bluetooth and then scan for the OBD-II adapter."] =
                "ჩართე Bluetooth და შემდეგ მოძებნე OBD-II ადაპტერი.",
            ["Turn on Bluetooth, turn on the vehicle ignition, and scan for the OBD-II adapter."] =
                "ჩართე Bluetooth, ჩართე მანქანის კონტაქტი და მოძებნე OBD-II ადაპტერი.",
            ["Connect the vehicle's OBD-II adapter for real diagnostics."] =
                "დაუკავშირე ავტომობილის OBD-II ადაპტერი რეალური დიაგნოსტიკისთვის.",
            ["Searching for OBD-II adapters…"] =
                "ვეძებ OBD-II ადაპტერებს…",
            ["No OBD-II adapter found."] =
                "OBD-II ადაპტერი ვერ მოიძებნა.",
            ["No OBD-II adapter found. Check the adapter and vehicle ignition."] =
                "OBD-II ადაპტერი ვერ მოიძებნა. შეამოწმე ადაპტერი და მანქანის კონტაქტი.",
            ["OBD-II adapter found."] =
                "ნაპოვნია OBD-II ადაპტერი.",
            ["OBD-II adapters found."] =
                "ნაპოვნია OBD-II ადაპტერები.",
            ["No OBD-II adapters found."] =
                "OBD-II ადაპტერები ვერ მოიძებნა.",
            ["OBD-II connection"] =
                "OBD-II კავშირი",
            ["OBD connection/initialization..."] =
                "OBD კავშირი/ინიციალიზაცია...",
            ["Adapter connected. OBD initialization in progress…"] =
                "ადაპტერი დაკავშირებულია. მიმდინარეობს OBD ინიციალიზაცია…",
            ["OBD is ready. Reading vehicle information…"] =
                "OBD მზად არის. ვკითხულობ მანქანის ინფორმაციას…",
            ["The OBD-II adapter is connected and ready."] =
                "OBD-II ადაპტერი დაკავშირებულია და მზად არის.",
            ["OBD-II adapter disconnected."] =
                "OBD-II ადაპტერი გათიშულია.",
            ["Could not connect to the OBD-II adapter."] =
                "OBD-II ადაპტერთან დაკავშირება ვერ მოხერხდა.",
            ["Connection failed. Check Bluetooth, vehicle ignition, and the adapter."] =
                "კავშირი ვერ დამყარდა. შეამოწმე Bluetooth, მანქანის კონტაქტი და ადაპტერი.",
            ["No device found"] =
                "მოწყობილობა ვერ მოიძებნა",
            ["No OBD-II device found"] =
                "OBD-II მოწყობილობა ვერ მოიძებნა",
            ["Signal"] = "სიგნალი",
            ["OBD-II Adapter"] =
                "OBD-II ადაპტერი",

            ["OK"] = "კარგი",
            ["Would you like to open Bluetooth settings?"] =
                "გინდა Bluetooth-ის პარამეტრების გახსნა?",
            ["Turn on Bluetooth and return to the app. Scanning will start automatically."] =
                "ჩართე Bluetooth და დაბრუნდი აპში. ძებნა ავტომატურად დაიწყება.",
            ["Bluetooth is not turned on."] =
                "Bluetooth ჩართული არ არის.",
            ["Bluetooth is unavailable on this device."] =
                "Bluetooth ამ მოწყობილობაზე მიუწვდომელია.",
            ["The OBD2 adapter is not connected."] =
                "OBD2 ადაპტერი არ არის დაკავშირებული.",
            ["Bluetooth scan failed."] =
                "Bluetooth ძებნა ვერ შესრულდა."
        };



    // =========================================================
    // APP UI TEXTS (view models + pages) — used for every Label/Button
    // =========================================================

    private static readonly Dictionary<string, string> UiKaToEn =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Fuel page — battery energy (EV / hybrid)
            ["მოგზაურობების ისტორია"] = "Trip history",
            ["ბატარეის ენერგია"] = "Battery energy",
            ["◀ რეკუპერაცია"] = "◀ Regeneration",
            ["ხარჯი ▶"] = "Use ▶",
            ["ბატარეის ძაბვა"] = "Battery voltage",
            ["დენი"] = "Current",
            ["დამუხტვა (SoC)"] = "State of charge (SoC)",
            ["ფასი 1 kWh (₾)"] = "Price per kWh (₾)",
            ["ღირებულება"] = "Cost",
            ["ლოგის კოპირება"] = "Copy log",
            ["ბატარეის ტევადობა (kWh)"] = "Battery capacity (kWh)",
            ["დამუხტვა"] = "Charge",
            ["მიახლოებითი ხარჯი"] = "Approximate use",
            // Home page (layout E)
            ["DTC სკანი"] = "DTC scan",
            ["სერვისი"] = "Service",
            ["ემისია"] = "Emissions",
            ["ECU ინფო"] = "ECU info",
            ["აკუმულატორი"] = "Battery",
            ["ჯანმრთელობა"] = "Health",
            ["შესაძლებლობები"] = "Capabilities",
            ["ანგარიში"] = "Report",
            ["ყველა ფუნქცია"] = "All features",
            ["შეცდომების სკანი"] = "Trouble code scan",
            ["ძრავის შეცდომები და მათი წაშლა"] = "Engine trouble codes and clearing",
            ["მანქანის ჯანმრთელობა"] = "Vehicle health",
            ["ქულა და მოკლე შეჯამება"] = "Score and short summary",
            ["შემოწმება ტექდათვალიერებამდე"] = "Check before the inspection",
            ["მონაცემები"] = "Data",
            ["სიჩქარე, ტემპერატურა, ბრუნები"] = "Speed, temperature, RPM",
            ["ძაბვა და დამუხტვა"] = "Voltage and charging",
            ["აჩქარების გაზომვა"] = "Acceleration timing",
            ["ზეთი, ფილტრები, შეხსენებები"] = "Oil, filters, reminders",
            ["შენახული შემოწმებები, PDF"] = "Saved checks, PDF",
            ["ტექნიკური"] = "Technical",
            ["რესეტის ჩაწერა"] = "Reset recorder",
            ["ყიდვა და იმპორტი"] = "Buying and import",
            ["განბაჟების კალკულატორი"] = "Customs calculator",
            ["აქციზი 2026 წლის წესებით + ფასი და ტრანსპორტი"] = "2026 excise rules + price and shipping",
            ["არ არის ზეთის ან Service due ღილაკი? ჩაიწერე, როგორ ნულდება მენიუდან — ბრძანებას შევამოწმებთ და ჩავრთავთ."] = "No oil or Service due button? Record how it is reset from the menu — we review the command and enable it.",
            ["სენსორები, ემისია, ECU, ადაპტერი და სხვა"] = "Sensors, emissions, ECU, adapter and more",
            ["მოდულების დიაგნოსტიკა"] = "Module diagnostics",
            ["აღწერე პრობლემა ან დაასკანერე — AI გეტყვის მიზეზს"] = "Describe the problem or scan — AI tells you the cause",
            ["ABS, აირბეგი, ძარა და სხვა"] = "ABS, airbag, body and more",
            ["ABS, საჭე, ბატარეა — ცოცხალი მონაცემები"] = "ABS, steering, battery — live data",
            ["ყველა მოდულის სკანი"] = "All-module scan",
            ["ABS, აირბეგი, ძარა, გადაცემათა კოლოფი…"] = "ABS, airbag, body, transmission…",
            ["რეკომენდებული შემოწმება"] = "Recommended checks",
            ["მანქანის შეფასება ყიდვამდე"] = "Check a car before buying",
            ["ოდომეტრის შედარება მოდულებთან"] = "Compare the odometer across modules",
            ["რეალური ხარჯი მგზავრობისას"] = "Real consumption while driving",
            ["AI დიაგნოსტიკა"] = "AI diagnostics",
            ["ინსტრუმენტები"] = "Tools",
            ["0–100 km/h ისტორია"] = "0–100 km/h history",
            ["1 რეალურად პასუხმგებელი Generic OBD-II ECU აღმოჩნდა. OEM მისამართის გარეშე ABS/SRS/BCM/TCM არ არის ნაჩვენები."] = "1 responding Generic OBD-II ECU found. ABS/SRS/BCM/TCM are not shown without an OEM address.",
            ["1/7 OBD adapter და ECU კავშირის შემოწმება…"] = "1/7 Checking the OBD adapter and ECU connection…",
            ["10 წამის შემდეგ წაკითხვა"] = "Read after 10 seconds",
            ["12V სისტემა ჯერ არ შემოწმებულა"] = "12V system not checked yet",
            ["12V სისტემის გაზომვა ვერ შესრულდა. შეამოწმე OBD კავშირი."] = "12V measurement failed. Check the OBD connection.",
            ["12V: დაბალი ძაბვა — შეამოწმე აკუმულატორი და დამუხტვა"] = "12V: low voltage — check the battery and charging",
            ["12V: ნორმალური მოსვენების ძაბვა"] = "12V: normal resting voltage",
            ["12V: შუალედური ძაბვა — გაზომვა მიღებულია"] = "12V: intermediate voltage — measurement accepted",
            ["2/7 VIN და ავტომობილის ინფორმაციის წაკითხვა…"] = "2/7 Reading VIN and vehicle information…",
            ["2500 RPM-ზე 10-წამიანი სტაბილიზაცია და წაკითხვა მიმდინარეობს…"] = "10-second stabilization and reading at 2500 RPM in progress…",
            ["3/7 შეცდომის კოდების წაკითხვა…"] = "3/7 Reading trouble codes…",
            ["4/7 Freeze Frame-ის შემოწმება…"] = "4/7 Checking Freeze Frame…",
            ["5/7 სწრაფი Live Data snapshot-ის წაკითხვა…"] = "5/7 Reading a quick Live Data snapshot…",
            ["6/7 Emissions / Readiness-ის შემოწმება…"] = "6/7 Checking Emissions / Readiness…",
            ["7/7 AI აანალიზებს მიღებულ მტკიცებულებას…"] = "7/7 AI is analysing the collected evidence…",
            ["AI Diagnostic და AI Chat ხელმისაწვდომია მხოლოდ PRO მომხმარებლებისთვის."] = "AI Diagnostic and AI Chat are available to PRO users only.",
            ["AI აანალიზებს..."] = "AI is analysing...",
            ["AI დიაგნოსტიკა ვერ დასრულდა. შეამოწმე OBD კავშირი და ინტერნეტი, შემდეგ სცადე თავიდან."] = "AI diagnostic could not finish. Check the OBD connection and internet, then try again.",
            ["AI დიაგნოსტიკა მზადაა — დასკვნები არის სავარაუდო, არა საბოლოო რემონტის დადასტურება."] = "AI diagnostic is ready — conclusions are likely causes, not a final repair confirmation.",
            ["AI პასუხი გამოჩნდება მას შემდეგ, რაც რეალური OBD სკანი დასრულდება."] = "The AI answer will appear after the real OBD scan finishes.",
            ["AI-სთვის რეალური მონაცემები მზადდება…"] = "Preparing real data for AI…",
            ["AI: OBD მონაცემები შენარჩუნებულია. AI სერვისის აღდგენის შემდეგ სცადე თავიდან."] = "AI: OBD data is kept. Try again when the AI service is back.",
            ["AI: სკანი დასრულდა. დამისვი კითხვა ამ კონკრეტული მონაცემების შესახებ."] = "AI: scan finished. Ask me about this specific data.",
            ["AI: ჯერ გაუშვი Start AI Diagnostic, რომ პასუხი ამ მანქანის რეალურ OBD მონაცემებს დაეყრდნოს."] = "AI: run Start AI Diagnostic first so the answer is based on this car's real OBD data.",
            ["ATI ბრძანებაზე გამოსადეგი პასუხი არ დაბრუნებულა."] = "The ATI command returned no usable response.",
            ["Adapter დაკავშირებულია, მაგრამ სტანდარტულ OBD ECU-ს პასუხი არ დაუბრუნებია. fake მოდული არ შექმნილა."] = "The adapter is connected, but no standard OBD ECU answered. No fake module was created.",
            ["Adapter-მა ATRV ბრძანებაზე გამოსადეგი პასუხი არ დააბრუნა."] = "The adapter returned no usable response to ATRV.",
            ["Adapter: მიუწვდომელია"] = "Adapter: unavailable",
            ["After Repair რეალური OBD მონაცემები იკითხება…"] = "Reading real OBD data after repair…",
            ["Before Repair baseline ვერ შეინახება: მიმდინარე სკანის VIN მიუწვდომელია. ჯერ გაუშვი AI Diagnostic დაკავშირებულ მანქანაზე."] = "Before Repair baseline can't be saved: the current scan VIN is unavailable. Run AI Diagnostic on a connected car first.",
            ["Before/After შემოწმებისთვის ჯერ შეინახე რეალური სკანი როგორც „Before Repair“ baseline."] = "For a Before/After check, first save a real scan as the “Before Repair” baseline.",
            ["Bluetooth კავშირი ან adapter session ვერ დადასტურდა."] = "Bluetooth connection or adapter session not confirmed.",
            ["Bluetooth კავშირი და adapter session დადასტურდა."] = "Bluetooth connection and adapter session confirmed.",
            ["Bluetooth ჩართული არ არის. გინდა Bluetooth-ის პარამეტრების გახსნა?"] = "Bluetooth is off. Open Bluetooth settings?",
            ["Bluetooth/ELM327/ECU ტესტი ვერ შესრულდა."] = "Bluetooth/ELM327/ECU test failed.",
            ["CAN — მანქანის ECU-ებთან მონაცემების გაცვლის თანამედროვე და ყველაზე გავრცელებული გზა."] = "CAN — the modern and most common way to exchange data with the car's ECUs.",
            ["Capability Detection განახლდა"] = "Capability Detection updated",
            ["Capability Detection ვერ შესრულდა."] = "Capability Detection failed.",
            ["Capability probe არ შესრულებულა — ECU-ს დადებითი/შეზღუდული პასუხი არ დადასტურდა."] = "Capability probe not performed — no positive/limited ECU response confirmed.",
            ["Charging: მაღალი ძაბვა — შეამოწმე დამუხტვის სისტემა"] = "Charging: high voltage — check the charging system",
            ["Charging: ნორმალური დამუხტვის ძაბვა"] = "Charging: normal charging voltage",
            ["Custom ტესტისთვის ჩაწერე სწორი საწყისი და საბოლოო სიჩქარე."] = "For a Custom test enter a valid start and end speed.",
            ["DTC კოდები ვერ წაიკითხა — სკანირება შეცდომით შეწყდა."] = "DTCs could not be read — the scan stopped with an error.",
            ["DTC კოდები იშლება..."] = "Clearing DTCs...",
            ["DTC კოდები წარმატებით წაიშალა."] = "DTCs cleared successfully.",
            ["DTC კოდების წაშლა წაშლის შენახულ დიაგნოსტიკურ ინფორმაციას. გაგრძელდეს?"] = "Clearing DTCs erases stored diagnostic information. Continue?",
            ["DTC კოდი"] = "DTC code",
            ["DTC მონაცემი ამ ECU-დან ვერ წაიკითხა. ეს არ ნიშნავს, რომ კოდი არ არის."] = "DTC data could not be read from this ECU. This does not mean there are no codes.",
            ["DTC მონაცემი ამ ECU-დან ვერ წაიკითხა; კოდების არარსებობა არ არის დადასტურებული."] = "DTC data could not be read from this ECU; the absence of codes is not confirmed.",
            ["DTC სკანირება დასრულდა."] = "DTC scan finished.",
            ["DTC სკანირება მიმდინარეობს…"] = "DTC scan in progress…",
            ["DTC სკანირების შემდეგ შეგიძლია ECU-ის შენახული snapshot წაიკითხო."] = "After a DTC scan you can read the snapshot stored in the ECU.",
            ["DTC წაშლის გადამოწმების დრო ამოიწურა."] = "DTC clear verification timed out.",
            ["DTC-ის წაშლის შემდეგ"] = "After clearing DTCs",
            ["DTC: მომსახურებები ვერ დადასტურდა; კოდების არარსებობა დადასტურებული არ არის."] = "DTC: services not confirmed; the absence of codes is not confirmed.",
            ["Dashboard odometer სურვილისამებრ შეიყვანე ქვემოთ."] = "Optionally enter the dashboard odometer below.",
            ["Dashboard მნიშვნელობა ვერ შეედარა."] = "The dashboard value could not be compared.",
            ["Dashboard-ის შესანახად ჯერ მანქანის რეალური VIN უნდა წაიკითხოს აპმა."] = "To save the dashboard, the app must first read the car's real VIN.",
            ["Diagnostic path მოწმდება…"] = "Checking diagnostic path…",
            ["ECM ჯერ არ არის დასკანერებული"] = "ECM not scanned yet",
            ["ECU Explorer განახლდა"] = "ECU Explorer updated",
            ["ECU diagnostic path ვერ დადასტურდა."] = "ECU diagnostic path not confirmed.",
            ["ECU ახლა ნაკლებ DTC-ს აჩვენებს და MIL გამორთულია. შედეგი დადებითია, მაგრამ drive cycle-ისა და Readiness-ის დასრულებამდე რემონტი საბოლოოდ დადასტურებულად არ ჩაითვალოს."] = "The ECU now shows fewer DTCs and the MIL is off. The result is positive, but don't consider the repair confirmed until the drive cycle and Readiness are complete.",
            ["ECU მოდულები: იკითხება…"] = "ECU modules: reading…",
            ["ECU მოდულები: —"] = "ECU modules: —",
            ["ECU მოთხოვნა ვერ შესრულდა, ამიტომ პასუხის დრო ვერ გაიზომა."] = "The ECU request failed, so the response time could not be measured.",
            ["ECU მონაცემები ახლდება..."] = "Updating ECU data...",
            ["ECU პასუხი: No Data / negative response."] = "ECU response: No Data / negative response.",
            ["ECU პასუხის დრო: მიუწვდომელია"] = "ECU response time: unavailable",
            ["ECU პასუხის დრო: —\nLive Data rate: —"] = "ECU response time: —\nLive Data rate: —",
            ["ECU პასუხს ჯერ არ აბრუნებს"] = "ECU is not responding yet",
            ["ECU-დან 12V Control Module Voltage (PID 0142) იკითხება…"] = "Reading 12V Control Module Voltage (PID 0142) from the ECU…",
            ["ECU-დან მიუწვდომელია"] = "Unavailable from ECU",
            ["ECU-ის 0100 მოთხოვნის გაგზავნა ვერ მოხერხდა."] = "Could not send the 0100 request to the ECU.",
            ["ECU-ის მიერ მხარდაჭერილი პარამეტრები გამოჩნდება ავტომატურად."] = "Parameters supported by the ECU will appear automatically.",
            ["ECU-ის მონაცემები დიაგნოსტიკური კოდების ბოლო წაშლის შემდეგ"] = "ECU data since the last DTC clear",
            ["ECU-ის მხარდაჭერილი სტანდარტული სენსორები იკითხება…"] = "Reading standard sensors supported by the ECU…",
            ["ECU-ის რეალური პასუხების მოლოდინი…"] = "Waiting for real ECU responses…",
            ["ECU-ის რეალური პასუხის მოლოდინი…"] = "Waiting for a real ECU response…",
            ["ECU-მ Control Module Voltage (PID 0142) მნიშვნელობა არ დააბრუნა."] = "The ECU did not return Control Module Voltage (PID 0142).",
            ["ECU-მ PID 015B მხარდაჭერად გამოაცხადა, მაგრამ დეკოდირებადი მნიშვნელობა არ დააბრუნა. სხვა HV მონაცემი არ არის ნაჩვენები."] = "The ECU reported PID 015B as supported but returned no decodable value. No other HV data is shown.",
            ["ECU-მ მხარდაჭერილი, დეკოდირებადი PID-ები არ დააბრუნა."] = "The ECU returned no supported, decodable PIDs.",
            ["ECU-მ პასუხი დააბრუნა, თუმცა ამ ფორმატისთვის უსაფრთხოდ დეკოდირებადი Mode 05 item ვერ მოიძებნა."] = "The ECU responded, but no safely decodable Mode 05 item was found for this format.",
            ["ECU-მ პასუხი დააბრუნა, მაგრამ ამ პასუხში დეკოდირებადი სტანდარტული Mode 06 item ვერ მოიძებნა."] = "The ECU responded, but no decodable standard Mode 06 item was found in this response.",
            ["ECU-მ სტანდარტულ OBD-II PID მხარდაჭერის მოთხოვნას უპასუხა."] = "The ECU answered the standard OBD-II PID support request.",
            ["ECU-მ სტანდარტული Generic OBD-II პასუხი არ დააბრუნა. ჩართე კონტაქტი ან შეამოწმე ავტომობილის OBD მხარდაჭერა."] = "The ECU returned no standard Generic OBD-II response. Turn the ignition on or check the car's OBD support.",
            ["ECU-ს ეგზავნება ერთი მოკლე, read-only Mode 05 მოთხოვნა."] = "Sending one short, read-only Mode 05 request to the ECU.",
            ["ECU-ს ვუგზავნით ერთ მოკლე, read-only Mode 06 მოთხოვნას."] = "Sending one short, read-only Mode 06 request to the ECU.",
            ["EGR / VVT სისტემა"] = "EGR / VVT system",
            ["EGR სისტემა"] = "EGR system",
            ["ERROR — OEM identification ვერ შესრულდა."] = "ERROR — OEM identification failed.",
            ["Emissions: Readiness მონაცემი მიუწვდომელია"] = "Emissions: Readiness data unavailable",
            ["Emissions: იკითხება…"] = "Emissions: reading…",
            ["FREEZEFRAME.READ: Available — ECU snapshot პასუხი დადასტურებულია"] = "FREEZEFRAME.READ: Available — ECU snapshot response confirmed",
            ["FREEZEFRAME.READ: Conditional — რეალური DTC snapshot-ისთვის DTC აუცილებელია"] = "FREEZEFRAME.READ: Conditional — a DTC is required for a real DTC snapshot",
            ["Freeze Frame · ხარვეზის მომენტი"] = "Freeze Frame · moment of the fault",
            ["Freeze Frame არ იკითხება, რადგან სკანირება ვერ დასრულდა."] = "Freeze Frame is not read because the scan did not finish.",
            ["Freeze Frame არ შემოწმებულა — პასუხმგებელი OBD ECU ვერ დადასტურდა."] = "Freeze Frame not checked — no responding OBD ECU confirmed.",
            ["Freeze Frame ვერ წაიკითხა"] = "Freeze Frame could not be read",
            ["Freeze Frame იკითხება..."] = "Reading Freeze Frame...",
            ["Freeze Frame ინფორმაცია ამ შენახულ სკანში არ არის."] = "This saved scan has no Freeze Frame information.",
            ["Freeze Frame მიუწვდომელია"] = "Freeze Frame unavailable",
            ["Freeze Frame მონაცემი ECU-ში არ არის შენახული ან ამ ECU-ზე მიუწვდომელია."] = "No Freeze Frame is stored in the ECU or it is unavailable on this ECU.",
            ["Freeze Frame მონაცემი ECU-ში არ არის შენახული."] = "No Freeze Frame is stored in the ECU.",
            ["Freeze Frame მონაცემი ECU-ში არ აღმოჩნდა."] = "No Freeze Frame found in the ECU.",
            ["Freeze Frame მონაცემი ამ სკანზე ECU-მ არ დააბრუნა."] = "The ECU returned no Freeze Frame on this scan.",
            ["Freeze Frame მხოლოდ DTC-ის არსებობისას და ECU-ის მხარდაჭერისას გამოჩნდება."] = "Freeze Frame appears only when a DTC exists and the ECU supports it.",
            ["Freeze Frame წაკითხვა ვერ დაიწყო."] = "Freeze Frame reading could not start.",
            ["Freeze Frame წაკითხვა ვერ დასრულდა."] = "Freeze Frame reading could not finish.",
            ["Freeze Frame ჯერ არ წაკითხულა."] = "Freeze Frame not read yet.",
            ["Freeze Frame — რეალური ECU snapshot"] = "Freeze Frame — real ECU snapshot",
            ["Freeze Frame-ის მონაცემები ჯერ არ არის წაკითხული."] = "Freeze Frame data not read yet.",
            ["Freeze Frame-ის რეალური მონაცემები ECU-დან წაიკითხა."] = "Real Freeze Frame data read from the ECU.",
            ["Freeze Frame: ECU-ში ამ DTC-ის snapshot არ აღმოჩნდა."] = "Freeze Frame: no snapshot for this DTC in the ECU.",
            ["Freeze Frame: ECU-ში შენახული snapshot არ აღმოჩნდა."] = "Freeze Frame: no stored snapshot in the ECU.",
            ["Freeze Frame: არ არის"] = "Freeze Frame: none",
            ["Freeze Frame: იკითხება…"] = "Freeze Frame: reading…",
            ["Fuel Rate ან MAF ჯერ არ მოსულა ECU-დან."] = "Fuel Rate or MAF has not arrived from the ECU yet.",
            ["Fuel Trim ორივე რეჟიმში მაღალია. ეს შეიძლება მიუთითებდეს ჰაერის/MAF-ის ან საწვავის მიწოდების საკითხზე; მხოლოდ ამ ტესტით ნაწილი ვერ დადასტურდება."] = "Fuel Trim is high in both modes. This may point to an air/MAF or fuel delivery issue; this test alone can't confirm a part.",
            ["Fuel consumption statistics ხელმისაწვდომია PRO-ში."] = "Fuel consumption statistics are available in PRO.",
            ["Generic ECU დადასტურდა. OEM discovery ვერ დასრულდა; Generic შედეგი შენახულია."] = "Generic ECU confirmed. OEM discovery did not finish; the Generic result is kept.",
            ["Generic OBD-II Powertrain ECU: პასუხი არ დადასტურდა."] = "Generic OBD-II Powertrain ECU: response not confirmed.",
            ["Generic OBD-II შესაძლებლობები მოწმდება. მხოლოდ რეალურად პასუხმგებელი ECU დაემატება."] = "Checking Generic OBD-II capabilities. Only really responding ECUs are added.",
            ["Generic OBD-II-მა Hybrid Battery Remaining Life (PID 015B) არ დაადასტურა. Pack voltage, current, SOC და cell data-სთვის საჭიროა დადასტურებული OEM/BMS პროფილი."] = "Generic OBD-II did not confirm Hybrid Battery Remaining Life (PID 015B). Pack voltage, current, SOC and cell data need a confirmed OEM/BMS profile.",
            ["Guided Test გაუქმდა მომხმარებლის მიერ."] = "Guided Test cancelled by the user.",
            ["Guided Test მიუწვდომელია: ამ ECU-მ Fuel Trim Bank 1 PID არ დაადასტურა."] = "Guided Test unavailable: this ECU did not confirm the Fuel Trim Bank 1 PID.",
            ["Guided Test: ჯერ არ შესრულებულა."] = "Guided Test: not run yet.",
            ["HV Battery/BECM მონაცემები ჯერ არ შემოწმებულა."] = "HV Battery/BECM data not checked yet.",
            ["Hybrid Battery PID ვერ განახლდა; მონაცემი არ არის გამოგონილი."] = "Hybrid Battery PID could not update; no data is invented.",
            ["ISO 9141 — ძველი OBD კავშირის სტანდარტი, რომელიც ზოგ მანქანაზე გამოიყენება."] = "ISO 9141 — an older OBD connection standard used on some cars.",
            ["Idle Fuel Trim ECU-მ არ დააბრუნა — შედარება ვერ შესრულდება."] = "The ECU did not return Idle Fuel Trim — comparison is not possible.",
            ["Idle Fuel Trim იკითხება…"] = "Reading Idle Fuel Trim…",
            ["Idle-ის წაკითხვა"] = "Idle reading",
            ["J1850 — ძველი OBD სტანდარტი, რომელიც ძირითადად ამერიკულ ავტომობილებში გვხვდება."] = "J1850 — an older OBD standard found mainly in American cars.",
            ["KWP — OBD კავშირის ძველი სტანდარტი, რომელიც ძირითადად ძველ ავტომობილებში გვხვდება."] = "KWP — an older OBD connection standard found mainly in older cars.",
            ["Live Data snapshot: ამ ECU-მ თავსებადი სტანდარტული PID არ გამოაცხადა"] = "Live Data snapshot: this ECU reported no compatible standard PID",
            ["Live Data: ECU-მ snapshot მნიშვნელობა არ დააბრუნა"] = "Live Data: the ECU returned no snapshot value",
            ["Live Data: იკითხება…"] = "Live Data: reading…",
            ["Live data დაპაუზებულია"] = "Live data paused",
            ["Live data მიმდინარეობს..."] = "Live data running...",
            ["Live data შეჩერებულია"] = "Live data stopped",
            ["Live მონაცემები"] = "Live data",
            ["Live მონაცემები ახლდება…"] = "Updating live data…",
            ["MIL / Readiness სტატუსი ECU-დან მიუწვდომელია."] = "MIL / Readiness status unavailable from the ECU.",
            ["MIL / Readiness სტატუსი დამუშავდა."] = "MIL / Readiness status processed.",
            ["MIL / Readiness სტატუსის წაკითხვა…"] = "Reading MIL / Readiness status…",
            ["MIL / Readiness: ECU-მ მონაცემი არ დააბრუნა."] = "MIL / Readiness: the ECU returned no data.",
            ["MIL სტატუსი"] = "MIL status",
            ["MIL: OFF ან მიუწვდომელია"] = "MIL: OFF or unavailable",
            ["MODE 05 ვერ წაიკითხა"] = "MODE 05 could not be read",
            ["MODE 05 მიუწვდომელია"] = "MODE 05 unavailable",
            ["MODE 05 მონაცემი ვერ გაიშიფრა"] = "MODE 05 data could not be decoded",
            ["MODE 05 შემოწმება…"] = "Checking MODE 05…",
            ["MODE 05 — არცერთი წაკითხული item არ არის flagged"] = "MODE 05 — no item read is flagged",
            ["MODE 06 ვერ წაიკითხა"] = "MODE 06 could not be read",
            ["MODE 06 მიუწვდომელია"] = "MODE 06 unavailable",
            ["MODE 06 მონაცემი ვერ გაიშიფრა"] = "MODE 06 data could not be decoded",
            ["MODE 06 შემოწმება…"] = "Checking MODE 06…",
            ["MODE 06 — ყველა წაკითხული ტესტი ზღვარშია"] = "MODE 06 — all tests read are within limits",
            ["Mileage Verification ხელმისაწვდომია PRO-ში."] = "Mileage Verification is available in PRO.",
            ["Mode 05 არის optional O₂ monitor service; ბევრ თანამედროვე ECU-ს ის არ აქვს."] = "Mode 05 is an optional O₂ monitor service; many modern ECUs don't have it.",
            ["Mode 06 optional სტანდარტული OBD-II სერვისია."] = "Mode 06 is an optional standard OBD-II service.",
            ["NMHC კატალიზატორი"] = "NMHC catalyst",
            ["NOT AVAILABLE — Vehicle identity ვერ დადასტურდა."] = "NOT AVAILABLE — vehicle identity not confirmed.",
            ["NOx / SCR სისტემა"] = "NOx / SCR system",
            ["Not Supported — OBD ადაპტერი არ არის დაკავშირებული."] = "Not Supported — OBD adapter not connected.",
            ["Not Supported — OBD კავშირი გათიშულია."] = "Not Supported — OBD connection lost.",
            ["Not Supported — PID-ის კითხვა შეწყდა."] = "Not Supported — PID reading stopped.",
            ["Not Supported — ამ PID-ის წაკითხვა ვერ დაიწყო."] = "Not Supported — this PID reading could not start.",
            ["Not Supported — ამ PID-ს მიმდინარე ECU არ უჭერს მხარს."] = "Not Supported — the current ECU does not support this PID.",
            ["OBD / Freeze Frame მზადდება…"] = "Preparing OBD / Freeze Frame…",
            ["OBD adapter-ის ინიციალიზება…"] = "Initializing OBD adapter…",
            ["OBD კავშირის შემოწმება…"] = "Checking OBD connection…",
            ["OBD მოდულის სკანირება ვერ დასრულდა. მონაცემი არ არის გამოგონილი."] = "OBD module scan could not finish. No data is invented.",
            ["OBD პროტოკოლი იტვირთება…"] = "Loading OBD protocol…",
            ["OBD პროტოკოლი მზადაა."] = "OBD protocol ready.",
            ["OBD სკანი დასრულდა — AI პასუხი დროებით მიუწვდომელია."] = "OBD scan finished — AI answer temporarily unavailable.",
            ["OBD-II დაკავშირებულია — ავტომობილის ინფორმაცია მზადაა."] = "OBD-II connected — vehicle information is ready.",
            ["OBD-II დაკავშირებულია. ავტომობილის VIN, წელი და საწვავი იკითხება…"] = "OBD-II connected. Reading the car's VIN, year and fuel…",
            ["OBD-II დაკავშირებულია. ავტომობილის ინფორმაცია Home-ზე ხელახლა განახლდება."] = "OBD-II connected. Vehicle information will refresh on Home.",
            ["OBD-II ემისიები და readiness მონიტორები"] = "OBD-II emissions and readiness monitors",
            ["OBD-II სკანირება იწყება…"] = "Starting OBD-II scan…",
            ["OBD2 არ არის დაკავშირებული"] = "OBD2 not connected",
            ["OBD2 • გათიშული"] = "OBD2 • disconnected",
            ["OBD2 • დაკავშირებულია"] = "OBD2 • connected",
            ["OEM ECU მოთხოვნა მხოლოდ verified definition-ის არსებობისას გაიგზავნება."] = "OEM ECU requests are sent only when a verified definition exists.",
            ["OEM Live Data გაჩერებულია — დააჭირე OEM Data-ს."] = "OEM Live Data stopped — tap OEM Data.",
            ["OEM Live Data ვერ დაიწყო; გამოგონილი მონაცემი არ გამოჩნდება."] = "OEM Live Data could not start; no invented data is shown.",
            ["OEM Live Data იწყება…"] = "Starting OEM Live Data…",
            ["OEM Live Data რეალური ECU პასუხებით ახლდება."] = "OEM Live Data is updating with real ECU responses.",
            ["OEM extension შეცდომით დასრულდა; Generic OBD შედეგი უცვლელად შენარჩუნდა."] = "OEM extension ended with an error; the Generic OBD result is kept unchanged.",
            ["OEM identification ჯერ არ შემოწმებულა."] = "OEM identification not checked yet.",
            ["OEM მოდულები მხოლოდ დადასტურებული მწარმოებლის პროფილისას გამოჩნდება."] = "OEM modules appear only with a confirmed manufacturer profile.",
            ["PRO ფუნქცია"] = "PRO feature",
            ["Peak/Avg acceleration გამოჩნდება გაზომვის დასრულების შემდეგ."] = "Peak/Avg acceleration will appear after the measurement finishes.",
            ["Plug-in ჰიბრიდი"] = "Plug-in hybrid",
            ["Quick Scan მზადაა"] = "Quick Scan ready",
            ["Raw პასუხი მივიღეთ, მაგრამ დადასტურებული სტანდარტული item ვერ მოიძებნა."] = "A raw response was received, but no confirmed standard item was found.",
            ["Raw პასუხი ჯერ არ არის."] = "No raw response yet.",
            ["Readiness მონიტორები და ემისიის სტატუსი იკითხება"] = "Reading readiness monitors and emission status",
            ["Readiness: ECU-მ მონაცემი არ დააბრუნა"] = "Readiness: the ECU returned no data",
            ["Readiness: მიუწვდომელია"] = "Readiness: unavailable",
            ["Refuel Log ხელმისაწვდომია PRO-ში."] = "Refuel Log is available in PRO.",
            ["Refuel Log-ის შესანახად საჭიროა ამ მანქანის რეალური VIN. ერთხელ დააკავშირე OBD და წაიკითხე Vehicle Info."] = "Saving the Refuel Log needs this car's real VIN. Connect OBD once and read Vehicle Info.",
            ["Refuel ისტორიის სანახავად ამოიცანი მანქანა რეალური VIN-ით."] = "Identify the car by its real VIN to see the refuel history.",
            ["Reviewed OEM DID რეალურ დროში ახლდება."] = "Reviewed OEM DID updates in real time.",
            ["Snapshot მნიშვნელობები არ დაბრუნებულა."] = "No snapshot values returned.",
            ["Snapshot ჯერ არ შექმნილა"] = "Snapshot not created yet",
            ["Standard OBD-II · მანქანის მიმდინარე odometer წაკითხულია."] = "Standard OBD-II · the car's current odometer was read.",
            ["Supported PID-ების რეალური მნიშვნელობები მბრუნავ რეჟიმში ახლდება."] = "Real values of supported PIDs update in rotation.",
            ["Supported PID-ების შემოწმება…"] = "Checking supported PIDs…",
            ["Tank-to-Tank ვერ გამოითვალა: ბოლო სრული ავზის ოდომეტრი წინა ჩანაწერზე დიდი უნდა იყოს."] = "Tank-to-Tank could not be calculated: the last full-tank odometer must be higher than the previous entry.",
            ["Tank-to-Tank ხარჯისთვის საჭიროა ერთი VIN-ის ორი „სრული ავზი“ ჩანაწერი."] = "Tank-to-Tank consumption needs two “full tank” entries for one VIN.",
            ["Trip History-ის სანახავად ამოიცანი მანქანა რეალური VIN-ით."] = "Identify the car by its real VIN to see Trip History.",
            ["VIN / Vehicle Info: მიუწვდომელია"] = "VIN / Vehicle Info: unavailable",
            ["VIN ECU-დან იტვირთება…"] = "Loading VIN from ECU…",
            ["VIN ECU-დან ჯერ არ მოსულა"] = "VIN not received from ECU yet",
            ["VIN და Vehicle Info-ის წაკითხვა…"] = "Reading VIN and Vehicle Info…",
            ["VIN ვერ წავიკითხეთ, ამიტომ სხვა მანქანის მონაცემებთან უსაფრთხო შედარება შეუძლებელია."] = "The VIN could not be read, so a safe comparison with other cars' data is not possible.",
            ["VIN მიუწვდომელია"] = "VIN unavailable",
            ["VIN მიუწვდომელია ამ ECU-ზე"] = "VIN unavailable on this ECU",
            ["VIN მიუწვდომელია; სხვადასხვა ავტომობილის სკანები უსაფრთხოდ არ შედარდება."] = "VIN unavailable; scans of different cars will not be compared.",
            ["VIN უცნობია"] = "VIN unknown",
            ["VIN: იკითხება…"] = "VIN: reading…",
            ["Vehicle Info მიღებულია."] = "Vehicle Info received.",
            ["Vehicle Speed PID 01 0D ვერ წაიკითხა ECU-მ."] = "The ECU could not read Vehicle Speed PID 01 0D.",
            ["Vehicle Speed PID 01 0D-ის რეალურ პასუხს ველოდები…"] = "Waiting for a real Vehicle Speed PID 01 0D response…",
            ["Vehicle Speed PID 01 0D-ის წასაკითხად საჭიროა აქტიური OBD კავშირი."] = "Reading Vehicle Speed PID 01 0D needs an active OBD connection.",
            ["Verified OEM ECU და Live Data DID-ები მოწმდება…"] = "Checking verified OEM ECU and Live Data DIDs…",
            ["აალების გამოტოვება"] = "Misfire",
            ["ავტომობილი"] = "Vehicle",
            ["ავტომობილი: იკითხება…"] = "Vehicle: reading…",
            ["ავტომობილი: —"] = "Vehicle: —",
            ["აირჩიე Live Data JSON"] = "Choose Live Data JSON",
            ["აირჩიე აღმოჩენილი მოდული დეტალების სანახავად."] = "Select a detected module to see details.",
            ["აირჩიე აღმოჩენილი მოდული."] = "Select a detected module.",
            ["აირჩიე მანქანა"] = "Choose car",
            ["აირჩიე მარკა დაკავშირებამდე"] = "Choose the brand before connecting",
            ["აირჩიე მხარდაჭერილი PID."] = "Select a supported PID.",
            ["აირჩიე სკანი და დააჭირე „წინასთან შედარება“."] = "Select a scan and tap “Compare with previous”.",
            ["აირჩიე „წინასთან შედარება“, რომ ამ VIN-ის წინა შენახულ სკანს შეადარო."] = "Tap “Compare with previous” to compare with this VIN's previous saved scan.",
            ["ამ ECU-მ Mode 02 Freeze Frame DTC არ დააბრუნა. ეს ხშირად ნორმალურია, როცა აქტიური/შენახული ხარვეზი არ არსებობს."] = "This ECU returned no Mode 02 Freeze Frame DTC. This is often normal when there is no active/stored fault.",
            ["ამ ECU-მ Mode 05 O₂ test მონაცემი არ დააბრუნა. ეს გაუმართაობას არ ნიშნავს — თანამედროვე მანქანებში ხშირად Mode 06 გამოიყენება."] = "This ECU returned no Mode 05 O₂ test data. This is not a fault — modern cars often use Mode 06.",
            ["ამ ECU-მ Mode 06 მონაცემი არ დააბრუნა. ეს თავისთავად გაუმართაობას არ ნიშნავს."] = "This ECU returned no Mode 06 data. This alone is not a fault.",
            ["ამ ECU-მ ამ კატეგორიის სტანდარტული PID ჯერ არ დაადასტურა."] = "This ECU has not confirmed a standard PID in this category yet.",
            ["ამ ECU-მ სტანდარტული, გასაშიფრი სენსორი არ დაადასტურა. Not Supported PID-ები დამალულია."] = "This ECU confirmed no standard, decodable sensor. Not Supported PIDs are hidden.",
            ["ამ OEM ECU-სთვის საჩვენებელი DTC ჩანაწერი არ არის."] = "No DTC entry to show for this OEM ECU.",
            ["ამ VIN-ის შენახული Live Dashboard აღდგა."] = "The saved Live Dashboard for this VIN was restored.",
            ["ამ VIN-ისთვის შედარებადი წინა სრული ანგარიში ვერ მოიძებნა."] = "No comparable previous full report found for this VIN.",
            ["ამ მონიტორებს კონკრეტული მართვის პირობები სჭირდება"] = "These monitors need specific driving conditions",
            ["ამ სესიის ფაილი ვერ მოიძებნა."] = "The file for this session was not found.",
            ["ამ სკანზე ECU-მ დეკოდირებადი live მნიშვნელობა არ დააბრუნა."] = "The ECU returned no decodable live value on this scan.",
            ["აორთქლების სისტემა (EVAP)"] = "Evaporative system (EVAP)",
            ["არ არის მითითებული"] = "Not specified",
            ["არ არის მხარდაჭერილი"] = "Not supported",
            ["არა"] = "No",
            ["არაუწყვეტი"] = "Continuous",
            ["არაუწყვეტი მონიტორები"] = "Continuous monitors",
            ["არჩეული PID-ის წაკითხვა ვერ დაიწყო. სცადე თავიდან."] = "Reading the selected PID could not start. Try again.",
            ["არჩეული PID-ის წასაკითხად ჯერ დააკავშირე OBD ადაპტერი."] = "Connect the OBD adapter first to read the selected PID.",
            ["არჩეულმა ECU-მ ამ ეტაპზე მონაცემი არ დააბრუნა."] = "The selected ECU returned no data at this step.",
            ["აქტიური DTC არ აღმოჩნდა."] = "No active DTC found.",
            ["აქტიური DTC: 0 — Generic OBD სკანზე კოდი არ აღმოჩნდა"] = "Active DTC: 0 — no code found in the Generic OBD scan",
            ["აქტიური DTC: იკითხება…"] = "Active DTC: reading…",
            ["აქტიური DTC: —"] = "Active DTC: —",
            ["აქტიური OBD პროტოკოლი adapter-მა ვერ დაადასტურა."] = "The adapter could not confirm the active OBD protocol.",
            ["აქტიური შეცდომის კოდები არ აღმოჩნდა — დიაგნოსტიკის მიხედვით პრობლემა არ დაფიქსირდა."] = "No active trouble codes found — no problem detected by the diagnostics.",
            ["აქტიური, Pending და Permanent შეცდომის კოდები არ აღმოჩნდა."] = "No active, Pending or Permanent trouble codes found.",
            ["აქტიური, შენახული, Pending ან Permanent კოდი არ აღმოჩნდა."] = "No active, stored, Pending or Permanent code found.",
            ["ახალი 12V გაზომვა ვერ შესრულდა; ბოლო წარმატებული მონაცემი ზემოთ რჩება."] = "New 12V measurement failed; the last successful value stays above.",
            ["ახალი 12V გაზომვა იკითხება; ეკრანზე ბოლო წარმატებული მონაცემია."] = "Reading a new 12V measurement; the last successful value is shown.",
            ["ახლა არა"] = "Not now",
            ["ბენზინი"] = "Gasoline",
            ["ბენზინი / ელექტრო"] = "Gasoline / Electric",
            ["ბოლო AI სკანი: —"] = "Last AI scan: —",
            ["ბოლო განახლება: შეცდომა"] = "Last update: error",
            ["ბოლო განახლება: ჯერ არ არის"] = "Last update: not yet",
            ["ბუსტის წნევა"] = "Boost pressure",
            ["გააქტიურება"] = "Activate",
            ["გაზომვების ისტორია ცარიელია."] = "Measurement history is empty.",
            ["გათბობადი კატალიზატორი"] = "Heated catalyst",
            ["გამონაბოლქვის გაზის სენსორი"] = "Exhaust gas sensor",
            ["განახლებისთვის დააჭირე ღილაკს."] = "Tap the button to refresh.",
            ["გარბენი ჯერ არ შემოწმებულა"] = "Mileage has not been checked yet",
            ["გარბენის ყველა ხელმისაწვდომი წყაროს შემოწმება..."] = "Checking all available mileage sources...",
            ["გარბენის შემოწმება დასრულდა."] = "Mileage check finished.",
            ["გარბენის ხელმისაწვდომი წყაროებიდან მნიშვნელობა ვერ მოიძებნა."] = "No value found in the available mileage sources.",
            ["გაჩერებულია"] = "Stopped",
            ["გახურების ციკლები"] = "Warm-up cycles",
            ["დააჭირე SCAN-ს, რომ დადასტურდეს რომელი ECU პასუხობს რეალურად."] = "Tap SCAN to confirm which ECU really responds.",
            ["დააჭირე TEST-ს რეალური adapter/ECU შემოწმებისთვის."] = "Tap TEST to check the real adapter/ECU.",
            ["დააჭირე „Start AI Diagnostic“-ს."] = "Tap “Start AI Diagnostic”.",
            ["დადასტურება"] = "Confirm",
            ["დადასტურებული PID-ებიდან რეალური მნიშვნელობა ვერ წაიკითხა. მიუწვდომელი PID-ები დამალულია."] = "No real value could be read from the confirmed PIDs. Unavailable PIDs are hidden.",
            ["დაკავშირება არ არის"] = "Not connected",
            ["დაკვირვება საჭიროა"] = "Needs attention",
            ["დასაწყებად დააჭირე „შემოწმება“-ს."] = "Tap “Check” to start.",
            ["დასრულებული მოგზაურობები ჯერ არ არის."] = "No completed trips yet.",
            ["დასრულებული მოგზაურობები ჯერ არ არის. Start → Stop შემდეგ ჩანაწერი ავტომატურად შეინახება, თუ ECU-დან მოვა რეალური speed და fuel data."] = "No completed trips yet. After Start → Stop the trip is saved automatically if real speed and fuel data come from the ECU.",
            ["დასრულებულია"] = "Complete",
            ["დაუსრულებელია"] = "Incomplete",
            ["დიაგნოსტიკური ანგარიში"] = "Diagnostic report",
            ["დიახ"] = "Yes",
            ["დიზელი"] = "Diesel",
            ["დრო · MIL ჩართული"] = "Time · MIL on",
            ["დრო ამოიწურა"] = "Timed out",
            ["ელექტრო"] = "Electric",
            ["ემისიები"] = "Emissions",
            ["ემისიის DTC"] = "Emission DTC",
            ["ერთ გრაფიკზე მაქსიმუმ 3 PID შეიძლება."] = "A chart can show at most 3 PIDs.",
            ["ეს არის ECU-ის raw Mode 05 სტატუსი. NOT FLAGGED არ ნიშნავს ყველა O₂ სისტემის აბსოლუტურ დიაგნოზს."] = "This is the ECU's raw Mode 05 status. NOT FLAGGED is not an absolute diagnosis of every O₂ system.",
            ["ეს არის მანქანის ECU-სთან კავშირის ტექნიკური პროტოკოლი."] = "This is the technical protocol used to talk to the car's ECU.",
            ["ეს შაბლონი შესაძლოა უფრო შეესაბამებოდეს vacuum/intake leak-ს idle-ზე. დაათვალიერე ჰაერის შლანგები და დაადასტურე სხვა ტესტებით."] = "This pattern may fit a vacuum/intake leak at idle. Inspect the air hoses and confirm with other tests.",
            ["ეს ძველი სკანი სრულ დეტალურ report მონაცემს არ შეიცავს."] = "This older scan does not contain full report data.",
            ["ექსპორტი"] = "Export",
            ["ექსპორტის შეცდომა"] = "Export error",
            ["თუ dashboard odometer იცი, ჩაწერე ქვემოთ — ხელით შეყვანილი მნიშვნელობა მკაფიოდ გამოიყოფა ECU მონაცემისგან."] = "If you know the dashboard odometer, enter it below — a manually entered value is clearly separated from ECU data.",
            ["ინსპექციის მზადყოფნა"] = "Inspection readiness",
            ["ინსპექციის წესები ქვეყნების და რეგიონების მიხედვით განსხვავდება. ეს გვერდი აჩვენებს ECU-ის მიერ დაბრუნებულ OBD-II readiness სტატუსს."] = "Inspection rules differ by country and region. This page shows the OBD-II readiness status returned by the ECU.",
            ["ისტორია ცარიელია."] = "History is empty.",
            ["კავშირი ან ECU პასუხი ვერ დადასტურდა; მონაცემი არ არის გამოგონილი."] = "Connection or ECU response not confirmed; no data is invented.",
            ["კავშირის შემოწმება ვერ შესრულდა"] = "Connection check failed",
            ["კავშირის შემოწმება მიმდინარეობს…"] = "Checking connection…",
            ["კავშირის შეცდომა."] = "Connection error.",
            ["კატალიზატორი"] = "Catalyst",
            ["კოდები იშლება..."] = "Clearing codes...",
            ["კოდები წაიშალა და Stored/Pending/Permanent/MIL verification წარმატებით დასრულდა."] = "Codes cleared and Stored/Pending/Permanent/MIL verification completed successfully.",
            ["კოდები წაიშალა, მაგრამ შეცდომა ისევ დაბრუნდა."] = "Codes were cleared, but the fault came back.",
            ["კომპლექტაცია"] = "Trim",
            ["კომპლექტაცია მიუწვდომელია"] = "Trim unavailable",
            ["კონდიციონერის სისტემა"] = "A/C system",
            ["კოპირება"] = "Copy",
            ["მანქანა არჩეული არ არის"] = "No car selected",
            ["მანქანა ისევ გაჩერებული უნდა იყოს. ნეიტრალში/P-ზე დაიჭირე დაახლოებით 2500 RPM 10 წამით. არ შეასრულო ეს ტესტი გზაზე ან დახურულ, უვენტილაციო სივრცეში."] = "The car must stay stationary. In Neutral/P hold about 2500 RPM for 10 seconds. Do not run this test on the road or in a closed, unventilated space.",
            ["მანქანა უცნობია"] = "Unknown car",
            ["მანქანის ინფორმაცია მიუწვდომელია"] = "Vehicle information unavailable",
            ["მანქანის ინფორმაციის კითხვა..."] = "Reading vehicle information...",
            ["მანძილი · MIL ჩართული"] = "Distance · MIL on",
            ["მანძილი წაშლის შემდეგ"] = "Distance since clear",
            ["მეორადი ჰაერის სისტემა"] = "Secondary air system",
            ["მზადაა"] = "Ready",
            ["მზადყოფნის სტატუსი"] = "Readiness status",
            ["მიმდინარე მართვის ციკლი"] = "Current drive cycle",
            ["მიმდინარე მნიშვნელობები დაფიქსირებულია ეკრანზე."] = "Current values are frozen on screen.",
            ["მიმდინარე ციკლის readiness — optional Mode 01 PID 41"] = "Current-cycle readiness — optional Mode 01 PID 41",
            ["მიუწვდომელია"] = "Unavailable",
            ["მიუწვდომელია — Live Data სესია არ არის აქტიური"] = "Unavailable — no active Live Data session",
            ["მნიშვნელობების განსხვავება ამ მოკლე ტესტში საკმარისი არ არის კონკრეტული მიზეზის დასასკვნელად."] = "The difference in values in this short test is not enough to conclude a specific cause.",
            ["მოგზაურობა არ შენახულა: რეალური VIN მიუწვდომელია."] = "Trip not saved: real VIN unavailable.",
            ["მოგზაურობა არ შენახულა: რეალური მანძილისა და საწვავის მონაცემი საკმარისი არ იყო."] = "Trip not saved: not enough real distance and fuel data.",
            ["მოდულის ინფორმაცია მიუწვდომელია."] = "Module information unavailable.",
            ["მოლოდინში"] = "Waiting",
            ["მონაცემები და სტატისტიკა გასუფთავდა"] = "Data and statistics cleared",
            ["მონაცემები ჯერ არ მოსულა"] = "No data received yet",
            ["მონაცემი გამოგონილი არ არის: ამ ECU/adapter-ის პასუხისთვის საჭიროა დამატებითი, დადასტურებული definition."] = "No data is invented: this ECU/adapter response needs an additional, confirmed definition.",
            ["მონიტორები, რომლებიც ძრავის მუშაობისას მუდმივად მოწმდება"] = "Monitors checked continuously while the engine runs",
            ["მოწმდება DTC კოდების წაშლა..."] = "Verifying DTC clearing...",
            ["მტკიცებულება: ჯერ არ არის წაკითხული."] = "Evidence: not read yet.",
            ["მუდმივი"] = "Permanent",
            ["მხარდაჭერილია"] = "Supported",
            ["ნამდვილად გსურთ DTC კოდების წაშლა? ამ მოქმედებამ შეიძლება Readiness მონიტორებიც გაანულოს."] = "Do you really want to clear DTCs? This may also reset the Readiness monitors.",
            ["ნაჩვენებია მხოლოდ ECU-ის მიერ რეალურად მხარდაჭერილი PID-ები."] = "Only PIDs really supported by the ECU are shown.",
            ["ნაწილაკების ფილტრი"] = "Particulate filter",
            ["ნელი პასუხი — შეამოწმე adapter/კონტაქტი"] = "Slow response — check the adapter/ignition",
            ["პირველ რიგში შეასრულე სკანირება."] = "Run a scan first.",
            ["პროტოკოლი ჯერ არ არის განსაზღვრული."] = "Protocol not determined yet.",
            ["პროფილი ჯერ არ არის დადასტურებული"] = "Profile not confirmed yet",
            ["ჟანგბადის სენსორი"] = "Oxygen sensor",
            ["ჟანგბადის სენსორის გამათბობელი"] = "Oxygen sensor heater",
            ["რეალური ECU დეტალები ამ შენახულ სკანში არ არის."] = "This saved scan has no real ECU details.",
            ["რეალური ECU მნიშვნელობების მოლოდინი…"] = "Waiting for real ECU values…",
            ["რეალური PID შედარება:"] = "Real PID comparison:",
            ["რეალური მოდული ვერ დადასტურდა. შეამოწმე კონტაქტი ON, adapter, Bluetooth და ავტომობილის OBD-II მხარდაჭერა."] = "No real module confirmed. Check ignition ON, adapter, Bluetooth and the car's OBD-II support.",
            ["რეალური პასუხების შედეგების მომზადება…"] = "Preparing results of real responses…",
            ["რეალური პასუხის დროის მოლოდინი…"] = "Waiting for the real response time…",
            ["რეალური სიჩქარის წაკითხვა ვერ დაიწყო. შეამოწმე OBD კავშირი."] = "Real speed reading could not start. Check the OBD connection.",
            ["რიცხვითი შედარება ვერ გაკეთდა; ნახე ეს მნიშვნელობები Live Data-ში."] = "Numeric comparison not possible; see these values in Live Data.",
            ["საბოლოო სიჩქარე საწყისზე მეტია აუცილებელი და 300 km/h-ს არ უნდა აჭარბებდეს."] = "The end speed must be higher than the start speed and not above 300 km/h.",
            ["საწვავის სისტემა"] = "Fuel system",
            ["სენსორების სკანირება დასრულდა."] = "Sensor scan finished.",
            ["სენსორების წაკითხვა ვერ დაიწყო. შეამოწმე OBD კავშირი."] = "Sensor reading could not start. Check the OBD connection.",
            ["სესია ცარიელია — ECU-დან მონაცემი არ მოსულა."] = "Session is empty — no data came from the ECU.",
            ["სესიის წაშლა"] = "Delete session",
            ["სკანი შენახულია, თუმცა იმ მომენტში Generic OBD ECU პასუხი ვერ დადასტურდა."] = "Scan saved, although no Generic OBD ECU response was confirmed at that moment.",
            ["სკანი შეწყდა — რეალური მონაცემები ვერ მივიღეთ."] = "Scan stopped — no real data received.",
            ["სკანირება დასრულდა — DTC მონაცემი ნაწილობრივ წაკითხულია."] = "Scan finished — DTC data partly read.",
            ["სკანირება დასრულდა — ECU პასუხობს, მაგრამ DTC მონაცემი მიუწვდომელია."] = "Scan finished — the ECU responds, but DTC data is unavailable.",
            ["სკანირება დასრულდა — აქტიური DTC არ არის."] = "Scan finished — no active DTC.",
            ["სკანირება დასრულდა — სტანდარტული OBD ECU პასუხი ვერ დადასტურდა."] = "Scan finished — no standard OBD ECU response confirmed.",
            ["სკანირება დასრულდა — შეცდომები ნაპოვნია."] = "Scan finished — faults found.",
            ["სკანირება ვერ დასრულდა"] = "Scan could not finish",
            ["სკანირება მზადაა"] = "Scan ready",
            ["სკანირება მიმდინარეობს"] = "Scanning",
            ["სკანირება წარმატებით დასრულდა"] = "Scan finished successfully",
            ["სკანირების შემდეგ გამოჩნდება მხოლოდ რეალურად პასუხმგებელი ECU-ები."] = "After scanning, only really responding ECUs will appear.",
            ["სკანირების შემდეგ გამოჩნდება მხოლოდ რეალურად პასუხმგებელი ECU."] = "After scanning, only the really responding ECU will appear.",
            ["სკანირების შემდეგ გამოჩნდება რეალურად წაკითხული DTC."] = "DTCs actually read will appear after scanning.",
            ["სრული report მონაცემი არ არის, ამიტომ შედარება შეუძლებელია."] = "No full report data, so comparison is not possible.",
            ["სრული სკანირება ვერ დასრულდა. შეამოწმე adapter და მანქანის კონტაქტი."] = "Full scan could not finish. Check the adapter and the car's ignition.",
            ["სტაბილური — ECU პასუხობს"] = "Stable — ECU responds",
            ["სწრაფ დაფაზე ერთდროულად მაქსიმუმ 3 დამატებითი PID აირჩიე."] = "Choose at most 3 extra PIDs at a time on the quick dashboard.",
            ["ტესტი დასრულდა — იხილე რეალური შედეგები ქვემოთ."] = "Test finished — see the real results below.",
            ["ტესტი ვერ დაიწყო: Bluetooth/adapter ინიციალიზაცია ვერ შესრულდა."] = "Test could not start: Bluetooth/adapter initialization failed.",
            ["ტესტი იწყება…"] = "Starting test…",
            ["ტესტი მიმდინარეობს…"] = "Test in progress…",
            ["ტესტი წარმატებით დასრულდა."] = "Test finished successfully.",
            ["უსაფრთხო ადგილზე გააჩერე მანქანა, ჩართე Parking brake და დატოვე ძრავა idle-ზე. ტესტი მხოლოდ გაჩერებულ მანქანაზე შეასრულე."] = "Stop the car in a safe place, apply the parking brake and leave the engine at idle. Run the test only on a stationary car.",
            ["უსაფრთხოების შემოწმება მიმდინარეობს…"] = "Safety check in progress…",
            ["უცნობი"] = "Unknown",
            ["უცნობი მანქანა"] = "Unknown car",
            ["უწყვეტი"] = "Continuous",
            ["უწყვეტი მონიტორები"] = "Continuous monitors",
            ["ფაილში წასაკითხი Live Data ჩანაწერი ვერ მოიძებნა."] = "No Live Data record to read was found in the file.",
            ["ფასის შეყვანის შემდეგ გამოჩნდება"] = "Appears after entering the price",
            ["ფრთხილად"] = "Caution",
            ["ქართული"] = "Georgian",
            ["ქვემოთ ნაჩვენებია ამ OEM ECU-დან რეალურად წაკითხული კოდები."] = "Codes actually read from this OEM ECU are shown below.",
            ["ქვემოთ ნაჩვენებია ამ სკანის დროს ECM-დან რეალურად წაკითხული კოდები."] = "Codes actually read from the ECM during this scan are shown below.",
            ["ყველა კატეგორია"] = "All categories",
            ["ყველა ხელმისაწვდომი"] = "All available",
            ["ყველა ხელმისაწვდომი OBD-II მონაცემი"] = "All available OBD-II data",
            ["შეამოწმე Bluetooth, OBD ადაპტერი და მანქანის კონტაქტი."] = "Check Bluetooth, the OBD adapter and the car's ignition.",
            ["შეამოწმე OBD ადაპტერი, მანქანის კონტაქტი და სცადე თავიდან."] = "Check the OBD adapter and the car's ignition, then try again.",
            ["შეამოწმე OBD კავშირი, ანთება და სცადე თავიდან."] = "Check the OBD connection and ignition, then try again.",
            ["შედარება აჩვენებს მხოლოდ ECU-ს ამჟამინდელ პასუხს. საბოლოო დასკვნამდე გადაამოწმე დარჩენილი DTC-ები და Readiness drive cycle-ის შემდეგ."] = "The comparison shows only the ECU's current response. Before a final conclusion, re-check remaining DTCs and Readiness after a drive cycle.",
            ["შედარება აჩვენებს მხოლოდ ამ ორ რეალურ სკანში შენახულ ცვლილებებს."] = "The comparison shows only the changes stored in these two real scans.",
            ["შედეგი ეფუძნება ECU-ის მიერ დაბრუნებულ raw minimum/maximum საზღვრებს. ზუსტი OEM მონიტორის სახელები მოგვიანებით დაემატება დადასტურებული definition pack-ებით."] = "The result is based on the raw minimum/maximum limits returned by the ECU. Exact OEM monitor names will be added later with verified definition packs.",
            ["შეიყვანე სწორი ოდომეტრი (km), ლიტრები და ფასი 1 ლიტრზე."] = "Enter a valid odometer (km), litres and price per litre.",
            ["შემოწმება ვერ დასრულდა"] = "Check could not finish",
            ["შენახული"] = "Stored",
            ["შენახული Before Repair baseline ვერ მოიძებნა. რემონტამდე გაუშვი AI Diagnostic და დააჭირე „Save Before Repair“."] = "No saved Before Repair baseline found. Before the repair run AI Diagnostic and tap “Save Before Repair”.",
            ["შენახული Before Repair baseline სხვა VIN-ს ეკუთვნის. შედარება შეჩერდა, რათა არასწორი დასკვნა არ მივიღოთ."] = "The saved Before Repair baseline belongs to another VIN. The comparison stopped to avoid a wrong conclusion.",
            ["შენახული ანგარიშების წაკითხვა ვერ შესრულდა."] = "Saved reports could not be read.",
            ["შესამოწმებელი"] = "To check",
            ["შეცდომა"] = "Error",
            ["შეცდომა დაბრუნდა"] = "Fault returned",
            ["ჩანაწერი წაიშალა."] = "Entry deleted.",
            ["ჩართვა"] = "Turn on",
            ["ჩაწერა დაიწყო — CSV/JSON შემდეგ ხელმისაწვდომი იქნება."] = "Recording started — CSV/JSON will be available afterwards.",
            ["ჩაწერილი Live Data სესიები ჯერ არ არის."] = "No recorded Live Data sessions yet.",
            ["ჩაწერილი Live Data სესიები ჯერ არ არის. Record-ით დაიწყე რეალური OBD მონაცემების ჩაწერა."] = "No recorded Live Data sessions yet. Start recording real OBD data with Record.",
            ["ჩაწერილი სესიები ჯერ არ არის."] = "No recorded sessions yet.",
            ["ძალიან კარგი — სწრაფი პასუხი"] = "Very good — fast response",
            ["ძებნა შეჩერდა."] = "Search stopped.",
            ["ძირითადი კომპონენტები"] = "Main components",
            ["ძრავა"] = "Engine",
            ["წაიშლება ყველა შენახული სკანი, DTC, სენსორის მონაცემი, readiness, ECU ინფორმაცია და freeze frame. გსურთ გაგრძელება?"] = "All saved scans, DTCs, sensor data, readiness, ECU info and freeze frames will be deleted. Continue?",
            ["წაკითხვის შემდეგ აქ გამოჩნდება მხოლოდ ECU-ის მიერ რეალურად შენახული snapshot."] = "After reading, only the snapshot really stored by the ECU will appear here.",
            ["წარმატება"] = "Success",
            ["წაშლა ვერ მოხერხდა"] = "Delete failed",
            ["წაშლის ბრძანება შესრულდა, მაგრამ ერთი ან რამდენიმე ხარვეზი ისევ არსებობს."] = "The clear command ran, but one or more faults still exist.",
            ["წყარო: Standard OBD-II Mode 01 / 03 / 07 / 0A"] = "Source: Standard OBD-II Mode 01 / 03 / 07 / 0A",
            ["წყაროები შემოწმდა: 1 · მნიშვნელობა წაკითხულია, მაგრამ დამოუკიდებელი გადამოწმება არ არსებობს."] = "Sources checked: 1 · value read, but there is no independent verification.",
            ["წყაროები ჯერ არ შემოწმებულა"] = "Sources not checked yet",
            ["წყაროების შემოწმება შეცდომით შეწყდა."] = "Source check stopped with an error.",
            ["ხელმისაწვდომია"] = "Available",
            ["ჯერ აირჩიე სრული შენახული ანგარიში."] = "Select a full saved report first.",
            ["ჯერ არ არის განსაზღვრული"] = "Not determined yet",
            ["ჯერ არ არის დაკავშირებული."] = "Not connected yet.",
            ["ჯერ არ არის დამატებული შევსების ჩანაწერი."] = "No refuel entry added yet.",
            ["ჯერ არ არის მზად"] = "Not ready yet",
            ["ჯერ არ არის წაკითხული სენსორები."] = "No sensors read yet.",
            ["ჯერ არ შემოწმებულა"] = "Not checked yet",
            ["ჯერ დააკავშირე OBD2 adapter."] = "Connect the OBD2 adapter first.",
            ["ჯერ დაიწყე Record, რომ CSV შეინახოს."] = "Start Record first to save a CSV.",
            ["ჯერ დაიწყე Record, რომ JSON შეინახოს."] = "Start Record first to save a JSON.",
            ["ჯერ შეასრულე სრული Generic OBD-II სკანირება."] = "Run a full Generic OBD-II scan first.",
            ["ჯერ შეასრულე სრული Generic OBD-II სკანირება. დასრულებული სკანი ავტომატურად შეინახება აქ."] = "Run a full Generic OBD-II scan first. The finished scan is saved here automatically.",
            ["ჰაერი"] = "Air",
            ["ჰიბრიდი"] = "Hybrid",
            ["გარბენის შეუსაბამობა აღმოჩნდა."] = "Mileage mismatch found.",
            ["Adapter/transport ამ მოთხოვნას არ უჭერს მხარს"] = "The adapter/transport does not support this request",
            ["ECU ჯერ არ შემოწმებულა"] = "ECU not checked yet",
            ["Engine ECU პასუხი ვერ დადასტურდა"] = "Engine ECU response not confirmed",
            ["კავშირი მოწმდება…"] = "Checking connection…",
            ["მითითებულ მისამართზე პასუხი არ დაბრუნდა"] = "No response at the given address",
            ["Freeze Frame მოთხოვნა შესრულდა · snapshot არ არის"] = "Freeze Frame request done · no snapshot",
            ["OEM DTC კოდი არ აღმოჩნდა"] = "No OEM DTC found",
            ["ECU probe შეცდომით დასრულდა"] = "ECU probe ended with an error",
            ["ECU მონაცემი მიუწვდომელია"] = "ECU data unavailable",
            ["გათიშულია"] = "Disconnected",
            ["პასუხი მიღებულია, მაგრამ ვერ დადასტურდა"] = "Response received but not confirmed",
            ["ECU პასუხობს — მოთხოვნა უარყოფილია/შეზღუდულია"] = "ECU responds — request refused/limited",
            ["Communication OK — OBD პასუხი დადასტურებულია"] = "Communication OK — OBD response confirmed",
            ["Communication OK — დადებითი read-only პასუხი"] = "Communication OK — positive read-only response",
            ["ECU დაკავშირებულია"] = "ECU connected",
            ["Generic Engine / Powertrain ECU დასკანერებულია"] = "Generic Engine / Powertrain ECU scanned",
            ["AI ჩატი"] = "AI chat",
            ["დაიწყე AI დიაგნოსტიკა ან მომწერე კითხვა მანქანის შესახებ"] = "Start AI diagnostics or ask a question about your car",
            ["მიწერე AI-ს..."] = "Message the AI...",
            ["რეალური ECU კავშირის მიხედვით"] = "Based on the real ECU connection",
            ["სტანდარტული OBD-II შესაძლებლობები, რომლებიც ECU-მ რეალურად დაადასტურა"] = "Standard OBD-II capabilities actually confirmed by the ECU",
            ["ფუნქციები ხელმისაწვდომია მხოლოდ მაშინ, როცა საჭირო რეალური telemetry არსებობს"] = "Functions are available only when the required real telemetry exists",
            ["ABS, SRS, BCM, Coding და სხვა OEM ფუნქციები მხოლოდ დადასტურებული ECU კავშირისას"] = "ABS, SRS, BCM, Coding and other OEM functions only with a confirmed ECU connection",
            ["Vehicle capabilities მოწმდება..."] = "Checking vehicle capabilities...",
            ["↻ განახლება"] = "↻ Refresh",
            // --- phase 2: services, models, dialogs ---
            ["ეს შესაძლებლობა ხელმისაწვდომია მხოლოდ აქტიური PRO ვერსიით."] = "This feature is available only with an active PRO version.",
            ["რა პრობლემა აქვს მანქანას?"] = "What is wrong with the car?",
            ["რა აქვს მანქანას?"] = "What does the car have?",
            ["არის სხვა პრობლემა?"] = "Is there another problem?",
            ["კიდევ რამე აქვს?"] = "Anything else?",
            ["სხვა რამე არის?"] = "Is there something else?",
            ["შეიძლება სიარული?"] = "Can I drive?",
            ["შეიძლება მანქანით მოძრაობა?"] = "Can I drive the car?",
            ["რისგან არის?"] = "What is it from?",
            ["რა იწვევს?"] = "What causes it?",
            ["AI-მ ცარიელი პასუხი დააბრუნა. რამდენიმე წამში სცადე თავიდან."] = "AI returned an empty answer. Try again in a few seconds.",
            ["AI პასუხის დრო ამოიწურა. OBD სკანის მონაცემები შენარჩუნებულია; გადაამოწმე ინტერნეტი და სცადე თავიდან."] = "AI answer timed out. OBD scan data is kept; check the internet and try again.",
            ["AI სერვერთან კავშირი ვერ დამყარდა. OBD სკანის მონაცემები შენარჩუნებულია; გადაამოწმე ინტერნეტი."] = "Could not connect to the AI server. OBD scan data is kept; check the internet.",
            ["AI მოთხოვნის იდენტიფიკაცია ვერ დადასტურდა. დახურე და ხელახლა გახსენი აპი."] = "AI request identity could not be confirmed. Close and reopen the app.",
            ["AI დიაგნოსტიკა აქტიურ PRO გამოწერას საჭიროებს. განაახლე ან აღადგინე გამოწერა."] = "AI diagnostic needs an active PRO subscription. Renew or restore the subscription.",
            ["AI მოთხოვნების დროებითი ლიმიტი ამოიწურა. OBD მონაცემები შენარჩუნებულია; ცოტა ხანში სცადე თავიდან."] = "The temporary AI request limit is reached. OBD data is kept; try again shortly.",
            ["AI პასუხის დრო ამოიწურა. OBD მონაცემები შენარჩუნებულია; სცადე თავიდან."] = "AI answer timed out. OBD data is kept; try again.",
            ["AI სერვისი დროებით მიუწვდომელია. OBD სკანის მონაცემები შენარჩუნებულია."] = "AI service is temporarily unavailable. OBD scan data is kept.",
            ["მომხმარებელი"] = "User",
            ["რა პრობლემა აქვს ჩემს მანქანას?"] = "What is wrong with my car?",
            ["დაფიქსირდა 3 შეცდომის კოდი."] = "3 trouble codes were found.",
            ["შეიძლება ერთმანეთთან იყოს დაკავშირებული"] = "They may be related to each other",
            ["მანქანით მოძრაობა შესაძლებელია, მაგრამ პრობლემა მალე უნდა შემოწმდეს."] = "You can drive the car, but the problem should be checked soon.",
            ["თუ მანქანა ნორმალურად მუშაობს, მოკლე მანძილზე ფრთხილად მოძრაობა შეიძლება."] = "If the car runs normally, you can drive carefully for a short distance.",
            ["თუ Check Engine ციმციმებს ან ძრავი ძლიერ კანკალებს, მოძრაობის გაგრძელება არ არის რეკომენდებული."] = "If Check Engine is flashing or the engine shakes strongly, continuing to drive is not recommended.",
            ["ამ პრობლემით მოძრაობის გაგრძელებამ შეიძლება დამატებითი დაზიანება გამოიწვიოს."] = "Continuing to drive with this problem may cause additional damage.",
            ["ამ სკანირებით აქტიური შეცდომის კოდი არ აღმოჩნდა."] = "No active trouble code was found in this scan.",
            ["რომელი შევამოწმო პირველად?"] = "What should I check first?",
            ["რისგან შეიძლება იყოს?"] = "What can it be from?",
            ["რამდენად სერიოზულია?"] = "How serious is it?",
            ["ჯერ არ განახლებულა"] = "Not updated yet",
            ["Reviewed OEM DID · მნიშვნელობა პირდაპირ ECU-დან"] = "Reviewed OEM DID · value directly from the ECU",
            ["ჩვეულებრივი: 11.8–14.8 V · გაფრთხილება: 11.5–15.5 V"] = "Normal: 11.8–14.8 V · Warning: 11.5–15.5 V",
            ["ჩვეულებრივი: −10–10 % · გაფრთხილება: ±25 %"] = "Normal: −10–10 % · Warning: ±25 %",
            ["0–100 % · მნიშვნელობა ავზის სენსორიდან"] = "0–100 % · value from the tank sensor",
            ["ECU-ის რეალური მონაცემი · შეფასება კონტექსტით ხდება"] = "Real ECU data · evaluated in context",
            ["მეთანოლი"] = "Methanol",
            ["ეთანოლი"] = "Ethanol",
            ["პროპანი"] = "Propane",
            ["ელექტრო/ჰიბრიდი"] = "Electric/hybrid",
            ["ზუსტი O₂ sensor, ერთეული და მნიშვნელობა ამ raw Test ID-სთვის მხოლოდ დადასტურებული ECU definition-ით განისაზღვრება."] = "The exact O₂ sensor, unit and value for this raw Test ID are defined only by a confirmed ECU definition.",
            ["Generic Mode 06 value — ზუსტი სახელი და ერთეული მხოლოდ დადასტურებული OEM test definition-ით განისაზღვრება."] = "Generic Mode 06 value — the exact name and unit are defined only by a confirmed OEM test definition.",
            ["Capability probe არ შესრულებულა."] = "Capability probe was not run.",
            ["სერიოზული"] = "Serious",
            ["მაღალი"] = "High",
            ["თარიღი მიუწვდომელია"] = "Date unavailable",
            ["სხვა"] = "Other",
            ["ხელმისაწვდომია პირობებით"] = "Available with conditions",
            ["საჭიროა PRO"] = "PRO required",
            ["საჭიროა დადასტურებული OEM პროფილი"] = "A confirmed OEM profile is required",
            ["OBD კავშირი არ არის"] = "No OBD connection",
            ["კავშირი არ შემოწმებულა"] = "Connection not checked",
            ["ავტომობილი ჯერ არ არის ამოცნობილი"] = "Vehicle not identified yet",
            ["მანქანის პროფილი ჯერ არ არის დადასტურებული"] = "Vehicle profile not confirmed yet",
            ["MIL ჩართულია"] = "MIL is on",
            ["MIL გამორთულია"] = "MIL is off",
            ["ECU პასუხი მიღებულია და readiness data decode შესრულდა."] = "ECU reply received and readiness data was decoded.",
            ["გააჩერე მანქანა 0–3 km/h-ზე, შემდეგ დაიწყე აჩქარება."] = "Stop the car at 0–3 km/h, then start accelerating.",
            ["ECU მონაცემი მიუწვდომელია"] = "ECU data unavailable",
            ["DTC სტატუსი მიუწვდომელია"] = "DTC status unavailable",
            ["DTC სტატუსი მიუწვდომელია"] = "DTC status unavailable",
            ["აქტიური/შენახული კოდი არ არის"] = "No active/stored codes",
            ["აქტიური/შენახული კოდი არ არის"] = "No active/stored codes",
            ["Pending DTC სტატუსი მიუწვდომელია"] = "Pending DTC status unavailable",
            ["Pending DTC სტატუსი მიუწვდომელია"] = "Pending DTC status unavailable",
            ["Pending კოდი არ არის"] = "No pending codes",
            ["Pending კოდი არ არის"] = "No pending codes",
            ["ჯერ არ დასკანირებულა"] = "Not scanned yet",
            ["სტატუსი ECU-ის პასუხიდანაა. შესაძლო მიზეზები და ნაბიჯები იხილე კოდების დეტალურ გვერდზე."] = "Status comes from the ECU reply. See possible causes and steps on the code details page.",
            ["Supported · ECU პასუხს ველოდები"] = "Supported · waiting for ECU reply",
            ["Supported · ECU პასუხი არ არის"] = "Supported · no ECU reply",
            ["ყურადღება"] = "Attention",
            ["ნორმა"] = "Normal",
            ["ნორმაში არ არის"] = "Not normal",
            ["NOT SUPPORTED · OEM დიაგნოსტიკა საჭიროა"] = "NOT SUPPORTED · OEM diagnostics required",
            ["რეალური ECU snapshot"] = "Real ECU snapshot",
            ["Bluetooth Classic • დაპეარებული"] = "Bluetooth Classic • paired",
            ["იდენტიფიკატორი მიუწვდომელია"] = "Identifier unavailable",
            ["სხვა მარკა"] = "Other make",
            ["ავტომობილი ჯერ არ არის ამოცნობილი."] = "Vehicle not identified yet.",
            ["ECU/VIN მონაცემი ჯერ არ არსებობს."] = "No ECU/VIN data yet.",
            ["ისტორია სრულად წაიშლება."] = "The history will be deleted completely.",
            ["შესრულდა"] = "Done",
            ["ისტორია წაიშალა."] = "History deleted.",
            ["კონფიდენციალურობის პოლიტიკის გახსნა ვერ მოხერხდა."] = "Could not open the privacy policy.",
            ["კონფიდენციალურობა"] = "Privacy",
            ["ადგილობრივი მანქანის მონაცემები, სკანები, ისტორია და გარბენის ჩანაწერები წაიშლება."] = "Local vehicle data, scans, history and mileage records will be deleted.",
            ["ადგილობრივი მანქანის მონაცემები წაიშალა."] = "Local vehicle data deleted.",
            ["მონაცემების ექსპორტი ვერ შესრულდა."] = "Data export failed.",
            ["მიმდინარეობს — ECU-ის Fuel Rate/MAF პასუხს ველოდები…"] = "In progress — waiting for the ECU Fuel Rate/MAF reply…",
            ["ტესტები განახლდა"] = "Tests updated",
            ["MIL: მიუწვდომელია"] = "MIL: unavailable",
            ["ემისიის DTC: მიუწვდომელია"] = "Emission DTC: unavailable",
            ["Readiness მონიტორების შესამოწმებლად დააჭირე „განახლება“-ს."] = "Press “Refresh” to check the readiness monitors.",
            ["READINESS მიუწვდომელია"] = "READINESS unavailable",
            ["READINESS მიუწვდომელია"] = "READINESS unavailable",
            ["მზადაა ემისიების ტესტისთვის"] = "Ready for the emissions test",
            ["მზადაა ემისიების ტესტისთვის"] = "Ready for the emissions test",
            ["ჯერ არ არის მზად"] = "Not ready yet",
            ["ECU-მ Readiness პასუხი არ დააბრუნა. ეს ავტომატურად გაუმართაობას არ ნიშნავს."] = "The ECU did not return a readiness reply. This does not automatically mean a fault.",
            ["ყველა მხარდაჭერილი მონიტორი დასრულებულია და MIL გამორთულია."] = "All supported monitors are complete and MIL is off.",
            ["ზოგი მხარდაჭერილი მონიტორი ჯერ დაუსრულებელია ან MIL/DTC აქტიურია. საჭიროა drive cycle და ხელახალი შემოწმება."] = "Some supported monitors are not complete yet or MIL/DTC is active. A drive cycle and a new check are needed.",
            ["ყველა გაფართოებული ფუნქცია ერთ გამოწერაში"] = "All advanced features in one subscription",
            ["თვეში"] = "per month",
            ["PRO-ის გააქტიურება"] = "Activate PRO",
            ["შეძენის აღდგენა"] = "Restore purchase",
            ["გამოწერა ყოველთვიურად განახლდება, სანამ არ გააუქმებ. საბოლოო ფასი გადახდამდე Google Play-ში გამოჩნდება."] = "The subscription renews monthly until you cancel. The final price is shown in Google Play before payment.",
            ["კონფიდენციალურობის პოლიტიკა"] = "Privacy policy",
            ["გამოყენების პირობები"] = "Terms of use",
            ["გამოწერის მართვა"] = "Manage subscription",
            ["ფასი გამოჩნდება Store-დან"] = "The price will be shown from the Store",
            ["PRO წარმატებით გააქტიურდა."] = "PRO activated successfully.",
            ["PRO უკვე აქტიურია."] = "PRO is already active.",
            ["Google Play იტვირთება…"] = "Loading Google Play…",
            ["შეძენა მოწმდება…"] = "Checking purchase…",
            ["PRO წარმატებით აღდგა."] = "PRO restored successfully.",
            ["აქტიური PRO გამოწერა ვერ მოიძებნა."] = "No active PRO subscription found.",
            ["PRO გააქტიურდა. გამოწერა ყოველთვიურად განახლდება გაუქმებამდე."] = "PRO activated. The subscription renews monthly until cancelled.",
            ["გადახდა მოლოდინშია. დადასტურების შემდეგ PRO ავტომატურად გააქტიურდება."] = "Payment is pending. PRO activates automatically after confirmation.",
            ["გადახდა გაუქმდა და თანხა არ ჩამოგეჭრა."] = "Payment cancelled and you were not charged.",
            ["Google Play დროებით მიუწვდომელია. შეამოწმე ინტერნეტი და სცადე ხელახლა."] = "Google Play is temporarily unavailable. Check the internet and try again.",
            ["გამოწერა უკვე შეძენილია. დააჭირე „შეძენის აღდგენას“."] = "The subscription is already purchased. Press “Restore purchase”.",
            ["PRO გამოწერა Google Play-ში ჯერ სწორად არ არის ხელმისაწვდომი."] = "The PRO subscription is not available correctly in Google Play yet.",
            ["გადახდის დადასტურება ვერ დასრულდა. თანხა მეორედ არ გადაიხადო — ცოტა ხანში სცადე შეძენის აღდგენა."] = "Payment confirmation did not finish. Do not pay again — try Restore purchase shortly.",
            ["გადახდა ვერ შესრულდა. სცადე ხელახლა."] = "Payment failed. Try again.",
            ["ღრმა ძიებისთვის ჩაწერე მანქანის ეკრანზე ნაჩვენები ზეთის % (მაგ. 90)."] = "For a deep search enter the oil % shown on the car's screen (e.g. 90).",
            ["• გააკეთე მხოლოდ ზეთის შეცვლის შემდეგ."] = "• Do it only after an oil change.",
            ["გააკეთე მხოლოდ ზეთის შეცვლის შემდეგ."] = "Do it only after an oil change.",
            ["• მანქანა P-ზე, ანთება ჩართული, არ იმოძრაო."] = "• Car in P, ignition on, do not drive.",
            ["მანქანა P-ზე, ანთება ჩართული, არ იმოძრაო."] = "Car in P, ignition on, do not drive.",
            ["• აპი ECU-ში ჩაწერს მხოლოდ ერთ მნიშვნელობას და მერე თავიდან წაიკითხავს.\n\nგაგრძელდეს?"] = "• The app writes only one value to the ECU and then reads it back.\n\nContinue?",
            ["• აპი ECU-ში ჩაწერს მხოლოდ ერთ მნიშვნელობას და მერე თავიდან წაიკითხავს."] = "• The app writes only one value to the ECU and then reads it back.",
            ["აპი ECU-ში ჩაწერს მხოლოდ ერთ მნიშვნელობას და მერე თავიდან წაიკითხავს."] = "The app writes only one value to the ECU and then reads it back.",
            ["გაგრძელდეს?"] = "Continue?",
            ["ამ მანქანაზე განულების იდენტიფიკატორი ჯერ ნაპოვნი არ არის."] = "The reset identifier has not been found on this car yet.",
            ["მანქანა მოძრაობს — გაჩერდი და P-ზე დააყენე. არაფერი გაგზავნილა."] = "The car is moving — stop and put it in P. Nothing was sent.",
            ["ძაბვა დაბალია — ჩართე READY/ძრავა ან დამტენი. არაფერი გაგზავნილა."] = "Voltage is low — turn on READY/engine or a charger. Nothing was sent.",
            ["მიმდინარე მნიშვნელობა ვერ წავიკითხე — არაფერი გაგზავნილა. ანთება ჩართულია?"] = "Could not read the current value — nothing was sent. Is the ignition on?",
            ["ვერ დადგინდა რომელი მოდული ინახავს ზეთის რესურსს — ბრძანება არ გაიგზავნა. გამოიყენე ქვემოთ მოცემული ინსტრუქცია."] = "Could not find which module stores the oil life — no command was sent. Use the instructions below.",
            ["ამ იდენტიფიკატორს რამდენიმე მოდული პასუხობს — უსაფრთხოებისთვის ბრძანება არ გაიგზავნა."] = "Several modules answer this identifier — no command was sent for safety.",
            ["ECU-მ უარი თქვა (7F 3B 22 — პირობები არ არის შესრულებული). სცადე: ანთება ON, ძრავა/READY გამორთული, მანქანა P-ზე."] = "The ECU refused (7F 3B 22 — conditions not correct). Try: ignition ON, engine/READY off, car in P.",
            ["ECU-მ უარი თქვა (7F 3B 33 — დაცულია Security Access-ით). ამ მანქანაზე განულება ხელით, ქვემოთ მოცემული ინსტრუქციით გააკეთე."] = "The ECU refused (7F 3B 33 — protected by Security Access). On this car do the reset by hand with the instructions below.",
            ["ECU-მ უარი თქვა (7F 3B 31 — ეს მნიშვნელობა ჩაწერადი არ არის)."] = "The ECU refused (7F 3B 31 — this value is not writable).",
            ["სერვისის შეხსენების განულება"] = "Service reminder reset",
            ["თავსებადობა დადასტურებულია:"] = "Compatibility confirmed:",
            ["მიმდინარე მდგომარეობა:"] = "Current state:",
            ["აირჩიე სერვისის მოქმედება:"] = "Choose a service action:",
            ["ყურადღება! Reset გამოიყენე მხოლოდ შესაბამისი სერვისის შესრულების შემდეგ."] = "Attention! Use Reset only after the matching service has been done.",
            ["მხოლოდ ECU-დან რეალურად წაკითხული ზეთის/სერვისის მნიშვნელობები"] = "Only oil/service values actually read from the ECU",
            ["ამ მანქანამ ამ კავშირზე დადასტურებული Service მნიშვნელობა არ დააბრუნა."] = "This car did not return a confirmed Service value on this connection.",
            ["ღილაკი ჩანს მხოლოდ exact vehicle/ECU-ზე reviewed reset operation-ის აღმოჩენისას. წარმატების შემდეგ მნიშვნელობა ECU-დან თავიდან იკითხება."] = "The button appears only when a reviewed reset operation is found for the exact vehicle/ECU. After success the value is read again from the ECU.",
            ["ესენი არის მანქანისთვის დადასტურებული manual პროცედურები. ისინი ECU-ში write ბრძანებას არ აგზავნიან."] = "These are confirmed manual procedures for the car. They do not send a write command to the ECU.",
            ["ამ მანქანისთვის guided პროცედურა არ მოიძებნა."] = "No guided procedure found for this car.",
            ["რეალური ECU მონაცემები · დინამიური ფუნქციები · Reset-ის შემდგომი გადამოწმება"] = "Real ECU data · dynamic functions · verification after Reset",
            ["სერვისის მონაცემი მიუწვდომელია"] = "Service data unavailable",
            ["ამ მანქანამ ამ კავშირზე მხარდაჭერილი სერვისის მნიშვნელობა არ დააბრუნა."] = "This car did not return a supported service value on this connection.",
            ["მხოლოდ ამ კონკრეტულ მანქანა/ECU-ზე აღმოჩენილი Adaptation, Coding და Active Test ფუნქციები."] = "Only Adaptation, Coding and Active Test functions found on this exact car/ECU.",
            ["მხოლოდ დადასტურებული Service Reset, Adaptation, Coding და Active Test ფუნქციები"] = "Only confirmed Service Reset, Adaptation, Coding and Active Test functions",
            ["ეს არის მიმდინარე read-only snapshot. თითოეული ოპერაცია გაშვებამდე პირობებს თავიდან ამოწმებს."] = "This is the current read-only snapshot. Each operation re-checks its conditions before it runs.",
            ["ზეთის სერვისი, EPB და სხვა ზუსტად დადასტურებული სერვის-პროცედურები"] = "Oil service, EPB and other exactly confirmed service procedures",
            ["კალიბრაცია, relearn და adaptation პროცედურები მხოლოდ catalog-ში დადასტურებული ოპერაციებიდან"] = "Calibration, relearn and adaptation procedures only from operations confirmed in the catalog",
            ["Coding მხოლოდ exact OEM definition-ზე: backup → write → verify"] = "Coding only on an exact OEM definition: backup → write → verify",
            ["ტესტი ირთვება მხოლოდ reviewed ECU command-ით და უსაფრთხო STOP ბრძანებით"] = "The test runs only with a reviewed ECU command and a safe STOP command",
            ["ამ მანქანისთვის დადასტურებული Service Reset ჯერ არ არის ხელმისაწვდომი."] = "No confirmed Service Reset is available for this car yet.",
            ["ამ მანქანისთვის დადასტურებული Adaptation ჯერ არ არის ხელმისაწვდომი."] = "No confirmed Adaptation is available for this car yet.",
            ["ამ მანქანისთვის დადასტურებული Coding ფუნქცია ჯერ არ არის ხელმისაწვდომი."] = "No confirmed Coding function is available for this car yet.",
            ["ამ მანქანისთვის დადასტურებული Active Test ჯერ არ არის ხელმისაწვდომი."] = "No confirmed Active Test is available for this car yet.",
            ["აპი არ აგზავნის guessed OEM ბრძანებებს. ფუნქცია აქტიურდება მხოლოდ მაშინ, როცა ზუსტი მანქანა, ECU definition, მისამართი, ძაბვა და საჭირო runtime პირობები დადასტურებულია. Security Access-ის საჭიროებისას ფუნქცია დაბლოკილი რჩება, სანამ შესაბამისი ავტორიზაცია არ არსებობს."] = "The app does not send guessed OEM commands. A function is enabled only when the exact car, ECU definition, address, voltage and required runtime conditions are confirmed. If Security Access is needed, the function stays locked until the matching authorisation exists.",
            ["თუ ზეთის % ზემოთ არ ჩანს: ჩაწერე მანქანის ეკრანზე ნაჩვენები % და დააჭირე ძიებას. აპი მხოლოდ კითხულობს (0x22), არაფერს წერს. ღრმა ძიებას რამდენიმე წუთი სჭირდება — ანთება ჩართული დატოვე."] = "If the oil % is not shown above: enter the % shown on the car's screen and press search. The app only reads (0x22) and writes nothing. A deep search takes a few minutes — keep the ignition on.",
            ["ამ მანქანაზე აპიდან განულება ჯერ მიუწვდომელია — გამოიყენე ქვემოთ მოცემული ინსტრუქცია."] = "Resetting from the app is not available on this car yet — use the instructions below.",
            ["ვამოწმებ OBD adapter-ს და ECU-ს…"] = "Checking the OBD adapter and ECU…",
            ["სერვისის მონაცემები იკითხება…"] = "Reading service data…",
            ["რეალური Generic OBD-II ECU პასუხი ვერ დადასტურდა, ამიტომ Service/Coding ბრძანება არ გაიგზავნება."] = "A real Generic OBD-II ECU reply could not be confirmed, so no Service/Coding command will be sent.",
            ["ECU პასუხობს, მაგრამ VIN/მწარმოებელი/მოდელი საკმარისად ვერ დადასტურდა."] = "The ECU answers, but the VIN/manufacturer/model could not be confirmed well enough.",
            ["OEM პროფილი დაუდასტურებელია"] = "OEM profile not confirmed",
            ["ამ მანქანამ მხარდაჭერილი სერვისის მნიშვნელობა არ დააბრუნა."] = "This car did not return a supported service value.",
            ["ქვემოთ არის შენი მანქანის სერვისის განულების ინსტრუქციები. დააჭირე „ინსტრუქცია“-ს."] = "Below are the service reset instructions for your car. Press “Instructions”.",
            ["ჯერ მანქანის ინფორმაცია უნდა ჩაიტვირთოს."] = "Vehicle information must be loaded first.",
            ["OEM მონაცემები იკითხება…"] = "Reading OEM data…",
            ["✓ OEM მონაცემები შენახულია:"] = "✓ OEM data saved:",
            ["OEM მონაცემები შენახულია:"] = "OEM data saved:",
            ["OBD adapter მზად არ არის. ჯერ დაუკავშირდი adapter-ს."] = "The OBD adapter is not ready. Connect to the adapter first.",
            ["Adapter დაკავშირებულია, მაგრამ მანქანის Generic OBD-II ECU პასუხი ვერ დადასტურდა."] = "The adapter is connected, but the car's Generic OBD-II ECU reply could not be confirmed.",
            ["Adapter და Generic OBD-II ECU პასუხი დადასტურებულია. OEM ფუნქციები ცალკე მოწმდება exact definition-ით."] = "Adapter and Generic OBD-II ECU reply confirmed. OEM functions are checked separately with an exact definition.",
            ["GUIDED · ECU write არ იგზავნება"] = "GUIDED · no ECU write is sent",
            ["ზეთის სერვისის ათვლა თავიდან დაიწყება დღევანდელი დღიდან. დააჭირე მხოლოდ ზეთის შეცვლის შემდეგ."] = "The oil service count will restart from today. Press it only after an oil change.",
            ["მანქანის ოდომეტრი ახლა არ იკითხება. შეაერთე ადაპტერი, ჩართე ანთება და სცადე ხელახლა."] = "The car's odometer cannot be read now. Connect the adapter, turn the ignition on and try again.",
            ["• გააკეთე მხოლოდ სერვისის შესრულების შემდეგ."] = "• Do it only after the service has been done.",
            ["გააკეთე მხოლოდ სერვისის შესრულების შემდეგ."] = "Do it only after the service has been done.",
            ["• მანქანა P-ზე, ანთება ჩართული."] = "• Car in P, ignition on.",
            ["მანქანა P-ზე, ანთება ჩართული."] = "Car in P, ignition on.",
            ["ჯერ დააჭირე „შემოწმება“-ს და დაადასტურე მანქანა/ECU."] = "First press “Check” and confirm the car/ECU.",
            ["ნამდვილად გსურთ Oil Life-ის განულება?"] = "Do you really want to reset Oil Life?",
            ["აპი გაშვებამდე თავიდან შეამოწმებს ძაბვას, სიჩქარეს, ძრავის მდგომარეობას და exact ECU definition-ს. გაგრძელდეს?"] = "Before running, the app will re-check voltage, speed, engine state and the exact ECU definition. Continue?",
            ["Active Test გაჩერდა. Reviewed STOP ბრძანება უსაფრთხოების მიზნით გაიგზავნა."] = "Active Test stopped. A reviewed STOP command was sent for safety.",
            ["· ECU backup შენახულია"] = "· ECU backup saved",
            ["ECU backup შენახულია"] = "ECU backup saved",
            ["Backup: ახლახან შენახულია"] = "Backup: saved just now",
            ["Active Test ჩერდება — reviewed STOP ბრძანება გაიგზავნება…"] = "Active Test is stopping — a reviewed STOP command is being sent…",
            ["აღდგება ამ VIN-ის უახლესი original coding backup. აპი თავიდან გადაამოწმებს უსაფრთხოების პირობებს და ჩაწერის შემდეგ ECU-ს პასუხს verify-ს გაუკეთებს. გაგრძელდეს?"] = "The latest original coding backup for this VIN will be restored. The app will re-check the safety conditions and verify the ECU reply after writing. Continue?",
            ["კავშირის/ECU შესაძლებლობების შემოწმება ვერ დასრულდა."] = "The connection/ECU capability check did not finish.",
            ["უსაფრთხოების მიზნით არცერთი OEM ბრძანება არ გაიგზავნა."] = "For safety, no OEM command was sent.",
            ["└── OEM discovery ვერ დასრულდა"] = "└── OEM discovery did not finish",
            ["OEM discovery ვერ დასრულდა"] = "OEM discovery did not finish",
            ["ECU-მ DTC კოდების წაშლა ვერ დაადასტურა."] = "The ECU did not confirm clearing the DTCs.",
            ["ამ კოდის აღწერა ბაზაში ვერ მოიძებნა (შესაძლოა მწარმოებლის სპეციფიკური კოდია)."] = "The description of this code was not found in the database (it may be a manufacturer-specific code).",
            ["მაქსიმალური სხვაობა:"] = "Maximum difference:",
            ["ერთი წყარო გარბენის ორიგინალობას ვერ ადასტურებს."] = "One source cannot confirm the mileage is original.",
            ["ECU წყაროებს შორის მნიშვნელოვანი სხვაობაა; ეს შეიძლება მოდულის შეცვლის ან reset-ის შედეგიც იყოს."] = "There is a significant difference between ECU sources; this may also be the result of a module replacement or reset.",
            ["ECU წყაროებს შორის მნიშვნელოვანი სხვაობაა; ეს შეიძლება მოდულის შეცვლის ან reset-ის შედეგიც იყოს."] = "There is a significant difference between ECU sources; this may also be the result of a module replacement or reset.",
            ["საწვავი მიუწვდომელია"] = "Fuel unavailable",
            ["Before Repair / After Repair შედარება"] = "Before Repair / After Repair comparison",
            ["Generic OBD-II Powertrain ECU: პასუხობს"] = "Generic OBD-II Powertrain ECU: responds",
            ["წაკითხულ სერვისებში კოდი არ აღმოჩნდა."] = "No code was found in the services read.",
            ["წაკითხულ სერვისებში კოდი არ აღმოჩნდა."] = "No code was found in the services read.",
            ["დააჭირე „წაკითხვას“ — ECU-დან წაიკითხება ხარვეზის მომენტში შენახული მონაცემები."] = "Press “Read” — the data stored at the moment of the fault will be read from the ECU.",
            ["Freeze Frame ჯერ არ არის წაკითხული."] = "Freeze Frame not read yet.",
            ["OBD ადაპტერი არ არის დაკავშირებული. ჯერ დაუკავშირდი მანქანას."] = "The OBD adapter is not connected. Connect to the car first.",
            ["Freeze Frame ვერ წაიკითხა. შეამოწმე OBD კავშირი და ანთება, შემდეგ სცადე თავიდან."] = "Could not read Freeze Frame. Check the OBD connection and ignition, then try again.",
            ["ECU-ში Freeze Frame შენახული არ არის. ეს ნორმალურია, როცა შეცდომის კოდი (DTC) არ არის ან ახლახან წაიშალა."] = "No Freeze Frame is stored in the ECU. This is normal when there is no trouble code (DTC) or it was cleared recently.",
            ["ECU-მ ამ კადრისთვის დამატებითი მონაცემები არ დააბრუნა."] = "The ECU returned no extra data for this frame.",
            ["ECU პასუხის დრო: —"] = "ECU reply time: —",
            ["Wi-Fi OBD ადაპტერი დაკავშირებულია. ვკითხულობ ავტომობილის ინფორმაციას…"] = "Wi-Fi OBD adapter connected. Reading vehicle information…",
            ["Wi-Fi OBD კავშირი მზადაა."] = "Wi-Fi OBD connection is ready.",
            ["Wi-Fi OBD დაკავშირებულია. ავტომობილის ინფორმაცია მთავარ გვერდზე განახლდება."] = "Wi-Fi OBD connected. Vehicle information will update on the home page.",
            ["TCP კავშირი გაიხსნა, მაგრამ ELM327 ადაპტერმა არ უპასუხა."] = "The TCP connection opened, but the ELM327 adapter did not answer.",
            ["Wi-Fi OBD კავშირი დაიკარგა."] = "Wi-Fi OBD connection lost.",
            ["Wi-Fi OBD კავშირის დრო ამოიწურა."] = "Wi-Fi OBD connection timed out.",
            ["Log export failed / ლოგის ექსპორტი ვერ შესრულდა"] = "Log export failed",
            ["ECU-დან შენახული კოდების წაკითხვა…"] = "Reading stored codes from the ECU…",
            ["ECU-დან Pending კოდების წაკითხვა…"] = "Reading pending codes from the ECU…",
            ["ECU-დან Permanent კოდების წაკითხვა…"] = "Reading permanent codes from the ECU…",
            ["DTC შედეგების დამუშავება…"] = "Processing DTC results…",
            ["ECU კოდების წაკითხვა დასრულდა."] = "ECU code reading finished.",
            ["ნაპოვნი კოდების ახსნის მომზადება…"] = "Preparing explanations for the codes found…",
            ["ამ კოდის დამატებითი ინფორმაცია ოფლაინ კატალოგში ვერ მოიძებნა."] = "No extra information for this code was found in the offline catalog.",
            ["მიზეზი უნდა დადასტურდეს შესაბამისი დიაგნოსტიკური ტესტით."] = "The cause must be confirmed with the matching diagnostic test.",
            ["უცნობია"] = "Unknown",
            ["Vehicle Speed PID 01 0D-მა ECU-დან პასუხი არ დააბრუნა. სიჩქარე არ არის გამოგონილი."] = "Vehicle Speed PID 01 0D returned no reply from the ECU. The speed is not invented.",
            ["რეალური სიჩქარე მიიღება ECU-დან (PID 01 0D)."] = "Real speed is taken from the ECU (PID 01 0D).",
            ["READY — GO ავტომატურად დაიწყება მოძრაობისას."] = "READY — GO will start automatically when you move.",
            ["OBD adapter და კავშირის შემოწმება…"] = "Checking the OBD adapter and connection…",
            ["სტანდარტული OBD ECU პასუხის დადასტურება…"] = "Confirming the standard OBD ECU reply…",
            ["სტანდარტული OBD ECU პასუხი ვერ დადასტურდა."] = "The standard OBD ECU reply could not be confirmed.",
            ["Generic OBD-II ECU პასუხი ვერ დადასტურდა. სხვა მოდულები არ არის გამოგონილი და არ დასკანირებულა."] = "The Generic OBD-II ECU reply could not be confirmed. Other modules are not invented and were not scanned.",
            ["Vehicle / VIN ინფორმაციის წაკითხვა…"] = "Reading Vehicle / VIN information…",
            ["Vehicle / VIN ინფორმაცია მიუწვდომელია."] = "Vehicle / VIN information unavailable.",
            ["Stored DTC კოდების წაკითხვა…"] = "Reading stored DTCs…",
            ["Pending DTC კოდების წაკითხვა…"] = "Reading pending DTCs…",
            ["Permanent DTC კოდების წაკითხვა…"] = "Reading permanent DTCs…",
            ["MIL / Readiness მონაცემი მიუწვდომელია."] = "MIL / Readiness data unavailable.",
            ["ECU-ს მხარდაჭერილი სენსორების წაკითხვა…"] = "Reading the ECU's supported sensors…",
            ["სწრაფი ECU live-data snapshot-ის წაკითხვა…"] = "Reading a quick ECU live-data snapshot…",
            ["ECU პასუხის და მიღებული მონაცემების შეჯამება…"] = "Summarising the ECU reply and the data received…",
            ["Freeze Frame-ის არსებობის შემოწმება…"] = "Checking whether a Freeze Frame exists…",
            ["Generic OBD-II scan დასრულდა."] = "Generic OBD-II scan finished.",
            ["მეთანოლი / ელექტრო"] = "Methanol / electric",
            ["ეთანოლი / ელექტრო"] = "Ethanol / electric",
            ["LPG / ელექტრო"] = "LPG / electric",
            ["CNG / ელექტრო"] = "CNG / electric",
            ["პროპანი / ელექტრო"] = "Propane / electric",
            ["ორმაგი საწვავი / ელექტრო"] = "Bi-fuel / electric",
            ["ბენზინი ჰიბრიდი"] = "Petrol hybrid",
            ["ეთანოლი ჰიბრიდი"] = "Ethanol hybrid",
            ["დიზელი ჰიბრიდი"] = "Diesel hybrid",
            ["ელექტრო ჰიბრიდი"] = "Electric hybrid",
            ["შერეული ჰიბრიდი"] = "Mixed hybrid",
            ["რეგენერაციული ჰიბრიდი"] = "Regenerative hybrid",
            ["დიზელი / ელექტრო"] = "Diesel / electric",
            ["ანგარიშის წაშლა ვერ შესრულდა."] = "Account deletion failed.",
            ["ანგარიში და მასთან დაკავშირებული სერვერის მონაცემები წაიშალა."] = "The account and its related server data were deleted.",
            ["ანგარიშის სერვერთან დაკავშირება ვერ მოხერხდა."] = "Could not connect to the account server.",
            ["Email და პაროლი აუცილებელია."] = "Email and password are required.",
            ["ამ Email-ით ანგარიში უკვე არსებობს."] = "An account with this email already exists.",
            ["ეს ანგარიში უკვე სხვა მოწყობილობაზეა მიბმული."] = "This account is already linked to another device.",
            ["Email ან პაროლი არასწორია."] = "Email or password is wrong.",
            ["სერვერის პასუხი არასწორია."] = "The server reply is invalid.",
            ["ანგარიში წარმატებით დაუკავშირდა."] = "Account connected successfully.",
            ["Adapter და კავშირის შემოწმება…"] = "Checking the adapter and connection…",
            ["ECU პასუხის დადასტურება…"] = "Confirming the ECU reply…",
            ["მხარდაჭერილი სტანდარტული PID-ების წაკითხვა…"] = "Reading supported standard PIDs…",
            ["VIN და ავტომობილის პროფილის შემოწმება…"] = "Checking the VIN and vehicle profile…",
            ["Readiness და დამატებითი OBD სერვისების შემოწმება…"] = "Checking Readiness and additional OBD services…",
            ["PRO და OEM შესაძლებლობების დამუშავება…"] = "Processing PRO and OEM capabilities…",
            ["შესაძლებლობების შემოწმება დასრულდა."] = "Capability check finished.",
            ["Universal OBD-II კავშირი"] = "Universal OBD-II connection",
            ["სტანდარტული Powertrain ECU-ს დიაგნოსტიკა."] = "Standard Powertrain ECU diagnostics.",
            ["მხოლოდ ECU-ის მიერ რეკლამირებული და დეკოდირებადი PID-ები."] = "Only PIDs advertised by the ECU and decodable.",
            ["ECU-მ დეკოდირებადი PID არ დააბრუნა."] = "The ECU returned no decodable PID.",
            ["Stored, Pending და Permanent DTC-ები; Freeze Frame მხოლოდ მაშინ, როცა ECU-ში მართლა შენახულია."] = "Stored, Pending and Permanent DTCs; Freeze Frame only when it is really stored in the ECU.",
            ["MIL, DTC count და readiness monitors Mode 01 PID 01-დან."] = "MIL, DTC count and readiness monitors from Mode 01 PID 01.",
            ["ECU-მ რეალური Readiness პასუხი დააბრუნა."] = "The ECU returned a real Readiness reply.",
            ["ამ ECU-მ Readiness პასუხი არ დააბრუნა."] = "This ECU did not return a Readiness reply.",
            ["Mode 06 მონიტორები"] = "Mode 06 monitors",
            ["Non-continuous emissions test მნიშვნელობები, საზღვრები და Passed/Failed."] = "Non-continuous emissions test values, limits and Passed/Failed.",
            ["ECU-მ Mode 06 მონაცემი დააბრუნა."] = "The ECU returned Mode 06 data.",
            ["ამ ECU-ზე Mode 06 მონაცემი ხელმისაწვდომი არ არის."] = "Mode 06 data is not available on this ECU.",
            ["Vehicle / VIN ინფორმაცია"] = "Vehicle / VIN information",
            ["VIN, მარკა და წელი მხოლოდ ECU/VIN-ის დადასტურებული მონაცემით."] = "VIN, make and year only from confirmed ECU/VIN data.",
            ["რეალური Vehicle Speed PID 01 0D-ით; ტესტი არ იწყება demo სიჩქარით."] = "With the real Vehicle Speed PID 01 0D; the test does not start with a demo speed.",
            ["Vehicle Speed PID 01 0D მხარდაჭერილია."] = "Vehicle Speed PID 01 0D is supported.",
            ["Vehicle Speed PID 01 0D ECU-მ არ დაადასტურა."] = "The ECU did not confirm Vehicle Speed PID 01 0D.",
            ["Live Data-ის ჩანაწერი, გრაფიკები და CSV export."] = "Live Data recording, graphs and CSV export.",
            ["Live PID მონაცემი ხელმისაწვდომია."] = "Live PID data is available.",
            ["ჯერ საჭიროა დეკოდირებადი Live Data PID."] = "A decodable Live Data PID is needed first.",
            ["რეალურ DTC, Freeze Frame, Readiness და Live Data კონტექსტზე აგებული სავარაუდო დიაგნოზი."] = "A likely diagnosis built on real DTC, Freeze Frame, Readiness and Live Data context.",
            ["AI-სთვის საჭირო Live Data კონტექსტი ხელმისაწვდომია."] = "The Live Data context needed for AI is available.",
            ["ჯერ საჭიროა მინიმუმ ერთი რეალური Live Data PID."] = "At least one real Live Data PID is needed first.",
            ["ECU Fuel Rate/MAF/Fuel Level-ის მიხედვით რეალური ან მკაფიოდ მონიშნული calculated consumption."] = "Real consumption from ECU Fuel Rate/MAF/Fuel Level, or clearly marked calculated consumption.",
            ["საწვავის გამოთვლისთვის აუცილებელი მინიმუმ ერთი PID არის."] = "At least one PID needed for the fuel calculation is present.",
            ["Fuel Rate, MAF ან Fuel Level PID ვერ დადასტურდა."] = "Fuel Rate, MAF or Fuel Level PID could not be confirmed.",
            ["VIN, სკანის შედეგი, DTC, Freeze Frame და readiness-ის PDF/გაზიარება."] = "PDF/sharing of the VIN, scan result, DTC, Freeze Frame and readiness.",
            ["სტანდარტული OBD სკანის შედეგი ხელმისაწვდომია."] = "A standard OBD scan result is available.",
            ["რამდენიმე ECU-ის გარბენისა და VIN-ის შედარება მხოლოდ დადასტურებული OEM პროფილების შემთხვევაში."] = "Comparing mileage and VIN across several ECUs only with confirmed OEM profiles.",
            ["Generic OBD-II ერთ ECU-დან „ორიგინალურ გარბენს“ ვერ ამტკიცებს."] = "Generic OBD-II cannot prove the “original mileage” from one ECU.",
            ["Vehicle coverage ჩანაწერი არსებობს, მაგრამ მისი executable ECU definition ვერ დადასტურდა."] = "A vehicle coverage record exists, but its executable ECU definition could not be confirmed.",
            ["VIN-ით დადასტურებული მარკის OEM პროფილი ჯერ არ არის."] = "There is no VIN-confirmed OEM profile for this make yet.",
            ["OEM მოდულების read-only სკანირება იწყება მხოლოდ რეალურად დადასტურებული ტრანსპორტით."] = "Read-only OEM module scanning starts only with a really confirmed transport.",
            ["ABS, SRS, TCM, BCM და სხვა ECU-ები მხოლოდ დადასტურებული მისამართებით."] = "ABS, SRS, TCM, BCM and other ECUs only with confirmed addresses.",
            ["მწარმოებლის სპეციფიკური sensor და hybrid/EV მონაცემები."] = "Manufacturer-specific sensor and hybrid/EV data.",
            ["Oil reset, EPB, calibration და აქტიური ტესტები მხოლოდ უსაფრთხოების შემოწმების შემდეგ."] = "Oil reset, EPB, calibration and active tests only after safety checks.",
            ["საჭიროა ძაბვის კონტროლი და მომხმარებლის დადასტურება."] = "Voltage control and user confirmation are required.",
            ["ECU-ს კონფიგურაციის შეცვლა მხოლოდ OEM definition, backup, security access და სტაბილური ძაბვის შემთხვევაში."] = "ECU configuration changes only with an OEM definition, backup, security access and stable voltage.",
            ["Generic adapter-ზე ბრძანება არ გაიგზავნება."] = "No command will be sent on a Generic adapter.",
            ["OBD adapter არ არის დაკავშირებული ან ვერ მომზადდა."] = "The OBD adapter is not connected or could not be prepared.",
            ["OBD adapter არ არის დაკავშირებული ან ვერ მომზადდა."] = "The OBD adapter is not connected or could not be prepared.",
            ["ჯერ დააკავშირე adapter და ჩართე კონტაქტი."] = "First connect the adapter and turn on the ignition.",
            ["საჭიროა აქტიური Bluetooth/Wi-Fi/USB adapter და ECU პასუხი."] = "An active Bluetooth/Wi-Fi/USB adapter and an ECU reply are required.",
            ["Adapter/ECU პასუხი არ დადასტურდა."] = "Adapter/ECU reply not confirmed.",
            ["PRO ანალიტიკა"] = "PRO analytics",
            ["AI, Recording, Fuel Analytics და Reports იყენებს მხოლოდ ნამდვილ OBD მონაცემებს."] = "AI, Recording, Fuel Analytics and Reports use only real OBD data.",
            ["ჯერ საჭიროა OBD კავშირი."] = "An OBD connection is needed first.",
            ["OEM ფუნქცია არ აქტიურდება მხოლოდ მარკის არჩევით."] = "An OEM function is not enabled just by choosing a make.",
            ["ჯერ საჭიროა VIN, ECU და დადასტურებული OEM definition."] = "A VIN, ECU and confirmed OEM definition are needed first.",
            ["Adapter არის, მაგრამ ECU პასუხი ვერ დადასტურდა."] = "The adapter is there, but the ECU reply could not be confirmed.",
            ["Adapter არის, მაგრამ ECU პასუხი ვერ დადასტურდა."] = "The adapter is there, but the ECU reply could not be confirmed.",
            ["ჩართე კონტაქტი და შეამოწმე OBD adapter."] = "Turn on the ignition and check the OBD adapter.",
            ["Adapter დაკავშირებულია, თუმცა სტანდარტულ 0100 მოთხოვნაზე ECU პასუხი ვერ მივიღეთ."] = "The adapter is connected, but we got no ECU reply to the standard 0100 request.",
            ["PRO არ ანაცვლებს ECU კავშირს."] = "PRO does not replace the ECU connection.",
            ["ჯერ საჭიროა რეალური Powertrain ECU პასუხი."] = "A real Powertrain ECU reply is needed first.",
            ["OEM სკანირება იწყება მხოლოდ დადასტურებული ECU ტრანსპორტით."] = "OEM scanning starts only with a confirmed ECU transport.",
            ["ECU პასუხი ჯერ არ არსებობს."] = "No ECU reply yet.",
            ["უკვე 2 კვირაა მანქანა არ დაგისკანერებიათ. დროა ახალი OBD-II სკანირება ჩაატაროთ."] = "You have not scanned the car for 2 weeks. Time for a new OBD-II scan.",
            ["OBD2 ადაპტერი არ არის დაკავშირებული. ჯერ დააკავშირე Bluetooth adapter."] = "The OBD2 adapter is not connected. Connect the Bluetooth adapter first.",
            ["სენსორების მომზადება…"] = "Preparing sensors…",
            ["ECU-მ DTC clear-ის სტანდარტული positive response (44) არ დააბრუნა."] = "The ECU did not return the standard positive response (44) for DTC clear.",
            ["Freeze Frame DTC-ის არსებობის შემოწმება…"] = "Checking whether a Freeze Frame DTC exists…",
            ["Freeze Frame დასრულდა."] = "Freeze Frame finished.",
            ["ELM327 პასუხის შემოწმება (ATI)…"] = "Checking the ELM327 reply (ATI)…",
            ["Adapter-ის საბაზისო ბრძანების შემოწმება (ATRV)…"] = "Checking the adapter's basic command (ATRV)…",
            ["მანქანის ECU პასუხის შემოწმება (0100)…"] = "Checking the car's ECU reply (0100)…",
            ["Adapter შესაძლებლობების უსაფრთხო შემოწმება…"] = "Safe check of adapter capabilities…",
            ["პროტოკოლისა და პასუხის დროის დამუშავება…"] = "Processing protocol and reply time…",
            ["ჯერ დააკავშირე OBD2 adapter და დაიწყე ინიციალიზაცია."] = "First connect the OBD2 adapter and start initialization.",
            ["VIN მიღებულია, მაგრამ მწარმოებელი ადგილობრივად ვერ განისაზღვრა."] = "VIN received, but the manufacturer could not be determined locally.",
            ["VIN და მწარმოებელი ECU-დან მიუწვდომელია."] = "VIN and manufacturer are unavailable from the ECU.",
            ["ადაპტერი დაკავშირებულია, მაგრამ მანქანის ECU-ს გამოსაყენებელი პასუხი არ მიუღია."] = "The adapter is connected, but it got no usable reply from the car's ECU.",
            ["ECU აფიქსირებს ჩართულ გაფრთხილების ნათურას, თუმცა ამ სკანზე წაკითხვადი DTC არ დაბრუნდა."] = "The ECU reports a warning lamp on, but no readable DTC was returned in this scan.",
            ["ჰიბრიდ"] = "Hybrid",
            ["Generic OBD-II engine diagnostics მზადაა"] = "Generic OBD-II engine diagnostics ready",
            ["ISO-TP multi-frame პასუხის შემოწმება (0902)…"] = "Checking the ISO-TP multi-frame reply (0902)…",
            ["Adapter capability შედეგების დამუშავება…"] = "Processing adapter capability results…",
            ["OEM Freeze Frame არ წაკითხულა — OEM DTC შედეგი არ არსებობს."] = "OEM Freeze Frame not read — there is no OEM DTC result.",
            ["OEM DTC არ აღმოჩნდა — DTC snapshot მოთხოვნა საჭირო არ არის."] = "No OEM DTC found — a DTC snapshot request is not needed.",
            ["OEM DTC სია ვერ დადასტურდა — snapshot მოთხოვნა უსაფრთხოდ არ გაიგზავნა."] = "The OEM DTC list could not be confirmed — for safety the snapshot request was not sent.",
            ["ECU-მ snapshot მოთხოვნას უპასუხა, მაგრამ შენახული snapshot არ დააბრუნა."] = "The ECU answered the snapshot request, but returned no stored snapshot.",
            ["Responding OEM ECU-სთვის reviewed freeze-frame definition არ არსებობს."] = "There is no reviewed freeze-frame definition for the responding OEM ECU.",
            ["ამ მოდელის/წლის executable ECU definition პაკეტში არ მოიძებნა."] = "No executable ECU definition for this model/year was found in the package.",
            ["VIN-ით დადასტურებული OEM მარკა ვერ განისაზღვრა."] = "The VIN-confirmed OEM make could not be determined.",
            ["დადასტურებულ მისამართებზე დადებითი read-only ECU პასუხი ვერ მივიღეთ."] = "We got no positive read-only ECU reply at the confirmed addresses.",
            ["OBD adapter არ არის დაკავშირებული."] = "The OBD adapter is not connected.",
            ["ამ ზუსტი ავტომობილისთვის reviewed OEM Live Data definition არ არსებობს."] = "There is no reviewed OEM Live Data definition for this exact vehicle.",
            ["Responding OEM ECU-სთვის reviewed DATA.READ command არ არსებობს."] = "There is no reviewed DATA.READ command for the responding OEM ECU.",
            ["OEM DTC წაკითხვა არ დაწყებულა — პასუხმგებელი verified ECU არ დადასტურდა."] = "OEM DTC reading did not start — no responding verified ECU was confirmed.",
            ["ECU-მ OEM DTC მოთხოვნას უპასუხა — კოდი არ აღმოჩნდა."] = "The ECU answered the OEM DTC request — no code found.",
            ["Responding OEM ECU-სთვის reviewed DTC command არ არსებობს."] = "There is no reviewed DTC command for the responding OEM ECU.",
            ["Manufacturer-specific აღწერა reviewed კატალოგში მიუწვდომელია."] = "A manufacturer-specific description is not available in the reviewed catalog.",
            ["ამ ზუსტი მოდელის/წლისთვის reviewed Active Test, Service ან Coding operation არ არსებობს."] = "There is no reviewed Active Test, Service or Coding operation for this exact model/year.",
            ["ჯერ საჭიროა Bluetooth adapter-ის კავშირი და ინიციალიზაცია."] = "The Bluetooth adapter must be connected and initialized first.",
            ["Adapter readiness არ დადასტურდა."] = "Adapter readiness not confirmed.",
            ["Adapter ჩანს, მაგრამ ECU პასუხი არ დადასტურდა."] = "The adapter is visible, but the ECU reply was not confirmed.",
            ["OEM მოდულების შემოწმება არ დაიწყება ECU პასუხის გარეშე."] = "OEM module checks will not start without an ECU reply.",
            ["CAN აქტიურია, მაგრამ adapter transport ჯერ სრულად არ დატესტილა."] = "CAN is active, but the adapter transport has not been fully tested yet.",
            ["ერთხელ გაუშვი OBD Adapter Center → TEST; OEM ECU მოთხოვნა მანამდე არ დაიწყება."] = "Run OBD Adapter Center → TEST once; OEM ECU requests will not start before that.",
            ["Adapter transport-ის სავალდებულო ტესტი არასრულია."] = "The required adapter transport test is incomplete.",
            ["Adapter-მა საჭირო CAN / ISO-TP transport შესაძლებლობა ვერ დაადასტურა."] = "The adapter could not confirm the required CAN / ISO-TP transport capability.",
            ["ISO-TP multi-frame პასუხიც რეალურად დადასტურდა."] = "The ISO-TP multi-frame reply was also really confirmed.",
            ["ISO-TP multi-frame პასუხი ამ ECU-ზე ჯერ ვერ დადასტურდა; მრავალფრეიმიანი OEM კითხვა მხარდაჭერილად არ ჩაითვლება."] = "The ISO-TP multi-frame reply could not be confirmed on this ECU yet; multi-frame OEM reading is not considered supported.",
            ["ISO/KWP აქტიურია, მაგრამ physical transport ჯერ არ დადასტურებულა."] = "ISO/KWP is active, but the physical transport has not been confirmed yet.",
            ["ერთხელ გაუშვი OBD Adapter Center → TEST; დაუდასტურებელი K-Line OEM მოთხოვნა არ გაიგზავნება."] = "Run OBD Adapter Center → TEST once; unconfirmed K-Line OEM requests will not be sent.",
            ["ISO/KWP ტრანსპორტი აქტიურია."] = "ISO/KWP transport is active.",
            ["შეიძლება დაიწყოს მხოლოდ ამ K-Line მანქანისთვის დადასტურებული read-only ECU პროფილი."] = "Only a read-only ECU profile confirmed for this K-Line car can start.",
            ["ამ ტრანსპორტისთვის OEM მოდულების უსაფრთხო პროფილი ჯერ არ არის დადასტურებული."] = "A safe OEM module profile for this transport has not been confirmed yet.",
            ["ჩართე Bluetooth და სცადე თავიდან."] = "Turn on Bluetooth and try again.",
            ["BLE მოწყობილობის მონაცემები არასწორია."] = "The BLE device data is invalid.",
            ["ბრძანება ცარიელია."] = "The command is empty.",
            ["BLE write characteristic ვერ მოიძებნა."] = "BLE write characteristic not found.",
            ["ეს BLE მოწყობილობა OBD-II ადაპტერად ვერ დადასტურდა."] = "This BLE device could not be confirmed as an OBD-II adapter.",
            ["Classic Bluetooth მისამართი არ არსებობს."] = "The Classic Bluetooth address does not exist.",
            ["Bluetooth adapter ვერ მოიძებნა."] = "Bluetooth adapter not found.",
            ["OBD2 მოწყობილობა ვეღარ მოიძებნა."] = "The OBD2 device can no longer be found.",
            ["Classic Bluetooth OBD2-თან დაკავშირება ვერ მოხერხდა. დარწმუნდი, რომ ადაპტერი ტელეფონის Bluetooth-ში დაპეარებულია."] = "Could not connect to the Classic Bluetooth OBD2. Make sure the adapter is paired in the phone's Bluetooth.",
            ["ადაპტერის დაპეარება ვერ დაიწყო. ტელეფონის Bluetooth პარამეტრებში დააპეარე OBD ადაპტერი და სცადე თავიდან."] = "Adapter pairing could not start. Pair the OBD adapter in the phone's Bluetooth settings and try again.",
            ["ადაპტერის დაპეარება არ დასრულდა. დაადასტურე Bluetooth-ის მოთხოვნა ტელეფონზე და სცადე თავიდან."] = "Adapter pairing did not finish. Confirm the Bluetooth request on the phone and try again.",
            ["Classic Bluetooth output stream მიუწვდომელია."] = "Classic Bluetooth output stream unavailable.",
            ["OBD2 Bluetooth კავშირი გაითიშა."] = "OBD2 Bluetooth connection lost.",
            ["Bluetooth/Location permission საჭიროა."] = "Bluetooth/Location permission is required.",
            ["OBD Bluetooth კავშირი გაითიშა."] = "OBD Bluetooth connection lost.",
            ["მიმდინარე ოდომეტრი"] = "Current odometer",
            ["სხვა დამოუკიდებელი ECU/OEM mileage წყარო ვერ მოიძებნა."] = "No other independent ECU/OEM mileage source was found.",
            ["ეს არის მანქანიდან წაკითხული მიმდინარე მნიშვნელობა და არა გარბენის ისტორიის დადასტურება."] = "This is the current value read from the car, not a confirmation of the mileage history.",
            ["ოდომეტრი მიუწვდომელია"] = "Odometer unavailable",
            ["ოდომეტრი მიუწვდომელია"] = "Odometer unavailable",
            ["ვერ დადასტურდა"] = "Could not be confirmed",
            ["ვერ დადასტურდა"] = "Could not be confirmed",
            ["ამ მანქანამ დადასტურებული odometer წყარო არ დააბრუნა."] = "This car did not return a confirmed odometer source.",
            ["მხოლოდ ერთი ECU წყარო"] = "Only one ECU source",
            ["მხოლოდ ერთი ECU წყარო"] = "Only one ECU source",
            ["შესაძლო შეუსაბამობა"] = "Possible mismatch",
            ["შესაძლო შეუსაბამობა"] = "Possible mismatch",
            ["წყაროები თანხვედრაშია"] = "Sources match",
            ["წყაროები თანხვედრაშია"] = "Sources match",
            ["ერთი დადასტურებული წყარო"] = "One confirmed source",
            ["წყაროები ემთხვევა"] = "Sources match",
            ["გარბენის შეუსაბამობა"] = "Mileage mismatch",
            ["გარბენის შეუსაბამობა"] = "Mileage mismatch",
            ["გარბენის გადამოწმება PRO ფუნქციაა."] = "Mileage verification is a PRO feature.",
            ["გარბენის ყველა ხელმისაწვდომი დადასტურებული წყაროს შემოწმება..."] = "Checking all available confirmed mileage sources...",
            ["Live მონაცემებს ველოდები…"] = "Waiting for live data…",
            ["აპის Settings-ში ჩართე Nearby devices / ახლომდებარე მოწყობილობები."] = "Turn on Nearby devices in the app Settings.",
            ["Mileage-ის სპეციალიზებულმა წყაროებმა მონაცემი ვერ დააბრუნეს და Standard OBD-II odometer PID A6-იც მიუწვდომელია."] = "The dedicated mileage sources returned no data, and the Standard OBD-II odometer PID A6 is also unavailable.",
            ["განსხვავება შეიძლება მიუთითებდეს მოდულის შეცვლაზე, reset-ზე ან odometer-ის შეუსაბამობაზე; ეს არ არის საბოლოო დასკვნა."] = "The difference may point to a module replacement, a reset or an odometer mismatch; this is not a final conclusion.",
            ["განსხვავება შეიძლება მიუთითებდეს მოდულის შეცვლაზე, reset-ზე ან odometer-ის შეუსაბამობაზე; ეს არ არის საბოლოო დასკვნა."] = "The difference may point to a module replacement, a reset or an odometer mismatch; this is not a final conclusion.",
            // --- start screen ---
            ["რას შევამოწმებთ დღეს?"] = "What are we checking today?",
            ["OBD სკანერი"] = "OBD scanner",
            ["დიაგნოსტიკა მანქანიდან"] = "Diagnostics from the car",
            ["მანქანის ისტორია VIN-ით"] = "Vehicle history by VIN",
            // --- diagnostic report ---
            ["მხოლოდ მანქანიდან წაკითხული რეალური მონაცემები"] = "Only real data read from the car",
            ["სკანი"] = "Scan",
            ["Check Engine / მზადყოფნა"] = "Check Engine / readiness",
            ["შედარება"] = "Compare",
            // --- performance history ---
            ["ყველა გაზომვა ინახება ამ ტელეფონზე."] = "All measurements are saved on this phone.",
            ["მანქანა გაჩერდა — ცდა თავიდან დაიწყება მოძრაობისას."] = "The car stopped — the attempt restarts when you move.",
            ["დრო ამოიწურა — საბოლოო სიჩქარეს ვერ მიაღწიე. სცადე თავიდან."] = "Time is up — the target speed was not reached. Try again.",
            // --- module DTC scan ---
            ["გადაცემათა კოლოფი"] = "Transmission",
            ["ABS / მუხრუჭები"] = "ABS / brakes",
            ["აირბაგი (SRS)"] = "Airbag (SRS)",
            ["ძარის მოდული (BCM)"] = "Body module (BCM)",
            ["ხელსაწყოების დაფა"] = "Instrument cluster",
            ["საჭის გამაძლიერებელი"] = "Power steering",
            ["კლიმატ-კონტროლი"] = "Climate control",
            ["ჰიბრიდის სისტემა"] = "Hybrid system",
            ["მაღალი ძაბვის ბატარეა"] = "High-voltage battery",
            ["გეითვეი (Gateway)"] = "Gateway",
            ["მულტიმედია"] = "Infotainment",
            ["რადარი / კამერა"] = "Radar / camera",
            ["გასაღები / იმობილაიზერი"] = "Key / immobilizer",
            ["პარკინგის სისტემა"] = "Parking system",
            ["OBD მოდული"] = "OBD module",
            ["მოდული"] = "Module",
            ["მოდულები"] = "Modules",
            ["მოდულების შემოწმება (ABS, აირბაგი…)…"] = "Checking modules (ABS, airbag…)…",
            ["მოდულების შემოწმება დასრულდა."] = "Module check finished.",
            ["OBD adapter არ არის მზად."] = "The OBD adapter is not ready.",
            ["ამ მანქანის კავშირის ტიპზე (არა CAN) სხვა მოდულების სკანი ჯერ არ არის ხელმისაწვდომი."] = "Scanning other modules is not available yet for this car's connection type (not CAN).",
            ["სხვა მოდულების შემოწმება ვერ მოხერხდა ამ ადაპტერით."] = "Other modules could not be checked with this adapter.",
            ["სხვა მოდულების შემოწმება ვერ მოხერხდა."] = "Other modules could not be checked.",
            ["მანქანა მოძრაობდა, ამიტომ შემოწმდა მხოლოდ ძრავი და კოლოფი. სრული სკანისთვის გააჩერე მანქანა."] = "The car was moving, so only the engine and gearbox were checked. Stop the car for a full scan.",
            ["მარკა ვერ დადგინდა, ამიტომ მხოლოდ სტანდარტული მოდულები შემოწმდა."] = "The make could not be determined, so only standard modules were checked.",
            ["მოდულების კოდები იშლება..."] = "Clearing module codes...",
            ["ამ კოდის აღწერა ბაზაში ვერ მოიძებნა (მწარმოებლის სპეციფიკური კოდი)."] = "No description in the database (manufacturer-specific code).",
            ["მოდულები (ABS, აირბაგი…)"] = "Modules (ABS, airbag…)",
            ["მოდულები:"] = "Modules:",
        };

    private static readonly Dictionary<string, string> UiEnToKa =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["TEST"] = "ტესტი",
            ["DETECTION STATUS"] = "აღმოჩენის სტატუსი",
            ["Universal OBD-II"] = "უნივერსალური OBD-II",
            ["PRO Analytics"] = "PRO ანალიტიკა",
            ["Manufacturer Specific"] = "მწარმოებლის სპეციფიკური",
            ["A/C Refrigerant"] = "კონდიციონერის მაცივარი",
            ["ARM 0–100 TEST"] = "0–100 ტესტის დაწყება",
            ["Adapter connection"] = "ადაპტერთან კავშირი",
            ["Adapter: — · Protocol: — · Voltage: —"] = "ადაპტერი: — · პროტოკოლი: — · ძაბვა: —",
            ["Air / Intake"] = "ჰაერი / შეწოვა",
            ["Are you sure you want to clear the DTC codes? This will also reset readiness monitors."] = "ნამდვილად გსურთ DTC კოდების წაშლა? ეს Readiness მონიტორებსაც გაანულებს.",
            ["Bluetooth — · ELM327 — · ECU —"] = "Bluetooth — · ELM327 — · ECU —",
            ["Boost Pressure"] = "ბუსტის წნევა",
            ["Calculated from MAF + fuel type"] = "გამოთვლილია MAF-ით და საწვავის ტიპით",
            ["Calculated value — MAF + fuel type"] = "გამოთვლილი მნიშვნელობა — MAF + საწვავის ტიპი",
            ["Capability benchmark: not tested"] = "შესაძლებლობების ტესტი: არ შემოწმებულა",
            ["Car Scanner History"] = "სკანერის ისტორია",
            ["Clear Failed"] = "წაშლა ვერ მოხერხდა",
            ["Clearing DTC codes..."] = "DTC კოდები იშლება...",
            ["Comprehensive Components"] = "ძირითადი კომპონენტები",
            ["Critical Issues: —    Warnings: —"] = "კრიტიკული პრობლემები: —    გაფრთხილებები: —",
            ["Current-cycle readiness reported by optional Mode 01 PID 41"] = "მიმდინარე ციკლის readiness — optional Mode 01 PID 41",
            ["DISTANCE · MIL ON"] = "მანძილი · MIL ჩართული",
            ["DTC clear verification timed out."] = "DTC წაშლის გადამოწმების დრო ამოიწურა.",
            ["DTC codes cleared successfully."] = "DTC კოდები წარმატებით წაიშალა.",
            ["ECU counters recorded since diagnostic codes were cleared"] = "ECU-ის მონაცემები დიაგნოსტიკური კოდების ბოლო წაშლის შემდეგ",
            ["ECU response time"] = "ECU პასუხის დრო",
            ["EGR / VVT System"] = "EGR / VVT სისტემა",
            ["EGR System"] = "EGR სისტემა",
            ["EGR/VVT System"] = "EGR/VVT სისტემა",
            ["EMISSION DTC"] = "ემისიის DTC",
            ["Emissions: —"] = "ემისიები: —",
            ["Engine / ECM"] = "ძრავა / ECM",
            ["Engine load"] = "ძრავის დატვირთვა",
            ["Evaporative System"] = "აორთქლების სისტემა (EVAP)",
            ["Exhaust Gas Sensor"] = "გამონაბოლქვის გაზის სენსორი",
            ["Fault Returned"] = "შეცდომა დაბრუნდა",
            ["Flex Fuel"] = "Flex Fuel",
            ["Freeze Frame · Fault snapshot"] = "Freeze Frame · ხარვეზის მომენტი",
            ["Freeze Frame: —"] = "Freeze Frame: —",
            ["Fuel Consumption"] = "საწვავის ხარჯი",
            ["Fuel System"] = "საწვავის სისტემა",
            ["Generic OBD-II engine diagnostics"] = "Generic OBD-II ძრავის დიაგნოსტიკა",
            ["Guided Fuel Trim Test"] = "Fuel Trim ტესტი ინსტრუქციით",
            ["Heated Catalyst"] = "გათბობადი კატალიზატორი",
            ["Hybrid / EV"] = "ჰიბრიდი / ელექტრო",
            ["INCOMPLETE SAVED SCAN"] = "არასრული შენახული სკანი",
            ["Inspection requirements vary by country and region. This page shows the OBD-II readiness status reported by the ECU."] = "ინსპექციის წესები ქვეყნების და რეგიონების მიხედვით განსხვავდება. ეს გვერდი აჩვენებს ECU-ის მიერ დაბრუნებულ OBD-II readiness სტატუსს.",
            ["LAST VALUE"] = "ბოლო მნიშვნელობა",
            ["Live Data: —"] = "Live Data: —",
            ["Low power"] = "დაბალი სიმძლავრე",
            ["MIL STATUS"] = "MIL სტატუსი",
            ["MIL: ON"] = "MIL: ჩართულია",
            ["Mileage Verification"] = "გარბენის გადამოწმება",
            ["Mode 05 O₂ Tests"] = "Mode 05 O₂ ტესტები",
            ["Monitors checked continuously while the engine is operating"] = "მონიტორები, რომლებიც ძრავის მუშაობისას მუდმივად მოწმდება",
            ["NMHC Catalyst"] = "NMHC კატალიზატორი",
            ["NO DATA"] = "მონაცემი არ არის",
            ["NO RESPONSE"] = "პასუხი არ არის",
            ["NO SAVED SCAN"] = "შენახული სკანი არ არის",
            ["NOT AVAILABLE"] = "მიუწვდომელია",
            ["NOT CHECKED"] = "არ შემოწმებულა",
            ["NOT SCANNED"] = "არ დასკანერებულა",
            ["NOT SUPPORTED"] = "მხარდაჭერილი არ არის",
            ["NOT TESTED"] = "არ ტესტირებულა",
            ["Not Ready"] = "არ არის მზად",
            ["Not Verified"] = "არ დადასტურებულა",
            ["OBD protocol"] = "OBD პროტოკოლი",
            ["OBD-II emissions & readiness monitors"] = "OBD-II ემისიები და readiness მონიტორები",
            ["Oxygen / Lambda"] = "ჟანგბადი / ლამბდა",
            ["Oxygen Sensor"] = "ჟანგბადის სენსორი",
            ["Oxygen Sensor Heater"] = "ჟანგბადის სენსორის გამათბობელი",
            ["Oxygen Sensors"] = "ჟანგბადის სენსორები",
            ["PM Filter"] = "ნაწილაკების ფილტრი",
            ["Protocol: —"] = "პროტოკოლი: —",
            ["READINESS STATUS"] = "მზადყოფნის სტატუსი",
            ["RESPONDED / LIMITED"] = "პასუხობს / შეზღუდული",
            ["Reading readiness monitors and emissions status"] = "Readiness მონიტორები და ემისიის სტატუსი იკითხება",
            ["SAVED DIAGNOSTIC REPORT"] = "შენახული დიაგნოსტიკური ანგარიში",
            ["SAVED NO-ECU REPORT"] = "შენახული ანგარიში ECU-ს გარეშე",
            ["Safe & Reliable OBD-II Diagnostics"] = "უსაფრთხო და სანდო OBD-II დიაგნოსტიკა",
            ["Secondary Air System"] = "მეორადი ჰაერის სისტემა",
            ["Supported: — parameters"] = "მხარდაჭერილი: — პარამეტრი",
            ["TIME · MIL ON"] = "დრო · MIL ჩართული",
            ["The DTC codes were cleared successfully."] = "DTC კოდები წარმატებით წაიშალა.",
            ["The clear command was accepted, but one or more faults are still present."] = "წაშლის ბრძანება შესრულდა, მაგრამ ერთი ან რამდენიმე ხარვეზი ისევ არსებობს.",
            ["The codes were cleared, but a fault returned."] = "კოდები წაიშალა, მაგრამ შეცდომა ისევ დაბრუნდა.",
            ["These monitors usually require specific driving conditions"] = "ამ მონიტორებს კონკრეტული მართვის პირობები სჭირდება",
            ["Updating ECU data..."] = "ECU მონაცემები ახლდება...",
            ["VIN: —"] = "VIN: —",
            ["Vehicle Diagnostics"] = "ავტომობილის დიაგნოსტიკა",
            ["Vehicle Speed"] = "ავტომობილის სიჩქარე",
            ["Verifying DTC clear..."] = "მოწმდება DTC კოდების წაშლა...",
            ["Warning indicator requested"] = "გამაფრთხილებელი ინდიკატორი ჩართულია",
            ["● Connected"] = "● დაკავშირებულია",
            ["● Disconnected"] = "● გათიშულია",
            ["Freeze Frame: Error"] = "Freeze Frame: შეცდომა",
            ["Freeze Frame: Invalid Response"] = "Freeze Frame: არასწორი პასუხი",
            ["OEM DTC: Error"] = "OEM DTC: შეცდომა",
            ["OEM DTC: Invalid Response"] = "OEM DTC: არასწორი პასუხი",
            ["DTC: Not Checked"] = "DTC: არ შემოწმებულა",
            ["Freeze Frame: No Response"] = "Freeze Frame: პასუხი არ არის",
            ["Freeze Frame: Not Configured"] = "Freeze Frame: არ არის კონფიგურირებული",
            ["Freeze Frame: Not Supported"] = "Freeze Frame: მხარდაჭერილი არ არის",
            ["OEM DTC: No Response"] = "OEM DTC: პასუხი არ არის",
            ["OEM DTC: Not Supported"] = "OEM DTC: მხარდაჭერილი არ არის",
            ["ECU Connected"] = "ECU დაკავშირებულია",
            ["Engine Control Module — Communication OK"] = "ძრავის მართვის მოდული — კავშირი OK",
            ["Start Scan"] = "სკანირების დაწყება",
            ["Log export failed"] = "ლოგის ექსპორტი ვერ შესრულდა",
        };

    // Sentences with values inside: {0}, {1} … are the values.
    private static readonly (Regex Pattern, string Target)[] UiKaToEnTemplates = BuildTemplates(new (string, string)[]
    {
        ("0100 მოთხოვნის გაზომილი დრო: {0} ms", "0100 request time: {0} ms"),
        ("12V სისტემა ვერ შემოწმდა — {0}", "12V system could not be checked — {0}"),
        ("AI: პასუხი ვერ მივიღე — {0}", "AI: no answer received — {0}"),
        ("ATRV პასუხი: {0}", "ATRV response: {0}"),
        ("Adapter-მა ATI ბრძანებას უპასუხა: {0}", "The adapter answered ATI: {0}"),
        ("Adapter-მა აქტიური პროტოკოლი დააბრუნა: {0}", "The adapter returned the active protocol: {0}"),
        ("Before Repair baseline შენახულია: {0} · DTC: {1} · PID: {2}.", "Before Repair baseline saved: {0} · DTC: {1} · PID: {2}."),
        ("DTC clear ვერ დადასტურდა: {0}", "DTC clear not confirmed: {0}"),
        ("DTC კოდების წაშლა ვერ შესრულდა: {0}", "Clearing DTCs failed: {0}"),
        ("DTC წაშლის შედეგი: {0}", "DTC clear result: {0}"),
        ("DTC-ის ნაწილი წაკითხულია ({0}/{1}); დარჩენილი სერვისები ECU-მ არ დააბრუნა.", "DTCs partly read ({0}/{1}); the ECU did not return the remaining services."),
        ("ECU მონაცემთა წყარო: Generic OBD-II · {0}", "ECU data source: Generic OBD-II · {0}"),
        ("ECU-reported Hybrid Battery Remaining Life: {0}%. ეს არ არის ავტომატურად Battery SOC, pack voltage ან cell მდგომარეობა.", "ECU-reported Hybrid Battery Remaining Life: {0}%. This is not automatically the battery SOC, pack voltage or cell condition."),
        ("ECU-მ {0} რეალური Mode 05 item დააბრუნა.", "The ECU returned {0} real Mode 05 items."),
        ("ECU-მ {0} რეალური Mode 06 item დააბრუნა.", "The ECU returned {0} real Mode 06 items."),
        ("ECU-მ დაადასტურა {0} PID · რეალურად წაკითხულია {1} სენსორი · მიუწვდომელი პასუხი: {2}.", "The ECU confirmed {0} PIDs · {1} sensors actually read · unavailable responses: {2}."),
        ("Freeze Frame წაკითხვა ვერ დასრულდა: {0}", "Freeze Frame reading could not finish: {0}"),
        ("Freeze Frame: {0} რეალური მნიშვნელობა · Trigger: {1}", "Freeze Frame: {0} real values · Trigger: {1}"),
        ("Generic ECU დადასტურდა. OEM discovery: {0}", "Generic ECU confirmed. OEM discovery: {0}"),
        ("Generic ECU დადასტურდა; {0} verified OEM მისამართი შემოწმდა, დადებითი პასუხი: {1}.", "Generic ECU confirmed; {0} verified OEM addresses checked, positive responses: {1}."),
        ("Guided Test ვერ შესრულდა: {0}", "Guided Test failed: {0}"),
        ("Idle შედეგი: {0}. მეორე წაკითხვა გაუქმდა.", "Idle result: {0}. The second reading was cancelled."),
        ("Idle: {0}. 2500 RPM-ზე ECU-მ მნიშვნელობა არ დააბრუნა.", "Idle: {0}. The ECU returned no value at 2500 RPM."),
        ("Live Data snapshot: {0}/{1} რეალური მნიშვნელობა · სულ მხარდაჭერილი PID: {2}", "Live Data snapshot: {0}/{1} real values · total supported PIDs: {2}"),
        ("Live Data snapshot: მოთხოვნილი {0} PID-დან დეკოდირებადი პასუხი არ დაბრუნებულა", "Live Data snapshot: no decodable response from the {0} requested PIDs"),
        ("Live Data: {0} რეალური PID snapshot-დან", "Live Data: {0} real PIDs from the snapshot"),
        ("MIL: გამორთულია — ECU ამ მომენტში არ ითხოვს Check Engine ინდიკატორს. შენახული DTC: {0}.", "MIL: off — the ECU is not requesting the Check Engine light right now. Stored DTCs: {0}."),
        ("MIL: ჩართულია — ECU ითხოვს Check Engine ინდიკატორს. შენახული DTC: {0}.", "MIL: on — the ECU is requesting the Check Engine light. Stored DTCs: {0}."),
        ("MODE 06 — {0} item ზღვარს გარეთაა", "MODE 06 — {0} items out of limits"),
        ("OBD სკანი წარმატებით დასრულდა, მაგრამ AI პასუხი ამჯერად ვერ მივიღეთ. {0}", "The OBD scan finished successfully, but no AI answer this time. {0}"),
        ("PDF ექსპორტი ვერ შესრულდა: {0}", "PDF export failed: {0}"),
        ("PID 01{0} ამ ECU-ზე მიუწვდომელია.", "PID 01{0} is unavailable on this ECU."),
        ("PID 01{0} არჩეულია Live Data-სთვის.", "PID 01{0} selected for Live Data."),
        ("Repair Verification ვერ დასრულდა: {0}", "Repair Verification could not finish: {0}"),
        ("Trigger: {0} · {1} რეალური მნიშვნელობა", "Trigger: {0} · {1} real values"),
        ("Vehicle: მიუწვდომელია · {0}", "Vehicle: unavailable · {0}"),
        ("{0}: ნაჩვენებია მხოლოდ რეალურად დაბრუნებული reviewed DID მნიშვნელობები.", "{0}: only reviewed DID values actually returned are shown."),
        ("{0}: რეალური OEM მნიშვნელობა ჯერ არ დაბრუნებულა.", "{0}: no real OEM value returned yet."),
        ("{0} პროფილი არჩეულია. ჩართე Bluetooth, ჩართე მანქანის კონტაქტი და მოძებნე OBD-II ადაპტერი.", "{0} profile selected. Turn on Bluetooth and the car's ignition, then search for the OBD-II adapter."),
        ("{0} PID/s (აქტიური Live Data სესია)", "{0} PID/s (active Live Data session)"),
        ("{0} ხელმისაწვდომია მხოლოდ აქტიური PRO ვერსიით.", "{0} is available only with an active PRO version."),
        ("{0}: ადრე {1} → ახლა {2}", "{0}: before {1} → now {2}"),
        ("{0} — {1} წმ · {2}", "{0} — {1} s · {2}"),
        ("ავტომობილი: {0} · {1} · საწვავი: {2}", "Vehicle: {0} · {1} · fuel: {2}"),
        ("ამ სკანზე {0} რეალური PID მნიშვნელობა წაკითხულია. Live Data Center-ში იხილავ პერიოდულ განახლებას მხოლოდ მხარდაჭერილი PID-ებისთვის.", "{0} real PID values were read on this scan. Live Data Center shows periodic updates for supported PIDs only."),
        ("აქტიური DTC: {0} — ნახე AI-ის სავარაუდო მიზეზები ქვემოთ", "Active DTCs: {0} — see the AI's likely causes below"),
        ("ახალი გაზომვა მიუწვდომელია — {0} ბოლო წარმატებული გაზომვა ზემოთ რჩება.", "New measurement unavailable — {0} The last successful measurement stays above."),
        ("ბოლო AI სკანი: {0}", "Last AI scan: {0}"),
        ("ბოლო განახლება: {0}", "Last update: {0}"),
        ("ბოლო შემოწმება: {0}", "Last check: {0}"),
        ("გაზიარება ვერ შესრულდა: {0}", "Sharing failed: {0}"),
        ("დადასტურებული წყარო: {0}", "Confirmed source: {0}"),
        ("დაკავშირებულია: {0}. მიმდინარეობს OBD ინიციალიზაცია…", "Connected: {0}. Initializing OBD…"),
        ("ვუკავშირდები {0}…", "Connecting to {0}…"),
        ("თქვენ: {0}", "You: {0}"),
        ("იმპორტი ვერ შესრულდა: {0}", "Import failed: {0}"),
        ("კოდი წაკითხულ სერვისებში არ აღმოჩნდა, მაგრამ DTC წაკითხვა ნაწილობრივ დასრულდა ({0}/{1}).", "No code in the services read, but DTC reading finished only partly ({0}/{1})."),
        ("მიზეზი: {0}", "Reason: {0}"),
        ("მოგზაურობა შენახულია: {0} · {1}.", "Trip saved: {0} · {1}."),
        ("ნაპოვნია {0} OBD-II ადაპტერი.", "Found {0} OBD-II adapters."),
        ("ნაპოვნია {0} კოდი", "Found {0} codes"),
        ("რეალურ დროში ახლდება · {0}", "Updating in real time · {0}"),
        ("რეალური ECU პასუხი · Vehicle Speed PID 01 0D · {0}", "Real ECU response · Vehicle Speed PID 01 0D · {0}"),
        ("რეალური Tank-to-Tank: {0} · {1} · {2} L · ₾{3}. გამოთვლაში გამოყენებულია ორი სრული ავზი.", "Real Tank-to-Tank: {0} · {1} · {2} L · ₾{3}. Two full tanks were used in the calculation."),
        ("სესია დამატებულია: {0} მონაცემი.", "Session added: {0} samples."),
        ("ტესტი ვერ შესრულდა: {0}", "Test failed: {0}"),
        ("შევსება შენახულია: {0} L · ₾{1}.", "Refuel saved: {0} L · ₾{1}."),
        ("შენახულია {0} მოგზაურობა ამ VIN-ზე.", "{0} trips saved for this VIN."),
        ("შენახულია: {0} · ეს ანგარიში ეფუძნება დასრულებულ Generic OBD-II სკანს.", "Saved: {0} · this report is based on a finished Generic OBD-II scan."),
        ("შენახულია: {0} მონაცემი · {1}", "Saved: {0} samples · {1}"),
        ("ჩაწერა დასრულდა და მოწყობილობაზე შეინახა: {0} მონაცემი.", "Recording finished and saved on the device: {0} samples."),
        ("წაიშალოს {0} ჩანაწერი? ეს მოქმედება ვერ დაბრუნდება.", "Delete the {0} entry? This can't be undone."),
        ("Dashboard და წაკითხული ECU მნიშვნელობა განსხვავდება · სხვაობა: {0}. ეს საბოლოო მტკიცებულება არ არის.", "Dashboard and ECU value differ · difference: {0}. This is not final proof."),
        ("Dashboard და წაკითხული ECU მნიშვნელობა ახლოსაა · სხვაობა: {0}", "Dashboard and ECU value are close · difference: {0}"),
        ("OEM DTC: {0} კოდი", "OEM DTC: {0} codes"),
        ("ECU-დან განახლდა {0} სერვისის მნიშვნელობა.", "Updated {0} service value(s) from the ECU."),
        ("AI სერვისმა მოთხოვნა ვერ დაამუშავა (HTTP {0}). OBD სკანის მონაცემები შენარჩუნებულია.", "AI service could not process the request (HTTP {0}). OBD scan data is kept."),
        ("ჩვეულებრივი: {0}–{1} · გაფრთხილება: {2}–{3} · კრიტიკული: >{4}", "Normal: {0}–{1} · Warning: {2}–{3} · Critical: >{4}"),
        ("საწვავის კოდი {0}", "Fuel code {0}"),
        ("{0} OEM ECU-მ რეალურად დააბრუნა read-only პასუხი.", "{0} OEM ECUs actually returned a read-only reply."),
        ("{0} ECU მისამართი შემოწმდა; {1}-მა სრული დადებითი პასუხი დააბრუნა.", "{0} ECU addresses were checked; {1} returned a full positive reply."),
        ("ჯერ ჩამოდი {0} km/h-მდე ან ქვემოთ, შემდეგ დაიწყე აჩქარება.", "First slow down to {0} km/h or below, then start accelerating."),
        ("DTC წაკითხვა ნაწილობრივ დასრულდა ({0}/{1})", "DTC reading partly finished ({0}/{1})"),
        ("DTC წაკითხვა ნაწილობრივ დასრულდა ({0}/{1})", "DTC reading partly finished ({0}/{1})"),
        ("{0} კოდი", "{0} codes"),
        ("{0} კოდი", "{0} codes"),
        ("· ნაწილობრივი წაკითხვა {0}/{1}", "· partial reading {0}/{1}"),
        ("ნაწილობრივი წაკითხვა {0}/{1}", "partial reading {0}/{1}"),
        ("განახლდა: {0}", "Updated: {0}"),
        ("ECU: {0} · სტატუსი ECU-ის პასუხიდანაა.", "ECU: {0} · status comes from the ECU reply."),
        ("{0} სენსორი", "{0} sensors"),
        ("სესია დამატებულია: {0} მონაცემი.", "Session added: {0} data points."),
        ("იმპორტი ვერ შესრულდა: {0}", "Import failed: {0}"),
        ("წაიშალოს {0} ჩანაწერი? ეს მოქმედება ვერ დაბრუნდება.", "Delete {0} records? This cannot be undone."),
        ("ბოლო განახლება: {0}", "Last update: {0}"),
        ("ახალი გაზომვა მიუწვდომელია — {0} ბოლო წარმატებული გაზომვა ზემოთ რჩება.", "New measurement unavailable — the last successful measurement from {0} stays above."),
        ("ECU-reported Hybrid Battery Remaining Life: {0}%. ეს არ არის ავტომატურად Battery SOC, pack voltage ან cell მდგომარეობა.", "ECU-reported Hybrid Battery Remaining Life: {0}%. This is not automatically Battery SOC, pack voltage or cell condition."),
        ("MIL: ჩართულია — ECU ითხოვს Check Engine ინდიკატორს. შენახული DTC: {0}.", "MIL: on — the ECU requests the Check Engine indicator. Stored DTCs: {0}."),
        ("MIL: გამორთულია — ECU ამ მომენტში არ ითხოვს Check Engine ინდიკატორს. შენახული DTC: {0}.", "MIL: off — the ECU does not request the Check Engine indicator right now. Stored DTCs: {0}."),
        ("ნაპოვნია {0} კოდი", "{0} codes found"),
        ("Freeze Frame წაკითხვა ვერ დასრულდა: {0}", "Freeze Frame reading failed: {0}"),
        ("შევსება შენახულია: {0} L · ₾{1}.", "Fill-up saved: {0} L · ₾{1}."),
        ("რეალური Tank-to-Tank: {0} · {1} · {2} L · ₾{3}. გამოთვლაში გამოყენებულია ორი სრული ავზი.", "Real Tank-to-Tank: {0} · {1} · {2} L · ₾{3}. Two full tanks were used for the calculation."),
        ("მოგზაურობა შენახულია: {0} · {1}.", "Trip saved: {0} · {1}."),
        ("შენახულია {0} მოგზაურობა ამ VIN-ზე.", "{0} trips saved for this VIN."),
        ("ემისიის DTC: {0}", "Emission DTC: {0}"),
        ("{0} გახურება", "{0} warm-ups"),
        ("{0} წთ", "{0} min"),
        ("Vehicle: მიუწვდომელია · {0}", "Vehicle: unavailable · {0}"),
        ("{0} / თვე", "{0} / month"),
        ("გადახდა ვერ შესრულდა: {0}", "Payment failed: {0}"),
        ("შეძენის აღდგენა ვერ შესრულდა: {0}", "Restoring purchase failed: {0}"),
        ("ზეთის რესურსი ({0}) დაყენდება {1}%-ზე.", "Oil life ({0}) will be set to {1}%."),
        ("ECU ამ ჩაწერას არ უჭერს მხარს (7F 3B {0}).", "The ECU does not support this write (7F 3B {0})."),
        ("ECU-მ უარი თქვა (7F 3B {0}). მნიშვნელობა არ შეცვლილა.", "The ECU refused (7F 3B {0}). The value did not change."),
        ("ECU მონაცემი: {0} · Direct Reset: {1} · დამატებითი: {2}", "ECU data: {0} · Direct Reset: {1} · Extra: {2}"),
        ("ECU-დან განახლდა {0} სერვისის მნიშვნელობა. ხელმისაწვდომია {1} დადასტურებული reset.", "{0} service values updated from the ECU. {1} confirmed resets available."),
        ("ECU-დან განახლდა {0} სერვისის მნიშვნელობა.", "{0} service values updated from the ECU."),
        ("✓ OEM მონაცემები შენახულია:\n{0}", "✓ OEM data saved:\n{0}"),
        ("OEM Capture ვერ შესრულდა: {0}", "OEM Capture failed: {0}"),
        ("ზეთის შეცვლისას დააჭირე „ზეთი შევცვალე“ — აპი თავად დაითვლის დარჩენილ კილომეტრს და დღეებს ({0} კმ / 12 თვე).", "When you change the oil, press “Oil changed” — the app will count the remaining km and days itself ({0} km / 12 months)."),
        ("ზეთი შეიცვალა: {0} · ინტერვალი {1} კმ / 12 თვე", "Oil changed: {0} · interval {1} km / 12 months"),
        ("{0} ({1}) განულდება.\n\n• გააკეთე მხოლოდ სერვისის შესრულების შემდეგ.\n• მანქანა P-ზე, ანთება ჩართული.\n\nგაგრძელდეს?", "{0} ({1}) will be reset.\n\n• Do it only after the service has been done.\n• Car in P, ignition on.\n\nContinue?"),
        ("{0} ({1}) განულდება.", "{0} ({1}) will be reset."),
        ("{0} — ინსტრუქცია ნაჩვენებია.", "{0} — instructions shown."),
        ("ინსტრუქციის ჩვენება ვერ მოხერხდა: {0}", "Could not show the instructions: {0}"),
        ("{0}\n\nაპი გაშვებამდე თავიდან შეამოწმებს ძაბვას, სიჩქარეს, ძრავის მდგომარეობას და exact ECU definition-ს. გაგრძელდეს?", "{0}\n\nBefore running, the app will re-check voltage, speed, engine state and the exact ECU definition. Continue?"),
        ("{0} იწყება…", "{0} is starting…"),
        ("✓ RESET დადასტურდა ECU readback-ით. {0}", "✓ RESET confirmed by ECU readback. {0}"),
        ("RESET დადასტურდა ECU readback-ით. {0}", "RESET confirmed by ECU readback. {0}"),
        ("✓ ECU-მ reviewed reset command დაადასტურა, მაგრამ წაკითხვადი Service მნიშვნელობა ჯერ არ შეცვლილა. აპი 100%-ს არ გააყალბებს. Ignition cycle-ის შემდეგ ხელახლა დააჭირე Check-ს. {0}", "✓ The ECU confirmed the reviewed reset command, but the readable Service value has not changed yet. The app will not fake 100%. Press Check again after an ignition cycle. {0}"),
        ("ECU-მ reviewed reset command დაადასტურა, მაგრამ წაკითხვადი Service მნიშვნელობა ჯერ არ შეცვლილა. აპი 100%-ს არ გააყალბებს. Ignition cycle-ის შემდეგ ხელახლა დააჭირე Check-ს. {0}", "The ECU confirmed the reviewed reset command, but the readable Service value has not changed yet. The app will not fake 100%. Press Check again after an ignition cycle. {0}"),
        ("✓ ECU-მ reviewed reset command დაადასტურა. დამოუკიდებელი Service-value readback ამ მანქანაზე მიუწვდომელია, ამიტომ დაფაზე/შემდეგ Check-ზე გადაამოწმე ახალი interval. {0}", "✓ The ECU confirmed the reviewed reset command. An independent Service-value readback is not available on this car, so check the new interval on the dashboard/next Check. {0}"),
        ("ECU-მ reviewed reset command დაადასტურა. დამოუკიდებელი Service-value readback ამ მანქანაზე მიუწვდომელია, ამიტომ დაფაზე/შემდეგ Check-ზე გადაამოწმე ახალი interval. {0}", "The ECU confirmed the reviewed reset command. An independent Service-value readback is not available on this car, so check the new interval on the dashboard/next Check. {0}"),
        ("მოქმედება არ დასრულდა: {0}", "Action not finished: {0}"),
        ("მოქმედება ვერ შესრულდა: {0}", "Action failed: {0}"),
        ("Backup-ის აღდგენა ვერ შესრულდა: {0}", "Restoring the backup failed: {0}"),
        ("DTC-ის ნაწილი წაკითხულია ({0}/{1}); დარჩენილი სერვისები ECU-მ არ დააბრუნა.", "Part of the DTCs was read ({0}/{1}); the ECU did not return the remaining services."),
        ("Live Data snapshot: მოთხოვნილი {0} PID-დან დეკოდირებადი პასუხი არ დაბრუნებულა", "Live Data snapshot: no decodable reply returned from the {0} requested PIDs"),
        ("Live Data snapshot: {0}/{1} რეალური მნიშვნელობა · სულ მხარდაჭერილი PID: {2}", "Live Data snapshot: {0}/{1} real values · total supported PIDs: {2}"),
        ("დადასტურებული წყარო: {0}", "Confirmed source: {0}"),
        ("Generic ECU დადასტურდა. OEM discovery: {0}", "Generic ECU confirmed. OEM discovery: {0}"),
        ("Generic ECU დადასტურდა; {0} verified OEM მისამართი შემოწმდა, დადებითი პასუხი: {1}.", "Generic ECU confirmed; {0} verified OEM addresses checked, positive replies: {1}."),
        ("ECU-მ DTC კოდების წაშლა ვერ დაადასტურა.\n{0}", "The ECU did not confirm clearing the DTCs.\n{0}"),
        ("კოდი წაკითხულ სერვისებში არ აღმოჩნდა, მაგრამ DTC წაკითხვა ნაწილობრივ დასრულდა ({0}/{1}).", "The code was not found in the services read, but DTC reading finished only partly ({0}/{1})."),
        ("ამ სკანზე {0} რეალური PID მნიშვნელობა წაკითხულია. Live Data Center-ში იხილავ პერიოდულ განახლებას მხოლოდ მხარდაჭერილი PID-ებისთვის.", "{0} real PID values were read in this scan. The Live Data Center shows periodic updates only for supported PIDs."),
        ("OEM DTC: {0} კოდი", "OEM DTC: {0} codes"),
        ("OEM DTC: {0} კოდი", "OEM DTC: {0} codes"),
        ("წყაროები შემოწმდა: {0} ·", "Sources checked: {0} ·"),
        ("სანდოობა: {0}", "Reliability: {0}"),
        ("Dashboard და წაკითხული ECU მნიშვნელობა ახლოსაა · სხვაობა: {0}", "Dashboard and the ECU value read are close · difference: {0}"),
        ("Dashboard და წაკითხული ECU მნიშვნელობა ახლოსაა · სხვაობა: {0}", "Dashboard and the ECU value read are close · difference: {0}"),
        ("Dashboard და წაკითხული ECU მნიშვნელობა განსხვავდება · სხვაობა: {0}. ეს საბოლოო მტკიცებულება არ არის.", "Dashboard and the ECU value read differ · difference: {0}. This is not final proof."),
        ("Dashboard და წაკითხული ECU მნიშვნელობა განსხვავდება · სხვაობა: {0}. ეს საბოლოო მტკიცებულება არ არის.", "Dashboard and the ECU value read differ · difference: {0}. This is not final proof."),
        ("Dashboard: {0} · ავტომატური ECU წყარო ვერ მოიძებნა.", "Dashboard: {0} · no automatic ECU source found."),
        ("რეალური ECU პასუხი · Vehicle Speed PID 01 0D · {0}", "Real ECU reply · Vehicle Speed PID 01 0D · {0}"),
        ("{0} — {1} წმ · {2}", "{0} — {1} s · {2}"),
        ("{0} ხელმისაწვდომია მხოლოდ აქტიური PRO ვერსიით.", "{0} is available only with an active PRO version."),
        ("ECU-მ {0} რეალური Mode 05 item დააბრუნა.", "The ECU returned {0} real Mode 05 items."),
        ("ბოლო შემოწმება: {0}", "Last check: {0}"),
        ("ECU მონაცემთა წყარო: Generic OBD-II · {0}", "ECU data source: Generic OBD-II · {0}"),
        ("აქტიური DTC: {0} — ნახე AI-ის სავარაუდო მიზეზები ქვემოთ", "Active DTC: {0} — see the AI's likely causes below"),
        ("Freeze Frame: {0} რეალური მნიშვნელობა · Trigger: {1}", "Freeze Frame: {0} real values · Trigger: {1}"),
        ("Live Data: {0} რეალური PID snapshot-დან", "Live Data: {0} from a real PID snapshot"),
        ("ბოლო AI სკანი: {0}", "Last AI scan: {0}"),
        ("OBD სკანი წარმატებით დასრულდა, მაგრამ AI პასუხი ამჯერად ვერ მივიღეთ. {0}", "The OBD scan finished successfully, but we could not get an AI answer this time. {0}"),
        ("მიზეზი: {0}", "Reason: {0}"),
        ("თქვენ: {0}", "You: {0}"),
        ("Idle შედეგი: {0} {1} {2}. მეორე წაკითხვა გაუქმდა.", "Idle result: {0} {1} {2}. The second reading was cancelled."),
        ("Idle: {0} {1} {2}. 2500 RPM-ზე ECU-მ მნიშვნელობა არ დააბრუნა.", "Idle: {0} {1} {2}. The ECU did not return a value at 2500 RPM."),
        ("Guided Test ვერ შესრულდა: {0}", "Guided Test failed: {0}"),
        ("Repair Verification ვერ დასრულდა: {0}", "Repair Verification did not finish: {0}"),
        ("ავტომობილი: {0} {1} · {2} · საწვავი: {3}", "Vehicle: {0} {1} · {2} · fuel: {3}"),
        ("რეალური Fuel Trim · Idle: {0} {1} · ~2500 RPM: {2} {3}.", "Real Fuel Trim · Idle: {0} {1} · ~2500 RPM: {2} {3}."),
        ("{0}: ადრე {1} {2} → ახლა {3} {4}", "{0}: before {1} {2} → now {3} {4}"),
        ("ECU-მ დაადასტურა {0} PID · რეალურად წაკითხულია {1} სენსორი · მიუწვდომელი პასუხი: {2}.", "The ECU confirmed {0} PIDs · {1} sensors actually read · unavailable replies: {2}."),
        ("თარიღი: {0}\nProtocol: {1}", "Date: {0}\nProtocol: {1}"),
        ("თარიღი: {0}", "Date: {0}"),
        ("წინა სკანი: {0}", "Previous scan: {0}"),
        ("მიმდინარე: {0}", "Current: {0}"),
        ("PDF ექსპორტი ვერ შესრულდა: {0}", "PDF export failed: {0}"),
        ("გაზიარება ვერ შესრულდა: {0}", "Sharing failed: {0}"),
        ("შენახულია: {0} · ეს ანგარიში ეფუძნება დასრულებულ Generic OBD-II სკანს.", "Saved: {0} · this report is based on a finished Generic OBD-II scan."),
        ("Generic OBD-II Powertrain ECU: პასუხობს\nმხარდაჭერილი PID: {0} · Snapshot: {1}/{2}", "Generic OBD-II Powertrain ECU: responds\nSupported PIDs: {0} · Snapshot: {1}/{2}"),
        ("მხარდაჭერილი PID: {0} · Snapshot: {1}/{2}", "Supported PIDs: {0} · Snapshot: {1}/{2}"),
        ("Trigger: {0} · {1} რეალური მნიშვნელობა", "Trigger: {0} · {1} real values"),
        ("ECU-მ {0} რეალური Mode 06 item დააბრუნა.", "The ECU returned {0} real Mode 06 items."),
        ("MODE 06 — {0} item ზღვარს გარეთაა", "MODE 06 — {0} items out of range"),
        ("კრიტიკული: {0}    გაფრთხილება: {1}", "Critical: {0}    Warning: {1}"),
        ("დაფარვა: {0}% · სენსორი: {1} · ECU მოდული: {2}", "Coverage: {0}% · sensor: {1} · ECU module: {2}"),
        ("შემოწმდა {0} · შენახული კადრი არ არის", "Checked {0} · no stored frame"),
        ("წაკითხულია {0} · {1} მნიშვნელობა", "Read {0} · {1} values"),
        ("ტესტი ვერ შესრულდა: {0}", "Test failed: {0}"),
        ("{0} PID/s (აქტიური Live Data სესია)", "{0} PID/s (active Live Data session)"),
        ("ECU პასუხის დრო (0100): {0}\nConnection quality: {1}\n{2}\nLive Data rate: {3}", "ECU reply time (0100): {0}\nConnection quality: {1}\n{2}\nLive Data rate: {3}"),
        ("ECU პასუხის დრო (0100): {0}", "ECU reply time (0100): {0}"),
        ("{0} პროფილი არჩეულია — Wi-Fi OBD ადაპტერი მზად არის.", "{0} profile selected — the Wi-Fi OBD adapter is ready."),
        ("{0} პროფილი არჩეულია — OBD-II ადაპტერი მზად არის.", "{0} profile selected — the OBD-II adapter is ready."),
        ("{0} პროფილი არჩეულია. ჩართე Bluetooth, ჩართე მანქანის კონტაქტი და მოძებნე OBD-II ადაპტერი.", "{0} profile selected. Turn on Bluetooth, turn on the car's ignition and search for the OBD-II adapter."),
        ("ვუკავშირდები Wi-Fi OBD ადაპტერს {0}:{1}…", "Connecting to the Wi-Fi OBD adapter {0}:{1}…"),
        ("ნაპოვნია {0} OBD-II ადაპტერი.", "{0} OBD-II adapters found."),
        ("ვუკავშირდები {0}…", "Connecting to {0}…"),
        ("დაკავშირებულია: {0}. მიმდინარეობს OBD ინიციალიზაცია…", "Connected: {0}. OBD initialization in progress…"),
        ("რეალურ დროში ახლდება · {0}", "Updating in real time · {0}"),
        ("{0}: ნაჩვენებია მხოლოდ რეალურად დაბრუნებული reviewed DID მნიშვნელობები.", "{0}: only reviewed DID values actually returned are shown."),
        ("{0}: რეალური OEM მნიშვნელობა ჯერ არ დაბრუნებულა.", "{0}: no real OEM value returned yet."),
        ("PID 01{0} ამ ECU-ზე მიუწვდომელია.", "PID 01{0} is unavailable on this ECU."),
        ("PID 01{0} არჩეულია Live Data-სთვის.", "PID 01{0} selected for Live Data."),
        ("შენახულია: {0} მონაცემი · {1}", "Saved: {0} data points · {1}"),
        ("ჩაწერა დასრულდა და მოწყობილობაზე შეინახა: {0} მონაცემი.", "Recording finished and was saved on the device: {0} data points."),
        ("კოდის ახსნა: {0}", "Code explanation: {0}"),
        ("READY — დრო ავტომატურად დაიწყება {0} km/h-ის გადაკვეთისას.", "READY — timing starts automatically when passing {0} km/h."),
        ("ტესტის დასაწყებად ჯერ {0} km/h-მდე ან ქვემოთ ჩამოდი.", "To start the test, first slow down to {0} km/h or below."),
        ("GO! — {0} იზომება რეალური ECU სიჩქარით…", "GO! — {0} is measured with the real ECU speed…"),
        ("{0}: {1} წმ (OBD შეფასება)", "{0}: {1} s (OBD estimate)"),
        ("სენსორი: {0}", "Sensor: {0}"),
        ("{0} მონაცემი მიუწვდომელია.", "{0} data unavailable."),
        ("Manufacturer catalog ვერ ჩაიტვირთა: {0}", "Manufacturer catalog could not be loaded: {0}"),
        ("ECU პასუხობს · {0} ms", "ECU responds · {0} ms"),
        ("ECU პასუხობს · {0} ms", "ECU responds · {0} ms"),
        ("ECU პასუხობს · {0} დეკოდირებადი სტანდარტული PID · {1}", "ECU responds · {0} decodable standard PIDs · {1}"),
        ("{0} OEM definition არ გაიხსნება.", "{0} OEM definition will not open."),
        ("{0} არჩეულია, მაგრამ ამ მოდელის/წლისთვის დადასტურებული ECU მისამართი არ არის.", "{0} is selected, but there is no confirmed ECU address for this model/year."),
        ("{0} დადასტურებული ECU მისამართი მოიძებნა; ECU probe აუცილებელია აქტივაციამდე.", "{0} confirmed ECU addresses found; an ECU probe is required before activation."),
        ("რეალურად აღმოჩენილი OEM ECU: {0}.", "OEM ECUs actually found: {0}."),
        ("ECU პასუხით დადასტურებული OEM Live Data command: {0}.", "OEM Live Data commands confirmed by the ECU reply: {0}."),
        ("ამ კავშირზე ახლა შესრულებადი reviewed Service/Active Test: {0}.", "Reviewed Service/Active Tests runnable now on this connection: {0}."),
        ("ამ კავშირზე ახლა შესრულებადი reviewed Coding operation: {0}.", "Reviewed Coding operations runnable now on this connection: {0}."),
        ("{0} VIN-ით დადასტურდა.", "{0} confirmed by VIN."),
        ("VIN-ით დადასტურდა: {0}.", "Confirmed by VIN: {0}."),
        ("არჩეულია {0}, მაგრამ VIN მიუთითებს {1}-ზე.", "{0} is selected, but the VIN points to {1}."),
        ("{0} მხოლოდ მომხმარებლის არჩევანია — VIN-ით ჯერ არ დადასტურებულა.", "{0} is only the user's choice — not confirmed by VIN yet."),
        ("{0} მხარდაჭერილი readiness მონიტორი დაუსრულებელია.", "{0} supported readiness monitors are not complete."),
        ("შემოწმდა {0} სენსორი და {1} პასუხმგებელი ECU მოდული. ქულა მხოლოდ აღმოჩენილ პრობლემებს ასახავს; მიუწვდომელ შემოწმებებს ქულა არ დაუკლია.", "{0} sensors and {1} responding ECU modules were checked. The score reflects only problems found; unavailable checks did not lower the score."),
        ("STFT {0}% + LTFT {1}% (ჯამური მოდული {2}%).", "STFT {0}% + LTFT {1}% (total magnitude {2}%)."),
        ("არჩეული პროფილი: {0} — VIN-ით დადასტურდება", "Selected profile: {0} — will be confirmed by VIN"),
        ("{0} პროფილი VIN-ით დადასტურდა", "{0} profile confirmed by VIN"),
        ("VIN მიუთითებს: {0} — მარკის გადარჩევა რეკომენდებულია", "The VIN points to: {0} — choosing the make again is recommended"),
        ("DTC პასუხში {0} header ვერ მოიძებნა: '{1}'.", "Header {0} not found in the DTC reply: '{1}'."),
        ("DTC პასუხი hex მონაცემებს არ შეიცავს: '{0}'.", "The DTC reply contains no hex data: '{0}'."),
        ("{0} OEM DTC snapshot record წაკითხულია.", "{0} OEM DTC snapshot records read."),
        ("OEM Freeze Frame: {0}/{1} ECU-მ snapshot მოთხოვნას უპასუხა.", "OEM Freeze Frame: {0}/{1} ECUs answered the snapshot request."),
        ("{0} OEM ECU მოთხოვნა არ გაიგზავნა.", "{0} OEM ECU requests were not sent."),
        ("{0}-ისთვის ამ მოდელის/წლის დადასტურებული ECU მისამართები ჯერ არ არის.", "There are no confirmed ECU addresses for this model/year of {0} yet."),
        ("{0} reviewed OEM DID მზადაა რეალურ დროში წასაკითხად.", "{0} reviewed OEM DIDs are ready to read in real time."),
        ("OEM Live Data: {0}/{1} DID პასუხობს · {2} მნიშვნელობა.", "OEM Live Data: {0}/{1} DIDs answer · {2} values."),
        ("{0} OEM DTC ჩანაწერი წაკითხულია.", "{0} OEM DTC records read."),
        ("OEM DTC reader: {0}/{1} ECU წარმატებით წაიკითხა.", "OEM DTC reader: {0}/{1} ECUs read successfully."),
        ("ტესტის შედეგი არ არსებობს: {0}.", "No test result: {0}."),
        ("CAN transport დადასტურებულია ({0}).", "CAN transport confirmed ({0})."),
        ("{0} OEM წვდომა დაიშვება მხოლოდ კონკრეტული მარკა/მოდელი/წლისთვის დადასტურებული read-only ECU პროფილით.", "{0} OEM access is allowed only with a read-only ECU profile confirmed for the specific make/model/year."),
        ("აქტიური პროტოკოლი: {0}.", "Active protocol: {0}."),
        ("OBD2 ადაპტერმა არ უპასუხა: {0}", "The OBD2 adapter did not answer: {0}"),
        ("მანქანის მიმდინარე odometer: {0} km", "The car's current odometer: {0} km"),
        ("დაშვებული განსხვავება: {0}", "Allowed difference: {0}"),
        ("მაქსიმალური სხვაობა: {0}", "Maximum difference: {0}"),
        ("წყაროები შემოწმდა: {0} · {1}", "Sources checked: {0} · {1}"),
        ("სკანი: {0}", "Scan: {0}"),
        ("სიჩქარე დაეცა — ცდა თავიდან დაიწყება {0} km/h-ის გადაკვეთისას.", "Speed dropped — the attempt restarts when passing {0} km/h."),
        ("{0}: {1} წმ — შენახვა ვერ მოხერხდა.", "{0}: {1} s — could not be saved."),
        ("{0}: {1} წმ", "{0}: {1} s"),
        ("მოდულების შემოწმება: {0} ({1}/{2})", "Checking modules: {0} ({1}/{2})"),
        ("შემოწმდა {0} მისამართი · უპასუხა {1} მოდულმა · შეცდომები მოდულებში: {2}", "Modules checked: {0} · answered: {1} · faults in modules: {2}"),
    });

    private static readonly (Regex Pattern, string Target)[] UiEnToKaTemplates = BuildTemplates(new (string, string)[]
    {
        ("ARM {0} TEST", "{0} ტესტის დაწყება"),
        ("Start {0} run", "{0} გაზომვის დაწყება"),
        ("Supported: {0} standard parameters", "მხარდაჭერილი: {0} სტანდარტული პარამეტრი"),
    });

    private static (Regex Pattern, string Target)[] BuildTemplates((string Source, string Target)[] items)
    {
        var result = new List<(Regex, string)>(items.Length);
        foreach (var (source, target) in items)
        {
            var pattern = Regex.Escape(source);
            for (var i = 0; i < 10; i++)
                pattern = pattern.Replace("\\{" + i + "}", "(.+?)");
            result.Add((new Regex("^" + pattern + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant), target));
        }

        return result.ToArray();
    }

    private static string? TranslateTemplate(string text, (Regex Pattern, string Target)[] templates)
    {
        foreach (var (pattern, target) in templates)
        {
            var match = pattern.Match(text);
            if (!match.Success)
                continue;

            var values = match.Groups.Cast<Group>().Skip(1).Select(group => (object)(_current?.TranslateWord(group.Value) ?? group.Value)).ToArray();
            try
            {
                return string.Format(CultureInfo.InvariantCulture, target, values);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return null;
    }

    private static readonly Regex GeorgianLetters = new("[\u10A0-\u10FF]", RegexOptions.Compiled);

    // =========================================================
    // CURRENT LANGUAGE
    // =========================================================

    /// <summary>
    /// "Not Georgian". True for English AND for every added language (Türkçe, Deutsch, Polski, Español,
    /// Português, Français, Italiano): all existing "IsEnglish ? english : georgian" code picks the English
    /// text, and <see cref="Translate"/> layers the selected language's pack on top of that English text.
    /// </summary>
    public bool IsEnglish => LanguageCatalog.UsesEnglishBase;

    public bool IsGeorgian => !IsEnglish;

    public AppLanguage Language => LanguageCatalog.Current;

    /// <summary>Native name of the selected language, exactly as stored in Preferences.</summary>
    public string CurrentLanguage => Language.DisplayName;

    public string CurrentLanguageCode => Language.Code;

    public string CurrentFlag => Language.Flag;


    // =========================================================
    // TRANSLATE
    // =========================================================

    /// <summary>Translates a text for dialogs etc. with the current language.</summary>
    public static string T(string? text) =>
        text is null ? string.Empty : _current?.Translate(text) ?? text;

    // Translate() runs for every Label/Button text that is drawn, including
    // live data refreshed several times per second. Results are cached per
    // language (bounded; cleared on language change).
    private const int TranslateCacheLimit = 4000;
    private const int TranslateCacheMaxTextLength = 600;
    private readonly object _translateCacheLock = new();
    private readonly Dictionary<string, string> _translateCache = new(StringComparer.Ordinal);
    private string? _translateCacheLanguage;

    /// <summary>True when the text has at least one letter (\p{L}); numbers/units/punctuation only → false.</summary>
    private static bool HasLetter(string text)
    {
        foreach (var c in text)
            if (char.IsLetter(c))
                return true;
        return false;
    }

    private void ClearTranslateCache()
    {
        lock (_translateCacheLock)
        {
            _translateCache.Clear();
            _translateCacheLanguage = null;
        }
    }

    public string Translate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Numbers, units-only symbols, dashes, percentages: nothing to translate.
        if (!HasLetter(text))
            return text;

        var language = Language;
        var languageCode = language.Code;
        lock (_translateCacheLock)
        {
            if (!string.Equals(_translateCacheLanguage, languageCode, StringComparison.Ordinal))
            {
                // Language changed (also when changed outside SetLanguage).
                _translateCache.Clear();
                _translateCacheLanguage = languageCode;
            }

            if (_translateCache.TryGetValue(text, out var cached))
                return cached;
        }

        var result = TranslateUncached(text);

        // Added languages: the text is English at this point (Georgian source was translated above,
        // English source stays as is); the language pack keyed by English is applied on top.
        if (languageCode is not ("ka" or "en"))
            result = LocalizeFromEnglish(result, language);

        if (text.Length <= TranslateCacheMaxTextLength)
        {
            lock (_translateCacheLock)
            {
                if (string.Equals(_translateCacheLanguage, languageCode, StringComparison.Ordinal))
                {
                    if (_translateCache.Count >= TranslateCacheLimit)
                        _translateCache.Clear();
                    _translateCache[text] = result;
                }
            }
        }

        return result;
    }

    private string TranslateUncached(string text)
    {
        var whole = TranslateExact(text);
        if (!string.Equals(whole, text, StringComparison.Ordinal))
            return whole;

        // Only text in the other language can need a partial translation.
        if (IsEnglish ? !GeorgianLetters.IsMatch(text) : !LatinWord.IsMatch(text))
            return text;

        if (text.IndexOf('\n') >= 0)
        {
            var lines = text.Split('\n');
            var changed = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var translated = TranslateLine(line.TrimEnd('\r'));
                if (!string.Equals(translated, line.TrimEnd('\r'), StringComparison.Ordinal))
                {
                    lines[i] = line.EndsWith('\r') ? translated + "\r" : translated;
                    changed = true;
                }
            }

            return changed ? string.Join('\n', lines) : text;
        }

        return TranslateLine(text);
    }

    /// <summary>English → selected added language: whole text, then per line, then like TranslateLine.</summary>
    private static string LocalizeFromEnglish(string english, AppLanguage language)
    {
        if (string.IsNullOrWhiteSpace(english))
            return english;

        if (LanguagePacks.TryTranslate(language, english, out var whole))
            return whole;

        if (english.IndexOf('\n') < 0)
            return LocalizePackLine(english, language);

        var lines = english.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var hadCr = line.EndsWith('\r');
            var translated = LocalizePackLine(line.TrimEnd('\r'), language);
            lines[i] = hadCr ? translated + "\r" : translated;
        }

        return string.Join('\n', lines);
    }

    private static string LocalizePackLine(string line, AppLanguage language)
    {
        if (string.IsNullOrWhiteSpace(line))
            return line;

        if (LanguagePacks.TryTranslate(language, line, out var exact))
            return exact;

        // "⚠ Text", "• Text", "1. Text" …
        var prefix = LeadingSymbols.Match(line);
        if (prefix.Success && prefix.Length < line.Length &&
            LanguagePacks.TryTranslate(language, line.Substring(prefix.Length), out var rest))
        {
            return prefix.Value + rest;
        }

        // "Mileage:" / "Mileage: 123 km"
        var colon = line.IndexOf(':');
        if (colon > 0 &&
            LanguagePacks.TryTranslate(language, line.Substring(0, colon), out var label))
        {
            return label + line.Substring(colon);
        }

        if (line.Contains(" · ", StringComparison.Ordinal))
        {
            var parts = line.Split(" · ");
            for (var i = 0; i < parts.Length; i++)
                parts[i] = LocalizePackLine(parts[i], language);
            return string.Join(" · ", parts);
        }

        return line;
    }

    private static readonly Regex LatinWord = new("[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex LeadingSymbols = new(@"^[^\p{L}\p{N}{(]+", RegexOptions.Compiled);

    /// <summary>One line: exact, then without a leading icon/bullet, then " · " parts.</summary>
    private string TranslateLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return line;

        var exact = TranslateExact(line);
        if (!string.Equals(exact, line, StringComparison.Ordinal))
            return exact;

        var prefix = LeadingSymbols.Match(line);
        if (prefix.Success && prefix.Length < line.Length)
        {
            var rest = line.Substring(prefix.Length);
            var translatedRest = TranslateExact(rest);
            if (!string.Equals(translatedRest, rest, StringComparison.Ordinal))
                return prefix.Value + translatedRest;
        }

        if (line.Contains(" · ", StringComparison.Ordinal))
        {
            var parts = line.Split(" · ");
            var changed = false;
            for (var i = 0; i < parts.Length; i++)
            {
                var translated = TranslateLine(parts[i]);
                if (!string.Equals(translated, parts[i], StringComparison.Ordinal))
                {
                    parts[i] = translated;
                    changed = true;
                }
            }

            if (changed)
                return string.Join(" · ", parts);
        }

        return line;
    }

    /// <summary>Dictionary-only lookup for a value inside a template (e.g. a feature name).</summary>
    private string TranslateWord(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (IsEnglish)
        {
            if (!GeorgianLetters.IsMatch(value))
                return value;
            if (MotoKaToEn.TryGetValue(value, out var moto)) return moto;
            if (ExtendedKaToEn.TryGetValue(value, out var extended)) return extended;
            if (KaToEn.TryGetValue(value, out var en)) return en;
            if (UiKaToEn.TryGetValue(value, out var uiEn)) return uiEn;
            return LookupExtraEnglish(value) ?? value;
        }

        if (EnToKa.TryGetValue(value, out var ka)) return ka;
        if (UiEnToKa.TryGetValue(value, out var uiKa)) return uiKa;
        return LookupExtraGeorgian(value) ?? value;
    }

    private string TranslateExact(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        if (text.StartsWith("{") ||
            text.StartsWith(
                "http",
                StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        if (IsEnglish)
        {
            if (MotoKaToEn.TryGetValue(
                    text,
                    out var motoEn))
            {
                return motoEn;
            }

            if (ExtendedKaToEn.TryGetValue(text, out var extended))
                return extended;

            if (KaToEn.TryGetValue(text, out var en))
                return en;

            if (UiKaToEn.TryGetValue(text, out var uiEn))
                return uiEn;

            // Only Georgian text can need an English template.
            if (!GeorgianLetters.IsMatch(text))
                return text;

            return LookupExtraEnglish(text)
                ?? TranslateTemplate(text, UiKaToEnTemplates)
                ?? TranslateTemplate(text, ExtraToEnglishTemplates.Value)
                ?? text;
        }

        if (EnToKa.TryGetValue(text, out var ka))
            return ka;

        if (UiEnToKa.TryGetValue(text, out var uiKa))
            return uiKa;

        if (GeorgianLetters.IsMatch(text))
            return text;

        return LookupExtraGeorgian(text)
            ?? TranslateTemplate(text, UiEnToKaTemplates)
            ?? TranslateTemplate(text, ExtraToGeorgianTemplates.Value)
            ?? text;
    }

    // =========================================================
    // DISPLAY TRANSLATION (every Label / Button, also bound texts)
    // =========================================================
    //
    // View models set many texts from code. Instead of replacing the bound
    // value (which would break the binding), only the text drawn on screen
    // is translated. Only exact dictionary/template matches are changed, so
    // VINs, codes, numbers etc. are never touched.

    private static LocalizationService? _current;

    public static void RegisterDisplayTranslation()
    {
        // OBD definitions/records (platform-independent) ask the language through this hook.
        // Every language except Georgian uses the English texts as its base.
        CarScanner.Services.OBD.ObdLang.IsEnglishProvider = static () => LanguageCatalog.UsesEnglishBase;

#if ANDROID
        Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping(nameof(Label.Text), (handler, view) =>
        {
            if (view is Label { TextType: TextType.Text, FormattedText: null } label &&
                handler.PlatformView is Android.Widget.TextView textView &&
                !string.IsNullOrWhiteSpace(label.Text))
            {
                var translated = _current?.Translate(label.Text) ?? label.Text;
                if (!string.Equals(translated, label.Text, StringComparison.Ordinal))
                    textView.Text = translated;
            }
        });

        Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping(nameof(Button.Text), (handler, view) =>
        {
            if (view is Button button &&
                handler.PlatformView is Android.Widget.TextView textView &&
                !string.IsNullOrWhiteSpace(button.Text))
            {
                var translated = _current?.Translate(button.Text) ?? button.Text;
                if (!string.Equals(translated, button.Text, StringComparison.Ordinal))
                    textView.Text = translated;
            }
        });
#elif IOS || MACCATALYST
        // Same display-only translation as Android: bound texts written in
        // Georgian by view models are shown in English when English is chosen.
        Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping(nameof(Label.Text), (handler, view) =>
        {
            if (view is Label { TextType: TextType.Text, FormattedText: null } label &&
                handler.PlatformView is UIKit.UILabel uiLabel &&
                !string.IsNullOrWhiteSpace(label.Text))
            {
                var translated = _current?.Translate(label.Text) ?? label.Text;
                if (!string.Equals(translated, label.Text, StringComparison.Ordinal))
                    uiLabel.Text = translated;
            }
        });

        Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping(nameof(Button.Text), (handler, view) =>
        {
            if (view is Button button &&
                handler.PlatformView is UIKit.UIButton uiButton &&
                !string.IsNullOrWhiteSpace(button.Text))
            {
                var translated = _current?.Translate(button.Text) ?? button.Text;
                if (!string.Equals(translated, button.Text, StringComparison.Ordinal))
                    uiButton.SetTitle(translated, UIKit.UIControlState.Normal);
            }
        });
#endif
    }


    // =========================================================
    // SET LANGUAGE
    // =========================================================

    public void SetLanguage(string language)
    {
        // Accepts a display name ("Deutsch"), code ("de") or English name ("German");
        // the native display name is stored, so "English" and "ქართული" stay exactly as before.
        Preferences.Default.Set(
            LanguageKey,
            LanguageCatalog.Find(language).DisplayName);

        ClearTranslateCache();

        LanguageChanged?.Invoke(
            this,
            EventArgs.Empty);

        ApplyCurrentPage();
    }


    // =========================================================
    // APPLY
    // =========================================================

    public void ApplyCurrentPage()
    {
        var page =
            Shell.Current?.CurrentPage
            ?? Application.Current?
                .Windows
                .FirstOrDefault()?
                .Page;

        if (page != null)
        {
            Apply(page);
        }

        // The header (GCar360 + car type / OBD status) lives in the Shell title view,
        // outside the page tree, so it is redrawn separately.
        if (Shell.Current is { } shell)
        {
            if (Shell.GetTitleView(shell) is View shellTitle)
                ApplyNode(shellTitle);
            if (page is not null && Shell.GetTitleView(page) is View pageTitle)
                ApplyNode(pageTitle);
        }
    }


    public void Apply(Element root)
    {
        // Text only. The theme is applied separately (AppShell navigation,
        // Settings, OS theme change); ThemeService.Apply is cheap but not needed here.
        ApplyNode(root);
    }


    private void ApplyNode(Element node)
    {
        try
        {
            ApplyNodeCore(node);
        }
        catch (Exception ex)
        {
            // A single unusual control must never break (or close) a page.
            System.Diagnostics.Debug.WriteLine($"Localization skipped {node.GetType().Name}: {ex.Message}");
        }
    }

    private void ApplyNodeCore(Element node)
    {
        // VIN Check pages translate their own texts (VinText); their labels
        // are bound, so they must not be overwritten here.
        if (node is Page vinPage &&
            vinPage.GetType().Namespace?.StartsWith("CarScanner.VinCheck", StringComparison.Ordinal) == true)
        {
            return;
        }

        switch (node)
        {
            case Page page when !string.IsNullOrWhiteSpace(page.Title):
                page.Title = TranslateTracked(page, page.Title);
                break;

            // Labels and buttons are never rewritten. Their Text stays what XAML or the
            // view model set (so bindings keep working and a later visit can never bring
            // back an old language); only the text drawn on screen is translated by the
            // handler mappers in RegisterDisplayTranslation. Here they are just redrawn.
            case Label label:
                if (label.FormattedText is { } formatted)
                {
                    foreach (var span in formatted.Spans)
                        if (!string.IsNullOrWhiteSpace(span.Text))
                            span.Text = TranslateTracked(span, span.Text);
                }
                else
                {
                    RedrawText(label, nameof(Label.Text));
                }
                break;

            case Button button:
                RedrawText(button, nameof(Button.Text));
                break;

            case SearchBar searchBar when !string.IsNullOrWhiteSpace(searchBar.Placeholder):
                searchBar.Placeholder = TranslateTracked(searchBar, searchBar.Placeholder);
                break;

            case Entry entry when !string.IsNullOrWhiteSpace(entry.Placeholder):
                entry.Placeholder = TranslateTracked(entry, entry.Placeholder);
                break;

            case Editor editor when !string.IsNullOrWhiteSpace(editor.Placeholder):
                editor.Placeholder = TranslateTracked(editor, editor.Placeholder);
                break;
        }

        // Walk everything that is on screen: layouts, borders, scroll views,
        // collection/bindable-layout items, refresh/swipe views, content views …
        if (node is not IVisualTreeElement tree)
            return;

        foreach (var child in tree.GetVisualChildren().OfType<Element>().ToList())
            ApplyNode(child);
    }

    private static void RedrawText(View view, string property)
    {
        if (view.Handler is null)
            return;

        view.Handler.UpdateValue(property);
        (view as IView).InvalidateMeasure();
    }

    /// <summary>
    /// Translates a value this service owns (page title, placeholder). If code or a
    /// binding changed it since the last pass, the new value becomes the source.
    /// </summary>
    private string TranslateTracked(Element element, string current)
    {
        var state = _originalTexts.GetValue(element, _ => new OriginalText { Value = current });
        if (state.Applied is not null && !string.Equals(current, state.Applied, StringComparison.Ordinal))
            state.Value = current;

        var translated = Translate(state.Value);
        state.Applied = translated;
        return translated;
    }
}
