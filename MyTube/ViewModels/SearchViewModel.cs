namespace MyTube.ViewModels;

/// <summary>Search your whole collection; recent searches; paste a YouTube link to add it.</summary>
public sealed partial class SearchViewModel(CatalogService catalog, LibraryService library) : ObservableObject, IQueryAttributable
{
    List<VideoItem> _results = [];
    string _filter = "All";

    [ObservableProperty] private string query = string.Empty;
    [ObservableProperty] private bool showSuggestions = true;
    [ObservableProperty] private bool showResults;
    [ObservableProperty] private bool noResults;
    [ObservableProperty] private bool isLinkDetected;
    [ObservableProperty] private string resultsHeader = string.Empty;

    public ObservableCollection<string> Suggestions { get; } = [];
    public ObservableCollection<VideoItem> Results { get; } = [];
    public ObservableCollection<ChipItem> Filters { get; } =
    [
        new() { Name = "All", IsSelected = true },
        new() { Name = "Videos" },
        new() { Name = "Shorts" },
        new() { Name = "Your videos" },
        new() { Name = "YouTube" },
    ];

    public void ApplyQueryAttributes(IDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue("query", out var q) && q is string text && text.Length > 0)
        {
            Query = text;
            Submit();
        }
        else
        {
            UpdateSuggestions();
        }
    }

    partial void OnQueryChanged(string value)
    {
        IsLinkDetected = YouTubeUrl.TryParse(value, out _) && value.Contains('/');
        ShowSuggestions = true;
        ShowResults = false;
        NoResults = false;
        UpdateSuggestions();
    }

    void UpdateSuggestions()
    {
        Suggestions.Clear();
        var q = Query.Trim();
        IEnumerable<string> items = q.Length == 0
            ? library.Data.SearchHistory
            : library.Data.SearchHistory.Where(s => s.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Concat(catalog.All.Select(v => v.Title).Where(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .Concat(catalog.All.Select(v => v.Channel).Where(c => c.Contains(q, StringComparison.OrdinalIgnoreCase)));
        foreach (var s in items.Distinct(StringComparer.OrdinalIgnoreCase).Take(12)) Suggestions.Add(s);
    }

    [RelayCommand]
    private void Submit()
    {
        var q = Query.Trim();
        if (q.Length == 0) return;
        if (IsLinkDetected) return; // the "Add this link" card handles it
        library.AddSearch(q);
        _results = catalog.Search(q);
        ApplyFilter();
        ShowSuggestions = false;
        ShowResults = true;
    }

    [RelayCommand]
    private void PickSuggestion(string suggestion)
    {
        Query = suggestion;
        Submit();
    }

    [RelayCommand]
    private void RemoveSuggestion(string suggestion)
    {
        library.RemoveSearch(suggestion);
        Suggestions.Remove(suggestion);
    }

    public void SelectFilter(ChipItem chip)
    {
        _filter = chip.Name;
        foreach (var c in Filters) c.IsSelected = c == chip;
        ApplyFilter();
    }

    void ApplyFilter()
    {
        Results.Clear();
        var filtered = _filter switch
        {
            "Videos" => _results.Where(v => !v.IsShort),
            "Shorts" => _results.Where(v => v.IsShort),
            "Your videos" => _results.Where(v => v.IsOwn),
            "YouTube" => _results.Where(v => v.IsYouTube),
            _ => _results,
        };
        foreach (var v in filtered) Results.Add(v);
        NoResults = Results.Count == 0;
        ResultsHeader = Results.Count == 1 ? "1 result" : $"{Results.Count} results";
    }

    [RelayCommand]
    private async Task AddLink()
    {
        var (video, error) = await catalog.AddYouTubeAsync(Query);
        if (video is null)
        {
            await Nav.Toast(error ?? "Couldn't add that link");
            return;
        }
        await Nav.Toast($"Added \"{video.Title}\"");
        Query = string.Empty;
        await Nav.OpenVideo(video);
    }
}
