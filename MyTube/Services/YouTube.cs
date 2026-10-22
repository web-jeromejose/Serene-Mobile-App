using System.Text.RegularExpressions;

namespace MyTube.Services;

public readonly record struct YouTubeLink(string VideoId, bool IsShort, double StartSeconds);

/// <summary>Understands every common YouTube link format.</summary>
public static partial class YouTubeUrl
{
    [GeneratedRegex(@"(?:youtube(?:-nocookie)?\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/|live/|v/|e/)|youtu\.be/)([A-Za-z0-9_-]{11})", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")]
    private static partial Regex BareIdRegex();

    [GeneratedRegex(@"[?&#](?:t|start)=(?:(\d+)h)?(?:(\d+)m)?(\d+)s?", RegexOptions.IgnoreCase)]
    private static partial Regex StartRegex();

    public static bool TryParse(string? text, out YouTubeLink link)
    {
        link = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        if (BareIdRegex().IsMatch(text))
        {
            link = new YouTubeLink(text, false, 0);
            return true;
        }

        var match = LinkRegex().Match(text);
        if (!match.Success) return false;

        double start = 0;
        var t = StartRegex().Match(text);
        if (t.Success)
        {
            start = (t.Groups[1].Success ? int.Parse(t.Groups[1].Value) * 3600 : 0)
                  + (t.Groups[2].Success ? int.Parse(t.Groups[2].Value) * 60 : 0)
                  + int.Parse(t.Groups[3].Value);
        }

        link = new YouTubeLink(match.Groups[1].Value, text.Contains("/shorts/", StringComparison.OrdinalIgnoreCase), start);
        return true;
    }

    public static bool LooksLikeUrl(string? text) =>
        !string.IsNullOrWhiteSpace(text) && (text.Contains("youtu", StringComparison.OrdinalIgnoreCase) || text.StartsWith("http", StringComparison.OrdinalIgnoreCase));
}

public sealed record YouTubeMeta(string Title, string Channel, bool IsVertical);

/// <summary>
/// Fetches title/channel through YouTube's public oEmbed endpoint (no API key, no login).
/// Also tells Shorts apart from regular videos: oEmbed reports a vertical frame for Shorts.
/// </summary>
public sealed class YouTubeMetadataService
{
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MyTube/1.0 (.NET MAUI)");
        return client;
    }

    public async Task<(YouTubeMeta? Meta, string? Error)> GetAsync(string videoId, CancellationToken ct = default)
    {
        try
        {
            // Asking about the /shorts/ URL returns a 113x200 frame for real Shorts and 200x113 for normal videos.
            var target = Uri.EscapeDataString($"https://www.youtube.com/shorts/{videoId}");
            using var response = await Http.GetAsync($"https://www.youtube.com/oembed?format=json&url={target}", ct);
            if (!response.IsSuccessStatusCode)
            {
                return (null, (int)response.StatusCode switch
                {
                    401 or 403 => "The owner doesn't allow this video to be played in other apps.",
                    404 => "Video not found. It may be private or deleted.",
                    _ => $"YouTube returned {(int)response.StatusCode}.",
                });
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var dto = JsonSerializer.Deserialize(json, AppJson.Default.OEmbedResponse);
            if (dto is null) return (null, "Unexpected response from YouTube.");
            var vertical = dto.Height > dto.Width;
            return (new YouTubeMeta(dto.Title ?? "YouTube video", dto.AuthorName ?? "YouTube", vertical), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (null, "No connection to YouTube. Check your internet and try again.");
        }
    }

    public static async Task<string> DownloadStringAsync(string url, CancellationToken ct = default) =>
        await Http.GetStringAsync(url, ct);
}
