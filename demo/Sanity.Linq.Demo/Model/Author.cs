using System.Collections.Generic;
using Sanity.Linq.CommonTypes;

namespace Sanity.Linq.Demo.Model;

public class Author : SanityDocument
{
    public string Name { get; set; } = string.Empty;

    public SanitySlug? Slug { get; set; }

    [Include]
    public List<SanityReference<Category>> FavoriteCategories { get; set; } = [];

    [Include]
    public SanityImage[] Images { get; set; } = [];

    public object[] Bio { get; set; } = [];

}