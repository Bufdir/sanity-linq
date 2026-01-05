namespace Sanity.Linq.QueryProvider;

public interface IProjectionCache
{
    bool TryGetValue(Type type, int nestingLevel, int maxNestingLevel, out string[]? projections);
    void TryAdd(Type type, int nestingLevel, int maxNestingLevel, string[] projections);
    void Clear();
}
