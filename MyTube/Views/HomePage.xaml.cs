using MyTube.ViewModels;

namespace MyTube.Views;

public partial class HomePage : ContentPage
{
    readonly HomeViewModel _vm;

    public HomePage(HomeViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        LayoutMetrics.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LayoutMetrics.FeedColumns)) ApplyColumns();
        };
        ApplyColumns();
    }

    // One column on phones, a grid on tablets / landscape.
    void ApplyColumns()
    {
        var columns = LayoutMetrics.Current.FeedColumns;
        FeedList.ItemsLayout = columns == 1
            ? LinearItemsLayout.Vertical
            : new GridItemsLayout(columns, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 12 };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        App.ApplySystemBars();
        await _vm.InitAsync();
    }

    void OnChip(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ChipItem chip)
        {
            _vm.SelectChip(chip);
            if (_vm.Feed.Count > 0) FeedList.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
        }
    }

    // A horizontal list inside a vertical list needs an explicit height on Android.
    void OnShelfLoaded(object? sender, EventArgs e)
    {
        if (sender is CollectionView shelf && !shelf.IsSet(HeightRequestProperty))
            shelf.SetBinding(HeightRequestProperty, new Binding(nameof(LayoutMetrics.ShortCardHeight), source: LayoutMetrics.Current));
    }

    async void OnSearch(object? sender, TappedEventArgs e) => await Nav.OpenSearch();

    async void OnSettings(object? sender, TappedEventArgs e) => await Nav.OpenSettings();
}
