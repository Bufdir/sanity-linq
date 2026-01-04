using System;
using Sanity.Linq.QueryProvider;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityQueryPrettyPrintTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void PrettyPrintQuery_Handles_NullOrEmpty(string? input, string? expected)
    {
        var result = SanityQueryFormatter.Format(input!);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("*[_type == \"movie\"]", "*[_type == \"movie\"]")]
    [InlineData("*[_type == 'movie']", "*[_type == 'movie']")]
    public void PrettyPrintQuery_Handles_BasicQuery(string input, string expected)
    {
        var result = SanityQueryFormatter.Format(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("*[_type == \"movie\"]{title, year}", "*[_type == \"movie\"]{\n  title,\n  year\n}")]
    [InlineData("*[_type == \"movie\"]{title,year}", "*[_type == \"movie\"]{\n  title,\n  year\n}")]
    public void PrettyPrintQuery_Handles_SimpleProjection(string input, string expected)
    {
        var result = SanityQueryFormatter.Format(input);
        Assert.Equal(expected.Replace("\n", Environment.NewLine), result);
    }

    [Theory]
    [InlineData("*[_type == \"movie\"]{title, \"mainActor\": actors[0]->name}", "*[_type == \"movie\"]{\n  title,\n  \"mainActor\": actors[0]->name\n}")]
    [InlineData("*[_type == \"movie\"]{\"info\": {title, year}}", "*[_type == \"movie\"]{\n  \"info\": {\n    title,\n    year\n  }\n}")]
    public void PrettyPrintQuery_Handles_NestedObjects(string input, string expected)
    {
        var result = SanityQueryFormatter.Format(input);
        Assert.Equal(expected.Replace("\n", Environment.NewLine), result);
    }

    [Theory]
    [InlineData("*[_type == \"movie\" && title == \"A {Braced} Title\"]", "*[_type == \"movie\" && title == \"A {Braced} Title\"]")]
    [InlineData("*[_type == \"movie\" && title == 'A {Braced} Title']", "*[_type == \"movie\" && title == 'A {Braced} Title']")]
    [InlineData("*[title == \"Escaped \\\" Quote\"]", "*[title == \"Escaped \\\" Quote\"]")]
    public void PrettyPrintQuery_Ignores_Content_In_Quotes(string input, string expected)
    {
        var result = SanityQueryFormatter.Format(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("*[_type == \"movie\"][0...10]", "*[_type == \"movie\"][0...10]")]
    [InlineData("*[_type == \"movie\"]{actors[role == \"Director\"]{name}}", "*[_type == \"movie\"]{\n  actors[role == \"Director\"]{\n    name\n  }\n}")]
    public void PrettyPrintQuery_Handles_Brackets_And_Slices(string input, string expected)
    {
        var result = SanityQueryFormatter.Format(input);
        Assert.Equal(expected.Replace("\n", Environment.NewLine), result);
    }

    [Theory]
    [InlineData("count(*[_type == \"movie\"])", "count(*[_type == \"movie\"])")]
    [InlineData("defined(publishDate)", "defined(publishDate)")]
    public void PrettyPrintQuery_Handles_Functions(string input, string expected)
    {
        var result = SanityQueryFormatter.Format(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("*[_type == \"movie\"]  {  title,  year  }", "*[_type == \"movie\"] {\n  title,\n  year\n}")]
    public void PrettyPrintQuery_Normalizes_Whitespace(string input, string expected)
    {
        var result = SanityQueryFormatter.Format(input);
        Assert.Equal(expected.Replace("\n", Environment.NewLine), result);
    }

    [Theory]
    [InlineData("*[_type == \"movie\"]{...}", "*[_type == \"movie\"]{ ... }")]
    [InlineData("*[_type == \"movie\"]{  ...  }", "*[_type == \"movie\"]{ ... }")]
    [InlineData("*[_type == \"movie\"]{title, ...}", "*[_type == \"movie\"]{\n  title,\n  ...\n}")]
    public void PrettyPrintQuery_Handles_SpreadOperator(string input, string expected)
    {
        var result = SanityQueryFormatter.Format(input);
        Assert.Equal(expected.Replace("\n", Environment.NewLine), result);
    }

    [Fact]
    public void PrettyPrintQuery_ComplexQuery()
    {
        var input = "*[_type == \"movie\" && (year > 2000 || rating > 8)]{title, \"actors\": actors[]->{name, \"birthYear\": details.birthYear}, \"director\": director->name}[0...5]";
        var expected = @"*[_type == ""movie"" && (year > 2000 || rating > 8)]{
  title,
  ""actors"": actors[]->{
    name,
    ""birthYear"": details.birthYear
  },
  ""director"": director->name
}[0...5]";

        var result = SanityQueryFormatter.Format(input);
        Assert.Equal(expected.Replace("\r\n", "\n"), result.Replace("\r\n", "\n"));
    }
}