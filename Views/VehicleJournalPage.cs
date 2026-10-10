using CarScanner.Services;
using CarScanner.Services.Localization;
using Microsoft.Maui.Controls.Shapes;

namespace CarScanner.Views;

/// <summary>
/// One light page: "Scan" builds a plain-text report from everything the app has stored, "Save" writes it to a
/// .txt file and opens the share sheet (Notes, Keep, Drive, Files...). Built in code so it is a single file.
/// </summary>
public sealed class VehicleJournalPage : ContentPage
{
    private readonly VehicleJournalService _journal;
    private readonly LocalizationService _localization;

    private readonly Button _scan;
    private readonly Button _save;
    private readonly ActivityIndicator _busy;
    private readonly Label _status;
    private readonly Label _report;
    private readonly Border _reportCard;

    private string? _text;

    public VehicleJournalPage(VehicleJournalService journal, LocalizationService localization)
    {
        _journal = journal;
        _localization = localization;

        Title = T("მანქანის ანგარიში", "Vehicle report");
        this.SetDynamicResource(BackgroundColorProperty, "PageBg");

        var heading = new Label { Text = T("მანქანის ანგარიში", "Vehicle report"), FontSize = 28, FontAttributes = FontAttributes.Bold };
        heading.SetDynamicResource(Label.TextColorProperty, "TextColor");

        var hint = new Label
        {
            Text = T("დააჭირე სკანირებას — აპი ECU-ს წაიკითხავს (თუ ადაპტერი დაკავშირებულია), შენახულ მონაცემებს გადახედავს და მოკლე ტექსტურ ანგარიშს ააწყობს.",
                "Tap Scan — the app reads the ECU (if the adapter is connected), goes through your saved data and builds a short text report."),
            FontSize = 13
        };
        hint.SetDynamicResource(Label.TextColorProperty, "SubTextColor");

        _scan = new Button { Text = T("სკანირება", "Scan"), HeightRequest = 54, CornerRadius = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
        _scan.SetDynamicResource(Button.BackgroundColorProperty, "Primary");
        _scan.Clicked += async (_, _) => await ScanAsync();

        _save = new Button { Text = "💾  " + T("შენახვა", "Save"), HeightRequest = 54, CornerRadius = 16, FontAttributes = FontAttributes.Bold, IsVisible = false, BorderWidth = 1 };
        _save.SetDynamicResource(Button.BackgroundColorProperty, "ButtonBg");
        _save.SetDynamicResource(Button.TextColorProperty, "ButtonTextColor");
        _save.SetDynamicResource(Button.BorderColorProperty, "BorderColor");
        _save.Clicked += async (_, _) => await SaveAsync();

        _busy = new ActivityIndicator { IsRunning = false, IsVisible = false, HorizontalOptions = LayoutOptions.Center };

        _status = new Label { FontSize = 12, IsVisible = false, HorizontalTextAlignment = TextAlignment.Center };
        _status.SetDynamicResource(Label.TextColorProperty, "SubTextColor");

        _report = new Label { FontSize = 14, LineBreakMode = LineBreakMode.WordWrap };
        _report.SetDynamicResource(Label.TextColorProperty, "TextColor");

        _reportCard = new Border
        {
            IsVisible = false,
            Padding = new Thickness(16),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = _report
        };
        _reportCard.SetDynamicResource(Border.BackgroundColorProperty, "CardBg");
        _reportCard.SetDynamicResource(Border.StrokeProperty, "BorderColor");

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 16, 16, 28),
                Spacing = 12,
                Children = { heading, hint, _scan, _busy, _status, _reportCard, _save }
            }
        };
    }

    private string T(string ka, string en) => _localization.IsEnglish ? en : ka;

    private async Task ScanAsync()
    {
        _scan.IsEnabled = false;
        _save.IsVisible = false;
        _busy.IsVisible = _busy.IsRunning = true;
        _status.IsVisible = true;
        _status.Text = T("ECU და შენახული მონაცემები მოწმდება…", "Checking the ECU and saved data…");

        try
        {
            var progress = new Progress<string>(text => _status.Text = text);
            _text = await _journal.BuildAsync(progress);
            _report.Text = _text;
            _reportCard.IsVisible = true;
            _save.IsVisible = true;
            _status.Text = T("მზადაა · ", "Ready · ") + DateTime.Now.ToString("HH:mm");
        }
        catch (Exception ex)
        {
            _reportCard.IsVisible = false;
            _status.Text = T("ანგარიშის აწყობა ვერ მოხერხდა: ", "Could not build the report: ") + ex.Message;
        }
        finally
        {
            _busy.IsRunning = _busy.IsVisible = false;
            _scan.IsEnabled = true;
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_text))
            return;

        try
        {
            var folder = System.IO.Path.Combine(FileSystem.AppDataDirectory, "reports");
            Directory.CreateDirectory(folder);

            var path = System.IO.Path.Combine(folder, $"car-report-{DateTime.Now:yyyyMMdd-HHmm}.txt");
            await File.WriteAllTextAsync(path, _text);

            _status.Text = T("შენახულია. აირჩიე სად გინდა: Notes, Drive, Keep…", "Saved. Pick where to put it: Notes, Drive, Keep…");

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = T("მანქანის ანგარიში", "Vehicle report"),
                File = new ShareFile(path, "text/plain")
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(T("შეცდომა", "Error"), ex.Message, "OK");
        }
    }
}
