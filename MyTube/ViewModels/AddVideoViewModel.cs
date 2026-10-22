namespace MyTube.ViewModels;

/// <summary>
/// The "+" tab: add YouTube videos/Shorts by link (one or many), upload your own videos from the
/// phone (gallery or camera) or add a link to a video you host online.
/// </summary>
public sealed partial class AddVideoViewModel(CatalogService catalog, SettingsService settings) : ObservableObject
{
    [ObservableProperty] private string youTubeLink = string.Empty;
    [ObservableProperty] private string bulkLinks = string.Empty;
    [ObservableProperty] private string ownLink = string.Empty;
    [ObservableProperty] private string ownLinkTitle = string.Empty;
    [ObservableProperty] private bool ownLinkIsShort;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string status = string.Empty;

    // Upload draft (after picking a file)
    [ObservableProperty] private VideoItem? draft;
    [ObservableProperty] private string draftTitle = string.Empty;
    [ObservableProperty] private string draftDescription = string.Empty;
    [ObservableProperty] private string draftCategory = "My videos";
    [ObservableProperty] private bool draftIsShort;

    public ObservableCollection<VideoItem> RecentlyAdded { get; } = [];

    public bool CanRecord => MediaPicker.Default.IsCaptureSupported;

    public async Task RefreshAsync()
    {
        await catalog.EnsureLoadedAsync();
        RecentlyAdded.Clear();
        foreach (var v in catalog.All.Where(v => v.IsUserAdded).OrderByDescending(v => v.AddedAt).Take(15)) RecentlyAdded.Add(v);
    }

    [RelayCommand]
    private async Task Paste()
    {
        var text = await Clipboard.Default.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text)) YouTubeLink = text.Trim();
    }

    [RelayCommand]
    private async Task AddYouTube()
    {
        if (string.IsNullOrWhiteSpace(YouTubeLink) || IsBusy) return;
        IsBusy = true;
        Status = "Checking link…";
        try
        {
            var (video, error) = await catalog.AddYouTubeAsync(YouTubeLink);
            if (video is null)
            {
                Status = error ?? "Couldn't add that link.";
                return;
            }
            Status = $"Added {(video.IsShort ? "Short" : "video")}: {video.Title}";
            YouTubeLink = string.Empty;
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddBulk()
    {
        var lines = BulkLinks.Split(['\n', '\r', ' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || IsBusy) return;
        IsBusy = true;
        int added = 0, failed = 0;
        try
        {
            foreach (var line in lines)
            {
                Status = $"Adding {added + failed + 1} of {lines.Length}…";
                var (video, _) = await catalog.AddYouTubeAsync(line);
                if (video is null) failed++;
                else added++;
            }
            Status = failed == 0 ? $"Added {added} videos." : $"Added {added} videos, {failed} links failed.";
            if (failed == 0) BulkLinks = string.Empty;
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task PickVideo() => ImportAsync(async () =>
        (await MediaPicker.Default.PickVideosAsync(new MediaPickerOptions { Title = "Choose a video", SelectionLimit = 1 }))?.FirstOrDefault());

    [RelayCommand]
    private Task RecordVideo() => ImportAsync(() => MediaPicker.Default.CaptureVideoAsync(new MediaPickerOptions { Title = "Record a video" }));

    async Task ImportAsync(Func<Task<FileResult?>> pick)
    {
        if (IsBusy) return;
        try
        {
            var file = await pick();
            if (file is null) return;
            IsBusy = true;
            Status = "Preparing your video…";
            var (video, _) = await catalog.ImportFileAsync(file);
            Draft = video;
            DraftTitle = video.Title;
            DraftDescription = string.Empty;
            DraftCategory = "My videos";
            DraftIsShort = video.IsShort;
            Status = string.Empty;
        }
        catch (PermissionException)
        {
            Status = "Permission denied. Allow access to photos/camera in your phone's settings.";
        }
        catch (Exception ex)
        {
            Status = $"Couldn't import the video: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PublishDraft()
    {
        if (Draft is null) return;
        Draft.Title = string.IsNullOrWhiteSpace(DraftTitle) ? Draft.Title : DraftTitle.Trim();
        Draft.Description = string.IsNullOrWhiteSpace(DraftDescription) ? null : DraftDescription.Trim();
        Draft.Category = string.IsNullOrWhiteSpace(DraftCategory) ? "My videos" : DraftCategory.Trim();
        Draft.Format = DraftIsShort ? VideoFormat.Short : VideoFormat.Video;
        Draft.Channel = settings.DisplayName;
        catalog.Add(Draft);
        Status = $"Published \"{Draft.Title}\" to your channel.";
        Draft = null;
        await RefreshAsync();
    }

    [RelayCommand]
    private void DiscardDraft()
    {
        if (Draft?.MediaUri is { } uri)
        {
            try { File.Delete(LocalPaths.Resolve(uri)); } catch { }
        }
        Draft = null;
        Status = string.Empty;
    }

    [RelayCommand]
    private async Task AddOwnLink()
    {
        if (!Uri.TryCreate(OwnLink.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
        {
            Status = "Enter a full link that starts with https://";
            return;
        }
        if (YouTubeUrl.TryParse(OwnLink, out _))
        {
            YouTubeLink = OwnLink;
            OwnLink = string.Empty;
            await AddYouTube();
            return;
        }

        var video = await catalog.AddOwnUrlAsync(uri.ToString(), OwnLinkTitle, OwnLinkIsShort);
        Status = $"Added \"{video.Title}\" to your videos.";
        OwnLink = OwnLinkTitle = string.Empty;
        OwnLinkIsShort = false;
        await RefreshAsync();
    }
}

/// <summary>Settings: theme, playback, your name, online list, data management.</summary>
public sealed partial class SettingsViewModel(SettingsService settings, LibraryService library, CatalogService catalog) : ObservableObject
{
    public IReadOnlyList<string> Themes { get; } = ["Use device theme", "Light", "Dark"];

    [ObservableProperty] private int themeIndex = settings.Theme switch { AppTheme.Light => 1, AppTheme.Dark => 2, _ => 0 };
    [ObservableProperty] private bool autoplay = settings.AutoplayNext;
    [ObservableProperty] private bool shortsAutoAdvance = settings.ShortsAutoAdvance;
    [ObservableProperty] private bool includeOwn = settings.IncludeOwnVideos;
    [ObservableProperty] private string displayName = settings.DisplayName;
    [ObservableProperty] private string remoteUrl = settings.RemoteCatalogUrl;
    [ObservableProperty] private string syncStatus = settings.LastSyncStatus;
    [ObservableProperty] private bool isSyncing;

    public string Version => $"MyTube {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

    partial void OnThemeIndexChanged(int value) =>
        settings.Theme = value switch { 1 => AppTheme.Light, 2 => AppTheme.Dark, _ => AppTheme.Unspecified };

    partial void OnAutoplayChanged(bool value) => settings.AutoplayNext = value;
    partial void OnShortsAutoAdvanceChanged(bool value) => settings.ShortsAutoAdvance = value;

    partial void OnIncludeOwnChanged(bool value)
    {
        settings.IncludeOwnVideos = value;
        catalog.NotifyChanged();
    }

    [RelayCommand]
    private void SaveName()
    {
        settings.DisplayName = DisplayName;
        DisplayName = settings.DisplayName;
        _ = Nav.Toast("Name saved");
    }

    [RelayCommand]
    private async Task Sync()
    {
        settings.RemoteCatalogUrl = RemoteUrl;
        IsSyncing = true;
        SyncStatus = "Syncing…";
        SyncStatus = await catalog.SyncRemoteAsync();
        IsSyncing = false;
    }

    [RelayCommand]
    private async Task ClearHistory()
    {
        if (Nav.CurrentPage is { } page && await page.DisplayAlertAsync("Clear watch history?", "Resume positions are cleared too.", "Clear", "Cancel"))
        {
            library.ClearHistory();
            await Nav.Toast("Watch history cleared");
        }
    }

    [RelayCommand]
    private async Task ClearSearches()
    {
        library.ClearSearches();
        await Nav.Toast("Search history cleared");
    }

    [RelayCommand]
    private async Task Export()
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"mytube-library-{DateTime.Now:yyyyMMdd}.json");
        await File.WriteAllTextAsync(path, library.Export());
        await Share.Default.RequestAsync(new ShareFileRequest { Title = "MyTube library", File = new ShareFile(path, "application/json") });
    }

    [RelayCommand]
    private async Task Import()
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a MyTube library file" });
            if (file is null) return;
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            library.Import(await reader.ReadToEndAsync());
            await catalog.ReloadAsync();
            await Nav.Toast("Library imported");
        }
        catch (Exception ex)
        {
            await Nav.Toast($"Import failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ResetAll()
    {
        if (Nav.CurrentPage is not { } page) return;
        if (!await page.DisplayAlertAsync("Reset MyTube?", "Removes everything you added, liked, saved and watched on this device.", "Reset", "Cancel")) return;
        foreach (var v in library.Data.UserVideos.ToList()) catalog.Remove(v);
        library.ResetAll();
        catalog.NotifyChanged();
        await Nav.Toast("MyTube was reset");
    }
}
