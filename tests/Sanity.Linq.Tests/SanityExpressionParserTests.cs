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

    [Fact]
    public void GetSanityQuery_With_MethodCall_In_Lambda_DoesNot_Duplicate_Constraints()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);

        // We want to trigger the logic in Visit(LambdaExpression) and then ensure Visit(expression switch) doesn't double-add.
        var queryable = set.Where(d => d.Title!.StartsWith("A"));
        var provider = (SanityQueryProvider)queryable.Provider;

        // Act
        var groq = provider.GetSanityQuery<IEnumerable<MyDoc>>(queryable.Expression);

        // Assert
        // The constraint should appear only once.
        // "title match \"A*\""
        var matches = System.Text.RegularExpressions.Regex.Matches(groq, "title match \"A\\*\"");
        Assert.Single(matches);
    }

    [Fact]
    public void GetSanityQuery_With_Multiple_Includes_Returns_Expected_Groq()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<IncludeDoc>(context, 3);

        // Act
        var queryable = set.Include(d => d.Refs).Include(d => d.Title);
        var groq = SanityDocumentSetExtensions.GetSanityQuery<IncludeDoc>(queryable);

        // Assert
        Assert.Contains("refs", groq);
        Assert.Contains("title", groq);
    }

    [Fact]
    public void GetSanityQuery_With_Complex_Where_Chains_Returns_Expected_Groq()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        
        var queryable = set.Where(d => d.Title == "A" || (d.Title != null && d.Title.StartsWith("B")))
            .OrderByDescending(d => d.Title)
            .Take(10)
            .Skip(5);
        
        var provider = (SanityQueryProvider)queryable.Provider;
        var groq = provider.GetSanityQuery<IEnumerable<MyDoc>>(queryable.Expression);
        
        Assert.Contains("_type == \"myDoc\"", groq);
        Assert.Contains("title == \"A\" || (defined(title) && title != null) && title match \"B*\"", groq);
        Assert.Contains("| order(title desc)", groq);
        Assert.Contains("[5..14]", groq);
    }

    [Fact]
    public void GetSanityQuery_With_Binary_Null_Check_Returns_Defined_Or_Null()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        
        var queryable = set.Where(d => d.Title == null);
        var provider = (SanityQueryProvider)queryable.Provider;
        var groq = provider.GetSanityQuery<IEnumerable<MyDoc>>(queryable.Expression);
        
        // Expected: (!(defined(title)) || title == null)
        Assert.Contains("!(defined(title))", groq);
        Assert.Contains("title == null", groq);
    }

    [Fact]
    public void GetSanityQuery_With_Binary_Not_Null_Check_Returns_Defined_And_Not_Null()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        
        var queryable = set.Where(d => d.Title != null);
        var provider = (SanityQueryProvider)queryable.Provider;
        var groq = provider.GetSanityQuery<IEnumerable<MyDoc>>(queryable.Expression);
        
        // Expected: (defined(title) && title != null)
        Assert.Contains("defined(title)", groq);
        Assert.Contains("title != null", groq);
    }

    private sealed class IncludeDoc : SanityDocument
    {
        public List<SanityReference<IncludeDoc>>? Refs { get; set; }
        public string? Title { get; set; }
    }
}
