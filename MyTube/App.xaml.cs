namespace MyTube;

public partial class App : Application
{
    public App(SettingsService settings)
    {
        InitializeComponent();
        UserAppTheme = settings.Theme;
        RequestedThemeChanged += (_, _) => ApplySystemBars();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell()) { Title = "MyTube" };
        window.SizeChanged += (_, _) => LayoutMetrics.Current.Update(window.Width, window.Height);
        window.Created += (_, _) => ApplySystemBars();
        window.Resumed += (_, _) => ApplySystemBars();
        return window;
    }

    /// <summary>Status/navigation bar icons that stay readable in light and dark mode.</summary>
    public static void ApplySystemBars(bool? forceDark = null)
    {
#if ANDROID
        var dark = forceDark ?? (Current?.RequestedTheme == AppTheme.Dark);
        MainThread.BeginInvokeOnMainThread(() => MyTube.Droid.SystemBars.SetLightBackground(!dark));
#endif
    }
}
