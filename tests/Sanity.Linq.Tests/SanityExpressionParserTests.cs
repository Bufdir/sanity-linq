using System;
using System.Collections.Generic;
using System.Linq;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.QueryProvider;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityExpressionParserTests
{
    private static SanityDataContext CreateContext()
    {
        // Minimal options; no network call is made when only building the query
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

    [Fact]
    public void GetSanityQuery_ConstructsParserAndBuildsQuery_WithTypeConstraintAndPredicate()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);

        // Compose a simple LINQ expression: ds.Where(d => d.Title == "Hello")
        var queryable = set.Where(d => d.Title == "Hello");

        // Get provider and call the public API that internally constructs SanityExpressionParser
        var provider = (SanityQueryProvider)queryable.Provider;

        // Act
        var groq = provider.GetSanityQuery<IEnumerable<MyDoc>>(queryable.Expression);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(groq));
        // Should include type constraint for MyDoc (derived from SanityDocument)
        Assert.Contains("_type == \"myDoc\"", groq, StringComparison.Ordinal);
        // Should include predicate on title
        Assert.Contains("title == \"Hello\"", groq, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSanityQuery_With_Include_SelectMany_Returns_Expected_Groq()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<IncludeDoc>(context, 3);
        
        // Include(d => d.Refs.SelectMany(r => r))
        var queryable = SanityDocumentSetExtensions.Include<IncludeDoc, IEnumerable<SanityReference<IncludeDoc>>>(set, d => d.Refs!.SelectMany(r => new[] { r }));

        // Act
        var groq = SanityDocumentSetExtensions.GetSanityQuery<IncludeDoc>(queryable);

        // Assert
        Assert.Contains("refs[][defined(@)]", groq);
        Assert.Contains("_type=='reference'=>@->", groq);
    }

    [Fact]
    public void GetSanityQuery_With_Include_OfType_Returns_Expected_Groq()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<IncludeDoc>(context, 3);
        
        // Include(d => d.Refs.OfType<SanityReference<IncludeDoc>>())
        var queryable = SanityDocumentSetExtensions.Include<IncludeDoc, IEnumerable<SanityReference<IncludeDoc>>>(set, d => d.Refs!.OfType<SanityReference<IncludeDoc>>());

        // Act
        var groq = SanityDocumentSetExtensions.GetSanityQuery<IncludeDoc>(queryable);

        // Assert
        Assert.Contains("refs[][defined(@)]", groq);
        Assert.Contains("_type=='reference'=>@->", groq);
    }

    private sealed class IncludeDoc : SanityDocument
    {
        public List<SanityReference<IncludeDoc>>? Refs { get; set; }
    }
}
