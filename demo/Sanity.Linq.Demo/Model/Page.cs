using Sanity.Linq.CommonTypes;

namespace Sanity.Linq.Demo.Model;

/// <summary>
/// Document type with localization support
/// </summary>
public class Page : SanityDocument
{
    public SanityLocaleString Title { get; set; } = new();

    public SanityLocale<PageOptions> Options { get; set; } = new("pageOptions");
}

public class PageOptions
{
    public bool ShowOnFrontPage { get; set; }

    public string Subtitle { get; set; } = string.Empty;
}