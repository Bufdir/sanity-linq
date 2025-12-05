// Copy-write 2018 Oslofjord Operations AS

// This file is part of Sanity LINQ (https://github.com/oslofjord/sanity-linq).

//  Sanity LINQ is free software: you can redistribute it and/or modify
//  it under the terms of the MIT Licence.

//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
//  MIT Licence for more details.

//  You should have received a copy of the MIT Licence
//  along with this program.


using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Sanity.Linq.Internal;

namespace Sanity.Linq.QueryProvider;

public sealed class SanityQueryProvider(Type docType, SanityDataContext context, int maxNestingLevel) : IQueryProvider
{
    private readonly object _queryBuilderLock = new();
    public Type DocType { get; } = docType;
    public SanityDataContext Context { get; } = context;

    public int MaxNestingLevel { get; } = maxNestingLevel;

    public IQueryable CreateQuery(Expression expression)
    {
        var elementType = TypeSystem.GetElementType(expression.Type);
        try
        {
            var instance = Activator.CreateInstance(typeof(SanityDocumentSet<>).MakeGenericType(elementType), this, expression);
            return (IQueryable)(instance ?? throw new InvalidOperationException("Failed to create queryable instance."));
        }
        catch (TargetInvocationException tie)
        {
            throw tie.InnerException ?? tie;
        }
    }

    // Queryable's collection-returning standard query operators call this method. 
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        return new SanityDocumentSet<TElement>(this, expression);
    }

    public object Execute(Expression expression)
    {
        return Execute<object>(expression);
    }

    // Queryable's "single value" standard query operators call this method.
    public TResult Execute<TResult>(Expression expression)
    {
        return ExecuteAsync<TResult>(expression).Result;            
    }

    public string GetSanityQuery<TResult>(Expression expression)
    {
        var parser = new SanityExpressionParser(expression, DocType, MaxNestingLevel, typeof(TResult));
        return parser.BuildQuery();
    }

    public async Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        var query = GetSanityQuery<TResult>(expression);          

        // Execute query
        var result = await Context.Client.FetchAsync<TResult>(query, null, cancellationToken).ConfigureAwait(false);

        return result.Result;

    }

      
}