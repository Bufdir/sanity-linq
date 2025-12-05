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

using Sanity.Linq.QueryProvider;

namespace Sanity.Linq.Mutations.Model;

public class SanityPatchByQuery<TDoc> : SanityPatchByQuery
{
    public SanityPatchByQuery()
    {
        Query = string.Empty;
    }
    public SanityPatchByQuery(Expression<Func<TDoc, bool>> query)
    {
        if (query == null)
        {
            throw new ArgumentException("Query field must be set", nameof(query));
        }

        var parser = new SanityExpressionParser(query, typeof(TDoc), MutationQuerySettings.MAX_NESTING_LEVEL);
        var sanityQuery = parser.BuildQuery();
        Query = sanityQuery;
    }

    public SanityPatchByQuery(Expression query)
    {
        if (query == null)
        {
            throw new ArgumentException("Query field must be set", nameof(query));
        }

        var parser = new SanityExpressionParser(query, typeof(TDoc), MutationQuerySettings.MAX_NESTING_LEVEL);
        var sanityQuery = parser.BuildQuery();
        Query = sanityQuery;
    }
}

public class SanityPatchByQuery : SanityPatch
{

    public SanityPatchByQuery()
    {
    }

    public SanityPatchByQuery(Expression<Func<object,bool>> query)
    {
        if (query == null)
        {
            throw new ArgumentException("Query field must be set", nameof(query));
        }

        var parser = new SanityExpressionParser(query, typeof(object), MutationQuerySettings.MAX_NESTING_LEVEL);
        var sanityQuery = parser.BuildQuery();
        Query = sanityQuery;
    }

    public SanityPatchByQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentException("Query field must be set", nameof(query));
        }

        Query = query;
    }

    public string Query { get; set; } = string.Empty;

}