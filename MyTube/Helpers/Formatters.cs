using System.Globalization;

namespace MyTube.Helpers;

public static class Formatters
{
    static readonly Color[] AvatarPalette =
    [
        Color.FromArgb("#E53935"), Color.FromArgb("#8E24AA"), Color.FromArgb("#3949AB"), Color.FromArgb("#039BE5"),
        Color.FromArgb("#00897B"), Color.FromArgb("#7CB342"), Color.FromArgb("#F4511E"), Color.FromArgb("#6D4C41"),
        Color.FromArgb("#546E7A"), Color.FromArgb("#D81B60"), Color.FromArgb("#5E35B1"), Color.FromArgb("#00ACC1"),
    ];

    public static string Duration(double? seconds)
    {
        if (seconds is not > 0) return string.Empty;
        var t = TimeSpan.FromSeconds(Math.Round(seconds.Value));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    public static double? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs)) return secs;
        var parts = text.Split(':');
        double total = 0;
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out var n)) return null;
            total = total * 60 + n;
        }
        return total;
    }

    public static string Views(long views) => $"{Compact(views)} views";

    public static string Compact(long n) => n switch
    {
        >= 1_000_000_000 => $"{n / 1_000_000_000d:0.#}B",
        >= 1_000_000 => $"{n / 1_000_000d:0.#}M",
        >= 1_000 => $"{n / 1_000d:0.#}K",
        _ => n.ToString(CultureInfo.CurrentCulture),
    };

    public static string TimeAgo(DateTimeOffset when)
    {
        var span = DateTimeOffset.UtcNow - when;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return Plural((int)span.TotalMinutes, "minute");
        if (span.TotalDays < 1) return Plural((int)span.TotalHours, "hour");
        if (span.TotalDays < 7) return Plural((int)span.TotalDays, "day");
        if (span.TotalDays < 30) return Plural((int)(span.TotalDays / 7), "week");
        if (span.TotalDays < 365) return Plural((int)(span.TotalDays / 30), "month");
        return Plural((int)(span.TotalDays / 365), "year");

        static string Plural(int n, string unit) => n == 1 ? $"1 {unit} ago" : $"{n} {unit}s ago";
    }

    public static string Initial(string? name)
    {
        var trimmed = (name ?? string.Empty).TrimStart('@', ' ');
        return trimmed.Length == 0 ? "?" : char.ToUpperInvariant(trimmed[0]).ToString();
    }

    public static Color AvatarColor(string? name)
    {
        var hash = 17;
        foreach (var ch in name ?? string.Empty) hash = unchecked(hash * 31 + ch);
        return AvatarPalette[(int)((uint)hash % AvatarPalette.Length)];
    }

    /// <summary>"my_trip-to paris.mp4" → "My trip to paris"</summary>
    public static string TitleFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Replace('-', ' ').Trim();
        if (name.Length == 0) return "My video";
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    public static string FriendlyBundledTitle(string fileName)
    {
        string[] titles = [
            "Sweet Little Moments", "Baby Memories", "Tiny Adventures",
            "A Lovely Day", "Happy Little Memory", "Serene Moments",
            "A Special Memory", "Little Star"
        ];
        var hash = Math.Abs(fileName.GetHashCode()) % titles.Length;
        return titles[hash];
    }

    public static string FriendlyBundledDescription(string fileName, bool isShort)
    {
        string[] shortDescriptions = [
            "A tiny moment worth remembering.",
            "A sweet little clip from SereneAppTube.",
            "A quick burst of joy.",
            "A small memory with a big smile.",
            "A lovely moment, captured in seconds."
        ];
        string[] videoDescriptions = [
            "A personal video from SereneAppTube.",
            "A peaceful memory from your video collection.",
            "A special moment saved in SereneAppTube."
        ];
        var choices = isShort ? shortDescriptions : videoDescriptions;
        return choices[Math.Abs(fileName.GetHashCode()) % choices.Length];
    }
}

public static class LocalPaths
{
    public const string LocalScheme = "local://";

    /// <summary>local://thumbs/a.jpg → absolute path inside the app's data folder (survives app updates on iOS).</summary>
    public static string Resolve(string value) =>
        value.StartsWith(LocalScheme, StringComparison.Ordinal)
            ? Path.Combine(FileSystem.AppDataDirectory, value[LocalScheme.Length..])
            : value;

    public static string Folder(string name)
    {
        var dir = Path.Combine(FileSystem.AppDataDirectory, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Hash(string text)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    public static bool IsVideoFile(string name) =>
        Path.GetExtension(name).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
}
