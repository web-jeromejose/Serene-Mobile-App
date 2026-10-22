using MyTube.ViewModels;

namespace MyTube.Views;

public partial class PlaylistPage : ContentPage
{
    readonly PlaylistViewModel _vm;
    bool _firstAppear = true;

    public PlaylistPage(PlaylistViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        App.ApplySystemBars();
        if (!_firstAppear) _vm.Refresh(); // coming back from the player: progress/removals may have changed
        _firstAppear = false;
    }

    void OnRemove(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is VideoItem video) _vm.RemoveCommand.Execute(video);
    }
}
