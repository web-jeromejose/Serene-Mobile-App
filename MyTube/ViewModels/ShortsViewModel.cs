namespace MyTube.ViewModels;

/// <summary>Shorts: a vertical, swipeable, shuffled queue of YouTube Shorts and your vertical videos.</summary>
public sealed partial class ShortsViewModel(CatalogService catalog, LibraryService library, SettingsService settings) : ObservableObject
{
    List<VideoItem> _queue = [];
    int _index = -1;
    VideoItem? _pendingStart;

    [ObservableProperty] private VideoItem? current;
    [ObservableProperty] private bool isLiked;
    [ObservableProperty] private bool isDisliked;
    [ObservableProperty] private bool isSaved;
    [ObservableProperty] private bool isSubscribed;
    [ObservableProperty] private string commentCount = "0";
    [ObservableProperty] private bool isEmpty;

    public bool AutoAdvance => settings.ShortsAutoAdvance;

    /// <summary>Called when a Short is tapped elsewhere (Home shelf, search, channel…).</summary>
    public void StartWith(VideoItem video) => _pendingStart = video;

    /// <summary>Returns the Short to play now (null if nothing changed).</summary>
    public async Task<VideoItem?> PrepareAsync()
    {
        await catalog.EnsureLoadedAsync();

        if (_pendingStart is { } start)
        {
            _pendingStart = null;
            _queue = [start, .. CatalogService.Shuffle(catalog.Shorts.Where(s => s.Id != start.Id))];
            _index = -1;
            return Move(1);
        }

        if (_queue.Count == 0 || Current is null)
        {
            _queue = CatalogService.Shuffle(catalog.Shorts);
            _index = -1;
            IsEmpty = _queue.Count == 0;
            return Move(1);
        }

        return null;
    }

    public VideoItem? Next() => Move(1);
    public VideoItem? Previous() => _index > 0 ? Move(-1) : null;

    VideoItem? Move(int delta)
    {
        if (_queue.Count == 0)
        {
            IsEmpty = true;
            return null;
        }

        var next = _index + delta;
        if (next >= _queue.Count)
        {
            // Endless: append another shuffled round.
            var round = CatalogService.Shuffle(catalog.Shorts);
            if (round.Count == 0) return null;
            if (round.Count > 1 && round[0].Id == _queue[^1].Id) (round[0], round[^1]) = (round[^1], round[0]);
            _queue.AddRange(round);
        }

        _index = Math.Clamp(next, 0, _queue.Count - 1);
        Current = _queue[_index];
        IsEmpty = false;
        RefreshState();
        return Current;
    }

    public void RefreshState()
    {
        if (Current is null) return;
        IsLiked = library.IsLiked(Current.Id);
        IsDisliked = library.IsDisliked(Current.Id);
        IsSaved = library.IsInWatchLater(Current.Id);
        IsSubscribed = library.IsSubscribed(Current.Channel);
        CommentCount = library.CommentCount(Current.Id).ToString();
    }

    public void MarkWatched()
    {
        if (Current is not null) library.AddHistory(Current.Id);
    }

    [RelayCommand]
    private void Like()
    {
        if (Current is null) return;
        library.ToggleLike(Current.Id);
        RefreshState();
    }

    [RelayCommand]
    private void Dislike()
    {
        if (Current is null) return;
        library.ToggleDislike(Current.Id);
        RefreshState();
    }

    [RelayCommand]
    private async Task Save()
    {
        if (Current is null) return;
        await VideoActions.ToggleWatchLaterAsync(Current);
        RefreshState();
    }

    [RelayCommand]
    private Task Share() => Current is null ? Task.CompletedTask : VideoActions.ShareAsync(Current);

    [RelayCommand]
    private void Subscribe()
    {
        if (Current is null) return;
        library.ToggleSubscribe(Current.Channel);
        RefreshState();
    }

    [RelayCommand]
    private Task OpenChannel() => Current is null ? Task.CompletedTask : Nav.OpenChannel(Current.Channel);

    [RelayCommand]
    private Task More() => Current is null ? Task.CompletedTask : VideoActions.ShowMenuAsync(Current);

    [RelayCommand]
    private async Task Comments()
    {
        if (Current is null || Nav.CurrentPage is not { } page) return;
        var comments = library.GetComments(Current.Id).Take(8).Select(c => $"{c.Author}: {c.Text}").ToList();
        var message = comments.Count == 0 ? "No comments yet. Comments are saved on this device only." : string.Join("\n\n", comments);
        var text = await page.DisplayPromptAsync("Comments", message, "Post", "Close", "Add a comment…", 500);
        if (string.IsNullOrWhiteSpace(text)) return;
        library.AddComment(Current.Id, settings.DisplayName, text);
        RefreshState();
        await Nav.Toast("Comment posted");
    }
}
