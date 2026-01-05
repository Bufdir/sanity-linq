namespace Sanity.Linq.QueryProvider;

public interface IDocTypeCache
{
    string GetOrAdd(Type type, Func<Type, string> factory);
    void Clear();
}
