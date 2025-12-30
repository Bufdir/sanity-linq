namespace Sanity.Linq.QueryProvider;

internal interface ISanityQueryProvider : IQueryProvider
{
    TResult ExecuteWithCallback<TResult>(Expression expression, ContentCallback? callback = null);
    Task<TResult> ExecuteWithCallbackAsync<TResult>(Expression expression, ContentCallback? callback = null, CancellationToken cancellationToken = default);
}