using System.Collections.Generic;
using Newtonsoft.Json;
using Sanity.Linq.CommonTypes;

namespace Sanity.Linq.Demo.Model;

public class Category
{
    /// <summary>
    /// Use of JsonProperty to serialize to Sanity _id field.
    /// A alternative to inheriting SanityDocument class
    /// </summary>
    [JsonProperty("_id")]
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>
    /// Type field is also required
    /// </summary>
    [JsonProperty("_type")]
    public string DocumentType => "category";

    public int InternalId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string[] Tags { get; set; } = [];

    public int[] Numbers { get; set; } = [];

    public List<Category> SubCategories { get; set; } = [];

    [Include]
    public SanityImage? MainImage { get; set; }
}