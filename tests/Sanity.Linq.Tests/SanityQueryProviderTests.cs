using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.QueryProvider;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityQueryProviderTests
{
    [Fact]
    public void Constructor_Sets_Properties()
    {
        // Arrange
        var context = CreateContext();

        // Act
        var provider = new SanityQueryProvider(typeof(MyDoc), context, maxNestingLevel: 4);

        // Assert
        Assert.Equal(typeof(MyDoc), provider.DocType);
        Assert.Same(context, provider.Context);
        Assert.Equal(4, provider.MaxNestingLevel);
    }

    [Fact]
    public void CreateQuery_Typed_Returns_SanityDocumentSet()
    {
        // Arrange
        var context = CreateContext();
        var provider = new SanityQueryProvider(typeof(MyDoc), context, maxNestingLevel: 3);
        var baseSet = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        Expression expression = baseSet.Expression; // a valid IQueryable<T> expression

        // Act
        var result = provider.CreateQuery<MyDoc>(expression);

        // Assert
        var ds = Assert.IsType<SanityDocumentSet<MyDoc>>(result);
        Assert.Same(provider, ((SanityDocumentSet<MyDoc>)ds).Provider);
        Assert.Same(expression, ((SanityDocumentSet<MyDoc>)ds).Expression);
    }

    [Fact]
    public void CreateQuery_Untyped_Returns_SanityDocumentSet()
    {
        // Arrange
        var context = CreateContext();
        var provider = new SanityQueryProvider(typeof(MyDoc), context, maxNestingLevel: 3);
        var baseSet = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        Expression expression = baseSet.Expression;

        // Act
        var queryable = provider.CreateQuery(expression);

        // Assert
        Assert.IsAssignableFrom<IQueryable>(queryable);
        Assert.IsType<SanityDocumentSet<MyDoc>>(queryable);
        var ds = (SanityDocumentSet<MyDoc>)queryable;
        Assert.Same(provider, ds.Provider);
        Assert.Same(expression, ds.Expression);
    }

    [Fact]
    public void GetSanityQuery_Builds_Query_With_Type_Filter_And_Predicate()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 2);
        var queryable = set.Where(d => d.Title == "Hello");
        var provider = (SanityQueryProvider)queryable.Provider;

        // Act
        var groq = provider.GetSanityQuery<IEnumerable<MyDoc>>(queryable.Expression);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(groq));
        Assert.Contains("_type == \"myDoc\"", groq, StringComparison.Ordinal);
        Assert.Contains("title == \"Hello\"", groq, StringComparison.Ordinal);
    }

    private static SanityDataContext CreateContext()
    {
        var options = new SanityOptions
        {
            ProjectId = "testProject",
            Dataset = "testDataset",
            UseCdn = true,
            ApiVersion = "v2021-10-21"
        };
        return new SanityDataContext(options);
    }

    private sealed class MyDoc : SanityDocument
    {
        public string? Title { get; set; }
    }
}