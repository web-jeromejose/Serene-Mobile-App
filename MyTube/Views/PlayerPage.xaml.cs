using MyTube.ViewModels;

namespace MyTube.Views;

public partial class PlayerPage : ContentPage
{
    readonly PlayerViewModel _vm;
    readonly IDispatcherTimer _upNextTimer;
    int _countdown;
    bool _released;
    VideoItem? _sizedVideo;

    public PlayerPage(PlayerViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;

        _vm.PlayRequested += async (video, resume) =>
        {
            HideUpNext();
            if (DetailsList.ItemsSource is not null && _vm.UpNext.Count > 0)
                DetailsList.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
            await Player.LoadAsync(video, resume);
            ApplyVideoSize(video);
        };

        Player.Started += (_, _) => _vm.OnStarted();
        Player.ProgressChanged += (_, p) => _vm.OnProgress(p.Position, p.Duration);
        Player.Ended += (_, _) => OnEnded();

        _upNextTimer = Dispatcher.CreateTimer();
        _upNextTimer.Interval = TimeSpan.FromSeconds(1);
        _upNextTimer.Tick += (_, _) =>
        {
            if (--_countdown <= 0) OnPlayUpNext(this, EventArgs.Empty);
            else UpNextCountdown.Text = $"Up next in {_countdown}";
        };

        LayoutMetrics.Current.PropertyChanged += OnMetricsChanged;
    }

    /// <summary>Used when another video is chosen while this page is open.</summary>
    public void PlayInPlace(VideoItem video)
    {
        if (_vm.Video is not null) _vm.OnClosed();
        _vm.Load(video);
    }

    void OnMetricsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayoutMetrics.IsLandscape)) ApplyOrientation();
        else if (e.PropertyName == nameof(LayoutMetrics.PlayerHeight)) ApplyVideoSize(_vm.Video);
    }

    // Rotate to landscape → full-screen player, like YouTube.
    void ApplyOrientation()
    {
        var landscape = LayoutMetrics.Current.IsLandscape;
        DetailsList.IsVisible = false;
        BackButton.IsVisible = !landscape;
        Root.SafeAreaEdges = landscape ? new Microsoft.Maui.SafeAreaEdges(SafeAreaRegions.None) : new Microsoft.Maui.SafeAreaEdges(SafeAreaRegions.Container);
        Root.RowDefinitions[0].Height = GridLength.Star;
        Root.RowDefinitions[1].Height = new GridLength(0);
        Grid.SetRowSpan(PlayerHost, 1);
        PlayerHost.VerticalOptions = LayoutOptions.Fill;
        ApplyVideoSize(_vm.Video);
        Player.InvalidateMeasure();
#if ANDROID
        MyTube.Droid.SystemBars.HideForLandscape(landscape);
#endif
    }

    void ApplyVideoSize(VideoItem? video)
    {
        if (!ReferenceEquals(_sizedVideo, video))
        {
            if (_sizedVideo is not null) _sizedVideo.PropertyChanged -= OnVideoPropertyChanged;
            _sizedVideo = video;
            if (_sizedVideo is not null) _sizedVideo.PropertyChanged += OnVideoPropertyChanged;
        }

        var portrait = !LayoutMetrics.Current.IsLandscape && video?.IsPortrait == true;
        var normalHeight = Math.Max(1, LayoutMetrics.Current.PlayerHeight);
        var portraitHeight = Math.Min(
            Math.Max(1, LayoutMetrics.Current.Height * 0.72),
            Math.Max(1, LayoutMetrics.Current.Width * 16 / 9));

        if (portrait)
            PlayerHost.HeightRequest = portraitHeight;
        else if (LayoutMetrics.Current.IsLandscape)
            PlayerHost.ClearValue(HeightRequestProperty);
        else
            PlayerHost.HeightRequest = normalHeight;
        PlayerHost.MinimumHeightRequest = 1;
        PlayerHost.MaximumHeightRequest = -1;
    }

    void OnVideoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoItem.IsPortrait))
            MainThread.BeginInvokeOnMainThread(() => ApplyVideoSize(_vm.Video));
    }

    void OnEnded()
    {
        var next = _vm.NextVideo;
        if (!_vm.Autoplay || next is null) return;
        _countdown = 2;
        UpNextTitle.Text = next.Title;
        UpNextCountdown.Text = $"Up next in {_countdown}";
        UpNextOverlay.IsVisible = true;
        _upNextTimer.Start();
    }

    void HideUpNext()
    {
        _upNextTimer.Stop();
        UpNextOverlay.IsVisible = false;
    }

    void OnCancelUpNext(object? sender, EventArgs e) => HideUpNext();

    void OnPlayUpNext(object? sender, EventArgs e)
    {
        HideUpNext();
        if (_vm.NextVideo is { } next) _vm.PlayNowCommand.Execute(next);
    }

    async void OnBack(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("..");

    void OnCommentDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is LocalComment comment)
            _vm.DeleteCommentCommand.Execute(comment);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        App.ApplySystemBars();
        ApplyOrientation();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Player.Pause();
        HideUpNext();
        App.ApplySystemBars();
#if ANDROID
        MyTube.Droid.SystemBars.HideForLandscape(false);
#endif
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        // Popped (not just covered by another page): free the players.
        if (!_released && !Navigation.NavigationStack.Contains(this))
        {
            _released = true;
            _vm.OnClosed();
            LayoutMetrics.Current.PropertyChanged -= OnMetricsChanged;
            Player.Release();
        }
    }
}
