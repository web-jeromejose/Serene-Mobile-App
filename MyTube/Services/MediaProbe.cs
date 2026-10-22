namespace MyTube.Services;

public sealed record MediaInfo(double? DurationSeconds, int Width, int Height, bool HasThumbnail)
{
    public bool IsVertical => Height > Width && Width > 0;
}

/// <summary>
/// Native helpers for your own videos: reads duration / orientation and grabs a frame as thumbnail.
/// Also lists videos bundled in Resources/Raw/videos.
/// </summary>
public static class MediaProbe
{
    /// <param name="source">embed://…, local://… or an http(s) URL.</param>
    public static Task<MediaInfo?> ProbeAsync(string source, string thumbnailPath) => Task.Run(() =>
    {
        try
        {
            return Probe(source, thumbnailPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Probe failed for {source}: {ex.Message}");
            return null;
        }
    });

    public static IReadOnlyList<string> ListBundledVideos()
    {
        try
        {
#if ANDROID
            var names = Android.App.Application.Context.Assets?.List("videos") ?? [];
            return names.Where(LocalPaths.IsVideoFile).Select(n => "videos/" + n).ToList();
#elif IOS
            var dir = Path.Combine(Foundation.NSBundle.MainBundle.BundlePath, "videos");
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir).Where(LocalPaths.IsVideoFile).Select(f => "videos/" + Path.GetFileName(f)).ToList()
                : [];
#else
            return [];
#endif
        }
        catch
        {
            return [];
        }
    }

#if ANDROID
    static MediaInfo? Probe(string source, string thumbnailPath)
    {
        using var retriever = new Android.Media.MediaMetadataRetriever();
        if (source.StartsWith("embed://", StringComparison.Ordinal))
        {
            using var afd = Android.App.Application.Context.Assets!.OpenFd(source["embed://".Length..]);
            retriever.SetDataSource(afd.FileDescriptor, afd.StartOffset, afd.Length);
        }
        else if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            retriever.SetDataSource(source, new Dictionary<string, string>());
        }
        else
        {
            retriever.SetDataSource(LocalPaths.Resolve(source));
        }

        double? duration = long.TryParse(retriever.ExtractMetadata(Android.Media.MetadataKey.Duration), out var ms) ? ms / 1000d : null;
        _ = int.TryParse(retriever.ExtractMetadata(Android.Media.MetadataKey.VideoWidth), out var width);
        _ = int.TryParse(retriever.ExtractMetadata(Android.Media.MetadataKey.VideoHeight), out var height);
        _ = int.TryParse(retriever.ExtractMetadata(Android.Media.MetadataKey.VideoRotation), out var rotation);
        if (rotation is 90 or 270) (width, height) = (height, width);

        var hasThumb = false;
        var frameAt = duration is > 4 ? 1_500_000L : 0L;
        using (var bitmap = retriever.GetFrameAtTime(frameAt, Android.Media.Option.ClosestSync))
        {
            if (bitmap is not null)
            {
                using var stream = File.Create(thumbnailPath);
                hasThumb = bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, 80, stream);
            }
        }

        return new MediaInfo(duration, width, height, hasThumb);
    }
#elif IOS
    static MediaInfo? Probe(string source, string thumbnailPath)
    {
        Foundation.NSUrl url;
        if (source.StartsWith("embed://", StringComparison.Ordinal))
            url = Foundation.NSUrl.FromFilename(Path.Combine(Foundation.NSBundle.MainBundle.BundlePath, source["embed://".Length..]));
        else if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = new Foundation.NSUrl(source);
        else
            url = Foundation.NSUrl.FromFilename(LocalPaths.Resolve(source));

        using var asset = AVFoundation.AVAsset.FromUrl(url);
        var seconds = asset.Duration.Seconds;
        double? duration = double.IsFinite(seconds) && seconds > 0 ? seconds : null;

        using var generator = new AVFoundation.AVAssetImageGenerator(asset)
        {
            AppliesPreferredTrackTransform = true,
            MaximumSize = new CoreGraphics.CGSize(720, 720),
        };
#pragma warning disable CA1422 // still works on current iOS versions
        using var image = generator.CopyCGImageAtTime(CoreMedia.CMTime.FromSeconds(duration is > 4 ? 1.5 : 0, 600), out _, out _);
#pragma warning restore CA1422
        if (image is null) return new MediaInfo(duration, 0, 0, false);

        using var uiImage = UIKit.UIImage.FromImage(image);
        using var data = uiImage.AsJPEG(0.8f);
        var saved = data is not null && data.Save(thumbnailPath, true, out _);
        return new MediaInfo(duration, (int)image.Width, (int)image.Height, saved);
    }
#else
    static MediaInfo? Probe(string source, string thumbnailPath) => null;
#endif
}
