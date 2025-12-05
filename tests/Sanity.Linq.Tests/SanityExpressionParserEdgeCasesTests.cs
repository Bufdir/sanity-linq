using System;
using System.Collections.Generic;
using System.Linq;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.QueryProvider;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityExpressionParserEdgeCasesTests
{
    [Fact]
    public void Query_WithoutWhere_IncludesTypeConstraint()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, maxNestingLevel: 3);

        // Bare set: use the root expression directly (no Where/Select)
        var provider = (SanityQueryProvider)set.Provider;

        var groq = provider.GetSanityQuery<IEnumerable<EdgeDoc>>(set.Expression);

        Assert.False(string.IsNullOrWhiteSpace(groq));
        Assert.Contains("_type == \"edgeDoc\"", groq, StringComparison.Ordinal);
    }

    [Fact]
    public void Where_TagsContainsConstant_TranslatesToValueInPropertyArray()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, maxNestingLevel: 3);

        var queryable = set.Where(d => d.Tags!.Contains("news"));
        var provider = (SanityQueryProvider)queryable.Provider;

        var groq = provider.GetSanityQuery<IEnumerable<EdgeDoc>>(queryable.Expression);

        // Pattern: "news" in tags
        Assert.Contains("\"news\" in tags", groq, StringComparison.Ordinal);
    }

    [Fact]
    public void Where_TitleEqualsNull_EmitsDefinedOrNullPattern()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, maxNestingLevel: 3);

        var queryable = set.Where(d => d.Title == null);
        var provider = (SanityQueryProvider)queryable.Provider;

        var groq = provider.GetSanityQuery<IEnumerable<EdgeDoc>>(queryable.Expression);

        // Pattern: (!(defined(title)) || title == null)
        Assert.Contains("!(defined(title))", groq, StringComparison.Ordinal);
        Assert.Contains("title == null", groq, StringComparison.Ordinal);
    }

    [Fact]
    public void Where_TitleNotEqualsNull_EmitsDefinedAndNotNullPattern()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, maxNestingLevel: 3);

        var queryable = set.Where(d => d.Title != null);
        var provider = (SanityQueryProvider)queryable.Provider;

        var groq = provider.GetSanityQuery<IEnumerable<EdgeDoc>>(queryable.Expression);

        // Pattern: (defined(title) && title != null)
        Assert.Contains("defined(title)", groq, StringComparison.Ordinal);
        Assert.Contains("title != null", groq, StringComparison.Ordinal);
    }

    [Fact]
    public void Where_TitleStartsWith_UsesMatchWildcard()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, maxNestingLevel: 3);

        var queryable = set.Where(d => d.Title!.StartsWith("Hel"));
        var provider = (SanityQueryProvider)queryable.Provider;

        var groq = provider.GetSanityQuery<IEnumerable<EdgeDoc>>(queryable.Expression);

        // Pattern: title match "Hel*"
        Assert.Contains("title match \"Hel*\"", groq, StringComparison.Ordinal);
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

    private sealed class EdgeDoc : SanityDocument
    {
        public string[]? Tags { get; set; }
        public string? Title { get; set; }
    }

    // Note: Tests for List.Contains(property) are covered elsewhere in the suite
    // and may depend on evaluator capabilities; skipping here to avoid duplication/instability.
}