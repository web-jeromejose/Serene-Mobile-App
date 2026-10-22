namespace MyTube.Controls;

/// <summary>
/// WebView that hosts the YouTube IFrame player. Loads HTML with an https base URL built from the
/// app id, which YouTube requires to identify embedded players (otherwise "Error 153").
/// </summary>
public sealed class YouTubeWebView : WebView
{
    public static string BaseUrl => $"https://{AppInfo.Current.PackageName.ToLowerInvariant()}/";

    public YouTubeWebView()
    {
        BackgroundColor = Colors.Black;
        Navigating += OnNavigating;
    }

    public void LoadPlayerHtml(string html)
    {
#if ANDROID
        if (Handler?.PlatformView is Android.Webkit.WebView androidView)
        {
            androidView.LoadDataWithBaseURL(BaseUrl, html, "text/html", "UTF-8", null);
            return;
        }
#elif IOS
        if (Handler?.PlatformView is WebKit.WKWebView iosView)
        {
            iosView.LoadHtmlString(html, new Foundation.NSUrl(BaseUrl));
            return;
        }
#endif
        Source = new HtmlWebViewSource { Html = html, BaseUrl = BaseUrl };
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (Handler?.PlatformView is Android.Webkit.WebView view)
        {
            view.Settings.JavaScriptEnabled = true;
            view.Settings.DomStorageEnabled = true;
            view.Settings.MediaPlaybackRequiresUserGesture = false; // autoplay
            view.SetBackgroundColor(Android.Graphics.Color.Black);
            view.SetWebChromeClient(new MyTube.Droid.FullscreenChromeClient());
        }
#endif
    }

    // Tapping the YouTube logo/title tries to open youtube.com inside the player: open it outside instead.
    static void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        var url = e.Url ?? string.Empty;
        var isWatchPage = url.Contains("youtube.com/watch", StringComparison.OrdinalIgnoreCase)
                          || url.Contains("youtube.com/shorts", StringComparison.OrdinalIgnoreCase)
                          || url.Contains("youtube.com/channel", StringComparison.OrdinalIgnoreCase)
                          || url.Contains("youtube.com/@", StringComparison.OrdinalIgnoreCase)
                          || url.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase);
        if (!isWatchPage) return;
        e.Cancel = true;
        _ = Launcher.Default.OpenAsync(url);
    }
}
