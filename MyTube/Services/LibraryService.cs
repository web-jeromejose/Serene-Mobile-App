namespace MyTube.Services;

/// <summary>
/// Likes, history, Watch later, playlists, subscriptions, comments… all saved to a JSON
/// file on the device. Nothing requires an account.
/// </summary>
public sealed class LibraryService
{
    readonly string _path = Path.Combine(FileSystem.AppDataDirectory, "library.json");
    readonly Lock _gate = new();
    CancellationTokenSource? _pendingSave;

    public LibraryService()
    {
        try
        {
            if (File.Exists(_path))
                Data = JsonSerializer.Deserialize(File.ReadAllText(_path), AppJson.Default.LibraryData) ?? new();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Library load failed: {ex}");
            Data = new();
        }
    }

    public LibraryData Data { get; private set; } = new();

    /// <summary>Raised (on the caller's thread) whenever something in the library changes.</summary>
    public event Action? Changed;

    // ---------- Likes / dislikes ----------
    public bool IsLiked(string id) => Data.Liked.Contains(id);
    public bool IsDisliked(string id) => Data.Disliked.Contains(id);

    public bool ToggleLike(string id)
    {
        Data.Disliked.Remove(id);
        var liked = Toggle(Data.Liked, id);
        Save();
        return liked;
    }

    public bool ToggleDislike(string id)
    {
        Data.Liked.Remove(id);
        var disliked = Toggle(Data.Disliked, id);
        Save();
        return disliked;
    }

    // ---------- Watch later ----------
    public bool IsInWatchLater(string id) => Data.WatchLater.Contains(id);

    public bool ToggleWatchLater(string id)
    {
        var added = Toggle(Data.WatchLater, id);
        Save();
        return added;
    }

    // ---------- History ----------
    public void AddHistory(string id)
    {
        Data.History.RemoveAll(h => h.VideoId == id);
        Data.History.Insert(0, new HistoryEntry { VideoId = id, WatchedAt = DateTimeOffset.UtcNow });
        if (Data.History.Count > 500) Data.History.RemoveRange(500, Data.History.Count - 500);
        Save();
    }

    public void RemoveHistory(string id)
    {
        Data.History.RemoveAll(h => h.VideoId == id);
        Save();
    }

    public void ClearHistory()
    {
        Data.History.Clear();
        Data.ResumePositions.Clear();
        Save();
    }

    // ---------- Resume positions & durations ----------
    public double GetResume(string id) => Data.ResumePositions.GetValueOrDefault(id);

    public double GetProgress(string id)
    {
        var pos = GetResume(id);
        var dur = Data.Durations.GetValueOrDefault(id);
        return pos > 0 && dur > 0 ? Math.Clamp(pos / dur, 0, 1) : 0;
    }

    public void SetResume(string id, double position, double duration)
    {
        if (duration > 0) Data.Durations[id] = duration;
        // Like YouTube: don't resume the first seconds or the very end.
        if (position < 10 || (duration > 0 && position > duration - 10)) Data.ResumePositions.Remove(id);
        else Data.ResumePositions[id] = position;
        Save(notify: false);
    }

    public void SetDuration(string id, double duration)
    {
        if (duration <= 0 || Data.Durations.TryGetValue(id, out var d) && Math.Abs(d - duration) < 1) return;
        Data.Durations[id] = duration;
        Save(notify: false);
    }

    // ---------- Subscriptions ----------
    public bool IsSubscribed(string channel) => Data.Subscriptions.Contains(channel, StringComparer.OrdinalIgnoreCase);

    public bool ToggleSubscribe(string channel)
    {
        var existing = Data.Subscriptions.FirstOrDefault(c => string.Equals(c, channel, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) Data.Subscriptions.Remove(existing);
        else Data.Subscriptions.Add(channel);
        Save();
        return existing is null;
    }

    // ---------- Playlists ----------
    public Playlist CreatePlaylist(string name)
    {
        var playlist = new Playlist { Name = name.Trim() };
        Data.Playlists.Add(playlist);
        Save();
        return playlist;
    }

    public void AddToPlaylist(string playlistId, string videoId)
    {
        var playlist = Data.Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (playlist is null || playlist.VideoIds.Contains(videoId)) return;
        playlist.VideoIds.Add(videoId);
        Save();
    }

    public void RemoveFromPlaylist(string playlistId, string videoId)
    {
        Data.Playlists.FirstOrDefault(p => p.Id == playlistId)?.VideoIds.Remove(videoId);
        Save();
    }

    public void DeletePlaylist(string playlistId)
    {
        Data.Playlists.RemoveAll(p => p.Id == playlistId);
        Save();
    }

    // ---------- Comments (saved on this device only) ----------
    public IEnumerable<LocalComment> GetComments(string videoId) =>
        Data.Comments.Where(c => c.VideoId == videoId).OrderByDescending(c => c.CreatedAt);

    public int CommentCount(string videoId) => Data.Comments.Count(c => c.VideoId == videoId);

    public LocalComment AddComment(string videoId, string author, string text)
    {
        var comment = new LocalComment { VideoId = videoId, Author = author, Text = text.Trim() };
        Data.Comments.Add(comment);
        Save();
        return comment;
    }

    public void DeleteComment(string commentId)
    {
        Data.Comments.RemoveAll(c => c.Id == commentId);
        Save();
    }

    // ---------- Search history ----------
    public void AddSearch(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return;
        Data.SearchHistory.RemoveAll(q => string.Equals(q, query, StringComparison.OrdinalIgnoreCase));
        Data.SearchHistory.Insert(0, query);
        if (Data.SearchHistory.Count > 30) Data.SearchHistory.RemoveRange(30, Data.SearchHistory.Count - 30);
        Save(notify: false);
    }

    public void RemoveSearch(string query)
    {
        Data.SearchHistory.Remove(query);
        Save(notify: false);
    }

    public void ClearSearches()
    {
        Data.SearchHistory.Clear();
        Save(notify: false);
    }

    // ---------- Not interested ----------
    public bool IsHidden(string id) => Data.Hidden.Contains(id);

    public void Hide(string id)
    {
        if (!Data.Hidden.Contains(id)) Data.Hidden.Add(id);
        Save();
    }

    // ---------- Videos you added in the app ----------
    public void AddUserVideo(VideoItem video)
    {
        Data.UserVideos.RemoveAll(v => v.Id == video.Id);
        Data.UserVideos.Add(video);
        Data.Hidden.Remove(video.Id);
        Save();
    }

    public void RemoveUserVideo(string id)
    {
        Data.UserVideos.RemoveAll(v => v.Id == id);
        Save();
    }

    public void CacheMeta(string id, string? title, string? channel)
    {
        Data.MetaCache[id] = new CachedMeta { Title = title, Channel = channel };
        Save(notify: false);
    }

    // ---------- Export / import / reset ----------
    public string Export() => JsonSerializer.Serialize(Data, AppJson.Default.LibraryData);

    public void Import(string json)
    {
        var incoming = JsonSerializer.Deserialize(json, AppJson.Default.LibraryData) ?? throw new InvalidDataException("Not a MyTube library file.");
        foreach (var v in incoming.UserVideos.Where(v => Data.UserVideos.All(u => u.Id != v.Id))) Data.UserVideos.Add(v);
        MergeList(Data.Liked, incoming.Liked);
        MergeList(Data.WatchLater, incoming.WatchLater);
        MergeList(Data.Subscriptions, incoming.Subscriptions);
        MergeList(Data.Hidden, incoming.Hidden);
        foreach (var p in incoming.Playlists.Where(p => Data.Playlists.All(x => x.Id != p.Id))) Data.Playlists.Add(p);
        foreach (var c in incoming.Comments.Where(c => Data.Comments.All(x => x.Id != c.Id))) Data.Comments.Add(c);
        foreach (var h in incoming.History.Where(h => Data.History.All(x => x.VideoId != h.VideoId))) Data.History.Add(h);
        Data.History.Sort((a, b) => b.WatchedAt.CompareTo(a.WatchedAt));
        Save();

        static void MergeList(List<string> target, List<string> source)
        {
            foreach (var item in source.Where(i => !target.Contains(i))) target.Add(item);
        }
    }

    public void ResetAll()
    {
        Data = new LibraryData();
        Save();
    }

    static bool Toggle(List<string> list, string id)
    {
        if (list.Remove(id)) return false;
        list.Insert(0, id);
        return true;
    }

    /// <summary>Debounced, atomic save so rapid taps don't hammer the disk.</summary>
    public void Save(bool notify = true)
    {
        if (notify) Changed?.Invoke();

        CancellationTokenSource cts;
        lock (_gate)
        {
            _pendingSave?.Cancel();
            _pendingSave = cts = new CancellationTokenSource();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, cts.Token);
                string json;
                lock (_gate) json = JsonSerializer.Serialize(Data, AppJson.Default.LibraryData);
                var temp = _path + ".tmp";
                await File.WriteAllTextAsync(temp, json, cts.Token);
                File.Move(temp, _path, overwrite: true);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Library save failed: {ex}"); }
        });
    }
}
