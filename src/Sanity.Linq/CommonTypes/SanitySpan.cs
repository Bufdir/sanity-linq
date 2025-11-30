namespace Sanity.Linq.CommonTypes;

public class SanitySpan : SanityObject
{
    public string Text { get; set; } = string.Empty;

    public string[] Marks { get; set; } = [];
}