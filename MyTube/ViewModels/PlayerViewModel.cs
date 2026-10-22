namespace MyTube.ViewModels;

/// <summary>Watch page: player, likes, subscribe, share, save, description, local comments and Up next.</summary>
public sealed partial class PlayerViewModel(CatalogService catalog, LibraryService library, SettingsService settings)
    : ObservableObject, IQueryAttributable
{
    [ObservableProperty] private VideoItem? video;
    [ObservableProperty] private bool isLiked;
    [ObservableProperty] private bool isDisliked;
    [ObservableProperty] private bool isSaved;
    [ObservableProperty] private bool isSubscribed;
    [ObservableProperty] private bool isDescriptionExpanded;
    [ObservableProperty] private string newComment = string.Empty;
    [ObservableProperty] private string commentsHeader = "Comments";
    [ObservableProperty] private int channelVideoCount;
    [ObservableProperty] private bool autoplay;

    public ObservableCollection<VideoItem> UpNext { get; } = [];
    public ObservableCollection<LocalComment> Comments { get; } = [];

    public string DisplayName => settings.DisplayName;
    public string DisplayInitial => Formatters.Initial(settings.DisplayName);
    public Color DisplayColor => Formatters.AvatarColor(settings.DisplayName);

    /// <summary>Raised when the page should (re)load the player with <see cref="Video"/>.</summary>
    public event Action<VideoItem, double>? PlayRequested;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("video", out var value) && value is VideoItem item)
            Load(item);
    }

    public void Load(VideoItem item)
    {
        Video = item;
        _lastSavedPosition = 0;
        Autoplay = settings.AutoplayNext;
        IsDescriptionExpanded = false;
        RefreshState();
        ChannelVideoCount = catalog.ByChannel(item.Channel).Count();

        UpNext.Clear();
        foreach (var v in catalog.UpNext(item)) UpNext.Add(v);

        Comments.Clear();
        foreach (var c in library.GetComments(item.Id)) Comments.Add(c);
        CommentsHeader = Comments.Count == 0 ? "Comments" : $"Comments  {Comments.Count}";

        PlayRequested?.Invoke(item, library.GetResume(item.Id));
    }

    void RefreshState()
    {
        if (Video is null) return;
        IsLiked = library.IsLiked(Video.Id);
        IsDisliked = library.IsDisliked(Video.Id);
        IsSaved = library.IsInWatchLater(Video.Id);
        IsSubscribed = library.IsSubscribed(Video.Channel);
    }

    public void OnStarted()
    {
        if (Video is not null) library.AddHistory(Video.Id);
    }

    public void OnProgress(double position, double duration)
    {
        if (Video is null || duration <= 0) return;
        if (Video.DurationSeconds is null || Math.Abs(Video.DurationSeconds.Value - duration) > 1)
            Video.DurationSeconds = duration;
        // Save the resume point every few seconds, not on every progress tick.
        if (Math.Abs(position - _lastSavedPosition) < 5 && position < duration - 10) return;
        _lastSavedPosition = position;
        library.SetResume(Video.Id, position, duration);
    }

    double _lastSavedPosition;

    public void OnClosed()
    {
        if (Video is not null) catalog.RefreshProgress(Video.Id);
    }

    public VideoItem? NextVideo => UpNext.FirstOrDefault();

    partial void OnAutoplayChanged(bool value) => settings.AutoplayNext = value;

    [RelayCommand]
    private void PlayNow(VideoItem item)
    {
        if (Video is not null) catalog.RefreshProgress(Video.Id);
        if (item.IsShort) _ = Nav.OpenShort(item);
        else Load(item);
    }

    [RelayCommand]
    private void Like()
    {
        if (Video is null) return;
        library.ToggleLike(Video.Id);
        RefreshState();
    }

    [RelayCommand]
    private void Dislike()
    {
        if (Video is null) return;
        library.ToggleDislike(Video.Id);
        RefreshState();
    }

    [RelayCommand]
    private async Task Save()
    {
        if (Video is null) return;
        await VideoActions.ToggleWatchLaterAsync(Video);
        RefreshState();
    }

    [RelayCommand]
    private Task AddToPlaylist() => Video is null ? Task.CompletedTask : VideoActions.SaveToPlaylistAsync(Video);

    [RelayCommand]
    private Task Share() => Video is null ? Task.CompletedTask : VideoActions.ShareAsync(Video);

    [RelayCommand]
    private Task OpenInYouTube() => Video is null ? Task.CompletedTask : VideoActions.OpenInYouTubeAsync(Video);

    [RelayCommand]
    private void Subscribe()
    {
        if (Video is null) return;
        var subscribed = library.ToggleSubscribe(Video.Channel);
        RefreshState();
        _ = Nav.Toast(subscribed ? $"Subscribed to {Video.Channel}" : "Unsubscribed");
    }

    [RelayCommand]
    private Task OpenChannel() => Video is null ? Task.CompletedTask : Nav.OpenChannel(Video.Channel);

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

    [RelayCommand]
    private void PostComment()
    {
        if (Video is null || string.IsNullOrWhiteSpace(NewComment)) return;
        var comment = library.AddComment(Video.Id, settings.DisplayName, NewComment);
        Comments.Insert(0, comment);
        CommentsHeader = $"Comments  {Comments.Count}";
        NewComment = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteComment(LocalComment comment)
    {
        if (Nav.CurrentPage is not { } page) return;
        if (!await page.DisplayAlertAsync("Delete comment?", comment.Text, "Delete", "Cancel")) return;
        library.DeleteComment(comment.Id);
        Comments.Remove(comment);
        CommentsHeader = Comments.Count == 0 ? "Comments" : $"Comments  {Comments.Count}";
    }

    [RelayCommand]
    private Task More() => Video is null ? Task.CompletedTask : VideoActions.ShowMenuAsync(Video);
}
