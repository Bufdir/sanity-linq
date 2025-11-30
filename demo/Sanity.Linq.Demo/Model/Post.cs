using System;
using System.Collections.Generic;
using Sanity.Linq.CommonTypes;

namespace Sanity.Linq.Demo.Model;

public class Post : SanityDocument
{
    public string Title { get; set; } = string.Empty;

    public SanitySlug? Slug { get; set; }

    public SanityReference<Author> Author { get; set; } = new();

    [Include("author")]
    public Author? DereferencedAuthor { get; set; }

    public SanityImage? MainImage { get; set; }

    public List<SanityReference<Category>> Categories { get; set; } = [];

    public DateTimeOffset? PublishedAt { get; set; }

    public object[] Body { get; set; } = [];
}