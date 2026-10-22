namespace MyTube.Services;

/// <summary>
/// The list of everything the app can play. Merges, in order:
/// 1. Resources/Raw/catalog.json (your YouTube links, Shorts and own videos)
/// 2. Video files dropped into Resources/Raw/videos (picked up automatically)
/// 3. An optional online catalog (Settings → Online video list)
/// 4. Videos you added inside the app (+ tab)
/// </summary>
public sealed class CatalogService(LibraryService library, SettingsService settings, YouTubeMetadataService youTube)
{
    readonly Dictionary<string, VideoItem> _byId = new(StringComparer.Ordinal);
    List<VideoItem> _all = [];
    Task? _loading;
    string _defaultOwnChannel = "My Channel";

    static string RemoteCachePath => Path.Combine(FileSystem.AppDataDirectory, "remote-catalog.json");

    /// <summary>Raised on the main thread when videos are added, removed or refreshed.</summary>
    public event Action? Changed;

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    /// <summary>All visible videos (not marked "Not interested"; own videos only if enabled).</summary>
    public IReadOnlyList<VideoItem> All =>
        _all.Where(v => !library.IsHidden(v.Id) && (settings.IncludeOwnVideos || !v.IsOwn || v.IsUserAdded)).ToList();

    public IEnumerable<VideoItem> Videos => All.Where(v => !v.IsShort);
    public IEnumerable<VideoItem> Shorts => All.Where(v => v.IsShort);
    public IEnumerable<VideoItem> OwnVideos => _all.Where(v => v.IsOwn);

    public VideoItem? Find(string id) => _byId.GetValueOrDefault(id);

    public IEnumerable<VideoItem> FindMany(IEnumerable<string> ids) =>
        ids.Select(Find).Where(v => v is not null).Cast<VideoItem>();

    public IReadOnlyList<string> Categories =>
        All.Where(v => !string.IsNullOrWhiteSpace(v.Category))
           .GroupBy(v => v.Category!, StringComparer.OrdinalIgnoreCase)
           .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
           .Select(g => g.Key).ToList();

    public IReadOnlyList<ChannelInfo> Channels =>
        All.GroupBy(v => v.Channel, StringComparer.OrdinalIgnoreCase)
           .Select(g => new ChannelInfo { Name = g.Key, VideoCount = g.Count(), IsSubscribed = library.IsSubscribed(g.Key) })
           .OrderByDescending(c => c.VideoCount).ThenBy(c => c.Name)
           .ToList();

    public IEnumerable<VideoItem> ByChannel(string channel) =>
        All.Where(v => string.Equals(v.Channel, channel, StringComparison.OrdinalIgnoreCase));

    public static List<VideoItem> Shuffle(IEnumerable<VideoItem> items)
    {
        var array = items.ToArray();
        Random.Shared.Shuffle(array);
        return [.. array];
    }

    /// <summary>Videos to show under the player: same channel/category first, then a random mix.</summary>
    public List<VideoItem> UpNext(VideoItem current, int count = 20)
    {
        var pool = Videos.Where(v => v.Id != current.Id).ToList();
        var related = Shuffle(pool.Where(v => v.Channel == current.Channel || (v.Category is not null && v.Category == current.Category)));
        var others = Shuffle(pool.Except(related));
        return related.Take(6).Concat(others).Concat(related.Skip(6)).Take(count).ToList();
    }

    public List<VideoItem> Search(string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return [];
        return All.Select(v => (Video: v, Score: Score(v, terms)))
                  .Where(x => x.Score > 0)
                  .OrderByDescending(x => x.Score).ThenByDescending(x => x.Video.AddedAt)
                  .Select(x => x.Video).ToList();

        static int Score(VideoItem v, string[] terms)
        {
            var score = 0;
            foreach (var term in terms)
            {
                var hit = 0;
                if (v.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) hit += 4;
                if (v.Channel.Contains(term, StringComparison.OrdinalIgnoreCase)) hit += 3;
                if (v.Category?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) hit += 2;
                if (v.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase))) hit += 2;
                if (v.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) hit += 1;
                if (hit == 0) return 0; // every word must match somewhere
                score += hit;
            }
            return score;
        }
    }

    // ------------------------------------------------------------------ loading

    async Task LoadAsync()
    {
        var items = new List<VideoItem>();

        // 1. Bundled catalog.json
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("catalog.json");
            var catalog = await JsonSerializer.DeserializeAsync(stream, AppJson.Default.CatalogFile);
            if (catalog is not null)
            {
                _defaultOwnChannel = string.IsNullOrWhiteSpace(catalog.ChannelName) ? settings.DisplayName : catalog.ChannelName;
                items.AddRange(catalog.Videos.Select(e => FromEntry(e, _defaultOwnChannel)).OfType<VideoItem>());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"catalog.json could not be read: {ex.Message}");
        }

        // 2. Any video file in Resources/Raw/videos that catalog.json doesn't describe
        foreach (var file in MediaProbe.ListBundledVideos())
        {
            var id = "asset:" + file;
            if (items.Any(i => i.Id == id)) continue;
            items.Add(new VideoItem
            {
                Id = id,
                Source = VideoSource.Own,
                MediaUri = "embed://" + file,
                Title = Formatters.FriendlyBundledTitle(file),
                Channel = _defaultOwnChannel,
                Category = "My videos",
            });
        }

        // 3. Cached online catalog (refreshed in the background below)
        items.AddRange(ReadRemoteCache());

        // 4. Videos added inside the app
        items.AddRange(library.Data.UserVideos);

        Rebuild(items);
        _ = Task.Run(EnrichAsync);

        // Refresh the online list once per app start (a sync reloads the catalog, so never re-trigger it).
        if (!_autoSynced && !string.IsNullOrWhiteSpace(settings.RemoteCatalogUrl))
        {
            _autoSynced = true;
            _ = SyncRemoteAsync();
        }
    }

    bool _autoSynced;

    void Rebuild(IEnumerable<VideoItem> items)
    {
        _byId.Clear();
        foreach (var item in items)
        {
            if (library.Data.Durations.TryGetValue(item.Id, out var d) && item.DurationSeconds is null) item.DurationSeconds = d;
            if (library.Data.MetaCache.TryGetValue(item.Id, out var meta) && item.Title.Length == 0)
            {
                item.Title = meta.Title ?? item.Title;
                item.Channel = meta.Channel ?? item.Channel;
            }
            if (item.Title.Length == 0) item.Title = item.IsShort ? "YouTube Short" : "YouTube video";
            if (item.Channel.Length == 0) item.Channel = item.IsOwn ? _defaultOwnChannel : "YouTube";
            item.WatchProgress = library.GetProgress(item.Id);
            _byId[item.Id] = item; // later sources win (your own edits override the catalog)
        }
        _all = [.. _byId.Values];
    }

    void RaiseChanged() => MainThread.BeginInvokeOnMainThread(() => Changed?.Invoke());

    /// <summary>Re-reads watch progress after playback so thumbnails show the red bar.</summary>
    public void RefreshProgress(string id)
    {
        if (Find(id) is { } v) v.WatchProgress = library.GetProgress(id);
    }

    VideoItem? FromEntry(CatalogEntry e, string defaultChannel)
    {
        var item = new VideoItem
        {
            Title = e.Title?.Trim() ?? string.Empty,
            Channel = e.Channel?.Trim() ?? string.Empty,
            Category = e.Category,
            Description = e.Description,
            Thumbnail = e.Thumbnail,
            Tags = e.Tags ?? [],
            Views = e.Views,
            DurationSeconds = Formatters.ParseDuration(e.Duration),
            Published = DateTimeOffset.TryParse(e.Published, out var p) ? p : null,
        };

        if (!string.IsNullOrWhiteSpace(e.File))
        {
            var file = e.File.Replace('\\', '/').TrimStart('/');
            item.Id = "asset:" + file;
            item.Source = VideoSource.Own;
            item.MediaUri = "embed://" + file;
            item.Format = e.Short == true ? VideoFormat.Short : VideoFormat.Video;
            if (item.Title.Length == 0) item.Title = Formatters.TitleFromFileName(file);
            if (item.Channel.Length == 0) item.Channel = defaultChannel;
            return item;
        }

        if (string.IsNullOrWhiteSpace(e.Url)) return null;

        if (e.Own != true && YouTubeUrl.TryParse(e.Url, out var link))
        {
            item.Id = "yt:" + link.VideoId;
            item.Source = VideoSource.YouTube;
            item.YouTubeId = link.VideoId;
            item.Format = e.Short ?? link.IsShort ? VideoFormat.Short : VideoFormat.Video;
            return item;
        }

        item.Id = "url:" + LocalPaths.Hash(e.Url);
        item.Source = VideoSource.Own;
        item.MediaUri = e.Url.Trim();
        item.Format = e.Short == true ? VideoFormat.Short : VideoFormat.Video;
        if (item.Title.Length == 0) item.Title = Formatters.TitleFromFileName(new Uri(e.Url).AbsolutePath);
        if (item.Channel.Length == 0) item.Channel = defaultChannel;
        return item;
    }

    /// <summary>Fills in missing YouTube titles and creates thumbnails for own videos, in the background.</summary>
    async Task EnrichAsync()
    {
        var changed = false;
        using var throttle = new SemaphoreSlim(4);

        var youTubeTasks = _all
            .Where(v => v.IsYouTube && (v.Title is "YouTube video" or "YouTube Short") && !library.Data.MetaCache.ContainsKey(v.Id))
            .Select(async v =>
            {
                await throttle.WaitAsync();
                try
                {
                    var (meta, _) = await youTube.GetAsync(v.YouTubeId!);
                    if (meta is null) return;
                    MainThread.BeginInvokeOnMainThread(() => { v.Title = meta.Title; v.Channel = meta.Channel; });
                    library.CacheMeta(v.Id, meta.Title, meta.Channel);
                    changed = true;
                }
                finally { throttle.Release(); }
            });

        var ownTasks = _all
            .Where(v => v.IsOwn && string.IsNullOrWhiteSpace(v.Thumbnail) && v.MediaUri is not null)
            .Select(async v =>
            {
                await throttle.WaitAsync();
                try { changed |= await CreateThumbnailAsync(v); }
                finally { throttle.Release(); }
            });

        await Task.WhenAll(youTubeTasks.Concat(ownTasks));
        if (changed) RaiseChanged();
    }

    async Task<bool> CreateThumbnailAsync(VideoItem v)
    {
        var relative = $"thumbs/{LocalPaths.Hash(v.Id)}.jpg";
        var path = Path.Combine(LocalPaths.Folder("thumbs"), Path.GetFileName(relative));
        if (File.Exists(path))
        {
            MainThread.BeginInvokeOnMainThread(() => v.Thumbnail = LocalPaths.LocalScheme + relative);
            return true;
        }

        var info = await MediaProbe.ProbeAsync(v.MediaUri!, path);
        if (info is null) return false;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (info.HasThumbnail) v.Thumbnail = LocalPaths.LocalScheme + relative;
            if (info.DurationSeconds is > 0 && v.DurationSeconds is null)
            {
                var isShort = info.DurationSeconds < 11;
                v.DurationSeconds = info.DurationSeconds;
                v.IsPortrait = info.Height > info.Width && info.Width > 0;
                v.Format = isShort ? VideoFormat.Short : VideoFormat.Video;
                v.Category = isShort ? "Shorts" : "My videos";
                if (v.Id.StartsWith("asset:", StringComparison.Ordinal))
                {
                    v.Title = Formatters.FriendlyBundledTitle(v.MediaUri ?? v.Id);
                    v.Description = Formatters.FriendlyBundledDescription(v.MediaUri ?? v.Id, isShort);
                }
            }
        });
        return info.HasThumbnail || info.DurationSeconds is > 0;
    }

    // ------------------------------------------------------------------ online catalog

    List<VideoItem> ReadRemoteCache()
    {
        try
        {
            if (!File.Exists(RemoteCachePath)) return [];
            var catalog = JsonSerializer.Deserialize(File.ReadAllText(RemoteCachePath), AppJson.Default.CatalogFile);
            return catalog?.Videos.Select(e => FromEntry(e, catalog.ChannelName ?? _defaultOwnChannel)).OfType<VideoItem>().ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Downloads the online catalog (Settings) and merges it. Returns a status message.</summary>
    public async Task<string> SyncRemoteAsync()
    {
        var url = settings.RemoteCatalogUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            if (File.Exists(RemoteCachePath)) File.Delete(RemoteCachePath);
            await ReloadAsync();
            return settings.LastSyncStatus = "No online list set.";
        }

        try
        {
            var text = await YouTubeMetadataService.DownloadStringAsync(url);
            CatalogFile catalog;
            if (text.TrimStart().StartsWith('{'))
            {
                catalog = JsonSerializer.Deserialize(text, AppJson.Default.CatalogFile) ?? new();
            }
            else
            {
                // Also accept a plain text file with one link per line.
                catalog = new CatalogFile
                {
                    Videos = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                 .Where(l => !l.StartsWith('#'))
                                 .Select(l => new CatalogEntry { Url = l }).ToList(),
                };
            }

            await File.WriteAllTextAsync(RemoteCachePath, JsonSerializer.Serialize(catalog, AppJson.Default.CatalogFile));
            await ReloadAsync();
            return settings.LastSyncStatus = $"Synced {catalog.Videos.Count} videos · {DateTime.Now:g}";
        }
        catch (Exception ex)
        {
            return settings.LastSyncStatus = $"Sync failed: {ex.Message}";
        }
    }

    /// <summary>Re-reads every source (used after restoring a backup).</summary>
    public async Task ReloadAsync()
    {
        _loading = null;
        await EnsureLoadedAsync();
        RaiseChanged();
    }

    // ------------------------------------------------------------------ adding / removing

    public async Task<(VideoItem? Video, string? Error)> AddYouTubeAsync(string url, string? category = null)
    {
        await EnsureLoadedAsync();
        if (!YouTubeUrl.TryParse(url, out var link))
            return (null, "That doesn't look like a YouTube link.");

        var id = "yt:" + link.VideoId;
        if (Find(id) is { } existing && !library.IsHidden(id))
            return (existing, null);

        var (meta, error) = await youTube.GetAsync(link.VideoId);
        if (meta is null) return (null, error);

        var video = new VideoItem
        {
            Id = id,
            Source = VideoSource.YouTube,
            YouTubeId = link.VideoId,
            Format = link.IsShort || meta.IsVertical ? VideoFormat.Short : VideoFormat.Video,
            Title = meta.Title,
            Channel = meta.Channel,
            Category = string.IsNullOrWhiteSpace(category) ? null : category,
            IsUserAdded = true,
        };
        Add(video);
        return (video, null);
    }

    /// <summary>Copies a picked/recorded video into app storage and reads its details.</summary>
    public async Task<(VideoItem Video, MediaInfo? Info)> ImportFileAsync(FileResult file)
    {
        var id = "own:" + Guid.NewGuid().ToString("N");
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext)) ext = ".mp4";
        var relative = $"videos/{id[4..]}{ext}";
        var target = Path.Combine(LocalPaths.Folder("videos"), Path.GetFileName(relative));

        await using (var source = await file.OpenReadAsync())
        await using (var destination = File.Create(target))
            await source.CopyToAsync(destination);

        var video = new VideoItem
        {
            Id = id,
            Source = VideoSource.Own,
            MediaUri = LocalPaths.LocalScheme + relative,
            Title = Formatters.TitleFromFileName(file.FileName),
            Channel = settings.DisplayName,
            Category = "My videos",
            IsUserAdded = true,
        };

        var thumbRelative = $"thumbs/{LocalPaths.Hash(id)}.jpg";
        var info = await MediaProbe.ProbeAsync(video.MediaUri, Path.Combine(LocalPaths.Folder("thumbs"), Path.GetFileName(thumbRelative)));
        if (info is not null)
        {
            if (info.HasThumbnail) video.Thumbnail = LocalPaths.LocalScheme + thumbRelative;
            video.DurationSeconds = info.DurationSeconds;
            video.Format = info.IsVertical ? VideoFormat.Short : VideoFormat.Video;
        }
        return (video, info);
    }

    public async Task<VideoItem> AddOwnUrlAsync(string url, string? title, bool isShort)
    {
        await EnsureLoadedAsync();
        var video = new VideoItem
        {
            Id = "url:" + LocalPaths.Hash(url),
            Source = VideoSource.Own,
            MediaUri = url.Trim(),
            Title = string.IsNullOrWhiteSpace(title) ? Formatters.TitleFromFileName(new Uri(url).AbsolutePath) : title.Trim(),
            Channel = settings.DisplayName,
            Category = "My videos",
            Format = isShort ? VideoFormat.Short : VideoFormat.Video,
            IsUserAdded = true,
        };
        Add(video);
        _ = Task.Run(() => CreateThumbnailAsync(video));
        return video;
    }

    public void Add(VideoItem video)
    {
        video.AddedAt = DateTimeOffset.UtcNow;
        library.AddUserVideo(video);
        _byId[video.Id] = video;
        _all = [.. _byId.Values];
        RaiseChanged();
    }

    /// <summary>Removes a video you added (and its file, if it was uploaded from the phone).</summary>
    public void Remove(VideoItem video)
    {
        if (video.IsUserAdded)
        {
            library.RemoveUserVideo(video.Id);
            if (video.MediaUri?.StartsWith(LocalPaths.LocalScheme, StringComparison.Ordinal) == true)
                TryDelete(LocalPaths.Resolve(video.MediaUri));
            if (video.Thumbnail?.StartsWith(LocalPaths.LocalScheme, StringComparison.Ordinal) == true)
                TryDelete(LocalPaths.Resolve(video.Thumbnail));
            _byId.Remove(video.Id);
            _all = [.. _byId.Values];
        }
        else
        {
            library.Hide(video.Id); // catalog videos are hidden instead
        }
        RaiseChanged();

        static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    public void NotifyChanged() => RaiseChanged();
}
