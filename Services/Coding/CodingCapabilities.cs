namespace CarScanner.Services.Coding;

/// <summary>What the Coding page can do, and what it deliberately never does.</summary>
public enum CodingFeature
{
    /// <summary>Read identification blocks of every module that answers (read-only).</summary>
    Backup,

    /// <summary>Byte/bit comparison of two saved backups (no vehicle needed).</summary>
    Compare,

    /// <summary>Write a setting that the catalog lists as verified for this exact vehicle and module.</summary>
    ChangeSettings,

    /// <summary>Put an earlier value of a change made by this app back.</summary>
    RestoreHistory,

    /// <summary>Security access, seed/key, flashing, programming, adaptations: never done by this page.</summary>
    ForbiddenOperations
}

public enum CodingFeatureState
{
    Available,
    NeedsConnection,
    NeedsMoreBackups,
    NothingToRestore,
    NotVerifiedForThisVehicle,
    NotCan,
    ProRequired,
    NotSupported
}

/// <summary>Facts the page already has. Anything unknown stays unknown; nothing is assumed.</summary>
public sealed record CodingInputs(
    bool Connected,
    bool IsCan,
    bool VinKnown,
    int VerifiedOptionsForCar,
    int VerifiedOptionsInCatalog,
    int BackupCount,
    int RestorableChanges,
    bool ProActive,
    bool IsGm = false);

public sealed record CodingFeatureStatus(CodingFeature Feature, CodingFeatureState State)
{
    public bool IsAvailable => State == CodingFeatureState.Available;
}

public enum CodingCheckState
{
    Ok,
    Warning,
    Blocked,
    Unknown
}

/// <summary>One line of the "is the car ready" list.</summary>
public sealed record CodingCheck(string Key, CodingCheckState State, string TextKa, string TextEn)
{
    public string Text(bool english) => english ? TextEn : TextKa;
}

public sealed record CodingReadinessInput(bool Connected, bool IsCan, string? Vin, double? SpeedKph, double? Volts, bool VehicleIdentified);

/// <summary>
/// Pure decisions for the Coding page, in one place so they can be tested without a car:
/// which features are available for the facts at hand, and the readiness checks. Real support of a setting is
/// decided only by the verified catalog (exact brand, model, year, module address and part number); this class never
/// turns a brand or a guess into "supported".
/// </summary>
public static class CodingCapabilities
{
    /// <summary>The same limit the write path enforces (see <c>CodingService.CheckConditionsAsync</c>).</summary>
    public const double MinimumVolts = 12.2;

    public static IReadOnlyList<CodingFeatureStatus> Evaluate(CodingInputs input)
    {
        CodingFeatureState Backup()
        {
            if (!input.Connected) return CodingFeatureState.NeedsConnection;
            return input.IsCan ? CodingFeatureState.Available : CodingFeatureState.NotCan;
        }

        CodingFeatureState Change()
        {
            // Without a verified entry for THIS vehicle nothing can ever be written, connected or not.
            if (input.VerifiedOptionsForCar <= 0) return CodingFeatureState.NotVerifiedForThisVehicle;
            if (!input.Connected) return CodingFeatureState.NeedsConnection;
            if (!input.IsCan) return CodingFeatureState.NotCan;
            return input.ProActive ? CodingFeatureState.Available : CodingFeatureState.ProRequired;
        }

        CodingFeatureState Restore()
        {
            if (input.RestorableChanges <= 0) return CodingFeatureState.NothingToRestore;
            if (!input.Connected) return CodingFeatureState.NeedsConnection;
            return input.IsCan ? CodingFeatureState.Available : CodingFeatureState.NotCan;
        }

        return new[]
        {
            new CodingFeatureStatus(CodingFeature.Backup, Backup()),
            new CodingFeatureStatus(CodingFeature.Compare, input.BackupCount >= 2 ? CodingFeatureState.Available : CodingFeatureState.NeedsMoreBackups),
            new CodingFeatureStatus(CodingFeature.ChangeSettings, Change()),
            new CodingFeatureStatus(CodingFeature.RestoreHistory, Restore()),
            new CodingFeatureStatus(CodingFeature.ForbiddenOperations, CodingFeatureState.NotSupported)
        };
    }

    public static IReadOnlyList<CodingCheck> Readiness(CodingReadinessInput input)
    {
        var list = new List<CodingCheck>();

        list.Add(input.Connected
            ? new CodingCheck("adapter", CodingCheckState.Ok, "ადაპტერი დაკავშირებულია", "Adapter connected")
            : new CodingCheck("adapter", CodingCheckState.Blocked, "ადაპტერი არ არის დაკავშირებული", "Adapter not connected"));

        if (input.Connected)
        {
            list.Add(input.IsCan
                ? new CodingCheck("can", CodingCheckState.Ok, "CAN კავშირი (UDS/GMLAN წაკითხვა შესაძლებელია)", "CAN connection (UDS/GMLAN reads possible)")
                : new CodingCheck("can", CodingCheckState.Blocked, "კავშირი არ არის CAN — ამ გვერდის ფუნქციები არ მუშაობს", "The connection is not CAN — the functions of this page do not work"));
        }

        var vinKnown = input.Vin is { Length: 17 };
        list.Add(vinKnown
            ? new CodingCheck("vin", CodingCheckState.Ok, "VIN წაკითხულია — ასლები ამ მანქანას მიეკუთვნება", "VIN read — backups belong to this car")
            : new CodingCheck("vin", CodingCheckState.Warning,
                "VIN უცნობია — ასლები „უცნობი“ საქაღალდეში ინახება და აღდგენა დაბლოკილია",
                "VIN unknown — backups are filed under “unknown” and restoring is blocked"));

        if (input.Connected)
        {
            list.Add(!input.VehicleIdentified
                ? new CodingCheck("identity", CodingCheckState.Warning,
                    "მანქანა ვერ იდენტიფიცირდა — პარამეტრების კატალოგი ვერ შეადარებს მოდელს",
                    "The vehicle could not be identified — the settings catalog cannot match a model")
                : new CodingCheck("identity", CodingCheckState.Ok, "მანქანა იდენტიფიცირებულია", "Vehicle identified"));

            list.Add(input.SpeedKph switch
            {
                null => new CodingCheck("speed", CodingCheckState.Unknown, "სიჩქარე ვერ წავიკითხე (ცვლილებებისთვის საჭიროა)", "Speed could not be read (needed for changes)"),
                < 0.5 => new CodingCheck("speed", CodingCheckState.Ok, "მანქანა დგას", "Car is stopped"),
                var speed => new CodingCheck("speed", CodingCheckState.Blocked, $"მანქანა მოძრაობს ({speed:0} კმ/სთ)", $"Car is moving ({speed:0} km/h)")
            });

            list.Add(input.Volts switch
            {
                null => new CodingCheck("volts", CodingCheckState.Unknown, "აკუმულატორის ძაბვა ვერ წავიკითხე (ცვლილებებისთვის საჭიროა)", "Battery voltage could not be read (needed for changes)"),
                >= MinimumVolts => new CodingCheck("volts", CodingCheckState.Ok, $"აკუმულატორი {input.Volts:0.0} V", $"Battery {input.Volts:0.0} V"),
                var volts => new CodingCheck("volts", CodingCheckState.Warning, $"აკუმულატორი {volts:0.0} V — ცვლილებისთვის მინ. {MinimumVolts:0.0} V", $"Battery {volts:0.0} V — changes need at least {MinimumVolts:0.0} V")
            });
        }

        return list;
    }

    /// <summary>
    /// Key that tells one car from another for cached module lists: the VIN when valid, else make/model/year.
    /// Empty when nothing identifies the car (then no cached list may be trusted).
    /// </summary>
    public static string VehicleKey(string? vin, string? make, string? model, string? year)
    {
        if (vin is { Length: 17 })
            return "VIN:" + vin.Trim().ToUpperInvariant();

        static string Known(string? value) =>
            string.IsNullOrWhiteSpace(value) || value.Trim().Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? string.Empty : value.Trim().ToUpperInvariant();

        var parts = new[] { Known(make), Known(model), Known(year) };
        return parts.All(string.IsNullOrEmpty) ? string.Empty : "CAR:" + string.Join("|", parts);
    }
}

/// <summary>How the read of one module ended, from the values that came back.</summary>
public static class CodingBackupStatus
{
    public const string Read = "read";
    public const string Refused = "refused";
    public const string Foreign = "foreign";
    public const string Silent = "silent";

    /// <summary>Status plus a short technical note (the refusal code, or the id that really answered).</summary>
    public static (string Status, string Note) Classify(IReadOnlyList<CodingDidValue> values)
    {
        if (values.Any(value => value.IsRead))
            return (Read, string.Empty);

        var foreign = values.Select(value => value.Error).FirstOrDefault(error => error.StartsWith("reply from another module", StringComparison.Ordinal));
        if (foreign is not null)
            return (Foreign, foreign.Replace("reply from another module", "answered by", StringComparison.Ordinal));

        var refusal = values.Select(value => value.Error).FirstOrDefault(error => error.StartsWith("NRC", StringComparison.Ordinal));
        if (refusal is not null)
            return (Refused, refusal);

        return (Silent, string.Empty);
    }
}

/// <summary>Wording for the Coding page capability list (Georgian / English).</summary>
public static class CodingTexts
{
    /// <summary>Why a normal backup of a GM car cannot show where a setting lives, and what the extended read does about it.</summary>
    public static string GmBackupNote(bool en) => en
        ? "GM car: the normal backup reads identification blocks only. Use “Extended read” to also read every GMLAN $1A identifier, so that a before/after comparison can show where a setting is stored."
        : "GM მანქანა: ჩვეულებრივი ასლი მხოლოდ იდენტიფიკაციის ბლოკებს კითხულობს. გამოიყენეთ „გაფართოებული წაკითხვა“, რომ ყველა GMLAN $1A იდენტიფიკატორიც წაიკითხოს — ასე შედარება აჩვენებს, სად ინახება პარამეტრი.";

    public static string ExtendedReadTitle(bool en) => en ? "Extended read (GM)" : "გაფართოებული წაკითხვა (GM)";

    public static string ExtendedReadHelp(bool en) => en
        ? "Reads every $1A identifier (00–FF) of each GMLAN module that answered. Read-only; it can take a few minutes. Nothing is written."
        : "კითხულობს ყველა $1A იდენტიფიკატორს (00–FF) თითოეული პასუხიანი GMLAN მოდულიდან. მხოლოდ კითხვა; შეიძლება რამდენიმე წუთი დასჭირდეს. არაფერი იწერება.";

    public static string Title(CodingFeature feature, bool en) => feature switch
    {
        CodingFeature.Backup => en ? "Full backup (read-only)" : "სრული სარეზერვო ასლი (მხოლოდ კითხვა)",
        CodingFeature.Compare => en ? "Compare two backups" : "ორი ასლის შედარება",
        CodingFeature.ChangeSettings => en ? "Change a setting" : "პარამეტრის შეცვლა",
        CodingFeature.RestoreHistory => en ? "Restore a change made by this app" : "ამ აპით გაკეთებული ცვლილების აღდგენა",
        _ => en ? "Security access, flashing, programming, adaptations" : "დაცვის კოდი, გადაპროგრამება, ადაპტაციები"
    };

    public static string StateText(CodingFeatureState state, bool en) => state switch
    {
        CodingFeatureState.Available => en ? "Available" : "ხელმისაწვდომია",
        CodingFeatureState.NeedsConnection => en ? "Connect the adapter" : "დააკავშირეთ ადაპტერი",
        CodingFeatureState.NeedsMoreBackups => en ? "Needs two backups" : "საჭიროა ორი ასლი",
        CodingFeatureState.NothingToRestore => en ? "Nothing to restore" : "აღსადგენი არაფერია",
        CodingFeatureState.NotVerifiedForThisVehicle => en ? "Not verified for this car" : "ამ მანქანაზე დადასტურებული არ არის",
        CodingFeatureState.NotCan => en ? "Needs a CAN connection" : "საჭიროა CAN კავშირი",
        CodingFeatureState.ProRequired => en ? "PRO required" : "საჭიროა PRO",
        _ => en ? "Not supported" : "არ არის მხარდაჭერილი"
    };

    public static string Reason(CodingFeatureStatus status, CodingInputs input, bool en) => status.Feature switch
    {
        CodingFeature.Backup => status.State switch
        {
            CodingFeatureState.Available => (en
                ? "Asks the modules that answer for their identification blocks. Nothing is written to the car."
                : "აკითხავს პასუხიან მოდულებს იდენტიფიკაციის ბლოკებს. მანქანაზე არაფერი იწერება.")
                + (input.IsGm ? "\n" + CodingTexts.GmBackupNote(en) : string.Empty),
            CodingFeatureState.NotCan => en ? "Module reads need a CAN connection." : "მოდულების წაკითხვას CAN კავშირი სჭირდება.",
            _ => en ? "Connect the adapter and switch the ignition on." : "დააკავშირეთ ადაპტერი და ჩართეთ ანთება."
        },
        CodingFeature.Compare => status.IsAvailable
            ? (en ? "Shows exactly which bytes and bits differ between two saved backups." : "აჩვენებს, რომელი ბაიტები და ბიტები განსხვავდება ორ შენახულ ასლს შორის.")
            : (en ? $"{input.BackupCount} saved backup(s). Make a second one after changing a setting in the car’s own menu." : $"შენახულია {input.BackupCount} ასლი. პარამეტრის მანქანის მენიუში შეცვლის შემდეგ შექმენით მეორე."),
        CodingFeature.ChangeSettings => status.State switch
        {
            CodingFeatureState.NotVerifiedForThisVehicle => en
                ? $"The catalog has {input.VerifiedOptionsInCatalog} verified setting(s) in total and none for this exact car, module and part number. Nothing will be written."
                : $"კატალოგში სულ {input.VerifiedOptionsInCatalog} დადასტურებული პარამეტრია და არცერთი ზუსტად ამ მანქანის, მოდულისა და ნაწილის ნომრისთვის. არაფერი ჩაიწერება.",
            CodingFeatureState.ProRequired => en ? "Verified settings exist for this car; changing them needs PRO." : "ამ მანქანისთვის დადასტურებული პარამეტრებია; შეცვლას PRO სჭირდება.",
            CodingFeatureState.NotCan => en ? "Writing needs a CAN connection." : "ჩაწერას CAN კავშირი სჭირდება.",
            CodingFeatureState.NeedsConnection => en ? "Connect the adapter first." : "ჯერ დააკავშირეთ ადაპტერი.",
            _ => en
                ? "Backup first, write, read back; the original is restored automatically on any mismatch."
                : "ჯერ ასლი, შემდეგ ჩაწერა და უკან წაკითხვა; შეუსაბამობისას თავდაპირველი ავტომატურად ბრუნდება."
        },
        CodingFeature.RestoreHistory => status.State == CodingFeatureState.NothingToRestore
            ? (en ? "This app has not changed anything on this car." : "ამ აპს ამ მანქანაზე არაფერი შეუცვლია.")
            : status.IsAvailable
                ? (en ? "Writes the earlier value back after checking the VIN and that nothing else changed it." : "აბრუნებს ძველ მნიშვნელობას VIN-ის და სხვა ცვლილების არარსებობის შემოწმების შემდეგ.")
                : (en ? "Connect the adapter first." : "ჯერ დააკავშირეთ ადაპტერი."),
        _ => en
            ? "Never done by this page: no security access, no flashing or programming, no adaptations, no actuator tests."
            : "ამ გვერდიდან არასდროს ხდება: არ არის დაცვის კოდი, გადაპროგრამება, ადაპტაცია, აქტივატორების ტესტი."
    };
}
