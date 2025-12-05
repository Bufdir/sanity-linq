using System;
using System.Collections.Generic;
using System.Reflection;
using Sanity.Linq.CommonTypes;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityQueryBuilderTests
{
    [Fact]
    public void Build_Expands_Includes_And_Adds_Order_And_Slice()
    {
        var builder = CreateBuilder();
        var t = builder.GetType();

        // Set a simple projection with a field that will be expanded by includes
        t.GetProperty("Projection")!.SetValue(builder, "title,author");

        // Provide includes for the field 'author'
        var includes = new Dictionary<string, string>
        {
            { "author", CallGetJoinProjection("author", "author", typeof(SanityReference<Simple>)) }
        };
        t.GetProperty("Includes")!.SetValue(builder, includes);

        // Add ordering and slice
        t.GetProperty("Orderings")!.SetValue(builder, new List<string> { "title asc" });
        t.GetProperty("Skip")!.SetValue(builder, 2);
        t.GetProperty("Take")!.SetValue(builder, 3);

        // Build
        var miBuild = t.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance)!;
        var result = (string)miBuild.Invoke(builder, [true, 2])!;

        // Should start with star selection and contain expanded include for author
        Assert.StartsWith("*{", result);
        Assert.Contains("author->", result);
        // Should contain ordering and slice [2..4]
        Assert.Contains("| order(title asc)", result);
        Assert.EndsWith(" [2..4]", result);
    }

    [Fact]
    public void GetJoinProjection_Primitive_And_Rename()
    {
        var same = CallGetJoinProjection("title", "title", typeof(string));
        Assert.Equal("title", same);

        var renamed = CallGetJoinProjection("authorRef", "author", typeof(string));
        Assert.Equal("\"author\":authorRef", renamed);
    }

    [Fact]
    public void GetJoinProjection_SanityReference_Minimal()
    {
        var proj = CallGetJoinProjection("author", "author", typeof(SanityReference<Simple>));
        // Note: formatting has a space after the opening brace in this case
        Assert.Equal("author->{ ... }", proj);
    }

    private static string CallGetJoinProjection(string sourceName, string targetName, Type propertyType, int nestingLevel = 0, int maxNestingLevel = 2)
    {
        var t = GetBuilderType();
        var mi = t.GetMethod("GetJoinProjection", BindingFlags.Public | BindingFlags.Static)!;
        return (string)mi.Invoke(null, [sourceName, targetName, propertyType, nestingLevel, maxNestingLevel])!;
    }

    private static object CreateBuilder()
    {
        var t = GetBuilderType();
        return Activator.CreateInstance(t, nonPublic: true)!;
    }

    private static Type GetBuilderType()
    {
        var asm = typeof(SanityClient).Assembly;
        return asm.GetType("Sanity.Linq.QueryProvider.SanityQueryBuilder", throwOnError: true)!;
    }

    private class Simple
    { }
}