using MyTube.ViewModels;

namespace MyTube.Views;

public partial class PlayerPage : ContentPage
{
    readonly PlayerViewModel _vm;
    readonly IDispatcherTimer _upNextTimer;
    int _countdown;
    bool _released;

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

        PlayerHost.SetBinding(HeightRequestProperty, new Binding(nameof(LayoutMetrics.PlayerHeight), source: LayoutMetrics.Current));
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
    }

    // Rotate to landscape → full-screen player, like YouTube.
    void ApplyOrientation()
    {
        var landscape = LayoutMetrics.Current.IsLandscape;
        DetailsList.IsVisible = !landscape;
        BackButton.IsVisible = !landscape;
        Root.SafeAreaEdges = landscape ? new Microsoft.Maui.SafeAreaEdges(SafeAreaRegions.None) : new Microsoft.Maui.SafeAreaEdges(SafeAreaRegions.Container);
        Grid.SetRowSpan(PlayerHost, landscape ? 2 : 1);
#if ANDROID
        MyTube.Droid.SystemBars.HideForLandscape(landscape);
#endif
    }

    void OnEnded()
    {
        var next = _vm.NextVideo;
        if (!_vm.Autoplay || next is null) return;
        _countdown = 5;
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
