using CommunityToolkit.Maui.Alerts;
using MyTube.ViewModels;
using MyTube.Views;

namespace MyTube.Services;

public static class ServiceHelper
{
    public static T Get<T>() where T : notnull =>
        IPlatformApplication.Current!.Services.GetRequiredService<T>();
}

/// <summary>Central place for moving between screens.</summary>
public static class Nav
{
    public static Task OpenVideo(VideoItem video)
    {
        if (video.IsShort) return OpenShort(video);

        // Already watching something: switch video in place, like YouTube does.
        if (Shell.Current.CurrentPage is PlayerPage player)
        {
            player.PlayInPlace(video);
            return Task.CompletedTask;
        }

        return Shell.Current.GoToAsync(nameof(PlayerPage), new ShellNavigationQueryParameters { ["video"] = video });
    }

    public static Task OpenShort(VideoItem video)
    {
        ServiceHelper.Get<ShortsViewModel>().StartWith(video);
        return Shell.Current.GoToAsync("//shorts");
    }

    public static Task OpenChannel(string channel) =>
        Shell.Current.GoToAsync(nameof(ChannelPage), new ShellNavigationQueryParameters { ["channel"] = channel });

    public static Task OpenPlaylist(string kind, string? id = null) =>
        Shell.Current.GoToAsync(nameof(PlaylistPage), new ShellNavigationQueryParameters { ["kind"] = kind, ["id"] = id ?? string.Empty });

    public static Task OpenSearch(string? query = null) =>
        Shell.Current.GoToAsync(nameof(SearchPage), new ShellNavigationQueryParameters { ["query"] = query ?? string.Empty });

    public static Task OpenSettings() => Shell.Current.GoToAsync(nameof(SettingsPage));

    public static Task Toast(string message) =>
        CommunityToolkit.Maui.Alerts.Toast.Make(message).Show();

    public static Page? CurrentPage => Shell.Current?.CurrentPage;
}

/// <summary>The "⋮" menu and share/save actions shared by every screen.</summary>
public static class VideoActions
{
    public static async Task ShowMenuAsync(VideoItem video)
    {
        var library = ServiceHelper.Get<LibraryService>();
        var catalog = ServiceHelper.Get<CatalogService>();
        var page = Nav.CurrentPage;
        if (page is null) return;

        var watchLater = library.IsInWatchLater(video.Id) ? "Remove from Watch later" : "Save to Watch later";
        const string playlist = "Save to playlist";
        const string share = "Share";
        const string channel = "Go to channel";
        const string openYouTube = "Open in YouTube app";
        const string notInterested = "Not interested";
        const string remove = "Remove from MyTube";

        var options = new List<string> { watchLater, playlist, share, channel };
        if (video.IsYouTube) options.Add(openYouTube);
        options.Add(video.IsUserAdded ? remove : notInterested);

        var choice = await page.DisplayActionSheetAsync(video.Title, "Cancel", null, [.. options]);
        switch (choice)
        {
            case var c when c == watchLater:
                await ToggleWatchLaterAsync(video);
                break;
            case playlist:
                await SaveToPlaylistAsync(video);
                break;
            case share:
                await ShareAsync(video);
                break;
            case channel:
                await Nav.OpenChannel(video.Channel);
                break;
            case openYouTube:
                await OpenInYouTubeAsync(video);
                break;
            case notInterested:
                catalog.Remove(video);
                await Nav.Toast("Video removed. You won't see it again.");
                break;
            case remove:
                if (await page.DisplayAlertAsync("Remove video?", $"\"{video.Title}\" will be removed from MyTube.", "Remove", "Cancel"))
                {
                    catalog.Remove(video);
                    await Nav.Toast("Removed");
                }
                break;
        }
    }

    public static async Task ToggleWatchLaterAsync(VideoItem video)
    {
        var added = ServiceHelper.Get<LibraryService>().ToggleWatchLater(video.Id);
        await Nav.Toast(added ? "Saved to Watch later" : "Removed from Watch later");
    }

    public static async Task SaveToPlaylistAsync(VideoItem video)
    {
        var library = ServiceHelper.Get<LibraryService>();
        var page = Nav.CurrentPage;
        if (page is null) return;

        const string create = "+ New playlist";
        var names = library.Data.Playlists.Select(p => p.Name).Append(create).ToArray();
        var choice = await page.DisplayActionSheetAsync("Save to…", "Cancel", null, names);
        if (choice is null || choice == "Cancel") return;

        Playlist? target;
        if (choice == create)
        {
            var name = await page.DisplayPromptAsync("New playlist", "Name your playlist", "Create", "Cancel", "My playlist", 60);
            if (string.IsNullOrWhiteSpace(name)) return;
            target = library.CreatePlaylist(name);
        }
        else
        {
            target = library.Data.Playlists.FirstOrDefault(p => p.Name == choice);
        }

        if (target is null) return;
        library.AddToPlaylist(target.Id, video.Id);
        await Nav.Toast($"Saved to {target.Name}");
    }

    public static async Task ShareAsync(VideoItem video)
    {
        if (video.ShareUrl is { } url)
        {
            await Share.Default.RequestAsync(new ShareTextRequest { Title = video.Title, Text = video.Title, Uri = url });
            return;
        }

        if (video.MediaUri?.StartsWith(LocalPaths.LocalScheme, StringComparison.Ordinal) == true)
        {
            await Share.Default.RequestAsync(new ShareFileRequest { Title = video.Title, File = new ShareFile(LocalPaths.Resolve(video.MediaUri)) });
            return;
        }

        await Nav.Toast("This video can't be shared");
    }

    public static Task OpenInYouTubeAsync(VideoItem video) =>
        video.ShareUrl is { } url ? Launcher.Default.OpenAsync(url) : Task.CompletedTask;
}
