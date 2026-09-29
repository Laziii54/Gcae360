using CarScanner.Services.Localization;

namespace CarScanner.VinCheck.Services;

/// <summary>
/// Georgian / English texts of the VIN Check section. The language is the
/// same one the whole app uses (LocalizationService).
/// </summary>
public static class VinText
{
    private static readonly Dictionary<string, (string En, string Ka)> Text = new()
    {
        ["VinHistory"] = ("VIN Check", "VIN შემოწმება"),
        ["Subtitle"] = ("Check vehicle history before buying", "შეამოწმეთ ავტომობილის ისტორია შეძენამდე"),
        ["EnterVin"] = ("Enter 17-character VIN", "შეიყვანეთ 17-სიმბოლოიანი VIN"),
        ["CheckVin"] = ("Check VIN", "VIN-ის შემოწმება"),
        ["Searching"] = ("Searching vehicle history...", "მიმდინარეობს ავტომობილის ისტორიის მოძიება..."),
        ["VinLength"] = ("VIN must contain exactly 17 characters.", "VIN ზუსტად 17 სიმბოლოს უნდა შეიცავდეს."),
        ["VinInvalidChars"] = ("VIN contains invalid characters (I, O and Q are not allowed).", "VIN შეიცავს დაუშვებელ სიმბოლოებს (I, O და Q არ გამოიყენება)."),
        ["History"] = ("Search history", "ძიების ისტორია"),
        ["Favorites"] = ("Favorites", "რჩეულები"),
        ["VehicleSummary"] = ("Vehicle Summary", "ავტომობილის მიმოხილვა"),
        ["ImportantHistory"] = ("Important History", "მნიშვნელოვანი ისტორია"),
        ["AuctionHistory"] = ("Auction History", "აუქციონის ისტორია"),
        ["AccidentHistory"] = ("Accident History", "ავარიების ისტორია"),
        ["MileageHistory"] = ("Mileage History", "გარბენის ისტორია"),
        ["TitleHistory"] = ("Title History", "ტიტულის ისტორია"),
        ["OwnershipHistory"] = ("Ownership History", "მფლობელობის ისტორია"),
        ["TheftInfo"] = ("Theft Information", "ქურდობის ინფორმაცია"),
        ["ServiceHistory"] = ("Service History", "სერვისის ისტორია"),
        ["Recalls"] = ("Recalls", "გაწვევები"),
        ["Specifications"] = ("Vehicle Specifications", "ავტომობილის მონაცემები"),
        ["CopyVin"] = ("Copy VIN", "VIN-ის კოპირება"),
        ["Share"] = ("Share", "გაზიარება"),
        ["Refresh"] = ("Refresh", "განახლება"),
        ["ClearHistory"] = ("Clear History", "ისტორიის გასუფთავება"),
        ["NoHistory"] = ("No history records were found for this vehicle.", "ამ ავტომობილის ისტორიის ჩანაწერები ვერ მოიძებნა."),
        ["NoInternet"] = ("No internet connection.", "ინტერნეტთან კავშირი არ არის."),
        ["NotFound"] = ("Vehicle not found.", "ავტომობილი ვერ მოიძებნა."),
        ["Unavailable"] = ("VIN service temporarily unavailable.", "VIN სერვისი დროებით მიუწვდომელია."),
        ["TooMany"] = ("Too many requests. Please try again later.", "ძალიან ბევრი მოთხოვნაა. მოგვიანებით სცადეთ."),
        ["VinCopied"] = ("VIN copied", "VIN დაკოპირდა"),
        ["PossibleMileage"] = ("Possible mileage inconsistency", "შესაძლო გარბენის შეუსაბამობა"),
        ["SampleHint"] = ("The VIN is in the registration certificate and at the bottom corner of the windshield. With the car connected it is filled in automatically.", "VIN წერია ტექპასპორტში და საქარე მინის ქვედა კუთხეში. მანქანასთან დაკავშირებისას თავისით ჩაიწერება."),
        ["VinCode"] = ("VIN code", "VIN კოდი"),
        ["TapHint"] = ("Tap the boxes to type, or the camera to scan the VIN.", "შეეხე უჯრებს ჩასაწერად, ან კამერას — VIN-ს თავად წაიკითხავს."),
        ["Clear"] = ("Clear", "გასუფთავება"),
        ["FeatPhotos"] = ("Auction photos", "აუქციონის ფოტოები"),
        ["FeatDamage"] = ("Damage", "დაზიანება"),
        ["FeatMileage"] = ("Mileage", "გარბენი"),
        ["FeatTitle"] = ("Title", "ტიტული"),
        ["FeatRecalls"] = ("Recalls", "გაწვევები"),
        ["ScanTitle"] = ("Scan VIN", "VIN-ის სკანირება"),
        ["ScanHint"] = ("Point the camera at the VIN: bottom corner of the windshield, the door frame sticker or the registration paper. Keep the VIN inside the frame.", "მიმართე კამერა VIN-ს: საქარე მინის ქვედა კუთხე, კარის ჩარჩოს სტიკერი ან ტექპასპორტი. VIN ჩარჩოში მოაქციე."),
        ["ScanLooking"] = ("Looking for the VIN…", "ვეძებ VIN-ს…"),
        ["ScanOpenSettings"] = ("Allow camera in settings", "კამერის ნებართვა პარამეტრებში"),
        ["ScanNoPermission"] = ("Camera permission is needed to scan the VIN.", "VIN-ის სკანირებას კამერის ნებართვა სჭირდება."),
        ["ScanCameraError"] = ("The camera could not be started.", "კამერა ვერ ჩაირთო."),
        ["ScanUnsupported"] = ("VIN scanning is not available on this device.", "ამ მოწყობილობაზე VIN-ის სკანირება მიუწვდომელია."),
        ["Language"] = ("ქართული", "English"),
        ["Report"] = ("VIN REPORT", "VIN ანგარიში"),
        ["Make"] = ("MAKE", "მარკა"),
        ["Model"] = ("MODEL", "მოდელი"),
        ["Year"] = ("YEAR", "წელი"),
        ["Trim"] = ("TRIM", "კომპლექტაცია"),
        ["Engine"] = ("ENGINE", "ძრავი"),
        ["Fuel"] = ("FUEL", "საწვავი"),
        ["Transmission"] = ("TRANSMISSION", "გადაცემათა კოლოფი"),
        ["Drivetrain"] = ("DRIVETRAIN", "წამყვანი თვლები"),
        ["Lot"] = ("Lot", "ლოტი"),
        ["Sale"] = ("Sale", "გაყიდვა"),
        ["Primary"] = ("Primary", "ძირითადი დაზიანება"),
        ["Secondary"] = ("Secondary", "მეორადი დაზიანება"),
        ["Odometer"] = ("Odometer", "გარბენი"),
        ["Title"] = ("Title", "ტიტული"),
        ["Severity"] = ("Severity", "სიმძიმე"),
        ["Remedy"] = ("Remedy", "გამოსწორება"),
        ["Open"] = ("Open", "გახსნა"),
        ["Yes"] = ("Yes", "დიახ"),
        ["No"] = ("No", "არა"),

        // Important history summary
        ["AlertAuction"] = ("Auction records", "აუქციონის ჩანაწერები"),
        ["AlertAuctionFound"] = ("{0} auction record(s) found", "ნაპოვნია {0} აუქციონის ჩანაწერი"),
        ["NoAuctionTitle"] = ("Not found on US auctions", "აშშ-ის აუქციონებზე არ მოიძებნა"),
        ["NoAuctionHint"] = ("Copart and IAAI have no record of this VIN. That does not prove the car was never damaged: it may have come from Europe or been sold privately.", "Copart-სა და IAAI-ზე ამ VIN-ის ჩანაწერი არ არის. ეს არ ნიშნავს, რომ მანქანა დაუზიანებელია: შეიძლება ევროპიდან იყოს ჩამოყვანილი ან აუქციონის გარეშე გაყიდული."),
        ["RecallHint"] = ("Free manufacturer repair campaigns for this model. Ask a dealer whether they were done on this car.", "მწარმოებლის უფასო შეკეთების კამპანიები ამ მოდელზე. დილერთან გადაამოწმე, გაკეთებულია თუ არა ამ მანქანაზე."),
        ["ShowAll"] = ("Show all", "ყველას ნახვა"),
        ["ShowLess"] = ("Show less", "ნაკლები"),
        ["PhotoCount"] = ("{0} photos", "{0} ფოტო"),
        ["Auction"] = ("Auction", "აუქციონი"),
        ["SoldFor"] = ("Sold for", "გაიყიდა"),
        ["Keys"] = ("Keys", "გასაღები"),
        ["RunsDrives"] = ("Runs & drives", "იქოქება და დადის"),
        ["AtAGlance"] = ("At a glance", "მოკლედ"),
        ["AlertAuctionNone"] = ("Not on US auctions", "აშშ-ის აუქციონებზე არ ჩანს"),
        ["AlertDamage"] = ("Damage", "დაზიანება"),
        ["NoInformation"] = ("No information", "ინფორმაცია არ არის"),
        ["AlertTitle"] = ("Title", "ტიტული"),
        ["AlertTitleClean"] = ("No branded title found", "პრობლემური ტიტული ვერ მოიძებნა"),
        ["AlertMileage"] = ("Mileage", "გარბენი"),
        ["AlertMileageOk"] = ("No inconsistency found", "შეუსაბამობა ვერ მოიძებნა"),
        ["AlertRecalls"] = ("{0} recall campaign(s) for this model", "ამ მოდელზე {0} გაწვევის კამპანიაა"),

        // Locked history / VIN PRO
        ["LockedTitle"] = ("Full vehicle history", "სრული ისტორია"),
        ["WebPhotosTitle"] = ("Photos found on the web", "ინტერნეტში ნაპოვნი ფოტოები"),
        ["WebPhotosHint"] = ("From other websites that mention this VIN. Tap a photo to open it.", "სხვა საიტებიდან, სადაც ეს VIN არის ნახსენები. ფოტოზე დააჭირე გასახსნელად."),
        ["SearchGoogle"] = ("Search photos on Google", "ფოტოების ძებნა Google-ში"),
        ["SourcesTitle"] = ("Data sources (only you see this)", "მონაცემთა წყაროები (მხოლოდ შენ ხედავ)"),
        // Paid full history (official US title data)
        ["FullTitle"] = ("Full history (official US data)", "სრული ისტორია (აშშ-ის ოფიციალური ბაზა)"),
        ["FullOfferHint"] = ("Free sources did not find everything. The official US title database (NMVTIS) has records for this VIN:", "უფასო წყაროებმა ყველაფერი ვერ იპოვა. აშშ-ის ოფიციალურ ტიტულის ბაზაში (NMVTIS) ამ VIN-ზე ჩანაწერები არის:"),
        ["FullIncludes"] = ("• Title records and odometer readings\n• Salvage, flood, rebuilt, junk brands\n• Insurance total-loss records\n• Title history by state and date", "• ტიტულის ჩანაწერები და გარბენი\n• Salvage, flood, rebuilt, junk ბრენდები\n• სადაზღვევო სრული ზარალის ჩანაწერები\n• ტიტულის ისტორია შტატებისა და თარიღების მიხედვით"),
        ["FullNoPhotosNote"] = ("Photos are not part of this report.", "ფოტოები ამ რეპორტში არ შედის."),
        ["FullBuy"] = ("Pay {0} ₾ and open", "გადაიხადე {0} ₾ და გახსენი"),
        ["FullOpening"] = ("Opening the bank page…", "იხსნება ბანკის გვერდი…"),
        ["FullWaiting"] = ("Waiting for the payment… The report appears here automatically.", "ველოდებით გადახდას… რეპორტი აქ ავტომატურად გამოჩნდება."),
        ["FullPending"] = ("Payment received. The report is being prepared…", "გადახდა მიღებულია. რეპორტი მზადდება…"),
        ["FullFailed"] = ("The payment could not be started. Try again later.", "გადახდა ვერ დაიწყო. სცადე მოგვიანებით."),
        ["FullNoRecords"] = ("The official database has no records for this VIN, so nothing was charged.", "ოფიციალურ ბაზაში ამ VIN-ზე ჩანაწერი არ არის, ამიტომ თანხა არ ჩამოგეჭრება."),
        ["FullLastOdometer"] = ("Last recorded odometer: {0}", "ბოლო დაფიქსირებული გარბენი: {0}"),
        ["FullRefresh"] = ("Check again", "ხელახლა შემოწმება"),
        ["LockedFoundNoCount"] = ("Found {0} auction record(s)", "ნაპოვნია {0} აუქციონის ჩანაწერი"),
        ["LockedFound"] = ("Found {0} auction(s) · {1} photo(s)", "ნაპოვნია {0} აუქციონი · {1} ფოტო"),
        ["LockedHint"] = ("Photos, damage, mileage, title and sale history", "ფოტოები, დაზიანება, გარბენი, ტიტული და გაყიდვის ისტორია"),
        ["LockedDeferred"] = ("Auction history is checked when you open it", "აუქციონის ისტორია შემოწმდება გახსნისას"),
        ["NoAuctionFound"] = ("No auction history was found for this VIN.", "ამ VIN-ზე აუქციონის ისტორია ვერ მოიძებნა."),
        ["HistoryUnavailable"] = ("Auction history is temporarily unavailable. Please try again later.", "აუქციონის ისტორია დროებით მიუწვდომელია. სცადე მოგვიანებით."),
        ["UnlockButton"] = ("Open full history", "სრული ისტორიის გახსნა"),
        ["Unlocking"] = ("Opening the history…", "ისტორია იხსნება…"),
        ["QuotaLeft"] = ("Left this month: {0} / {1}", "დარჩენილია ამ თვეში: {0} / {1}"),
        ["QuotaCredits"] = ("extra: {0}", "დამატებითი: {0}"),
        ["QuotaUsedTitle"] = ("This month's {0} VIN checks are used", "ამ თვის {0} VIN გამოყენებულია"),
        ["QuotaReset"] = ("{0} new checks open on {1}", "ახალი {0} გაიხსნება: {1}"),
        ["QuotaChoose"] = ("Add more VIN checks:", "დაამატე VIN შემოწმებები:"),
        ["Pack5"] = ("+5 VIN — {0}", "+5 VIN — {0}"),
        ["Pack10"] = ("+10 VIN — {0}", "+10 VIN — {0}"),
        ["PlanRequiredTitle"] = ("Full history · PRO", "სრული ისტორია · PRO"),
        ["PlanRequiredMessage"] = ("The full history is available with VIN PRO or Full PRO on your account.", "სრული ისტორია ხელმისაწვდომია VIN PRO ან სრული PRO გეგმით შენს ანგარიშზე."),
        ["ChoosePlan"] = ("Choose a PRO plan", "PRO გეგმის არჩევა"),
        ["SingleReport"] = ("Only this report — {0}", "მხოლოდ ეს ანგარიში — {0}"),
        ["Cancel"] = ("Cancel", "გაუქმება"),
        ["SignInRequired"] = ("Sign in to your account to open the full history.", "სრული ისტორიის გასახსნელად ჯერ შედი შენს ანგარიშში."),
        ["NotCharged"] = ("No auction history was found for this VIN. Nothing was used from your checks.", "ამ VIN-ზე აუქციონის ისტორია ვერ მოიძებნა. შემოწმება არ დაგაკლდა."),
        ["PurchaseFailed"] = ("The payment could not be completed. Please try again.", "გადახდა ვერ შესრულდა. სცადე ხელახლა."),
        ["PurchasePending"] = ("The payment is pending. The history opens after confirmation.", "გადახდა მოლოდინშია. დადასტურების შემდეგ ისტორია გაიხსნება."),

        // Specifications (NHTSA names)
        ["Spec_Manufacturer"] = ("Manufacturer", "მწარმოებელი"),
        ["Spec_Make"] = ("Make", "მარკა"),
        ["Spec_Model"] = ("Model", "მოდელი"),
        ["Spec_ModelYear"] = ("Model year", "გამოშვების წელი"),
        ["Spec_Model Year"] = ("Model year", "გამოშვების წელი"),
        ["Spec_Trim"] = ("Trim", "კომპლექტაცია"),
        ["Spec_Series"] = ("Series", "სერია"),
        ["Spec_BodyClass"] = ("Body type", "ძარის ტიპი"),
        ["Spec_VehicleType"] = ("Vehicle type", "ავტომობილის ტიპი"),
        ["Spec_Doors"] = ("Doors", "კარები"),
        ["Spec_Seats"] = ("Seats", "ადგილები"),
        ["Spec_DisplacementL"] = ("Engine volume (L)", "ძრავის მოცულობა (ლ)"),
        ["Spec_Engine"] = ("Engine", "ძრავი"),
        ["Spec_EngineCylinders"] = ("Cylinders", "ცილინდრები"),
        ["Spec_Cylinders"] = ("Cylinders", "ცილინდრები"),
        ["Spec_EngineHP"] = ("Power (hp)", "სიმძლავრე (ც.ძ.)"),
        ["Spec_FuelTypePrimary"] = ("Fuel", "საწვავი"),
        ["Spec_Fuel"] = ("Fuel", "საწვავი"),
        ["Spec_ElectrificationLevel"] = ("Electrification", "ელექტრიფიკაცია"),
        ["Spec_TransmissionStyle"] = ("Transmission", "გადაცემათა კოლოფი"),
        ["Spec_Transmission"] = ("Transmission", "გადაცემათა კოლოფი"),
        ["Spec_TransmissionSpeeds"] = ("Gears", "სიჩქარეები"),
        ["Spec_DriveType"] = ("Drivetrain", "წამყვანი თვლები"),
        ["Spec_Drivetrain"] = ("Drivetrain", "წამყვანი თვლები"),
        ["Spec_PlantCountry"] = ("Made in", "წარმოების ქვეყანა"),
        ["Spec_Country"] = ("Made in", "წარმოების ქვეყანა"),
        ["Spec_PlantCity"] = ("Plant", "ქარხანა"),
        ["Spec_Plant"] = ("Plant", "ქარხანა"),
        ["Spec_GVWR"] = ("Gross weight class", "სრული მასის კლასი"),
    };

    /// <summary>Name of a specification in the current language.</summary>
    public static string Spec(string key) =>
        Text.ContainsKey("Spec_" + key) ? T("Spec_" + key) : key;

    /// <summary>Text with {0}, {1}… filled in.</summary>
    public static string Format(string key, params object[] values) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, T(key), values);

    public static bool IsGeorgian =>
        !string.Equals(
            Preferences.Default.Get(LocalizationService.LanguageKey, "ქართული"),
            "English",
            StringComparison.OrdinalIgnoreCase);

    public static string T(string key) =>
        Text.TryGetValue(key, out var value) ? (IsGeorgian ? value.Ka : value.En) : key;

    /// <summary>"Label: value" in the current language; "—" when the value is missing.</summary>
    public static string Labeled(string key, string? value) =>
        $"{T(key)}: {(string.IsNullOrWhiteSpace(value) ? "—" : value)}";

    private static readonly (string Key, string En, string Ka)[] RecallCategories =
    [
        ("AIR BAG", "Airbags", "უსაფრთხოების ბალიშები"),
        ("SEAT BELT", "Seat belts", "უსაფრთხოების ღვედები"),
        ("CHILD SEAT", "Child seat", "ბავშვის სავარძელი"),
        ("SEAT", "Seats", "სავარძლები"),
        ("HYBRID PROPULSION", "Hybrid drive system", "ჰიბრიდული ამძრავის სისტემა"),
        ("ELECTRICAL", "Electrical system", "ელექტროსისტემა"),
        ("ENGINE", "Engine", "ძრავი"),
        ("FUEL", "Fuel system", "საწვავის სისტემა"),
        ("POWER TRAIN", "Transmission / powertrain", "ტრანსმისია"),
        ("STEERING", "Steering", "საჭის მართვა"),
        ("SERVICE BRAKES", "Brakes", "მუხრუჭები"),
        ("PARKING BRAKE", "Parking brake", "ხელის მუხრუჭი"),
        ("BRAKE", "Brakes", "მუხრუჭები"),
        ("SUSPENSION", "Suspension", "სავალი ნაწილი"),
        ("STRUCTURE", "Body structure", "ძარა"),
        ("TIRE", "Tyres", "საბურავები"),
        ("WHEEL", "Wheels", "დისკები"),
        ("LATCHES", "Locks and latches", "საკეტები"),
        ("VISIBILITY", "Visibility (mirrors, wipers)", "ხილვადობა (სარკეები, მინასაწმენდები)"),
        ("EXTERIOR LIGHTING", "Exterior lights", "გარე განათება"),
        ("BACK OVER", "Rear camera", "უკანა კამერა"),
        ("ELECTRONIC STABILITY", "Stability control (ESC)", "მდგრადობის სისტემა (ESC)"),
        ("FORWARD COLLISION", "Collision avoidance", "შეჯახების თავიდან აცილება"),
        ("LANE DEPARTURE", "Lane assist", "ზოლის შენარჩუნება"),
        ("VEHICLE SPEED CONTROL", "Cruise / throttle control", "სიჩქარის მართვა"),
        ("COMMUNICATION", "Communication system", "კავშირის სისტემა"),
        ("EQUIPMENT", "Equipment", "აღჭურვილობა"),
    ];

    /// <summary>NHTSA recall component → short name in the app language.</summary>
    public static string RecallCategory(string? component)
    {
        if (string.IsNullOrWhiteSpace(component))
            return T("Recalls");
        var upper = component.ToUpperInvariant();
        foreach (var (key, en, ka) in RecallCategories)
            if (upper.Contains(key))
                return IsGeorgian ? ka : en;
        return component;
    }

    /// <summary>Switches the language of the whole app.</summary>
    public static void Toggle()
    {
        var target = IsGeorgian ? "English" : "ქართული";
        var localization = IPlatformApplication.Current?.Services.GetService<LocalizationService>();
        if (localization is not null)
            localization.SetLanguage(target);
        else
            Preferences.Default.Set(LocalizationService.LanguageKey, target);
    }
}
