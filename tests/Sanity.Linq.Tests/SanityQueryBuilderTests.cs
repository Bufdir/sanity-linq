using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.QueryProvider;
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
        Assert.Contains("author", result);
        Assert.Contains("_type=='reference'=>@->", result);
        // Should contain ordering and slice [2..4]
        Assert.Contains("| order(title asc)", result);
        Assert.EndsWith(" [2..4]", result);
    }

    [Fact]
    public void ExpandIncludesInProjection_Uses_Method_Parameter_Not_Instance_Field()
    {
        var builder = CreateBuilder();
        var t = builder.GetType();
        var helperType = typeof(SanityQueryBuilderHelper);

        // Set a simple projection where include will expand
        t.GetProperty("Projection")!.SetValue(builder, "author");

        // Leave instance Includes empty to ensure method parameter is honored
        t.GetProperty("Includes")!.SetValue(builder, new Dictionary<string, string>());

        // Prepare includes dictionary passed directly to the private method
        var paramIncludes = new Dictionary<string, string>
        {
            { "author", CallGetJoinProjection("author", "author", typeof(SanityReference<Simple>)) }
        };

        // Invoke private ExpandIncludesInProjection via reflection from Helper
        var mi = helperType.GetMethod("ExpandIncludesInProjection", BindingFlags.Public | BindingFlags.Static)!;
        var expanded = (string)mi.Invoke(null, new object[] { "author", paramIncludes })!;
        
        Assert.Contains("author", expanded);
        Assert.Contains("_type=='reference'=>@->", expanded);
    }

    [Fact]
    public void GroqToJson_ShouldPreserveSpacesInStrings()
    {
        var helperType = typeof(SanityQueryBuilderHelper);
        var mi = helperType.GetMethod("GroqToJson", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(mi);

        // This simulates a projection that might contain a string with a space
        var groq = "title == \"John Doe\"";
        var json = (string)mi.Invoke(null, new object[] { groq })!;

        var miJsonToGroq = helperType.GetMethod("JsonToGroq", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(miJsonToGroq);
        var finalGroq = (string)miJsonToGroq.Invoke(null, new object[] { json })!;

        Assert.Equal("title==\"John Doe\"", finalGroq);
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
        Assert.Contains("author", proj);
        Assert.Contains("_type=='reference'=>@->", proj);
    }

    [Fact]
    public void GetJoinProjection_Reference_UsesDereferencingSwitch()
    {
        var proj = CallGetJoinProjection("author", "author", typeof(SanityReference<Simple>));
        
        // Expected format: author{...,_type=='reference'=>@->{...}}
        Assert.Contains("author{", proj);
        Assert.Contains("_type=='reference'=>@->", proj);
        // It should contain the fields (or spread) twice, once for the reference itself and once for the dereferenced object
        Assert.Contains("...", proj);
    }

    [Fact]
    public void GetJoinProjection_IEnumerableReference_UsesDereferencingSwitchAndDefinedCheck()
    {
        var proj = CallGetJoinProjection("authors", "authors", typeof(List<SanityReference<Simple>>));
        
        // Expected format: authors[][defined(@)]{...,_type=='reference'=>@->{...}}
        Assert.Contains("authors[][defined(@)]", proj);
        Assert.Contains("_type=='reference'=>@->", proj);
    }

    [Fact]
    public void GetJoinProjection_ImageAsset_HandlesDereferencedAsset()
    {
        var proj = CallGetJoinProjection("image", "image", typeof(Image));
        
        // Expected to contain asset->{...}
        Assert.Contains("asset->", proj);
        Assert.Contains("image{", proj);
    }

    [Fact]
    public void GetJoinProjection_PropertyToSanityReference_HandlesDereferencedProperty()
    {
        var proj = CallGetJoinProjection("prop", "prop", typeof(PropertyWithRef));
        
        // Should contain the reference field with dereferencing switch
        Assert.Contains("myRef{", proj);
        Assert.Contains("_type=='reference'=>@->", proj);
    }

    [Fact]
    public void GetJoinProjection_PropertyToListOfSanityReference_HandlesDereferencedProperty()
    {
        var proj = CallGetJoinProjection("prop", "prop", typeof(PropertyWithRefList));
        
        // Should contain the reference field with dereferencing switch
        Assert.Contains("myRefs[][defined(@)]", proj);
        Assert.Contains("_type=='reference'=>@->", proj);
    }

    [Fact]
    public void AddProjection_ChainsProjections()
    {
        var builder = (SanityQueryBuilder)CreateBuilder();
        builder.AddProjection("field1");
        builder.AddProjection("field2");
        Assert.Equal((string)"field1.field2", (string)builder.Projection);

        builder.Projection = string.Empty;
        builder.AddProjection("{a}");
        builder.AddProjection("{b}");
        Assert.Equal((string)"{a} {b}", (string)builder.Projection);
    }

    [Fact]
    public void AppendProjection_WithAggregate_UsesDotNotation()
    {
        var builder = (SanityQueryBuilder)CreateBuilder();
        builder.AggregateFunction = "count";
        var sb = new StringBuilder();
        
        var t = builder.GetType();
        var mi = t.GetMethod("AppendProjection", BindingFlags.NonPublic | BindingFlags.Instance)!;
        mi.Invoke(builder, [sb, "title"]);
        
        Assert.Equal((string)".title", (string)sb.ToString());
    }

    [Fact]
    public void AppendProjection_Flattening_WrapsWithSpread()
    {
        var builder = (SanityQueryBuilder)CreateBuilder();
        builder.FlattenProjection = true;
        var sb = new StringBuilder();
        
        var t = builder.GetType();
        var mi = t.GetMethod("AppendProjection", BindingFlags.NonPublic | BindingFlags.Instance)!;
        mi.Invoke(builder, [sb, "title, author"]);
        
        Assert.Contains(" {...title, author}", sb.ToString());
    }

    [Fact]
    public void GetPropertyProjectionList_IsCached()
    {
        var t = typeof(SanityQueryBuilder);
        var mi = t.GetMethod("GetPropertyProjectionList", BindingFlags.Public | BindingFlags.Static)!;
        
        // Initial call
        var first = (List<string>)mi.Invoke(null, [typeof(Simple), 0, 2])!;
        
        // Second call should come from cache (we can't easily prove it's from cache without reflection on the private field, 
        // but we can at least ensure it returns the same content)
        var second = (List<string>)mi.Invoke(null, [typeof(Simple), 0, 2])!;
        
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first[0], second[0]);

        // Reflection to check cache
        var cacheInstance = ProjectionCache.Instance;
        var cacheField = typeof(ProjectionCache).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cacheDict = (System.Collections.ICollection)cacheField.GetValue(cacheInstance)!;
        Assert.True(cacheDict.Count > 0);
    }

    [Fact]
    public void DocTypeCache_IsCached()
    {
        var builder = (SanityQueryBuilder)CreateBuilder();
        builder.DocType = typeof(AssetDoc);
        
        var t = builder.GetType();
        var mi = t.GetMethod("AddDocTypeConstraintIfAny", BindingFlags.NonPublic | BindingFlags.Instance)!;
        
        // Initial call
        mi.Invoke(builder, null);
        
        // Reflection to check cache
        var cacheInstance = DocTypeCache.Instance;
        var cacheField = typeof(DocTypeCache).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cacheDict = (System.Collections.ICollection)cacheField.GetValue(cacheInstance)!;
        Assert.True(cacheDict.Count > 0);
    }

    private static string CallGetJoinProjection(string sourceName, string targetName, Type propertyType, int nestingLevel = 0, int maxNestingLevel = 2)
    {
        var t = GetBuilderType();
        var mi = t.GetMethod("GetJoinProjection", BindingFlags.Public | BindingFlags.Static)!;
        return (string)mi.Invoke(null, [sourceName, targetName, propertyType, nestingLevel, maxNestingLevel, false])!;
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

    public class Simple
    { }

    public class AssetDoc : SanityDocument { }

    public class Image
    {
        [Include]        public SanityReference<AssetDoc>? Asset { get; set; }
    }

    public class PropertyWithRef
    {
        [Include]        public SanityReference<Simple>? MyRef { get; set; }
    }

    public class PropertyWithRefList
    {
        [Include]        public List<SanityReference<Simple>>? MyRefs { get; set; }
    }
}
