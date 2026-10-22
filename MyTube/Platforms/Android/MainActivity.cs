using Android.App;
using Android.Content.PM;

namespace MyTube;

// SingleTask + ResizeableActivity are required by MediaElement; config changes are handled in-app
// so rotating into full-screen doesn't restart the video.
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ResizeableActivity = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
