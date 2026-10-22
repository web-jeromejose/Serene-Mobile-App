namespace MyTube.Models;

public enum VideoSource { YouTube, Own }

public enum VideoFormat { Video, Short }

/// <summary>
/// One playable item: a YouTube video, a YouTube Short, or one of your own videos
/// (bundled with the app, picked from the phone, or hosted at a URL).
/// </summary>
public sealed class VideoItem : ObservableObject
{
    string _title = string.Empty;
    string _channel = string.Empty;
    string? _thumbnail;
    bool _isPortrait;
    double? _durationSeconds;
    double _watchProgress;

    /// <summary>"yt:&lt;id&gt;", "asset:&lt;path&gt;", "url:&lt;hash&gt;" or "own:&lt;guid&gt;".</summary>
    public string Id { get; set; } = string.Empty;
    public VideoSource Source { get; set; }
    public VideoFormat Format { get; set; }
    public string? YouTubeId { get; set; }

    /// <summary>Own videos only: https://…, embed://&lt;Resources/Raw path&gt; or local://&lt;path in app data&gt;.</summary>
    public string? MediaUri { get; set; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Channel
    {
        get => _channel;
        set
        {
            if (SetProperty(ref _channel, value))
            {
                OnPropertyChanged(nameof(ChannelInitial));
                OnPropertyChanged(nameof(ChannelColor));
                OnPropertyChanged(nameof(MetaText));
            }
        }
    }

    public string? Description { get; set; }
    public string? Category { get; set; }

    /// <summary>Remote image URL or local://thumbs/… ; YouTube items fall back to the YouTube thumbnail.</summary>
    public string? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (SetProperty(ref _thumbnail, value))
            {
                OnPropertyChanged(nameof(ThumbnailSource));
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }
    }

    public double? DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            if (SetProperty(ref _durationSeconds, value))
            {
                OnPropertyChanged(nameof(DurationText));
                OnPropertyChanged(nameof(HasDuration));
            }
        }
    }

    public long? Views { get; set; }
    public DateTimeOffset? Published { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<string> Tags { get; set; } = [];

    /// <summary>True when added inside the app (can be removed from the app).</summary>
    public bool IsUserAdded { get; set; }

    /// <summary>0..1, how much of the video you already watched (red bar under the thumbnail).</summary>
    [JsonIgnore]
    public double WatchProgress
    {
        get => _watchProgress;
        set
        {
            if (SetProperty(ref _watchProgress, value))
                OnPropertyChanged(nameof(HasProgress));
        }
    }

    [JsonIgnore] public bool HasProgress => WatchProgress > 0.01;
    [JsonIgnore] public bool IsShort => Format == VideoFormat.Short;
    [JsonIgnore] public bool IsOwn => Source == VideoSource.Own;
    [JsonIgnore] public bool IsYouTube => Source == VideoSource.YouTube;
    [JsonIgnore]
    public bool IsPortrait
    {
        get => _isPortrait;
        set => SetProperty(ref _isPortrait, value);
    }
    [JsonIgnore] public bool HasDuration => !IsShort && DurationSeconds > 0;
    [JsonIgnore] public string DurationText => Formatters.Duration(DurationSeconds);
    [JsonIgnore] public string ChannelInitial => Formatters.Initial(Channel);
    [JsonIgnore] public Color ChannelColor => Formatters.AvatarColor(Channel);
    [JsonIgnore] public bool HasThumbnail => ThumbnailSource is not null;

    [JsonIgnore]
    public ImageSource? ThumbnailSource
    {
        get
        {
            var thumb = Thumbnail;
            if (string.IsNullOrWhiteSpace(thumb) && YouTubeId is not null)
                thumb = $"https://img.youtube.com/vi/{YouTubeId}/hqdefault.jpg";
            if (string.IsNullOrWhiteSpace(thumb))
                return null;
            if (thumb.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return ImageSource.FromUri(new Uri(thumb));
            var path = LocalPaths.Resolve(thumb);
            return File.Exists(path) ? ImageSource.FromFile(path) : null;
        }
    }

    /// <summary>"Channel · 1.2M views · 3 years ago" (parts that are unknown are left out).</summary>
    [JsonIgnore]
    public string MetaText
    {
        get
        {
            var parts = new List<string> { Channel };
            if (Views is > 0) parts.Add(Formatters.Views(Views.Value));
            if (Published is not null) parts.Add(Formatters.TimeAgo(Published.Value));
            return string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }

    /// <summary>Link used for Share / Open in YouTube.</summary>
    [JsonIgnore]
    public string? ShareUrl => YouTubeId is null
        ? (MediaUri is not null && MediaUri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? MediaUri : null)
        : IsShort ? $"https://www.youtube.com/shorts/{YouTubeId}" : $"https://www.youtube.com/watch?v={YouTubeId}";
}

/// <summary>A horizontal row of Shorts inside the Home feed.</summary>
public sealed class ShortsShelf
{
    public required IReadOnlyList<VideoItem> Items { get; init; }
}

/// <summary>Filter chip at the top of Home / Search / Channel.</summary>
public sealed partial class ChipItem : ObservableObject
{
    public required string Name { get; init; }

    [ObservableProperty]
    private bool isSelected;
}

public sealed partial class ChannelInfo : ObservableObject
{
    public required string Name { get; init; }
    public string Initial => Formatters.Initial(Name);
    public Color Color => Formatters.AvatarColor(Name);
    public int VideoCount { get; init; }
    public string CountText => VideoCount == 1 ? "1 video" : $"{VideoCount} videos";

    [ObservableProperty]
    private bool isSubscribed;
}

public sealed class PlaylistInfo
{
    public required string Kind { get; init; }
    public string? Id { get; init; }
    public required string Title { get; init; }
    public required string Glyph { get; init; }
    public int Count { get; init; }
    public string CountText => Count == 1 ? "1 video" : $"{Count} videos";
    public ImageSource? Thumbnail { get; init; }
}
