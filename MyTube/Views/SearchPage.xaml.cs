using MyTube.ViewModels;

namespace MyTube.Views;

public partial class SearchPage : ContentPage
{
    readonly SearchViewModel _vm;

    public SearchPage(SearchViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (string.IsNullOrEmpty(_vm.Query))
        {
            await Task.Delay(250); // let the page finish animating in before showing the keyboard
            QueryEntry.Focus();
        }
    }

    async void OnBack(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("..");

    void OnSuggestion(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is string s)
        {
            QueryEntry.Unfocus();
            _vm.PickSuggestionCommand.Execute(s);
        }
    }

    void OnRemoveSuggestion(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is string s) _vm.RemoveSuggestionCommand.Execute(s);
    }

    void OnFilter(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ChipItem chip) _vm.SelectFilter(chip);
    }
}
