using MyTube.ViewModels;

namespace MyTube.Views;

public partial class AddVideoPage : ContentPage
{
    readonly AddVideoViewModel _vm;

    public AddVideoPage(AddVideoViewModel vm)
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
}
