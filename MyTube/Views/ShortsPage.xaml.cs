using MyTube.ViewModels;

namespace MyTube.Views;

public partial class ShortsPage : ContentPage
{
    readonly ShortsViewModel _vm;
    bool _visible;
    bool _animating;

    public ShortsPage(ShortsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;

        Player.Started += (_, _) => _vm.MarkWatched();
        Player.PlayingChanged += (_, playing) => PausedBadge.IsVisible = !playing && _vm.Current is not null && Player.Video is not null && _userPaused;
        Player.ProgressChanged += (_, p) => Progress.Progress = p.Duration > 0 ? p.Position / p.Duration : 0;
        Player.Ended += async (_, _) =>
        {
            if (_vm.AutoAdvance) await GoAsync(+1);
        };
    }

    bool _userPaused;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        App.ApplySystemBars(forceDark: true);

        var video = await _vm.PrepareAsync();
        if (!_visible) return;
        if (video is not null) await PlayAsync(video);
        else if (Player.Video is not null && !_userPaused) Player.Play();
        _vm.RefreshState();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _visible = false;
        Player.Pause();
        App.ApplySystemBars();
    }

    async Task PlayAsync(VideoItem video)
    {
        _userPaused = false;
        PausedBadge.IsVisible = false;
        Progress.Progress = 0;
        Player.Loop = !_vm.AutoAdvance;
        await Player.LoadAsync(video);
    }

    void OnTapped(object? sender, TappedEventArgs e)
    {
        if (Player.Video is null) return;
        _userPaused = Player.IsPlaying;
        Player.TogglePlay();
        PausedBadge.IsVisible = _userPaused;
    }

    void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_animating) return;
        switch (e.StatusType)
        {
            case GestureStatus.Running:
                Stage.TranslationY = e.TotalY * 0.5;
                _lastPanY = e.TotalY;
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                var dy = _lastPanY;
                _lastPanY = 0;
                if (dy < -70) _ = GoAsync(+1);
                else if (dy > 70) _ = GoAsync(-1);
                else _ = Stage.TranslateToAsync(0, 0, 150, Easing.CubicOut);
                break;
        }
    }

    double _lastPanY;

    async Task GoAsync(int direction)
    {
        if (_animating) return;
        var next = direction > 0 ? _vm.Next() : _vm.Previous();
        if (next is null)
        {
            await Stage.TranslateToAsync(0, 0, 150, Easing.CubicOut);
            return;
        }

        _animating = true;
        try
        {
            var h = Height > 0 ? Height : 800;
            await Stage.TranslateToAsync(0, -direction * h, 180, Easing.CubicIn);
            Stage.TranslationY = direction * h;
            await PlayAsync(next);
            await Stage.TranslateToAsync(0, 0, 200, Easing.CubicOut);
        }
        finally
        {
            _animating = false;
        }
    }

    async void OnSearch(object? sender, TappedEventArgs e) => await Nav.OpenSearch();
}
