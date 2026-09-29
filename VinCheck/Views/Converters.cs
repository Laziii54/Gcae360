using System.Globalization;
using CarScanner.VinCheck.Models;
using CarScanner.VinCheck.ViewModels;

namespace CarScanner.VinCheck.Views;

public sealed class NotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        System.Collections.ICollection x => x.Count > 0,
        string s => !string.IsNullOrWhiteSpace(s),
        _ => value is not null
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class StarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as bool?) == true ? "★" : "☆";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class AuctionImagesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AuctionRecord auction)
            return Array.Empty<PhotoThumb>();

        // Thumbnails under the main photo (the main photo is the first one).
        var all = auction.UniqueImages.ToList();
        const int shown = 8;
        return all.Skip(1).Take(shown).Select((url, i) => new PhotoThumb
        {
            Url = url,
            Overlay = i == shown - 1 && all.Count > shown + 1 ? $"+{all.Count - shown - 1}" : "",
            Selection = new PhotoSelection { Images = all, Index = i + 1 }
        }).ToList();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PhotoThumb
{
    public string Url { get; init; } = "";
    public string Overlay { get; init; } = "";
    public PhotoSelection Selection { get; init; } = new();
}

/// <summary>Opens the gallery at the first (main) photo of an auction lot.</summary>
public sealed class FirstPhotoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new PhotoSelection { Images = (value as AuctionRecord)?.UniqueImages.ToList() ?? [], Index = 0 };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Make ("Mercedes-Benz") → brand logo in Resources/Images/brands.</summary>
public sealed class BrandLogoConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["mercedes_benz"] = "mercedes",
        ["mercedes"] = "mercedes",
        ["vw"] = "volkswagen",
        ["chevy"] = "chevrolet",
        ["range_rover"] = "land_rover",
        ["landrover"] = "land_rover",
        ["rolls"] = "rolls_royce",
        ["alfa"] = "alfa_romeo",
        ["kgm"] = "ssangyong",
        ["ssang_yong"] = "ssangyong",
        ["great_wall"] = "haval",
        ["gwm"] = "haval",
        ["ds_automobiles"] = "ds",
        ["škoda"] = "skoda"
    };

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "acura","alfa_romeo","audi","bentley","bmw","byd","cadillac","chery","chevrolet","chrysler","citroen","dacia",
        "daewoo","dodge","dongfeng","ds","ferrari","fiat","ford","geely","gmc","haval","honda","hongqi","hyundai",
        "infiniti","isuzu","jac","jaguar","jeep","kia","lamborghini","lancia","land_rover","lexus","lincoln","lucid",
        "maserati","mazda","mclaren","mercedes","mg","mini","mitsubishi","nio","nissan","opel","peugeot","polestar",
        "porsche","ram","renault","rivian","rolls_royce","saab","seat","skoda","smart","ssangyong","subaru","suzuki",
        "tesla","toyota","uaz","volkswagen","volvo","zeekr"
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string make || string.IsNullOrWhiteSpace(make))
            return null;

        var key = new string(make.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        while (key.Contains("__")) key = key.Replace("__", "_");
        if (Aliases.TryGetValue(key, out var alias)) key = alias;
        else if (!Known.Contains(key) && Aliases.TryGetValue(key.Split('_')[0], out alias)) key = alias;

        if (key == "genesis") return "genesis.jpg";
        if (key == "lada") return "lada.jpg";
        return Known.Contains(key) ? key + ".png" : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Every photo of a lot (for the sliding main photo).</summary>
public sealed class AllPhotosConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AuctionRecord auction)
            return Array.Empty<PhotoThumb>();

        var all = auction.UniqueImages.ToList();
        return all.Select((url, i) => new PhotoThumb
        {
            Url = url,
            Selection = new PhotoSelection { Images = all, Index = i }
        }).ToList();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Moves a CarouselView to the next item every few seconds (loops).
/// Stops while the view is off screen.
/// </summary>
public sealed class AutoAdvanceBehavior : Behavior<CarouselView>
{
    private CarouselView? _view;
    private IDispatcherTimer? _timer;

    public double IntervalSeconds { get; set; } = 2;

    protected override void OnAttachedTo(CarouselView view)
    {
        base.OnAttachedTo(view);
        _view = view;
        view.Loaded += OnLoaded;
        view.Unloaded += OnUnloaded;
        if (view.IsLoaded)
            Start();
    }

    protected override void OnDetachingFrom(CarouselView view)
    {
        view.Loaded -= OnLoaded;
        view.Unloaded -= OnUnloaded;
        Stop();
        _view = null;
        base.OnDetachingFrom(view);
    }

    private void OnLoaded(object? sender, EventArgs e) => Start();

    private void OnUnloaded(object? sender, EventArgs e) => Stop();

    private void Start()
    {
        if (_view is null || _timer is not null)
            return;

        _timer = _view.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, IntervalSeconds));
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void Stop()
    {
        if (_timer is null)
            return;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_view?.ItemsSource is not System.Collections.ICollection items || items.Count < 2)
            return;

        try
        {
            _view.Position = (_view.Position + 1) % items.Count;
        }
        catch
        {
            // The view may be closing.
        }
    }
}
