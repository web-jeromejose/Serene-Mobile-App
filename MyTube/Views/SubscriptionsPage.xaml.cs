using MyTube.ViewModels;

namespace MyTube.Views;

public partial class SubscriptionsPage : ContentPage
{
    readonly SubscriptionsViewModel _vm;

    public SubscriptionsPage(SubscriptionsViewModel vm)
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

    async void OnSearch(object? sender, TappedEventArgs e) => await Nav.OpenSearch();

    async void OnChannelFilter(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ChannelInfo channel) await _vm.FilterAsync(channel);
    }

    void OnSubscribe(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ChannelInfo channel) _vm.ToggleSubscribeCommand.Execute(channel);
    }

    void OnOpenChannel(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ChannelInfo channel) _vm.OpenChannelCommand.Execute(channel);
    }
}
