using Android.Content.PM;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using AndroidX.Core.View;

namespace MyTube.Droid;

/// <summary>Makes the YouTube player's fullscreen button work: shows the video full screen in landscape.</summary>
public sealed class FullscreenChromeClient : WebChromeClient
{
    global::Android.Views.View? _customView;
    ICustomViewCallback? _callback;

    public override void OnShowCustomView(global::Android.Views.View? view, ICustomViewCallback? callback)
    {
        var activity = Platform.CurrentActivity;
        if (view is null || activity?.Window?.DecorView is not FrameLayout decor)
        {
            callback?.OnCustomViewHidden();
            return;
        }

        if (_customView is not null) OnHideCustomView();
        _customView = view;
        _callback = callback;
        view.SetBackgroundColor(global::Android.Graphics.Color.Black);
        decor.AddView(view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        SystemBars.EnterFullscreen(activity);
    }

    public override void OnHideCustomView()
    {
        var activity = Platform.CurrentActivity;
        if (_customView is not null && activity?.Window?.DecorView is FrameLayout decor)
            decor.RemoveView(_customView);
        _customView = null;
        _callback?.OnCustomViewHidden();
        _callback = null;
        if (activity is not null) SystemBars.ExitFullscreen(activity);
    }
}

public static class SystemBars
{
    public static void EnterFullscreen(global::Android.App.Activity activity)
    {
        activity.RequestedOrientation = ScreenOrientation.SensorLandscape;
        if (activity.Window is { } window)
        {
            var controller = WindowCompat.GetInsetsController(window, window.DecorView);
            controller!.Hide(WindowInsetsCompat.Type.SystemBars());
            controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        }
    }

    public static void ExitFullscreen(global::Android.App.Activity activity)
    {
        activity.RequestedOrientation = ScreenOrientation.Unspecified;
        if (activity.Window is { } window)
            WindowCompat.GetInsetsController(window, window.DecorView)!.Show(WindowInsetsCompat.Type.SystemBars());
    }

    /// <summary>Dark status-bar icons on light backgrounds, light icons on dark ones.</summary>
    public static void SetLightBackground(bool lightBackground)
    {
        var window = Platform.CurrentActivity?.Window;
        if (window is null) return;
        var controller = WindowCompat.GetInsetsController(window, window.DecorView);
        controller!.AppearanceLightStatusBars = lightBackground;
        controller.AppearanceLightNavigationBars = lightBackground;
    }

    public static void HideForLandscape(bool hide)
    {
        var window = Platform.CurrentActivity?.Window;
        if (window is null) return;
        var controller = WindowCompat.GetInsetsController(window, window.DecorView);
        if (hide)
        {
            controller!.Hide(WindowInsetsCompat.Type.SystemBars());
            controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        }
        else
        {
            controller!.Show(WindowInsetsCompat.Type.SystemBars());
        }
    }
}
