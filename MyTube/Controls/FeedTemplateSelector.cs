namespace MyTube.Controls;

/// <summary>Home feed rows are either a video card or a horizontal Shorts shelf.</summary>
public sealed class FeedTemplateSelector : DataTemplateSelector
{
    public DataTemplate? VideoTemplate { get; set; }
    public DataTemplate? ShelfTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
        (item is ShortsShelf ? ShelfTemplate : VideoTemplate)!;
}
