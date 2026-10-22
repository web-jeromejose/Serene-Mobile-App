namespace MyTube.Controls;

public partial class VideoCardView : ContentView
{
    public VideoCardView()
    {
        InitializeComponent();
        Thumb.SetBinding(HeightRequestProperty, new Binding(nameof(LayoutMetrics.FeedThumbHeight), source: LayoutMetrics.Current));
    }

    async void OnOpen(object? sender, TappedEventArgs e)
    {
        if (BindingContext is VideoItem video) await Nav.OpenVideo(video);
    }

    async void OnChannel(object? sender, TappedEventArgs e)
    {
        if (BindingContext is VideoItem video) await Nav.OpenChannel(video.Channel);
    }

    async void OnMenu(object? sender, TappedEventArgs e)
    {
        if (BindingContext is VideoItem video) await VideoActions.ShowMenuAsync(video);
    }
}
