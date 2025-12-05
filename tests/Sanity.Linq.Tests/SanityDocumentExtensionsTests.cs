using System;
using Newtonsoft.Json;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityDocumentExtensionsTests
{
    [Fact]
    public void GetValue_For_Missing_Or_Private_Members_Should_Return_Null_Or_Default()
    {
        var d = new DocWithPrivateMembers();
        // Use fields to avoid warnings
        Assert.True(d.Touch() > 0);

        // Missing member
        Assert.Null(d.GetValue("Nope"));
        Assert.Null(d.GetValue<string>("Nope"));

        // Private members are not accessible via GetValue
        Assert.Null(d.GetValue("Secret"));
        Assert.Null(d.GetValue("HiddenNumber"));
        Assert.Null(d.GetValue("PrivateProp"));

        // Public members still work
        Assert.Equal("pub", d.GetValue("PublicProp"));
        Assert.Equal(11, d.GetValue("PublicField"));
    }

    [Fact]
    public void GetValue_Should_Read_Public_Property_And_Field()
    {
        var a = new DocWithUnderscoreProps
        {
            Name = "Alpha",
            Number = 42
        };

        Assert.Equal("Alpha", a.GetValue("Name"));
        Assert.Equal(42, a.GetValue("Number"));
    }

    [Fact]
    public void GetValueT_Should_Convert_Value_Type()
    {
        var a = new DocWithUnderscoreProps { Name = "123" };
        // Use Number as string via property to test conversion to int
        Assert.Equal(123, a.GetValue<int>("Name"));

        // Non-existent returns default
        Assert.Equal(0, a.GetValue<int>("DoesNotExist"));
    }

    [Fact]
    public void IsDefined_Should_Handle_Null_And_NonNull()
    {
        object? obj = null;
        Assert.False(obj.IsDefined());
        obj = new DocWithUnderscoreProps();
        Assert.True(obj.IsDefined());
    }

    [Fact]
    public void IsDraft_Should_Be_Case_Sensitive()
    {
        var doc = new DocWithUnderscoreProps { _id = "DRAFTS.abc" };
        Assert.False(doc.IsDraft());
    }

    [Fact]
    public void IsDraft_Should_Be_True_When_Id_Starts_With_Drafts()
    {
        var doc = new DocWithUnderscoreProps { _id = "drafts.123" };
        Assert.True(doc.IsDraft());

        doc._id = "123";
        Assert.False(doc.IsDraft());

        doc._id = null;
        Assert.False(doc.IsDraft());
    }

    [Fact]
    public void JsonProperty_With_Uppercase_Names_Should_Not_Be_Recognized()
    {
        var d = new DocWithJsonAttributesUppercase
        {
            Id = "orig",
            Type = "T",
            Rev = "R",
            CreatedAt = DateTimeOffset.MinValue,
            UpdatedAt = DateTimeOffset.MinValue
        };

        // Attribute names are compared case-sensitively in implementation
        Assert.Null(d.SanityId());
        Assert.Null(d.SanityType());
        Assert.Null(d.SanityRevision());
        Assert.Null(d.SanityCreatedAt());
        Assert.Null(d.SanityUpdatedAt());

        d.SetSanityId("x");
        Assert.Equal("orig", d.Id); // unchanged
    }

    [Fact]
    public void Missing_Sanity_Properties_Should_Return_Null_And_Not_Throw_On_Set()
    {
        var d = new MinimalDoc { Something = "x" };
        Assert.Null(d.SanityId());
        Assert.Null(d.SanityType());
        Assert.Null(d.SanityRevision());
        Assert.Null(d.SanityCreatedAt());
        Assert.Null(d.SanityUpdatedAt());

        // Should not throw even when no id property exists
        d.SetSanityId("id");
        Assert.Equal("x", d.Something);
    }

    [Fact]
    public void SanityCreatedAt_And_UpdatedAt_Should_Return_DateTimeOffset()
    {
        var now = DateTimeOffset.UtcNow;
        var later = now.AddMinutes(5);

        var a = new DocWithUnderscoreProps { _createdAt = now, _updatedAt = later };
        Assert.Equal(now, a.SanityCreatedAt());
        Assert.Equal(later, a.SanityUpdatedAt());

        var b = new DocWithJsonAttributes { CreatedAt = now, UpdatedAt = later };
        Assert.Equal(now, b.SanityCreatedAt());
        Assert.Equal(later, b.SanityUpdatedAt());
    }

    [Fact]
    public void SanityId_Should_Read_And_Set_Via_Underscore_Property()
    {
        var doc = new DocWithUnderscoreProps { _id = "abc" };
        Assert.Equal("abc", doc.SanityId());

        doc.SetSanityId("xyz");
        Assert.Equal("xyz", doc._id);
        Assert.Equal("xyz", doc.SanityId());
    }

    [Fact]
    public void SanityId_Should_Work_With_JsonProperty_Mapped_Id()
    {
        var doc = new DocWithJsonAttributes { Id = "id-1" };
        Assert.Equal("id-1", doc.SanityId());

        doc.SetSanityId("id-2");
        Assert.Equal("id-2", doc.Id);
        Assert.Equal("id-2", doc.SanityId());
    }

    [Fact]
    public void SanityType_And_Revision_Should_Be_Resolved()
    {
        var a = new DocWithUnderscoreProps { _type = "post", _rev = "r1" };
        Assert.Equal("post", a.SanityType());
        Assert.Equal("r1", a.SanityRevision());

        var b = new DocWithJsonAttributes { Type = "author", Rev = "r2" };
        Assert.Equal("author", b.SanityType());
        Assert.Equal("r2", b.SanityRevision());
    }

    [Fact]
    public void Underscore_Properties_Should_Be_Case_Insensitive()
    {
        var d = new DocWithWeirdCasingUnderscore
        {
            _ID = "case-id",
            _TYPE = "CaseType",
            _REV = "CaseRev",
            _CREATEDAT = DateTimeOffset.Parse("2020-01-01T00:00:00+00:00"),
            _UPDATEDAT = DateTimeOffset.Parse("2020-01-01T01:00:00+00:00")
        };

        Assert.Equal("case-id", d.SanityId());
        Assert.Equal("CaseType", d.SanityType());
        Assert.Equal("CaseRev", d.SanityRevision());
        Assert.Equal(DateTimeOffset.Parse("2020-01-01T00:00:00+00:00"), d.SanityCreatedAt());
        Assert.Equal(DateTimeOffset.Parse("2020-01-01T01:00:00+00:00"), d.SanityUpdatedAt());

        d.SetSanityId("new-id");
        Assert.Equal("new-id", d._ID);
    }

    private class DocWithJsonAttributes
    {
        [JsonProperty("_createdAt")] public DateTimeOffset CreatedAt { get; set; }
        [JsonProperty("_id")] public string? Id { get; set; }
        [JsonProperty("_rev")] public string? Rev { get; set; }
        public string? Title { get; set; }
        [JsonProperty("_type")] public string? Type { get; set; }
        [JsonProperty("_updatedAt")] public DateTimeOffset UpdatedAt { get; set; }
    }

    private class DocWithJsonAttributesUppercase
    {
        [JsonProperty("_CREATEDAT")] public DateTimeOffset CreatedAt { get; set; }
        [JsonProperty("_ID")] public string? Id { get; set; }
        [JsonProperty("_REV")] public string? Rev { get; set; }
        [JsonProperty("_TYPE")] public string? Type { get; set; }
        [JsonProperty("_UPDATEDAT")] public DateTimeOffset UpdatedAt { get; set; }
    }

    private class DocWithPrivateMembers
    {
        public int PublicField = 11;
        private int HiddenNumber = 7;
        private string Secret = "shh";
        public string PublicProp { get; set; } = "pub";
        private string PrivateProp { get; set; } = "priv";

        // Helper to use private fields/properties to avoid warning-as-error
        public int Touch()
        {
            return (Secret?.Length ?? 0) + HiddenNumber + (PrivateProp?.Length ?? 0) + (PublicProp?.Length ?? 0) + PublicField;
        }
    }

    private class DocWithUnderscoreProps
    {
        public int Number;
        public DateTimeOffset _createdAt { get; set; }
        public string? _id { get; set; }
        public string? _rev { get; set; }
        public string? _type { get; set; }
        public DateTimeOffset _updatedAt { get; set; }

        public string? Name { get; set; }
    }

    private class DocWithWeirdCasingUnderscore
    {
        public DateTimeOffset _CREATEDAT { get; set; }
        public string? _ID { get; set; }
        public string? _REV { get; set; }
        public string? _TYPE { get; set; }
        public DateTimeOffset _UPDATEDAT { get; set; }
    }

    private class MinimalDoc
    {
        public string? Something { get; set; }
    }
}