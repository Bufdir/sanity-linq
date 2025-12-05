using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sanity.Linq.Mutations;
using System;
using System.Linq;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityMutationBuilderTests
{
    [Fact]
    public void Build_Includes_CreateIfNotExists_Key()
    {
        var (client, _, fooBuilder, _) = CreateBuilders();
        fooBuilder.CreateIfNotExists(new Foo { Id = "foo-2", Type = "foo" });

        var json = fooBuilder.Build();
        var jo = JsonConvert.DeserializeObject<JObject>(json, client.SerializerSettings)!;
        var mutations = (JArray)jo["mutations"]!;
        Assert.Single(mutations);
        var obj = (JObject)mutations[0]!;
        Assert.True(obj.ContainsKey("createIfNotExists"));
    }

    [Fact]
    public void Build_Includes_CreateOrReplace_Key()
    {
        var (client, _, fooBuilder, _) = CreateBuilders();
        fooBuilder.CreateOrReplace(new Foo { Id = "foo-3", Type = "foo" });

        var json = fooBuilder.Build();
        var jo = JsonConvert.DeserializeObject<JObject>(json, client.SerializerSettings)!;
        var mutations = (JArray)jo["mutations"]!;
        Assert.Single(mutations);
        var obj = (JObject)mutations[0]!;
        Assert.True(obj.ContainsKey("createOrReplace"));
    }

    [Fact]
    public void Build_Only_Includes_Generic_Type_Mutations()
    {
        var (client, _, fooBuilder, barBuilder) = CreateBuilders();

        fooBuilder.Create(new Foo { Id = "foo-1", Type = "foo" });
        barBuilder.Create(new Bar { Id = "bar-1", Type = "bar" });

        var json = fooBuilder.Build();

        // Expect camelCase property name due to default serializer settings
        var jo = JsonConvert.DeserializeObject<JObject>(json, client.SerializerSettings)!;
        var mutations = (JArray)jo["mutations"]!;
        Assert.Single(mutations);

        // The single mutation should be a create-mutation for Foo
        var obj = (JObject)mutations[0]!;
        Assert.True(obj.ContainsKey("create") || obj.ContainsKey("delete") || obj.ContainsKey("patch"));
        // For our scenario we added a create
        Assert.True(obj.ContainsKey("create"));
        var create = (JObject)obj["create"]!;
        Assert.Equal("foo-1", (string?)create["_id"]);
        Assert.Equal("foo", (string?)create["_type"]);
    }

    [Fact]
    public void Build_With_No_Matching_Mutations_Returns_Empty_Array()
    {
        var (client, inner, fooBuilder, barBuilder) = CreateBuilders();

        // Add only Bar mutation, build via Foo builder
        barBuilder.Create(new Bar { Id = "bar-1", Type = "bar" });

        var json = fooBuilder.Build();
        var jo = JsonConvert.DeserializeObject<JObject>(json, client.SerializerSettings)!;
        var mutations = (JArray)jo["mutations"]!;
        Assert.Empty(mutations);
        // Ensure inner still contains the Bar mutation
        Assert.Single(inner.Mutations);
        Assert.Equal(typeof(Bar), inner.Mutations.Single().DocType);
    }

    [Fact]
    public void Clear_Removes_Only_Generic_Type_Mutations()
    {
        var (_, inner, fooBuilder, barBuilder) = CreateBuilders();

        fooBuilder.Create(new Foo { Id = "foo-1", Type = "foo" });
        barBuilder.Create(new Bar { Id = "bar-1", Type = "bar" });

        fooBuilder.Clear();

        Assert.Single(inner.Mutations);
        Assert.Equal(typeof(Bar), inner.Mutations.Single().DocType);
        Assert.Empty(fooBuilder.Mutations);
    }

    [Fact]
    public void Clear_When_No_Matching_Mutations_Does_Not_Remove_Others()
    {
        var (_, inner, fooBuilder, barBuilder) = CreateBuilders();
        barBuilder.Create(new Bar { Id = "bar-1", Type = "bar" });

        fooBuilder.Clear();

        Assert.Single(inner.Mutations);
        Assert.Equal(typeof(Bar), inner.Mutations.Single().DocType);
    }

    [Fact]
    public void Create_AddsMutation_WithCorrectDocType()
    {
        var (_, inner, fooBuilder, _) = CreateBuilders();

        var doc = new Foo { Id = "foo-1", Type = "foo" };
        fooBuilder.Create(doc);

        Assert.Single(inner.Mutations);
        var m = inner.Mutations.Single();
        Assert.Equal(typeof(Foo), m.DocType);
    }

    [Fact]
    public void Create_Null_Throws()
    {
        var (_, _, fooBuilder, _) = CreateBuilders();
        Assert.Throws<ArgumentNullException>(() => fooBuilder.Create(null!));
    }

    [Fact]
    public void CreateIfNotExists_Null_Throws()
    {
        var (_, _, fooBuilder, _) = CreateBuilders();
        Assert.Throws<ArgumentNullException>(() => fooBuilder.CreateIfNotExists(null!));
    }

    [Fact]
    public void CreateOrReplace_Null_Throws()
    {
        var (_, _, fooBuilder, _) = CreateBuilders();
        Assert.Throws<ArgumentNullException>(() => fooBuilder.CreateOrReplace(null!));
    }

    [Fact]
    public void DeleteById_NullOrEmpty_Throws()
    {
        var (_, _, fooBuilder, _) = CreateBuilders();
        Assert.Throws<ArgumentException>(() => fooBuilder.DeleteById(null!));
        Assert.Throws<ArgumentException>(() => fooBuilder.DeleteById(""));
    }

    [Fact]
    public void Mutations_List_Is_Snapshot_And_Not_Live()
    {
        var (_, inner, fooBuilder, _) = CreateBuilders();
        fooBuilder.Create(new Foo { Id = "foo-1", Type = "foo" });

        var snapshot = fooBuilder.Mutations; // creates a new list via ToList()
        // Mutate the returned list locally
        var list = snapshot.ToList();
        list.Clear();

        // Should have no effect on inner storage
        Assert.Single(inner.Mutations);
        Assert.Single(fooBuilder.Mutations);
    }

    [Fact]
    public void Mutations_Property_IsFiltered_By_Generic_Type()
    {
        var (_, _, fooBuilder, barBuilder) = CreateBuilders();

        fooBuilder.Create(new Foo { Id = "foo-1", Type = "foo" });
        barBuilder.Create(new Bar { Id = "bar-1", Type = "bar" });

        var fooMutations = fooBuilder.Mutations;
        Assert.Single(fooMutations);
        Assert.All(fooMutations, m => Assert.Equal(typeof(Foo), m.DocType));
    }

    private static (SanityClient client, SanityMutationBuilder inner, SanityMutationBuilder<Foo> fooBuilder, SanityMutationBuilder<Bar> barBuilder) CreateBuilders()
    {
        var options = new SanityOptions { ProjectId = "proj", Dataset = "ds", UseCdn = true };
        var client = new SanityClient(options);
        var inner = new SanityMutationBuilder(client);
        var foo = inner.For<Foo>();
        var bar = inner.For<Bar>();
        return (client, inner, foo, bar);
    }

    public class Bar
    {
        [JsonProperty("_id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("_type")] public string Type { get; set; } = string.Empty;
    }

    public class Foo
    {
        [JsonProperty("_id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("_type")] public string Type { get; set; } = string.Empty;
    }
}