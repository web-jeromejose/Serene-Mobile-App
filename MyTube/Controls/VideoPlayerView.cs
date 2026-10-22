using System.Globalization;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;

namespace MyTube.Controls;

/// <summary>
/// One player for everything: YouTube videos/Shorts play through the official IFrame player in a
/// WebView, your own videos play natively through MediaElement (ExoPlayer / AVPlayer).
/// Shows the thumbnail until playback starts and a friendly message when something can't play.
/// </summary>
public sealed class VideoPlayerView : ContentView
{
    static string? _htmlTemplate;

    readonly Grid _root = new() { BackgroundColor = Colors.Black };
    readonly Image _poster = new() { Aspect = Aspect.AspectFill, InputTransparent = true };
    readonly ActivityIndicator _spinner = new() { Color = Colors.White, IsRunning = false, WidthRequest = 40, HeightRequest = 40, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, InputTransparent = true };
    readonly VerticalStackLayout _errorPanel;
    readonly Label _errorLabel;
    readonly Button _errorButton;
    readonly IDispatcherTimer _timer;

    YouTubeWebView? _web;
    MediaElement? _media;
    VideoItem? _video;
    bool _htmlLoaded;
    bool _started;
    bool _ended;
    int _lastState = -2;
    int _pollFailures;
    double _position;
    double _duration;

    public VideoPlayerView()
    {
        _errorLabel = new Label { TextColor = Colors.White, FontSize = 15, HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(24, 0) };
        _errorButton = new Button { Text = "Watch on YouTube", BackgroundColor = Colors.White, TextColor = Colors.Black, HorizontalOptions = LayoutOptions.Center, CornerRadius = 18, Padding = new Thickness(18, 8) };
        _errorButton.Clicked += async (_, _) => { if (_video is not null) await VideoActions.OpenInYouTubeAsync(_video); };
        _errorPanel = new VerticalStackLayout
        {
            Spacing = 14,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
            Children =
            {
                new Label { Text = Glyphs.Error, FontFamily = "MaterialIcons", FontSize = 40, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center },
                _errorLabel,
                _errorButton,
            },
        };

        _root.Add(_poster);
        _root.Add(_spinner);
        _root.Add(_errorPanel);
        Content = _root;

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += async (_, _) => await PollYouTubeAsync();
    }

    /// <summary>Shorts mode: no player controls, video fills the frame.</summary>
    public bool ShortsMode { get; set; }

    /// <summary>Restart automatically at the end (Shorts).</summary>
    public bool Loop { get; set; }

    public bool IsPlaying { get; private set; }
    public VideoItem? Video => _video;

    public event EventHandler? Started;
    public event EventHandler? Ended;
    public event EventHandler<bool>? PlayingChanged;
    public event EventHandler<(double Position, double Duration)>? ProgressChanged;

    public async Task LoadAsync(VideoItem video, double startSeconds = 0)
    {
        _video = video;
        _started = _ended = false;
        _lastState = -2;
        _pollFailures = 0;
        _position = startSeconds;
        _duration = video.DurationSeconds ?? 0;
        _errorPanel.IsVisible = false;
        _poster.Source = video.ThumbnailSource;
        _poster.Aspect = ShortsMode ? Aspect.AspectFill : Aspect.AspectFit;
        _poster.IsVisible = true;
        _spinner.IsRunning = _spinner.IsVisible = true;
        SetPlaying(false);

        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet
            && (video.IsYouTube || video.MediaUri?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true))
        {
            ShowError("You're offline. Connect to the internet to watch this video.", showYouTube: false);
            return;
        }

        if (video.IsYouTube) await LoadYouTubeAsync(video, startSeconds);
        else LoadOwn(video, startSeconds);
    }

    async Task LoadYouTubeAsync(VideoItem video, double start)
    {
        if (_media is not null)
        {
            _media.Stop();
            _media.IsVisible = false;
        }

        if (_web is null)
        {
            _web = new YouTubeWebView();
            _root.Insert(0, _web);
            _htmlLoaded = false;
        }
        _web.IsVisible = true;

        if (_htmlLoaded)
        {
            await EvalAsync($"appLoop({(Loop ? "true" : "false")});appLoad('{video.YouTubeId}',{start.ToString("0", CultureInfo.InvariantCulture)})");
        }
        else
        {
            _htmlTemplate ??= await ReadTemplateAsync();
            var html = _htmlTemplate
                .Replace("__VIDEO_ID__", video.YouTubeId)
                .Replace("__START__", start.ToString("0", CultureInfo.InvariantCulture))
                .Replace("__CONTROLS__", ShortsMode ? "0" : "1")
                .Replace("__LOOP__", Loop ? "true" : "false");
            if (_web.Handler is null)
                await WaitForHandlerAsync(_web);
            _web.LoadPlayerHtml(html);
        }
        _timer.Start();
    }

    static async Task<string> ReadTemplateAsync()
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync("player.html");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    static Task WaitForHandlerAsync(View view)
    {
        var tcs = new TaskCompletionSource();
        void OnChanged(object? s, EventArgs e)
        {
            if (view.Handler is null) return;
            view.HandlerChanged -= OnChanged;
            tcs.TrySetResult();
        }
        view.HandlerChanged += OnChanged;
        return Task.WhenAny(tcs.Task, Task.Delay(3000));
    }

    void LoadOwn(VideoItem video, double start)
    {
        _timer.Stop();
        if (_web is not null)
        {
            _ = EvalAsync("appPause()");
            _web.IsVisible = false;
        }

        if (_media is null)
        {
            _media = new MediaElement
            {
                ShouldAutoPlay = true,
                ShouldKeepScreenOn = true,
                BackgroundColor = Colors.Black,
            };
            _media.MediaOpened += OnMediaOpened;
            _media.MediaEnded += OnMediaEnded;
            _media.MediaFailed += OnMediaFailed;
            _media.StateChanged += OnMediaStateChanged;
            _media.PositionChanged += OnMediaPositionChanged;
            _root.Insert(0, _media);
        }

        _media.IsVisible = true;
        _media.ShouldShowPlaybackControls = !ShortsMode;
        _media.ShouldLoopPlayback = Loop;
        _media.Aspect = ShortsMode ? Aspect.AspectFill : Aspect.AspectFit;
        _media.MetadataTitle = video.Title;
        _media.MetadataArtist = video.Channel;
        _pendingSeek = start;
        _media.Source = ToMediaSource(video.MediaUri!);
    }

    double _pendingSeek;

    static MediaSource ToMediaSource(string uri)
    {
        if (uri.StartsWith("embed://", StringComparison.Ordinal))
            return MediaSource.FromResource(uri["embed://".Length..]);
        if (uri.StartsWith(LocalPaths.LocalScheme, StringComparison.Ordinal))
            return MediaSource.FromFile(LocalPaths.Resolve(uri));
        return MediaSource.FromUri(uri);
    }

    async void OnMediaOpened(object? sender, EventArgs e)
    {
        if (_media is null) return;
        _duration = _media.Duration.TotalSeconds;
        if (_pendingSeek > 1)
        {
            try { await _media.SeekTo(TimeSpan.FromSeconds(_pendingSeek), CancellationToken.None); } catch { }
        }
        _pendingSeek = 0;
    }

    void OnMediaEnded(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(() =>
    {
        SetPlaying(false);
        if (!Loop) Ended?.Invoke(this, EventArgs.Empty);
    });

    void OnMediaFailed(object? sender, MediaFailedEventArgs e) => MainThread.BeginInvokeOnMainThread(() =>
        ShowError("This video couldn't be played. Check the file or link.", showYouTube: false));

    void OnMediaStateChanged(object? sender, MediaStateChangedEventArgs e) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (e.NewState == MediaElementState.Playing)
        {
            MarkStarted();
            SetPlaying(true);
        }
        else if (e.NewState is MediaElementState.Paused or MediaElementState.Stopped)
        {
            SetPlaying(false);
        }
        _spinner.IsRunning = _spinner.IsVisible = e.NewState is MediaElementState.Opening or MediaElementState.Buffering;
    });

    void OnMediaPositionChanged(object? sender, MediaPositionChangedEventArgs e)
    {
        if (_media is null) return;
        _position = e.Position.TotalSeconds;
        _duration = _media.Duration.TotalSeconds;
        MainThread.BeginInvokeOnMainThread(() => ProgressChanged?.Invoke(this, (_position, _duration)));
    }

    async Task PollYouTubeAsync()
    {
        if (_web is null || _video is null || !_video.IsYouTube) return;

        string? result;
        try
        {
            result = await _web.EvaluateJavaScriptAsync("appState()");
        }
        catch
        {
            result = null;
        }

        if (string.IsNullOrEmpty(result) || !result.Contains('|'))
        {
            // Page not loaded yet. If it never loads, show a hint instead of an endless spinner.
            if (++_pollFailures == 40)
                ShowError("The YouTube player didn't load. Check your connection.", showYouTube: true);
            return;
        }

        _htmlLoaded = true;
        _pollFailures = 0;
        var parts = result.Trim('"').Split('|');
        if (parts.Length < 4) return;

        var error = int.TryParse(parts[3], out var err) ? err : 0;
        if (error != 0)
        {
            ShowError(error switch
            {
                100 => "This video is private or was removed.",
                101 or 150 => "The owner doesn't allow this video to play inside other apps.",
                153 => "YouTube couldn't verify the app. Try again or open it in YouTube.",
                _ => "This video can't be played here.",
            }, showYouTube: true);
            _timer.Stop();
            return;
        }

        if (parts[0] == "loading") return;
        var state = int.TryParse(parts[0], out var s) ? s : -1;
        _ = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _position);
        if (double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0) _duration = d;

        switch (state)
        {
            case 1: // playing
                MarkStarted();
                _spinner.IsRunning = _spinner.IsVisible = false;
                _ended = false;
                SetPlaying(true);
                break;
            case 2: // paused
                SetPlaying(false);
                break;
            case 3: // buffering
                _spinner.IsRunning = _spinner.IsVisible = true;
                break;
            // "Ended" only counts once this video has actually played; right after switching videos
            // the player can still report the previous video's ended state.
            case 0 when _started && !_ended && !Loop:
                _ended = true;
                SetPlaying(false);
                Ended?.Invoke(this, EventArgs.Empty);
                break;
        }

        if (state != _lastState || state == 1)
            ProgressChanged?.Invoke(this, (_position, _duration));
        _lastState = state;
    }

    void MarkStarted()
    {
        if (_started) return;
        _started = true;
        _poster.IsVisible = false;
        _spinner.IsRunning = _spinner.IsVisible = false;
        Started?.Invoke(this, EventArgs.Empty);
    }

    void SetPlaying(bool playing)
    {
        if (IsPlaying == playing) return;
        IsPlaying = playing;
        PlayingChanged?.Invoke(this, playing);
    }

    void ShowError(string message, bool showYouTube)
    {
        _spinner.IsRunning = _spinner.IsVisible = false;
        _poster.IsVisible = true;
        _errorLabel.Text = message;
        _errorButton.IsVisible = showYouTube && _video?.IsYouTube == true;
        _errorPanel.IsVisible = true;
        SetPlaying(false);
    }

    public double Position => _position;
    public double Duration => _duration;

    public void Play()
    {
        if (_video?.IsYouTube == true) _ = EvalAsync("appPlay()");
        else _media?.Play();
    }

    public void Pause()
    {
        if (_video?.IsYouTube == true) _ = EvalAsync("appPause()");
        else _media?.Pause();
        SetPlaying(false);
    }

    public void TogglePlay()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void SeekTo(double seconds)
    {
        if (_video?.IsYouTube == true) _ = EvalAsync($"appSeek({seconds.ToString("0.0", CultureInfo.InvariantCulture)})");
        else _ = _media?.SeekTo(TimeSpan.FromSeconds(seconds), CancellationToken.None);
    }

    async Task EvalAsync(string script)
    {
        if (_web is null) return;
        try { await _web.EvaluateJavaScriptAsync(script); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"JS failed: {ex.Message}"); }
    }

    /// <summary>Stops playback and frees the native players. Call when the screen closes.</summary>
    public void Release()
    {
        _timer.Stop();
        if (_web is not null)
        {
            _web.Source = new HtmlWebViewSource { Html = "<html><body style='background:#000'></body></html>" };
            _web.Handler?.DisconnectHandler();
            _root.Remove(_web);
            _web = null;
            _htmlLoaded = false;
        }
        if (_media is not null)
        {
            _media.Stop();
            _media.Handler?.DisconnectHandler();
            _root.Remove(_media);
            _media = null;
        }
        _video = null;
        SetPlaying(false);
    }
}
