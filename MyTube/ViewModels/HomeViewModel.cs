namespace MyTube.ViewModels;

/// <summary>
/// Home: an endless, freshly shuffled mix of your YouTube links and your own videos,
/// with a Shorts shelf, category chips and pull-to-refresh.
/// </summary>
public sealed partial class HomeViewModel(CatalogService catalog, LibraryService library) : ObservableObject
{
    const string All = "All";
    const string Yours = "Your videos";
    const string Recent = "Recently added";
    const string Watched = "Watched";
    const string NewToYou = "New to you";
    const int PageSize = 10;

    List<VideoItem> _pool = [];
    int _cursor;
    int _rounds;
    bool _initialized;

    public ObservableCollection<object> Feed { get; } = [];
    public ObservableCollection<ChipItem> Chips { get; } = [];

    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool isLoading = true;
    [ObservableProperty] private bool isEmpty;

    string _selected = All;

    public async Task InitAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await catalog.EnsureLoadedAsync();
        catalog.Changed += Rebuild;
        Rebuild();
        IsLoading = false;
    }

    void Rebuild()
    {
        BuildChips();
        _pool = CatalogService.Shuffle(Filter(_selected));
        _cursor = 0;
        _rounds = 0;
        Feed.Clear();
        AppendPage(withShelf: true);
        IsEmpty = Feed.Count == 0;
    }

    void BuildChips()
    {
        var names = new List<string> { All };
        if (catalog.OwnVideos.Any()) names.Add(Yours);
        names.AddRange(catalog.Categories.Where(c => !c.Equals("My videos", StringComparison.OrdinalIgnoreCase)));
        names.AddRange([Recent, Watched, NewToYou]);
        if (!names.Contains(_selected)) _selected = All;

        Chips.Clear();
        foreach (var name in names) Chips.Add(new ChipItem { Name = name, IsSelected = name == _selected });
    }

    IEnumerable<VideoItem> Filter(string chip)
    {
        var videos = catalog.Videos;
        var history = library.Data.History.Select(h => h.VideoId).ToHashSet();
        return chip switch
        {
            All => videos,
            Yours => videos.Where(v => v.IsOwn),
            Recent => videos.OrderByDescending(v => v.AddedAt).Take(30),
            Watched => videos.Where(v => history.Contains(v.Id)),
            NewToYou => videos.Where(v => !history.Contains(v.Id)),
            _ => videos.Where(v => string.Equals(v.Category, chip, StringComparison.OrdinalIgnoreCase)),
        };
    }

    void AppendPage(bool withShelf = false)
    {
        if (_pool.Count == 0) return;
        var shorts = CatalogService.Shuffle(catalog.Shorts).Take(10).ToList();

        for (var i = 0; i < PageSize; i++)
        {
            if (_cursor >= _pool.Count)
            {
                // Reached the end of your list: reshuffle and keep going (like YouTube's endless feed),
                // but stop after a few rounds so small lists don't repeat forever.
                if (++_rounds >= 3 || _pool.Count < 4) return;
                var last = _pool[^1];
                _pool = CatalogService.Shuffle(_pool);
                if (_pool[0] == last) (_pool[0], _pool[^1]) = (_pool[^1], _pool[0]);
                _cursor = 0;
            }

            Feed.Add(_pool[_cursor++]);

            // A Shorts shelf after the 2nd video, then roughly every 10 videos.
            if (shorts.Count > 0 && (withShelf && Feed.Count == 2 || !withShelf && i == 4))
                Feed.Add(new ShortsShelf { Items = shorts });
        }
    }

    public void SelectChip(ChipItem chip)
    {
        if (chip.Name == _selected) return;
        _selected = chip.Name;
        foreach (var c in Chips) c.IsSelected = c == chip;
        _pool = CatalogService.Shuffle(Filter(_selected));
        _cursor = 0;
        _rounds = 0;
        Feed.Clear();
        AppendPage(withShelf: _selected == All);
        IsEmpty = Feed.Count == 0;
    }

    [RelayCommand]
    private void Refresh()
    {
        _pool = CatalogService.Shuffle(Filter(_selected));
        _cursor = 0;
        _rounds = 0;
        Feed.Clear();
        AppendPage(withShelf: _selected == All);
        IsEmpty = Feed.Count == 0;
        IsRefreshing = false;
    }

    [RelayCommand]
    private void LoadMore() => AppendPage();
}
