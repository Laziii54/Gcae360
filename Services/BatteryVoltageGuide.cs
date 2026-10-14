using CarScanner.Services.OBD;

namespace CarScanner.Services;

/// <summary>The kind of 12 V battery. It is not read from the car, so the owner picks it.</summary>
public enum Battery12VType
{
    /// <summary>Ordinary flooded lead-acid battery.</summary>
    Standard = 0,
    /// <summary>AGM or EFB (start-stop cars). Smart charging can lower the voltage on purpose.</summary>
    AgmEfb = 1,
    /// <summary>12 V auxiliary battery of a hybrid / EV: a DC-DC converter feeds it while the car is READY.</summary>
    HybridAuxiliary = 2,
    /// <summary>12 V lithium (LiFePO4) battery: a flat voltage curve, so voltage says little about the charge.</summary>
    Lithium = 3
}

public enum VoltageLevel { Neutral, Good, Attention, Problem }

public readonly record struct VoltageVerdict(string Text, VoltageLevel Level);

/// <summary>
/// What a 12 V reading means for each battery type. General guidance, not a vehicle-specific limit: it never reads
/// battery health, and a value it cannot judge (engine state unknown on a lead-acid car) is shown without a verdict.
/// </summary>
public static class BatteryVoltageGuide
{
    private static string L(string ka, string en) => ObdLang.IsEnglish ? en : ka;

    public static VoltageVerdict Describe(double volts, double? rpm, Battery12VType type) => type switch
    {
        Battery12VType.AgmEfb => Agm(volts, rpm),
        Battery12VType.HybridAuxiliary => Hybrid(volts),
        Battery12VType.Lithium => Lithium(volts, rpm),
        _ => Standard(volts, rpm)
    };

    /// <summary>One line about what is normal for the type, shown under the type picker.</summary>
    public static string Hint(Battery12VType type) => type switch
    {
        Battery12VType.AgmEfb => L(
            "AGM/EFB: ძრავის გამორთვისას ≈12.5–13.0 V; მუშაობისას ≈13.4–14.8 V. start-stop მანქანებზე ძაბვა განზრახ შეიძლება დაიწიოს.",
            "AGM/EFB: about 12.5–13.0 V with the engine off and 13.4–14.8 V while running. On start-stop cars the voltage can be lowered on purpose."),
        Battery12VType.HybridAuxiliary => L(
            "ჰიბრიდის/EV 12 V აკუმულატორი: READY-ზე DC-DC გარდამქმნელი ≈13.0–14.8 V-ს იძლევა; გამორთულზე ≈12.4–13.0 V.",
            "Hybrid/EV 12 V battery: with READY on, the DC-DC converter gives about 13.0–14.8 V; switched off about 12.4–13.0 V."),
        Battery12VType.Lithium => L(
            "ლითიუმის 12 V: ძაბვა თითქმის არ ცვლის მუხტის მაჩვენებელს; გამორთულზე ≈12.8–13.4 V, მუშაობისას ≈13.4–14.8 V.",
            "Lithium 12 V: the voltage hardly shows the charge level; about 12.8–13.4 V engine off and 13.4–14.8 V running."),
        _ => L(
            "ჩვეულებრივი ტყვიის აკუმულატორი: გამორთულზე ≈12.4–12.7 V; ძრავის მუშაობისას ≈13.6–14.8 V.",
            "Standard lead-acid battery: about 12.4–12.7 V with the engine off and 13.6–14.8 V while running.")
    };

    private static VoltageVerdict Standard(double value, double? rpm)
    {
        if (rpm is > 400)
        {
            return value switch
            {
                > 15.0 => new(L("ძრავა მუშაობს: გადამუხტვა — შეამოწმე დამუხტვის სისტემა (რეგულატორი)", "Engine running: overcharging — check the charging system (regulator)"), VoltageLevel.Problem),
                < 13.2 => new(L("ძრავა მუშაობს: გენერატორი არ მუხტავს — შეამოწმე გენერატორი და ღვედი", "Engine running: alternator not charging — check the alternator and belt"), VoltageLevel.Problem),
                < 13.6 => new(L("ძრავა მუშაობს: სუსტი დამუხტვა — შეამოწმე გენერატორი", "Engine running: weak charging — check the alternator"), VoltageLevel.Attention),
                <= 14.8 => new(L("ძრავა მუშაობს: ნორმალური დამუხტვის ძაბვა", "Engine running: normal charging voltage"), VoltageLevel.Good),
                _ => new(L("ძრავა მუშაობს: დამუხტვა ზედა ზღვართანაა (14.8–15.0 V)", "Engine running: charging near the upper limit (14.8–15.0 V)"), VoltageLevel.Attention)
            };
        }

        if (rpm is null)
        {
            return value switch
            {
                < 12.0 => new(L("ძრავის მდგომარეობა უცნობია: დაბალი ძაბვა — შეამოწმე აკუმულატორი და დამუხტვა", "Engine state unknown: low voltage — check the battery and charging"), VoltageLevel.Problem),
                > 15.0 => new(L("ძრავის მდგომარეობა უცნობია: მაღალი ძაბვა — შეამოწმე დამუხტვის სისტემა", "Engine state unknown: high voltage — check the charging system"), VoltageLevel.Problem),
                _ => new(L("ძრავის მდგომარეობა უცნობია (RPM ვერ წავიკითხე) — ძაბვა შეფასების გარეშე", "Engine state unknown (RPM not readable) — voltage shown without a verdict"), VoltageLevel.Neutral)
            };
        }

        return value switch
        {
            < 12.0 => new(L("ძრავა გამორთულია: დაბალი ძაბვა — შეამოწმე აკუმულატორი", "Engine off: low voltage — check the battery"), VoltageLevel.Problem),
            < 12.4 => new(L("ძრავა გამორთულია: აკუმულატორი ნაწილობრივ დაცლილია", "Engine off: battery partly discharged"), VoltageLevel.Attention),
            <= 13.0 => new(L("ძრავა გამორთულია: ნორმალური მოსვენების ძაბვა", "Engine off: normal resting voltage"), VoltageLevel.Good),
            _ => new(L("ძრავა გამორთულია: ზედაპირული მუხტი (ახლახან იმუხტებოდა)", "Engine off: surface charge (recently charged)"), VoltageLevel.Good)
        };
    }

    private static VoltageVerdict Agm(double value, double? rpm)
    {
        if (rpm is > 400)
        {
            return value switch
            {
                > 15.2 => new(L("ძრავა მუშაობს: გადამუხტვა — შეამოწმე დამუხტვის სისტემა", "Engine running: overcharging — check the charging system"), VoltageLevel.Problem),
                < 12.6 => new(L("ძრავა მუშაობს: დამუხტვა არ ჩანს — შეამოწმე გენერატორი და ღვედი", "Engine running: no charging seen — check the alternator and belt"), VoltageLevel.Problem),
                < 13.4 => new(L("ძრავა მუშაობს: ძაბვა დაბალია — start-stop სისტემა განზრახ ამცირებს ხოლმე; გაიმეორე მუდმივ ბრუნზე რამდენიმე წუთის შემდეგ", "Engine running: low voltage — a smart-charging system may lower it on purpose; repeat at steady rpm after a few minutes"), VoltageLevel.Attention),
                <= 14.9 => new(L("ძრავა მუშაობს: ნორმალური დამუხტვის ძაბვა (AGM/EFB)", "Engine running: normal charging voltage (AGM/EFB)"), VoltageLevel.Good),
                _ => new(L("ძრავა მუშაობს: დამუხტვა ზედა ზღვართანაა", "Engine running: charging near the upper limit"), VoltageLevel.Attention)
            };
        }

        if (rpm is null)
        {
            return value switch
            {
                < 12.0 => new(L("ძრავის მდგომარეობა უცნობია: დაბალი ძაბვა — შეამოწმე აკუმულატორი და დამუხტვა", "Engine state unknown: low voltage — check the battery and charging"), VoltageLevel.Problem),
                > 15.2 => new(L("ძრავის მდგომარეობა უცნობია: მაღალი ძაბვა — შეამოწმე დამუხტვის სისტემა", "Engine state unknown: high voltage — check the charging system"), VoltageLevel.Problem),
                _ => new(L("ძრავის მდგომარეობა უცნობია (RPM ვერ წავიკითხე) — ძაბვა შეფასების გარეშე", "Engine state unknown (RPM not readable) — voltage shown without a verdict"), VoltageLevel.Neutral)
            };
        }

        return value switch
        {
            < 12.0 => new(L("ძრავა გამორთულია: დაბალი ძაბვა — შეამოწმე აკუმულატორი", "Engine off: low voltage — check the battery"), VoltageLevel.Problem),
            < 12.5 => new(L("ძრავა გამორთულია: აკუმულატორი ნაწილობრივ დაცლილია", "Engine off: battery partly discharged"), VoltageLevel.Attention),
            <= 13.2 => new(L("ძრავა გამორთულია: ნორმალური მოსვენების ძაბვა (AGM/EFB)", "Engine off: normal resting voltage (AGM/EFB)"), VoltageLevel.Good),
            _ => new(L("ძრავა გამორთულია: ზედაპირული მუხტი (ახლახან იმუხტებოდა)", "Engine off: surface charge (recently charged)"), VoltageLevel.Good)
        };
    }

    // RPM is not used: a hybrid/EV in READY has no engine rpm, yet the converter is feeding the 12 V battery.
    private static VoltageVerdict Hybrid(double value) => value switch
    {
        > 15.0 => new(L("12 V ძაბვა მაღალია — შეამოწმე DC-DC გარდამქმნელი", "12 V voltage is high — check the DC-DC converter"), VoltageLevel.Problem),
        < 11.8 => new(L("12 V აკუმულატორი დაცლილია — შეამოწმე ან დატენე", "12 V battery is flat — check or charge it"), VoltageLevel.Problem),
        < 12.4 => new(L("12 V ძაბვა დაბალია — აკუმულატორი ნაწილობრივ დაცლილია", "12 V voltage is low — the battery is partly discharged"), VoltageLevel.Attention),
        < 13.0 => new(L("12 V ნორმალურია მოსვენების რეჟიმში (მანქანა გამორთულია/READY-ის გარეშე)", "12 V is normal at rest (car off / not READY)"), VoltageLevel.Good),
        _ => new(L("12 V იკვებება DC-DC გარდამქმნელიდან — ნორმალურია", "12 V is fed by the DC-DC converter — normal"), VoltageLevel.Good)
    };

    private static VoltageVerdict Lithium(double value, double? rpm)
    {
        if (rpm is > 400)
        {
            return value switch
            {
                > 15.0 => new(L("ძრავა მუშაობს: ძაბვა მაღალია — შეამოწმე დამუხტვის სისტემა და BMS", "Engine running: high voltage — check the charging system and the BMS"), VoltageLevel.Problem),
                < 13.0 => new(L("ძრავა მუშაობს: დამუხტვა არ ჩანს — შეამოწმე გენერატორი", "Engine running: no charging seen — check the alternator"), VoltageLevel.Problem),
                < 13.4 => new(L("ძრავა მუშაობს: დამუხტვა სუსტია", "Engine running: charging is weak"), VoltageLevel.Attention),
                _ => new(L("ძრავა მუშაობს: ნორმალური დამუხტვის ძაბვა (ლითიუმი)", "Engine running: normal charging voltage (lithium)"), VoltageLevel.Good)
            };
        }

        if (rpm is null)
            return new(L("ძრავის მდგომარეობა უცნობია (RPM ვერ წავიკითხე) — ძაბვა შეფასების გარეშე", "Engine state unknown (RPM not readable) — voltage shown without a verdict"), VoltageLevel.Neutral);

        return value switch
        {
            < 12.0 => new(L("ძრავა გამორთულია: ძაბვა დაბალია — შეამოწმე აკუმულატორი და BMS", "Engine off: low voltage — check the battery and the BMS"), VoltageLevel.Problem),
            < 12.8 => new(L("ძრავა გამორთულია: ძაბვა შედარებით დაბალია — ლითიუმზე ეს დაცლილობას ნიშნავს", "Engine off: voltage is on the low side — on lithium this means a low charge"), VoltageLevel.Attention),
            _ => new(L("ძრავა გამორთულია: ნორმალური მოსვენების ძაბვა (ლითიუმი; მუხტის დონეს ზუსტად არ აჩვენებს)", "Engine off: normal resting voltage (lithium; it does not show the charge level precisely)"), VoltageLevel.Good)
        };
    }
}
