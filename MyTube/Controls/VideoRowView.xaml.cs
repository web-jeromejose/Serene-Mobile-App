namespace MyTube.Controls;

public partial class VideoRowView : ContentView
{
    public VideoRowView() => InitializeComponent();

    async void OnOpen(object? sender, TappedEventArgs e)
    {
        if (BindingContext is VideoItem video) await Nav.OpenVideo(video);
    }

    async void OnMenu(object? sender, TappedEventArgs e)
    {
        if (BindingContext is VideoItem video) await VideoActions.ShowMenuAsync(video);
    }
}
