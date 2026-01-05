// Copy-write 2018 Oslofjord Operations AS

// This file is part of Sanity LINQ (https://github.com/oslofjord/sanity-linq).

//  Sanity LINQ is free software: you can redistribute it and/or modify
//  it under the terms of the MIT License.

//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
//  MIT License for more details.

//  You should have received a copy of the MIT License
//  along with this program.

using System.Collections;
using Sanity.Linq.Internal;
using Sanity.Linq.Mutations;
using Sanity.Linq.QueryProvider;

namespace Sanity.Linq;

public abstract class SanityDocumentSet
{
    public SanityDataContext Context { get; protected set; } = null!;
}

public class SanityDocumentSet<TDoc> : SanityDocumentSet, IOrderedQueryable<TDoc>
{
    /// <summary>
    ///     The client calls this constructor to create the data source.
    /// </summary>
    public SanityDocumentSet(SanityOptions options, int maxNestingLevel)
    {
        MaxNestingLevel = maxNestingLevel;
        Context = new SanityDataContext(options, false);
        Provider = new SanityQueryProvider(typeof(TDoc), Context, MaxNestingLevel);
        Expression = Expression.Constant(this);
    }

    public SanityDocumentSet(SanityDataContext context, int maxNestingLevel)
    {
        MaxNestingLevel = maxNestingLevel;
        Context = context;
        Provider = new SanityQueryProvider(typeof(TDoc), context, MaxNestingLevel);
        Expression = Expression.Constant(this);
    }

    /// <summary>
    ///     This constructor is called by Provider.CreateQuery().
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="expression"></param>
    internal SanityDocumentSet(SanityQueryProvider provider, Expression expression)
    {
        Context = provider.Context;

        if (expression == null) throw new ArgumentNullException(nameof(expression));

        if (!typeof(IQueryable<TDoc>).IsAssignableFrom(expression.Type)) throw new ArgumentOutOfRangeException(nameof(expression));

        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Expression = expression;
    }

    public int MaxNestingLevel { get; }

    public SanityMutationBuilder<TDoc> Mutations => Context.Mutations.For<TDoc>();

    public Type ElementType => typeof(TDoc);
    public Expression Expression { get; private set; }
    public IQueryProvider Provider { get; }

    public IEnumerator<TDoc> GetEnumerator()
    {
        var results = Provider.Execute<IEnumerable<TDoc>>(Expression) ?? [];
        return FilterResults(results).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return Provider.Execute<IEnumerable>(Expression).GetEnumerator();
    }

    public async Task<IEnumerable<TDoc>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var results = await ((SanityQueryProvider)Provider).ExecuteWithCallbackAsync<IEnumerable<TDoc>>(Expression, null, cancellationToken).ConfigureAwait(false) ??
                      [];
        return FilterResults(results);
    }

    public async Task<int> ExecuteCountAsync(CancellationToken cancellationToken = default)
    {
        var countMethod = TypeSystem.GetMethod(nameof(Queryable.Count)).MakeGenericMethod(typeof(TDoc));
        var exp = Expression.Call(null, countMethod, Expression);
        return await ((SanityQueryProvider)Provider).ExecuteWithCallbackAsync<int>(exp, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ExecuteCountWithCallbackAsync(ClientCallback callback, CancellationToken cancellationToken = default)
    {
        var countMethod = TypeSystem.GetMethod(nameof(Queryable.Count)).MakeGenericMethod(typeof(TDoc));
        var exp = Expression.Call(null, countMethod, Expression);
        return await ((SanityQueryProvider)Provider).ExecuteWithCallbackAsync<int>(exp, callback, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> ExecuteLongCountAsync(CancellationToken cancellationToken = default)
    {
        var countMethod = TypeSystem.GetMethod(nameof(Queryable.LongCount)).MakeGenericMethod(typeof(TDoc));
        var exp = Expression.Call(null, countMethod, Expression);
        return await ((SanityQueryProvider)Provider).ExecuteWithCallbackAsync<long>(exp, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TDoc?> ExecuteSingleAsync(CancellationToken cancellationToken = default)
    {
        var result = await ((SanityQueryProvider)Provider).ExecuteWithCallbackAsync<TDoc>(Expression, null, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    ///     Executes the query asynchronously and invokes the specified callback with the query result.
    /// </summary>
    /// <param name="callback">
    ///     An optional callback of type <see cref="ClientCallback" /> that will be invoked with the query result.
    /// </param>
    /// <param name="cancellationToken">
    ///     A <see cref="CancellationToken" /> to observe while waiting for the task to complete.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains an <see cref="IEnumerable{TDoc}" />
    ///     representing the query results.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if the query provider or expression is null.
    /// </exception>
    /// <exception cref="Exception">
    ///     Thrown if the query execution fails.
    /// </exception>
    public async Task<IEnumerable<TDoc>> ExecuteWithCallBackAsync(ClientCallback? callback = null, CancellationToken cancellationToken = default)
    {
        var results = await ((SanityQueryProvider)Provider).ExecuteWithCallbackAsync<IEnumerable<TDoc>>(Expression, callback, cancellationToken).ConfigureAwait(false) ??
                      [];
        return FilterResults(results);
    }

    public TDoc? Get(string id)
    {
        return GetAsync(id).GetAwaiter().GetResult();
    }

    public async Task<TDoc?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        return await this.Where(d => d.SanityId() == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Includes the specified property in the query, enabling eager loading of related data.
    /// </summary>
    /// <typeparam name="TProperty">The type of the property to include.</typeparam>
    /// <param name="property">
    ///     An expression representing the property to include. This is typically a navigation property
    ///     that defines a relationship to another entity or collection.
    /// </param>
    /// <returns>
    ///     A <see cref="SanityDocumentSet{TDoc}" /> instance with the specified property included in the query.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the appropriate overload of the Include method cannot be found.
    /// </exception>
    public SanityDocumentSet<TDoc> Include<TProperty>(Expression<Func<TDoc, TProperty>> property)
    {
        var methodInfo = typeof(SanityDocumentSetExtensions).GetMethods().FirstOrDefault(m => m.Name.StartsWith("Include") && m.GetParameters().Length == 2);
        if (methodInfo == null) throw new InvalidOperationException("Include method overload not found.");
        var includeMethod = methodInfo.MakeGenericMethod(typeof(TDoc), typeof(TProperty));
        var exp = Expression.Call(null, includeMethod, Expression, property);
        Expression = exp;
        return this;
    }

    /// <summary>
    ///     Includes the specified property in the query and associates it with the given source name.
    /// </summary>
    /// <typeparam name="TProperty">The type of the property to include.</typeparam>
    /// <param name="property">
    ///     An expression that specifies the property to include in the query.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source to associate with the included property.
    /// </param>
    /// <returns>
    ///     A <see cref="SanityDocumentSet{TDoc}" /> instance with the specified property included.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the corresponding Include method overload with <paramref name="sourceName" /> is not found.
    /// </exception>
    public SanityDocumentSet<TDoc> Include<TProperty>(Expression<Func<TDoc, TProperty>> property, string sourceName)
    {
        var methodInfo = typeof(SanityDocumentSetExtensions).GetMethods().FirstOrDefault(m => m.Name.StartsWith("Include") && m.GetParameters().Length == 3);
        if (methodInfo == null) throw new InvalidOperationException("Include method overload with sourceName not found.");
        var includeMethod = methodInfo.MakeGenericMethod(typeof(TDoc), typeof(TProperty));
        var exp = Expression.Call(null, includeMethod, Expression, property, Expression.Constant(sourceName));
        Expression = exp;
        return this;
    }

    protected virtual IEnumerable<TDoc> FilterResults(IEnumerable<TDoc> results)
    {
        //TODO: Consider merging additions / updates with data source results
        // A full implementation would also require reevaluating ordering and slicing on client side...
        foreach (var item in results) yield return item;
    }
}