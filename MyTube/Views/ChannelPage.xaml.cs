using MyTube.ViewModels;

namespace MyTube.Views;

public partial class ChannelPage : ContentPage
{
    readonly ChannelViewModel _vm;

    public ChannelPage(ChannelViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        App.ApplySystemBars();
    }

    void OnTab(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ChipItem tab) _vm.SelectTab(tab);
    }
}
