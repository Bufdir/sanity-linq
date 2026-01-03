namespace Sanity.Linq.QueryProvider;

public sealed class DocTypeCache : IDocTypeCache
{
    private static readonly Lazy<DocTypeCache> _instance = new(() => new DocTypeCache());
    public static DocTypeCache Instance => _instance.Value;

    private readonly ConcurrentDictionary<Type, string> _cache = new();

    private DocTypeCache() { }

    public string GetOrAdd(Type type, Func<Type, string> factory)
    {
        return _cache.GetOrAdd(type, factory);
    }

    public void Clear()
    {
        _cache.Clear();
    }
}
