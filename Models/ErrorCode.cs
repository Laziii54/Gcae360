using CommunityToolkit.Mvvm.ComponentModel;

namespace CarScanner.Models;

public partial class ErrorCode : ObservableObject
{
    // ============================================================
    // BASIC DTC DATA
    // ============================================================

    public string Code { get; set; } = string.Empty;


    // ============================================================
    // GEORGIAN
    // ============================================================

    public string TitleKa { get; set; } = string.Empty;

    public string DescriptionKa { get; set; } = string.Empty;

    public string PossibleCausesKa { get; set; } = string.Empty;

    public string SymptomsKa { get; set; } = string.Empty;

    /// <summary>What to check first, one item per line (Georgian).</summary>
    public string ChecksKa { get; set; } = string.Empty;


    // ============================================================
    // ENGLISH
    // ============================================================

    public string TitleEn { get; set; } = string.Empty;

    public string DescriptionEn { get; set; } = string.Empty;

    public string PossibleCausesEn { get; set; } = string.Empty;

    public string SymptomsEn { get; set; } = string.Empty;


    // ============================================================
    // STATUS
    // ============================================================

    public string Severity { get; set; } = "უცნობი";

    public string Status { get; set; } = "შენახული";


    // ============================================================
    // CURRENT LANGUAGE
    // ============================================================

    private static bool IsEnglish
    {
        get
        {
            return Preferences.Default
                .Get(
                    "language",
                    "ქართული")
                .Equals(
                    "English",
                    StringComparison.OrdinalIgnoreCase);
        }
    }


    // ============================================================
    // DISPLAY TITLE
    // ============================================================

    public string DisplayTitle
    {
        get
        {
            if (IsEnglish &&
                !string.IsNullOrWhiteSpace(TitleEn))
            {
                return TitleEn;
            }

            return !string.IsNullOrWhiteSpace(TitleKa)
                ? TitleKa
                : TitleEn;
        }
    }


    // ============================================================
    // DISPLAY DESCRIPTION
    // ============================================================

    public string DisplayDescription
    {
        get
        {
            if (IsEnglish &&
                !string.IsNullOrWhiteSpace(DescriptionEn))
            {
                return DescriptionEn;
            }

            return !string.IsNullOrWhiteSpace(DescriptionKa)
                ? DescriptionKa
                : DescriptionEn;
        }
    }


    // ============================================================
    // DISPLAY CAUSES
    // ============================================================

    public string DisplayCauses
    {
        get
        {
            if (IsEnglish &&
                !string.IsNullOrWhiteSpace(PossibleCausesEn))
            {
                return PossibleCausesEn;
            }

            return !string.IsNullOrWhiteSpace(PossibleCausesKa)
                ? PossibleCausesKa
                : PossibleCausesEn;
        }
    }


    // ============================================================
    // DISPLAY SYMPTOMS
    // ============================================================

    public string DisplaySymptoms
    {
        get
        {
            if (IsEnglish &&
                !string.IsNullOrWhiteSpace(SymptomsEn))
            {
                return SymptomsEn;
            }

            return !string.IsNullOrWhiteSpace(SymptomsKa)
                ? SymptomsKa
                : SymptomsEn;
        }
    }


    // ============================================================
    // DISPLAY CHECKS
    // ============================================================

    private const string GenericChecksKa =
        "1. შეამოწმე შესაბამისი ცოცხალი მონაცემები · 2. დაათვალიერე ჰაერის/ვაკუუმის გზები · 3. შეამოწმე სენსორი და გაყვანილობა · 4. შეადარე Freeze Frame მონაცემებს";

    private const string GenericChecksEn =
        "1. Check the related Live Data · 2. Inspect the air/vacuum paths · 3. Check the sensor and wiring · 4. Compare with the Freeze Frame data";

    public string DisplayChecks
    {
        get
        {
            if (IsEnglish)
                return GenericChecksEn;

            if (string.IsNullOrWhiteSpace(ChecksKa))
                return GenericChecksKa;

            var lines = ChecksKa
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Join(
                "\n",
                lines.Select((line, index) => $"{index + 1}. {line}"));
        }
    }


    // ============================================================
    // DISPLAY STATUS
    // ============================================================

    public string DisplayStatus
    {
        get
        {
            if (!IsEnglish)
                return Status;

            return Status switch
            {
                "შენახული" => "Stored",
                "მოლოდინში" => "Pending",
                "მუდმივი" => "Permanent",

                "Stored" => "Stored",
                "Pending" => "Pending",
                "Permanent" => "Permanent",

                _ => Status
            };
        }
    }


    // ============================================================
    // SEVERITY COLOR
    // ============================================================

    public string SeverityColor
    {
        get
        {
            return Severity switch
            {
                "კარგი" or "Good"
                    => "#4CAF50",

                "საშუალო" or "Medium"
                    => "#FFC107",

                "სერიოზული" or "High"
                    => "#F44336",

                "მაღალი" or "Critical"
                    => "#F44336",

                "დაბალი" or "Low"
                    => "#8BC34A",

                _ => "#9E9E9E"
            };
        }
    }


    // ============================================================
    // LANGUAGE CHANGE
    // ============================================================

    public void OnChanged()
    {
        OnPropertyChanged(
            nameof(DisplayTitle));

        OnPropertyChanged(
            nameof(DisplayDescription));

        OnPropertyChanged(
            nameof(DisplayCauses));

        OnPropertyChanged(
            nameof(DisplaySymptoms));

        OnPropertyChanged(
            nameof(DisplayChecks));

        OnPropertyChanged(
            nameof(DisplayStatus));

        OnPropertyChanged(
            nameof(SeverityColor));
    }
}