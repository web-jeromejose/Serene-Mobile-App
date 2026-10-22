namespace MyTube.Helpers;

/// <summary>
/// Screen-dependent sizes (16:9 thumbnails, 9:16 Shorts cards, player height),
/// recalculated whenever the window size changes (rotation, split screen, tablets).
/// </summary>
public sealed partial class LayoutMetrics : ObservableObject
{
    public static LayoutMetrics Current { get; } = new();

    [ObservableProperty] private double width = 400;
    [ObservableProperty] private double height = 800;
    [ObservableProperty] private double feedThumbHeight = 225;
    [ObservableProperty] private double playerHeight = 225;
    [ObservableProperty] private double shortCardWidth = 170;
    [ObservableProperty] private double shortCardHeight = 302;
    [ObservableProperty] private bool isLandscape;

    LayoutMetrics()
    {
        var info = DeviceDisplay.Current.MainDisplayInfo;
        if (info.Density > 0 && info.Width > 0)
            Update(info.Width / info.Density, info.Height / info.Density);
    }

    public void Update(double w, double h)
    {
        if (w <= 0 || h <= 0) return;
        Width = w;
        Height = h;
        IsLandscape = w > h;

        // Phones show one full-width card; tablets/landscape get a grid of cards.
        var columns = w >= 1000 ? 3 : w >= 640 ? 2 : 1;
        var cardWidth = columns == 1 ? w : (w - 12 * (columns + 1)) / columns;
        FeedThumbHeight = Math.Round(cardWidth * 9 / 16);
        PlayerHeight = IsLandscape ? h : Math.Round(w * 9 / 16);

        ShortCardWidth = Math.Round(Math.Min(220, (w - 36) / 2.15));
        ShortCardHeight = Math.Round(ShortCardWidth * 16 / 9);
    }

    public int FeedColumns => Width >= 1000 ? 3 : Width >= 640 ? 2 : 1;

    partial void OnWidthChanged(double value) => OnPropertyChanged(nameof(FeedColumns));
}
