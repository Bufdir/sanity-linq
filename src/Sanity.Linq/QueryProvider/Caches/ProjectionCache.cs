namespace Sanity.Linq.QueryProvider;

public sealed class ProjectionCache : IProjectionCache
{
    private static readonly Lazy<ProjectionCache> _instance = new(() => new ProjectionCache());
    public static ProjectionCache Instance => _instance.Value;

    private readonly ConcurrentDictionary<(Type, int, int), string[]> _cache = new();

    private ProjectionCache() { }

    public bool TryGetValue(Type type, int nestingLevel, int maxNestingLevel, out string[]? projections)
    {
        return _cache.TryGetValue((type, nestingLevel, maxNestingLevel), out projections);
    }

    public void TryAdd(Type type, int nestingLevel, int maxNestingLevel, string[] projections)
    {
        _cache.TryAdd((type, nestingLevel, maxNestingLevel), projections);
    }

    public void Clear()
    {
        _cache.Clear();
    }
}
