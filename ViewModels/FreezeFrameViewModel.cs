using CarScanner.Services;
using CarScanner.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace CarScanner.ViewModels;

/// <summary>One row on the Freeze Frame page.</summary>
public sealed class FreezeFrameDisplayItem
{
    public string Name { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public string ValueColor { get; init; } = "#60A5FA";
}

public partial class FreezeFrameViewModel : BaseViewModel
{
    private readonly FreezeFrameReaderService _reader;
    private readonly DtcCatalogService _dtcCatalog;
    private readonly IBluetoothService _bluetooth;
    private readonly LocalizationService _localization;
    private readonly UnitSystemService _units;

    private Mode02FreezeFrameResult? _snapshot;
    private CancellationTokenSource? _readCts;

    public ObservableCollection<FreezeFrameDisplayItem> Values { get; } = new();

    [ObservableProperty] private bool isReading;
    [ObservableProperty] private int readProgress;
    [ObservableProperty] private double progressFraction;
    [ObservableProperty] private bool hasFrame;

    [ObservableProperty] private string pageTitle = "Freeze Frame";
    [ObservableProperty] private string readButtonText = "წაკითხვა";
    [ObservableProperty] private string copyButtonText = "კოპირება";
    [ObservableProperty] private string progressText = string.Empty;
    [ObservableProperty] private string stateDetail = string.Empty;
    [ObservableProperty] private string triggerSectionTitle = "DTC კოდი";
    [ObservableProperty] private string valuesSectionTitle = "ხარვეზის მომენტის მონაცემები";
    [ObservableProperty] private string triggerCode = string.Empty;
    [ObservableProperty] private string triggerBadge = "FREEZE FRAME";
    [ObservableProperty] private string triggerTitle = string.Empty;
    [ObservableProperty] private string conditionsText = string.Empty;
    [ObservableProperty] private string emptyMessage = string.Empty;

    public bool IsNotReading => !IsReading;
    public bool CanCopy => HasFrame && !IsReading;

    partial void OnIsReadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotReading));
        OnPropertyChanged(nameof(CanCopy));
    }

    public bool HasNoFrame => !HasFrame;

    partial void OnHasFrameChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(HasNoFrame));
    }
    partial void OnReadProgressChanged(int value) => ProgressFraction = Math.Clamp(value / 100d, 0d, 1d);

    public FreezeFrameViewModel(
        FreezeFrameReaderService reader,
        DtcCatalogService dtcCatalog,
        IBluetoothService bluetooth,
        LocalizationService localization,
        UnitSystemService units)
    {
        _reader = reader;
        _dtcCatalog = dtcCatalog;
        _bluetooth = bluetooth;
        _localization = localization;
        _units = units;

        _localization.LanguageChanged += (_, __) =>
            MainThread.BeginInvokeOnMainThread(() => _ = RenderAsync());
        _units.UnitSystemChanged += (_, __) =>
            MainThread.BeginInvokeOnMainThread(() => _ = RenderAsync());

        UpdateStaticTexts();
        StateDetail = L(
            "დააჭირე „წაკითხვას“ — ECU-დან წაიკითხება ხარვეზის მომენტში შენახული მონაცემები.",
            "Tap Read to load the data the ECU stored at the moment of the fault.");
        EmptyMessage = L(
            "Freeze Frame ჯერ არ არის წაკითხული.",
            "Freeze Frame has not been read yet.");
    }

    /// <summary>Called when the page appears: reads automatically once.</summary>
    public async Task OnAppearingAsync()
    {
        UpdateStaticTexts();
        if (_snapshot is null && !IsReading && _bluetooth.IsConnected)
            await Read();
        else
            await RenderAsync();
    }

    public void OnDisappearing()
    {
        try { _readCts?.Cancel(); } catch { }
    }

    [RelayCommand]
    private async Task Read()
    {
        try
        {
        if (IsReading)
            return;

        if (!_bluetooth.IsConnected)
        {
            StateDetail = L(
                "OBD ადაპტერი არ არის დაკავშირებული. ჯერ დაუკავშირდი მანქანას.",
                "The OBD adapter is not connected. Connect to the vehicle first.");
            return;
        }

        _readCts?.Dispose();
        _readCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        try
        {
            IsReading = true;
            ReadProgress = 0;
            ProgressText = L("Freeze Frame იკითხება…", "Reading Freeze Frame…");

            var progress = new System.Progress<int>(percent =>
            {
                ReadProgress = percent;
                ProgressText = L($"Freeze Frame იკითხება… {percent}%", $"Reading Freeze Frame… {percent}%");
            });

            _snapshot = await _reader.ReadAsync(progress, _readCts.Token);
            await RenderAsync();
        }
        catch (OperationCanceledException)
        {
            StateDetail = L("წაკითხვა შეწყდა. სცადე თავიდან.", "Reading was cancelled. Try again.");
        }
        catch (Exception ex)
        {
            StateDetail = L(
                "Freeze Frame ვერ წაიკითხა. შეამოწმე OBD კავშირი და ანთება, შემდეგ სცადე თავიდან.",
                "Could not read Freeze Frame. Check the OBD connection and ignition, then try again.")
                + (string.IsNullOrWhiteSpace(ex.Message) ? string.Empty : $"\n{ex.Message}");
        }
        finally
        {
            IsReading = false;
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "FreezeFrameViewModel.Read");
        }
    }

    [RelayCommand]
    private async Task Copy()
    {
        try
        {
        if (_snapshot is null || !_snapshot.HasFrame)
            return;

        var text = new StringBuilder();
        text.AppendLine($"Freeze Frame · {TriggerCode}");
        if (!string.IsNullOrWhiteSpace(TriggerTitle)) text.AppendLine(TriggerTitle);
        if (!string.IsNullOrWhiteSpace(ConditionsText)) text.AppendLine(ConditionsText);
        text.AppendLine();
        foreach (var item in Values)
            text.AppendLine($"{item.Name}: {item.ValueText}");

        try
        {
            await Clipboard.Default.SetTextAsync(text.ToString().TrimEnd());
            StateDetail = L("მონაცემები დაკოპირდა.", "Data copied.");
        }
        catch
        {
            StateDetail = L("კოპირება ვერ მოხერხდა.", "Copy failed.");
        }
        }
        catch (Exception commandException)
        {
            CarScanner.Services.CommandErrors.Report(commandException, "FreezeFrameViewModel.Copy");
        }
    }

    private async Task RenderAsync()
    {
        UpdateStaticTexts();

        var snapshot = _snapshot;
        Values.Clear();

        if (snapshot is null)
        {
            HasFrame = false;
            return;
        }

        if (!snapshot.HasFrame)
        {
            HasFrame = false;
            TriggerCode = string.Empty;
            TriggerTitle = string.Empty;
            ConditionsText = string.Empty;
            EmptyMessage = L(
                "ECU-ში Freeze Frame შენახული არ არის. ეს ნორმალურია, როცა შეცდომის კოდი (DTC) არ არის ან ახლახან წაიშალა.",
                "No Freeze Frame is stored in the ECU. This is normal when there is no trouble code (DTC) or codes were recently cleared.");
            StateDetail = L(
                $"შემოწმდა {snapshot.ReadAt:HH:mm:ss} · შენახული კადრი არ არის",
                $"Checked {snapshot.ReadAt:HH:mm:ss} · no stored frame");
            return;
        }

        HasFrame = true;
        TriggerCode = snapshot.TriggerCode;
        TriggerTitle = await GetDtcTitleAsync(snapshot.TriggerCode);

        foreach (var reading in snapshot.Values)
            Values.Add(new FreezeFrameDisplayItem
            {
                Name = PidName(reading.Pid),
                ValueText = FormatValue(reading),
                ValueColor = ValueColor(reading)
            });

        ConditionsText = BuildConditions(snapshot);
        EmptyMessage = L(
            "ECU-მ ამ კადრისთვის დამატებითი მონაცემები არ დააბრუნა.",
            "The ECU returned no additional values for this frame.");
        StateDetail = L(
            $"წაკითხულია {snapshot.ReadAt:HH:mm:ss} · {snapshot.Values.Count} მნიშვნელობა",
            $"Read at {snapshot.ReadAt:HH:mm:ss} · {snapshot.Values.Count} values");
    }

    private async Task<string> GetDtcTitleAsync(string code)
    {
        try
        {
            var entry = await _dtcCatalog.GetAsync(code);
            if (entry is not null)
            {
                var title = _localization.IsEnglish
                    ? FirstNonEmpty(entry.TitleEn, entry.DescriptionEn)
                    : FirstNonEmpty(entry.TitleKa, entry.DescriptionKa, entry.TitleEn, entry.DescriptionEn);
                if (!string.IsNullOrWhiteSpace(title))
                    return title;
            }
        }
        catch
        {
            // Description is optional; the code itself is always shown.
        }

        return L("კოდის აღწერა ბაზაში ვერ მოიძებნა.", "No description found for this code.");
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private void UpdateStaticTexts()
    {
        PageTitle = "Freeze Frame";
        ReadButtonText = L("წაკითხვა", "Read");
        CopyButtonText = L("კოპირება", "Copy");
        TriggerSectionTitle = L("DTC კოდი", "DTC code");
        ValuesSectionTitle = L("ხარვეზის მომენტის მონაცემები", "Conditions at the moment of the fault");
        TriggerBadge = "FREEZE FRAME";
    }

    private string L(string ka, string en) => _localization.IsEnglish ? en : ka;

    private string PidName(int pid) => pid switch
    {
        0x03 => L("საწვავის სისტემა", "Fuel system"),
        0x04 => L("ძრავის დატვირთვა", "Engine load"),
        0x05 => L("ძრავის ტემპერატურა", "Coolant temperature"),
        0x06 => L("მოკლე კორექცია (Bank 1)", "Short fuel trim (Bank 1)"),
        0x07 => L("გრძელი კორექცია (Bank 1)", "Long fuel trim (Bank 1)"),
        0x08 => L("მოკლე კორექცია (Bank 2)", "Short fuel trim (Bank 2)"),
        0x09 => L("გრძელი კორექცია (Bank 2)", "Long fuel trim (Bank 2)"),
        0x0A => L("საწვავის წნევა", "Fuel pressure"),
        0x0B => L("კოლექტორის წნევა (MAP)", "Intake pressure (MAP)"),
        0x0C => L("ძრავის ბრუნები", "Engine RPM"),
        0x0D => L("სიჩქარე", "Vehicle speed"),
        0x0E => L("ანთების დასწრება", "Timing advance"),
        0x0F => L("შემავალი ჰაერის ტემპერატურა", "Intake air temperature"),
        0x10 => L("ჰაერის ნაკადი (MAF)", "Air flow (MAF)"),
        0x11 => L("დროსელის პოზიცია", "Throttle position"),
        0x1F => L("ძრავის მუშაობის დრო", "Engine run time"),
        0x2F => L("საწვავის დონე", "Fuel level"),
        0x33 => L("ატმოსფერული წნევა", "Barometric pressure"),
        0x42 => L("ძაბვა", "Module voltage"),
        0x46 => L("გარე ტემპერატურა", "Ambient temperature"),
        0x5C => L("ზეთის ტემპერატურა", "Oil temperature"),
        _ => $"PID {pid:X2}"
    };

    private string FormatValue(Mode02FreezeFrameValue reading)
    {
        if (reading.Pid == 0x03)
            return FuelSystemText(reading.Text);

        if (reading.Number is not double value)
            return "—";

        var format = reading.Pid switch
        {
            0x0C or 0x0D or 0x05 or 0x0F or 0x46 or 0x5C or 0x1F or 0x0B or 0x33 or 0x0A => "0",
            0x42 => "0.0",
            _ => "0.#"
        };

        if (reading.Pid is 0x06 or 0x07 or 0x08 or 0x09)
        {
            var sign = value > 0 ? "+" : string.Empty;
            return $"{sign}{value.ToString("0.#", CultureInfo.InvariantCulture)} %";
        }

        if (reading.Pid == 0x1F)
        {
            var span = TimeSpan.FromSeconds(value);
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes}:{span.Seconds:00}";
        }

        return _units.Format(value, reading.MetricUnit, format);
    }

    private string FuelSystemText(string? text)
    {
        var code = int.TryParse(text?.Replace("FUEL_STATUS_", string.Empty), out var parsed) ? parsed : 0;
        return code switch
        {
            1 => L("ღია ციკლი (ძრავა ცივია)", "Open loop (engine cold)"),
            2 => L("დახურული ციკლი (ნორმა)", "Closed loop (normal)"),
            4 => L("ღია ციკლი (დატვირთვა)", "Open loop (driving load)"),
            8 => L("ღია ციკლი (სისტემის ხარვეზი)", "Open loop (system fault)"),
            16 => L("დახურული ციკლი (ხარვეზით)", "Closed loop (with fault)"),
            _ => "—"
        };
    }

    private static string ValueColor(Mode02FreezeFrameValue reading)
    {
        if (reading.Number is not double value)
            return "#60A5FA";

        return reading.Pid switch
        {
            0x06 or 0x07 or 0x08 or 0x09 when Math.Abs(value) >= 20 => "#F87171",
            0x06 or 0x07 or 0x08 or 0x09 when Math.Abs(value) >= 10 => "#FBBF24",
            0x05 when value >= 110 => "#F87171",
            0x42 when value < 11.8 || value > 15.2 => "#FBBF24",
            _ => "#60A5FA"
        };
    }

    /// <summary>Short, plain-language summary of the conditions.</summary>
    private string BuildConditions(Mode02FreezeFrameResult snapshot)
    {
        double? Get(int pid) => snapshot.Values.FirstOrDefault(v => v.Pid == pid)?.Number;

        var parts = new List<string>();

        if (Get(0x05) is double coolant)
            parts.Add(coolant < 60
                ? L("ცივი ძრავა", "Cold engine")
                : coolant < 80 ? L("ძრავა თბება", "Warming up") : L("გამთბარი ძრავა", "Warm engine"));

        var speed = Get(0x0D);
        var rpm = Get(0x0C);
        if (speed is double s && s < 3)
            parts.Add(rpm is double r && r > 0 && r < 1100 ? L("უქმი სვლა, დგას", "Idling, stationary") : L("დგას", "Stationary"));
        else if (speed is double moving)
            parts.Add(L($"მოძრაობა {_units.FormatSpeed(moving)}", $"Driving {_units.FormatSpeed(moving)}"));

        if (Get(0x04) is double load && load >= 70)
            parts.Add(L("მაღალი დატვირთვა", "High load"));

        var trims = new[] { 0x06, 0x07, 0x08, 0x09 }.Select(Get).OfType<double>().ToArray();
        if (trims.Length > 0)
        {
            var total = trims.OrderByDescending(Math.Abs).First();
            if (total >= 10) parts.Add(L("ნარევი ღარიბი (კორექცია +)", "Lean mixture (trim +)"));
            else if (total <= -10) parts.Add(L("ნარევი მდიდარი (კორექცია −)", "Rich mixture (trim −)"));
        }

        return string.Join(" · ", parts);
    }
}
