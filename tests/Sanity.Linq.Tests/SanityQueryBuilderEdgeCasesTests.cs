using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Sanity.Linq.CommonTypes;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityQueryBuilderEdgeCasesTests
{
    [Fact]
    public void Build_Slice_Skip_Only_Open_Range_To_Max()
    {
        var builder = CreateBuilder();
        var t = builder.GetType();

        t.GetProperty("Skip")!.SetValue(builder, 7);
        t.GetProperty("Take")!.SetValue(builder, 0);

        var miBuild = t.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance)!;
        var result = (string)miBuild.Invoke(builder, [false, 2])!;

        Assert.EndsWith(" [7..2147483647]", result);
    }

    [Fact]
    public void Build_Slice_Take_Equals_One_Uses_Single_Index()
    {
        var builder = CreateBuilder();
        var t = builder.GetType();

        t.GetProperty("Skip")!.SetValue(builder, 5);
        t.GetProperty("Take")!.SetValue(builder, 1);

        var miBuild = t.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance)!;
        var result = (string)miBuild.Invoke(builder, [false, 2])!;

        Assert.EndsWith(" [5]", result);
        Assert.DoesNotContain("..", result);
    }

    [Fact]
    public void GetJoinProjection_IEnumerable_Of_SanityReference()
    {
        var s = CallGetJoinProjection("refs", "refs", typeof(List<SanityReference<Simple>>));
        // No spaces are inserted in this case
        Assert.Equal("refs[]->{...}", s);
    }

    [Fact]
    public void GetPropertyProjectionList_Handles_JObject_And_ListOfJObject()
    {
        var list = CallGetPropertyProjectionList(typeof(WithJObjectPublic), 0, 2);

        // Always contains spread include
        Assert.Contains("...", list);

        // JObject and List<JObject> are not expanded into explicit projections
        Assert.DoesNotContain(list, s => s.StartsWith("obj{", StringComparison.Ordinal));
        Assert.DoesNotContain(list, s => s.StartsWith("arr[]", StringComparison.Ordinal));
    }

    [Fact]
    public void GetPropertyProjectionList_Stops_At_MaxNestingLevel()
    {
        // When nesting level equals max, it should only return "..."
        var list = CallGetPropertyProjectionList(typeof(Simple), 2, 2);
        Assert.Single(list);
        Assert.Equal("...", list[0]);
    }

    // Helper: call static GetJoinProjection
    private static string CallGetJoinProjection(string sourceName, string targetName, Type propertyType, int nestingLevel = 0, int maxNestingLevel = 2)
    {
        var t = GetBuilderType();
        var mi = t.GetMethod("GetJoinProjection", BindingFlags.Public | BindingFlags.Static)!;
        return (string)mi.Invoke(null, [sourceName, targetName, propertyType, nestingLevel, maxNestingLevel])!;
    }

    // Helper: call static GetPropertyProjectionList
    private static List<string> CallGetPropertyProjectionList(Type type, int nestingLevel, int maxNestingLevel)
    {
        var t = GetBuilderType();
        var mi = t.GetMethod("GetPropertyProjectionList", BindingFlags.Public | BindingFlags.Static)!;
        return (List<string>)mi.Invoke(null, [type, nestingLevel, maxNestingLevel])!;
    }

    // Helper: create instance of internal builder
    private static object CreateBuilder()
    {
        var t = GetBuilderType();
        return Activator.CreateInstance(t, nonPublic: true)!;
    }

    // Helper: get internal builder type
    private static Type GetBuilderType()
    {
        var asm = typeof(SanityClient).Assembly;
        return asm.GetType("Sanity.Linq.QueryProvider.SanityQueryBuilder", throwOnError: true)!;
    }

    private class Simple
    { }

    // Use a public helper type to ensure reflection in the library can see its public properties
    // (public members on a non-public declaring type may not be returned by default GetProperties()).
}

// Public helper type used by tests above (must be in same file-scoped namespace)
public class WithJObjectPublic
{
    public List<JObject> Arr { get; set; } = [];
    public JObject? Obj { get; set; }
}