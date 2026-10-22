namespace MyTube.ViewModels;

/// <summary>Subscriptions: channels you follow (stored locally) and their videos.</summary>
public sealed partial class SubscriptionsViewModel(CatalogService catalog, LibraryService library) : ObservableObject
{
    string? _channelFilter;

    public ObservableCollection<ChannelInfo> Channels { get; } = [];
    public ObservableCollection<VideoItem> Videos { get; } = [];
    public ObservableCollection<ChannelInfo> Suggested { get; } = [];

    [ObservableProperty] private bool hasSubscriptions;

    public async Task RefreshAsync()
    {
        await catalog.EnsureLoadedAsync();
        var all = catalog.Channels;

        Channels.Clear();
        foreach (var c in all.Where(c => c.IsSubscribed)) Channels.Add(c);
        HasSubscriptions = Channels.Count > 0;
        if (_channelFilter is not null && Channels.All(c => c.Name != _channelFilter)) _channelFilter = null;

        Suggested.Clear();
        foreach (var c in all.Where(c => !c.IsSubscribed).Take(30)) Suggested.Add(c);

        Videos.Clear();
        var subscribed = Channels.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var videos = catalog.All
            .Where(v => _channelFilter is null ? subscribed.Contains(v.Channel) : string.Equals(v.Channel, _channelFilter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.Published ?? v.AddedAt);
        foreach (var v in videos) Videos.Add(v);
    }

    public async Task FilterAsync(ChannelInfo? channel)
    {
        _channelFilter = channel?.Name == _channelFilter ? null : channel?.Name;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ToggleSubscribe(ChannelInfo channel)
    {
        library.ToggleSubscribe(channel.Name);
        await RefreshAsync();
    }

    [RelayCommand]
    private Task OpenChannel(ChannelInfo channel) => Nav.OpenChannel(channel.Name);
}

/// <summary>"You" tab: history, Watch later, liked videos, your videos and playlists.</summary>
public sealed partial class LibraryViewModel(CatalogService catalog, LibraryService library, SettingsService settings) : ObservableObject
{
    public ObservableCollection<VideoItem> History { get; } = [];
    public ObservableCollection<PlaylistInfo> Playlists { get; } = [];

    [ObservableProperty] private string displayName = string.Empty;
    [ObservableProperty] private string initial = "?";
    [ObservableProperty] private Color avatarColor = Colors.Gray;
    [ObservableProperty] private string stats = string.Empty;
    [ObservableProperty] private bool hasHistory;

    public async Task RefreshAsync()
    {
        await catalog.EnsureLoadedAsync();
        DisplayName = settings.DisplayName;
        Initial = Formatters.Initial(DisplayName);
        AvatarColor = Formatters.AvatarColor(DisplayName);
        var own = catalog.OwnVideos.ToList();
        Stats = $"@{DisplayName.Replace(" ", string.Empty).ToLowerInvariant()} · {own.Count} of your videos · {catalog.All.Count} in MyTube";

        History.Clear();
        foreach (var v in catalog.FindMany(library.Data.History.Select(h => h.VideoId)).Take(20)) History.Add(v);
        HasHistory = History.Count > 0;

        Playlists.Clear();
        Playlists.Add(Make("history", null, "History", Glyphs.History, library.Data.History.Select(h => h.VideoId)));
        Playlists.Add(Make("watchlater", null, "Watch later", Glyphs.WatchLater, library.Data.WatchLater));
        Playlists.Add(Make("liked", null, "Liked videos", Glyphs.ThumbUp, library.Data.Liked));
        Playlists.Add(Make("own", null, "Your videos", Glyphs.Videocam, own.Select(v => v.Id)));
        foreach (var p in library.Data.Playlists.OrderByDescending(p => p.CreatedAt))
            Playlists.Add(Make("playlist", p.Id, p.Name, Glyphs.PlaylistPlay, p.VideoIds));
    }

    PlaylistInfo Make(string kind, string? id, string title, string glyph, IEnumerable<string> ids)
    {
        var videos = catalog.FindMany(ids).ToList();
        return new PlaylistInfo { Kind = kind, Id = id, Title = title, Glyph = glyph, Count = videos.Count, Thumbnail = videos.FirstOrDefault()?.ThumbnailSource };
    }

    [RelayCommand]
    private Task OpenPlaylist(PlaylistInfo playlist) => Nav.OpenPlaylist(playlist.Kind, playlist.Id);

    [RelayCommand]
    private async Task NewPlaylist()
    {
        if (Nav.CurrentPage is not { } page) return;
        var name = await page.DisplayPromptAsync("New playlist", "Name your playlist", "Create", "Cancel", "My playlist", 60);
        if (string.IsNullOrWhiteSpace(name)) return;
        library.CreatePlaylist(name);
        await RefreshAsync();
    }

    [RelayCommand]
    private Task OpenSettings() => Nav.OpenSettings();

    [RelayCommand]
    private Task OpenSearch() => Nav.OpenSearch();
}

/// <summary>Any list of videos: History, Watch later, Liked, Your videos or a custom playlist.</summary>
public sealed partial class PlaylistViewModel(CatalogService catalog, LibraryService library) : ObservableObject, IQueryAttributable
{
    string _kind = "watchlater";
    string? _id;

    public ObservableCollection<VideoItem> Items { get; } = [];

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string subtitle = string.Empty;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private bool canDelete;
    [ObservableProperty] private ImageSource? cover;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _kind = query.TryGetValue("kind", out var k) ? k as string ?? _kind : _kind;
        _id = query.TryGetValue("id", out var i) && i is string s && s.Length > 0 ? s : null;
        Refresh();
    }

    public void Refresh()
    {
        IEnumerable<string> ids;
        switch (_kind)
        {
            case "history":
                Title = "History";
                ids = library.Data.History.Select(h => h.VideoId);
                break;
            case "liked":
                Title = "Liked videos";
                ids = library.Data.Liked;
                break;
            case "own":
                Title = "Your videos";
                ids = catalog.OwnVideos.OrderByDescending(v => v.AddedAt).Select(v => v.Id);
                break;
            case "playlist":
                var playlist = library.Data.Playlists.FirstOrDefault(p => p.Id == _id);
                Title = playlist?.Name ?? "Playlist";
                ids = playlist?.VideoIds ?? [];
                break;
            default:
                Title = "Watch later";
                ids = library.Data.WatchLater;
                break;
        }

        Items.Clear();
        foreach (var v in catalog.FindMany(ids)) Items.Add(v);
        IsEmpty = Items.Count == 0;
        Subtitle = $"{(Items.Count == 1 ? "1 video" : $"{Items.Count} videos")} · Private · Saved on this device";
        CanDelete = _kind == "playlist";
        Cover = Items.FirstOrDefault()?.ThumbnailSource;
        OnPropertyChanged(nameof(ShowClear));
        OnPropertyChanged(nameof(ClearText));
    }

    [RelayCommand]
    private Task PlayAll() => Items.Count == 0 ? Task.CompletedTask : Nav.OpenVideo(Items[0]);

    [RelayCommand]
    private Task Shuffle() => Items.Count == 0 ? Task.CompletedTask : Nav.OpenVideo(Items[Random.Shared.Next(Items.Count)]);

    [RelayCommand]
    private void Remove(VideoItem video)
    {
        switch (_kind)
        {
            case "history": library.RemoveHistory(video.Id); break;
            case "liked": library.ToggleLike(video.Id); break;
            case "watchlater": library.ToggleWatchLater(video.Id); break;
            case "playlist" when _id is not null: library.RemoveFromPlaylist(_id, video.Id); break;
            case "own": catalog.Remove(video); break;
        }
        Refresh();
    }

    [RelayCommand]
    private async Task ClearOrDelete()
    {
        if (Nav.CurrentPage is not { } page) return;
        if (_kind == "history")
        {
            if (!await page.DisplayAlertAsync("Clear watch history?", "This also clears resume positions.", "Clear", "Cancel")) return;
            library.ClearHistory();
            Refresh();
        }
        else if (_kind == "playlist" && _id is not null)
        {
            if (!await page.DisplayAlertAsync("Delete playlist?", Title, "Delete", "Cancel")) return;
            library.DeletePlaylist(_id);
            await Shell.Current.GoToAsync("..");
        }
    }

    public bool ShowClear => _kind is "history" or "playlist";
    public string ClearText => _kind == "history" ? "Clear all" : "Delete";
}

/// <summary>Channel page built from your catalog (no account needed).</summary>
public sealed partial class ChannelViewModel(CatalogService catalog, LibraryService library) : ObservableObject, IQueryAttributable
{
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string initial = "?";
    [ObservableProperty] private Color color = Colors.Gray;
    [ObservableProperty] private string stats = string.Empty;
    [ObservableProperty] private bool isSubscribed;
    [ObservableProperty] private bool showShorts;

    public ObservableCollection<VideoItem> Videos { get; } = [];
    public ObservableCollection<VideoItem> Shorts { get; } = [];
    public ObservableCollection<ChipItem> Tabs { get; } = [new() { Name = "Videos", IsSelected = true }, new() { Name = "Shorts" }];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("channel", out var c) || c is not string channel) return;
        Name = channel;
        Initial = Formatters.Initial(channel);
        Color = Formatters.AvatarColor(channel);
        IsSubscribed = library.IsSubscribed(channel);

        var all = catalog.ByChannel(channel).ToList();
        Videos.Clear();
        foreach (var v in all.Where(v => !v.IsShort)) Videos.Add(v);
        Shorts.Clear();
        foreach (var v in all.Where(v => v.IsShort)) Shorts.Add(v);
        Stats = $"@{channel.Replace(" ", string.Empty).ToLowerInvariant()} · {Videos.Count} videos · {Shorts.Count} Shorts";
        if (Videos.Count == 0 && Shorts.Count > 0) SelectTab(Tabs[1]);
    }

    public void SelectTab(ChipItem tab)
    {
        foreach (var t in Tabs) t.IsSelected = t == tab;
        ShowShorts = tab.Name == "Shorts";
    }

    [RelayCommand]
    private void Subscribe()
    {
        IsSubscribed = library.ToggleSubscribe(Name);
        _ = Nav.Toast(IsSubscribed ? $"Subscribed to {Name}" : "Unsubscribed");
    }
}
