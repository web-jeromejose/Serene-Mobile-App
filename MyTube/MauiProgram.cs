using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using MyTube.ViewModels;
using MyTube.Views;

namespace MyTube;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false)
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons");
                fonts.AddFont("MaterialIconsOutlined-Regular.otf", "MaterialIconsOutlined");
            });

        // Services (one instance for the whole app)
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<LibraryService>();
        builder.Services.AddSingleton<YouTubeMetadataService>();
        builder.Services.AddSingleton<CatalogService>();

        // Tab pages live for the whole session
        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<ShortsViewModel>();
        builder.Services.AddSingleton<ShortsPage>();
        builder.Services.AddSingleton<AddVideoViewModel>();
        builder.Services.AddSingleton<AddVideoPage>();
        builder.Services.AddSingleton<SubscriptionsViewModel>();
        builder.Services.AddSingleton<SubscriptionsPage>();
        builder.Services.AddSingleton<LibraryViewModel>();
        builder.Services.AddSingleton<LibraryPage>();

        // Pushed pages get a fresh instance each time
        builder.Services.AddTransient<PlayerViewModel>();
        builder.Services.AddTransient<PlayerPage>();
        builder.Services.AddTransient<SearchViewModel>();
        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddTransient<PlaylistViewModel>();
        builder.Services.AddTransient<PlaylistPage>();
        builder.Services.AddTransient<ChannelViewModel>();
        builder.Services.AddTransient<ChannelPage>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
