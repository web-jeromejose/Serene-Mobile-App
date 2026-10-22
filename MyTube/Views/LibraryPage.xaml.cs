using MyTube.ViewModels;

namespace MyTube.Views;

public partial class LibraryPage : ContentPage
{
    readonly LibraryViewModel _vm;

    public LibraryPage(LibraryViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        App.ApplySystemBars();
        await _vm.RefreshAsync();
    }

    async void OnHistory(object? sender, TappedEventArgs e) => await Nav.OpenPlaylist("history");

    async void OnCreate(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("//create");

    async void OnVideo(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is VideoItem video) await Nav.OpenVideo(video);
    }

    void OnPlaylist(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is PlaylistInfo playlist) _vm.OpenPlaylistCommand.Execute(playlist);
    }
}
