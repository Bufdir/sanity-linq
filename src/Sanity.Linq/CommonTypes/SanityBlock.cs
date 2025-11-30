namespace Sanity.Linq.CommonTypes;

public class SanityBlock : SanityObject
{
    public string Style { get; set; } = "normal";

    public object[] MarkDefs { get; set; } = [];

    public object[] Children { get; set; } = [];

    public SanityReference<SanityImageAsset> Asset { get; set; } = new();

    public int? Level { get; set; }

    public string ListItem { get; set; } = string.Empty;
}