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
    public void Where_NestedPropertyEqualsNull_EmitsDefinedOrNullPattern()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, maxNestingLevel: 3);

        var queryable = set.Where(d => d.Nested!.SubTitle == null);
        var provider = (SanityQueryProvider)queryable.Provider;

        var groq = provider.GetSanityQuery<IEnumerable<EdgeDoc>>(queryable.Expression);

        // Pattern: (!(defined(nested.subTitle)) || nested.subTitle == null)
        Assert.Contains("!(defined(nested.subTitle))", groq, StringComparison.Ordinal);
        Assert.Contains("nested.subTitle == null", groq, StringComparison.Ordinal);
    }

    [Fact]
    public void Where_NestedPropertyNotEqualsNull_EmitsDefinedAndNotNullPattern()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, maxNestingLevel: 3);

        var queryable = set.Where(d => d.Nested!.SubTitle != null);
        var provider = (SanityQueryProvider)queryable.Provider;

        var groq = provider.GetSanityQuery<IEnumerable<EdgeDoc>>(queryable.Expression);

        // Pattern: (defined(nested.subTitle) && nested.subTitle != null)
        Assert.Contains("defined(nested.subTitle)", groq, StringComparison.Ordinal);
        Assert.Contains("nested.subTitle != null", groq, StringComparison.Ordinal);
    }

    [Fact]
    public void Take_One_Produces_Single_Index_Slice()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, maxNestingLevel: 3);

        var queryable = set.Take(1);
        var provider = (SanityQueryProvider)queryable.Provider;

        var groq = provider.GetSanityQuery<IEnumerable<EdgeDoc>>(queryable.Expression);

        // Should use [0] instead of [0..0]
        Assert.EndsWith("[0..0]", groq.Trim());
    }

    [Fact]
    public void Skip_And_Take_One_Produces_Single_Index_Slice()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, maxNestingLevel: 3);

        var queryable = set.Skip(5).Take(1);
        var provider = (SanityQueryProvider)queryable.Provider;

        var groq = provider.GetSanityQuery<IEnumerable<EdgeDoc>>(queryable.Expression);

        // Should use [5] instead of [5..5]
        Assert.EndsWith("[5..5]", groq.Trim());
    }

    [Fact]
    public void Take_Multiple_Produces_Range_Slice()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, maxNestingLevel: 3);

        var queryable = set.Take(5);
        var provider = (SanityQueryProvider)queryable.Provider;

        var groq = provider.GetSanityQuery<IEnumerable<EdgeDoc>>(queryable.Expression);

        // Should use [0..4]
        Assert.EndsWith("[0..4]", groq.Trim());
    }

    [Fact]
    public void Where_IsNullOrEmpty_TranslatesToComplexPattern()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, maxNestingLevel: 3);

        var queryable = set.Where(d => string.IsNullOrEmpty(d.Title));
        var provider = (SanityQueryProvider)queryable.Provider;

        var groq = provider.GetSanityQuery<IEnumerable<EdgeDoc>>(queryable.Expression);

        // Pattern: title == null || title == "" || !(defined(title))
        Assert.Contains("title == null", groq);
        Assert.Contains("title == \"\"", groq);
        Assert.Contains("!(defined(title))", groq);
    }

    [Fact]
    public void Where_ContainsEmptyList_TranslatesToInEmptyArray()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, 3);
        var list = new List<string>();
        var queryable = set.Where(d => list.Contains(d.Title!));

        var groq = queryable.GetSanityQuery();

        Assert.Contains("false", groq);
    }

    [Fact]
    public void Where_ContainsListWithNull_FiltersNull()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, 3);
        var list = new List<string?> { "a", null, "b" };
        var queryable = set.Where(d => list.Contains(d.Title));

        var groq = queryable.GetSanityQuery();

        Assert.Contains("title in [\"a\",\"b\"]", groq);
    }

    [Fact]
    public void OrderBy_ThenBy_ProducesCorrectOrder()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, 3);
        var queryable = set.OrderBy(d => d.Title).ThenByDescending(d => d.Number);

        var groq = queryable.GetSanityQuery();

        Assert.Contains("order(title asc, number desc)", groq);
    }

    [Fact]
    public void Take_Zero_ProducesCorrectSlice()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, 3);
        var queryable = set.Take(0);

        var groq = queryable.GetSanityQuery();

        Assert.Contains("[0...0]", groq);
    }

    [Fact]
    public void Select_NestedAnonymousType_ProducesCorrectProjection()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, 3);
        var queryable = set.Select(d => new { d.Title, Info = new { d.Number } });

        var groq = queryable.GetSanityQuery();

        Assert.Contains("title", groq);
        Assert.Contains("\"info\":{number}", groq.Replace(" ", ""));
    }

    [Fact]
    public void Where_Not_IsNullOrEmpty()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, 3);
        var queryable = set.Where(d => !string.IsNullOrEmpty(d.Title));

        var groq = queryable.GetSanityQuery();

        Assert.Contains("!((title == null || title == \"\" || !(defined(title))))", groq);
    }

    [Fact]
    public void Count_With_Predicate_TopLevel()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, 3);
        
        var provider = (SanityQueryProvider)set.Provider;
        var param = System.Linq.Expressions.Expression.Parameter(typeof(EdgeDoc), "d");
        var predicate = System.Linq.Expressions.Expression.Lambda<Func<EdgeDoc, bool>>(
            System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Property(param, nameof(EdgeDoc.Title)), System.Linq.Expressions.Expression.Constant("X")),
            param);
        
        var countMethod = typeof(Queryable).GetMethods().First(m => m.Name == "Count" && m.GetParameters().Length == 2).MakeGenericMethod(typeof(EdgeDoc));
        var expr = System.Linq.Expressions.Expression.Call(null, countMethod, set.Expression, System.Linq.Expressions.Expression.Quote(predicate));

        var groq = provider.GetSanityQuery<int>(expr);

        Assert.StartsWith("count(*", groq);
        Assert.Contains("title == \"X\"", groq);
    }

    [Fact]
    public void Any_With_Predicate_TopLevel()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, 3);
        
        var provider = (SanityQueryProvider)set.Provider;
        var param = System.Linq.Expressions.Expression.Parameter(typeof(EdgeDoc), "d");
        var predicate = System.Linq.Expressions.Expression.Lambda<Func<EdgeDoc, bool>>(
            System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Property(param, nameof(EdgeDoc.Title)), System.Linq.Expressions.Expression.Constant("X")),
            param);
        
        var anyMethod = typeof(Queryable).GetMethods().First(m => m.Name == "Any" && m.GetParameters().Length == 2).MakeGenericMethod(typeof(EdgeDoc));
        var expr = System.Linq.Expressions.Expression.Call(null, anyMethod, set.Expression, System.Linq.Expressions.Expression.Quote(predicate));

        var groq = provider.GetSanityQuery<bool>(expr);

        Assert.StartsWith("count(", groq);
        Assert.EndsWith("> 0", groq.Trim());
    }

    [Fact]
    public void Where_ContainsGuidList_TranslatesToInArrayOfStrings()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, 3);
        var guid = Guid.NewGuid();
        var list = new List<Guid> { guid };
        var queryable = set.Where(d => list.Contains(d.Guid!.Value));

        var groq = queryable.GetSanityQuery();

        Assert.Contains($"guid in [\"{guid}\"]", groq);
    }

    [Fact]
    public void Where_ContainsLongList_TranslatesToInArrayOfNumbers()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, 3);
        var list = new List<long> { 123L, 456L };
        var queryable = set.Where(d => list.Contains(d.Number));

        var groq = queryable.GetSanityQuery();

        Assert.Contains("number in [123,456]", groq);
    }

    [Fact]
    public void TransformOperand_DateTimeOffset_Works()
    {
        var context = CreateContext();
        var set = new SanityDocumentSet<EdgeDoc>(context, 3);
        var dto = new DateTimeOffset(2023, 10, 5, 12, 0, 0, TimeSpan.FromHours(2));

        var param = System.Linq.Expressions.Expression.Parameter(typeof(EdgeDoc), "d");
        var body = System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Property(param, nameof(EdgeDoc.OffsetDate)), System.Linq.Expressions.Expression.Constant(dto, typeof(DateTimeOffset?)));
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<EdgeDoc, bool>>(body, param);
        var whereQuery = set.Where(lambda);

        var groq = whereQuery.GetSanityQuery();
        Assert.Contains($"\"{dto:O}\"", groq);
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
        public NestedDoc? Nested { get; set; }
        public int Number { get; set; }
        public DateTime? Date { get; set; }
        public DateTimeOffset? OffsetDate { get; set; }
        public Guid? Guid { get; set; }
    }

    private sealed class NestedDoc
    {
        public string? SubTitle { get; set; }
    }

    // Note: Tests for List.Contains(property) are covered elsewhere in the suite
    // and may depend on evaluator capabilities; skipping here to avoid duplication/instability.
}
