namespace Sanity.Linq.CommonTypes;

public class SanitySlug(string current) : SanityObject
{
    public string Current { get; set; } = current;
}