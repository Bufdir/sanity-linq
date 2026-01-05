namespace Sanity.Linq.QueryProvider;

internal interface ISanityQueryProvider : IQueryProvider
{
    TResult ExecuteWithCallback<TResult>(Expression expression, ClientCallback? callback = null);
    Task<TResult> ExecuteWithCallbackAsync<TResult>(Expression expression, ClientCallback? callback = null, CancellationToken cancellationToken = default);
}