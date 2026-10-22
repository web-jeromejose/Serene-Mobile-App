namespace MyTube.Controls;

public partial class ShortCardView : ContentView
{
    public ShortCardView()
    {
        InitializeComponent();
        Card.SetBinding(WidthRequestProperty, new Binding(nameof(LayoutMetrics.ShortCardWidth), source: LayoutMetrics.Current));
        Card.SetBinding(HeightRequestProperty, new Binding(nameof(LayoutMetrics.ShortCardHeight), source: LayoutMetrics.Current));
    }

    async void OnOpen(object? sender, TappedEventArgs e)
    {
        if (BindingContext is VideoItem video) await Nav.OpenShort(video);
    }

    async void OnMenu(object? sender, TappedEventArgs e)
    {
        if (BindingContext is VideoItem video) await VideoActions.ShowMenuAsync(video);
    }
}
