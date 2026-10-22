namespace MyTube.Services;

/// <summary>Small user preferences, stored with MAUI Preferences.</summary>
public sealed class SettingsService
{
    public AppTheme Theme
    {
        get => (AppTheme)Preferences.Get("theme", (int)AppTheme.Unspecified);
        set
        {
            Preferences.Set("theme", (int)value);
            if (Application.Current is not null) Application.Current.UserAppTheme = value;
        }
    }

    /// <summary>Play the next video automatically when one ends.</summary>
    public bool AutoplayNext
    {
        get => Preferences.Get("autoplay", true);
        set => Preferences.Set("autoplay", value);
    }

    /// <summary>Move to the next Short when one ends (otherwise it loops, like YouTube).</summary>
    public bool ShortsAutoAdvance
    {
        get => Preferences.Get("shorts_auto_advance", false);
        set => Preferences.Set("shorts_auto_advance", value);
    }

    /// <summary>Mix your own videos into the Home feed and Shorts.</summary>
    public bool IncludeOwnVideos
    {
        get => Preferences.Get("include_own", true);
        set => Preferences.Set("include_own", value);
    }

    /// <summary>Your name, shown on your uploads and comments.</summary>
    public string DisplayName
    {
        get => Preferences.Get("display_name", "My Channel");
        set => Preferences.Set("display_name", string.IsNullOrWhiteSpace(value) ? "My Channel" : value.Trim());
    }

    /// <summary>Optional link to an online catalog.json so you can update your list without rebuilding the app.</summary>
    public string RemoteCatalogUrl
    {
        get => Preferences.Get("remote_catalog", string.Empty);
        set => Preferences.Set("remote_catalog", value?.Trim() ?? string.Empty);
    }

    public string LastSyncStatus
    {
        get => Preferences.Get("remote_status", string.Empty);
        set => Preferences.Set("remote_status", value);
    }
}
