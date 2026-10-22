namespace MyTube.Models;

/// <summary>Everything the app remembers on the device (no account, no cloud).</summary>
public sealed class LibraryData
{
    public List<VideoItem> UserVideos { get; set; } = [];
    public List<string> Liked { get; set; } = [];
    public List<string> Disliked { get; set; } = [];
    public List<string> WatchLater { get; set; } = [];
    public List<HistoryEntry> History { get; set; } = [];
    public List<Playlist> Playlists { get; set; } = [];
    public List<string> Subscriptions { get; set; } = [];
    public List<LocalComment> Comments { get; set; } = [];
    public List<string> SearchHistory { get; set; } = [];
    public List<string> Hidden { get; set; } = [];
    public Dictionary<string, double> ResumePositions { get; set; } = [];
    public Dictionary<string, double> Durations { get; set; } = [];
    public Dictionary<string, CachedMeta> MetaCache { get; set; } = [];
}

public sealed class HistoryEntry
{
    public string VideoId { get; set; } = string.Empty;
    public DateTimeOffset WatchedAt { get; set; }
}

public sealed class Playlist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<string> VideoIds { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LocalComment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string VideoId { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore] public string Initial => Formatters.Initial(Author);
    [JsonIgnore] public Color Color => Formatters.AvatarColor(Author);
    [JsonIgnore] public string Header => $"@{Author.Replace(" ", string.Empty).ToLowerInvariant()} · {Formatters.TimeAgo(CreatedAt)}";
}

public sealed class CachedMeta
{
    public string? Title { get; set; }
    public string? Channel { get; set; }
}

/// <summary>Format of Resources/Raw/catalog.json and of an online catalog.</summary>
public sealed class CatalogFile
{
    public string? ChannelName { get; set; }
    public List<CatalogEntry> Videos { get; set; } = [];
}

public sealed class CatalogEntry
{
    /// <summary>A YouTube link (video or Short) or a direct link to your own video file (mp4, m3u8…).</summary>
    public string? Url { get; set; }

    /// <summary>A video bundled in Resources/Raw, e.g. "videos/holiday.mp4".</summary>
    public string? File { get; set; }

    public string? Title { get; set; }
    public string? Channel { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? Thumbnail { get; set; }
    public bool? Short { get; set; }
    public bool? Own { get; set; }
    public List<string>? Tags { get; set; }
    public long? Views { get; set; }

    /// <summary>"2024-05-31"</summary>
    public string? Published { get; set; }

    /// <summary>"3:45", "1:02:03" or seconds.</summary>
    public string? Duration { get; set; }
}

public sealed class OEmbedResponse
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("author_name")] public string? AuthorName { get; set; }
    [JsonPropertyName("thumbnail_url")] public string? ThumbnailUrl { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(LibraryData))]
[JsonSerializable(typeof(CatalogFile))]
[JsonSerializable(typeof(OEmbedResponse))]
internal sealed partial class AppJson : JsonSerializerContext;
