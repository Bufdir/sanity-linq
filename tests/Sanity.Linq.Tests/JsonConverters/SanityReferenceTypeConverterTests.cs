using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.JsonConverters;
using Xunit;

namespace Sanity.Linq.Tests.JsonConverters;

public class SanityReferenceTypeConverterTests
{
    private readonly SanityReferenceTypeConverter _converter;
    private readonly JsonSerializer _serializer;

    public SanityReferenceTypeConverterTests()
    {
        _converter = new SanityReferenceTypeConverter();
        _serializer = new JsonSerializer
        {
            NullValueHandling = NullValueHandling.Ignore
        };
        _serializer.Converters.Add(_converter);
    }

    [Fact]
    public void CanConvert_ShouldReturnTrueForSanityReference()
    {
        Assert.True(_converter.CanConvert(typeof(SanityReference<TestDocument>)));
    }

    [Fact]
    public void CanConvert_ShouldReturnFalseForOtherTypes()
    {
        Assert.False(_converter.CanConvert(typeof(string)));
        Assert.False(_converter.CanConvert(typeof(TestDocument)));
    }

    [Fact]
    public void ReadJson_ShouldDeserializeNormalReference()
    {
        var json = @"{ ""_ref"": ""id123"", ""_type"": ""reference"" }";
        var reader = new JsonTextReader(new StringReader(json));

        var result = _converter.ReadJson(reader, typeof(SanityReference<TestDocument>), null, _serializer) as SanityReference<TestDocument>;

        Assert.NotNull(result);
        Assert.Equal("id123", result.Ref);
    }

    [Fact]
    public void ReadJson_ShouldDeserializeExpandedReference()
    {
        var json = @"{ ""_id"": ""id123"", ""_type"": ""testDoc"", ""_key"": ""key456"", ""_weak"": true, ""name"": ""Test Name"" }";
        var reader = new JsonTextReader(new StringReader(json));

        var result = _converter.ReadJson(reader, typeof(SanityReference<TestDocument>), null, _serializer) as SanityReference<TestDocument>;

        Assert.NotNull(result);
        Assert.Equal("id123", result.Ref);
        Assert.Equal("key456", result.SanityKey);
        Assert.True(result.Weak);
        Assert.NotNull(result.Value);
        Assert.Equal("Test Name", result.Value.Name);
    }

    [Fact]
    public void WriteJson_ShouldSerializeReferenceWithRef()
    {
        var reference = new SanityReference<TestDocument>
        {
            Ref = "id123",
            SanityKey = "key456",
            Weak = true
        };

        var sw = new StringWriter();
        _converter.WriteJson(new JsonTextWriter(sw), reference, _serializer);
        var json = sw.ToString();

        var obj = JObject.Parse(json);
        Assert.Equal("id123", obj["_ref"]?.ToString());
        Assert.Equal("reference", obj["_type"]?.ToString());
        Assert.Equal("key456", obj["_key"]?.ToString());
        Assert.Equal(true, obj["_weak"]?.Value<bool>());
    }

    [Fact]
    public void WriteJson_ShouldSerializeReferenceWithValue()
    {
        var reference = new SanityReference<TestDocument>
        {
            Value = new TestDocument { Id = "id789", Name = "Document Name" },
            SanityKey = "key101"
        };

        var sw = new StringWriter();
        _converter.WriteJson(new JsonTextWriter(sw), reference, _serializer);
        var json = sw.ToString();

        var obj = JObject.Parse(json);
        Assert.Equal("id789", obj["_ref"]?.ToString());
        Assert.Equal("reference", obj["_type"]?.ToString());
        Assert.Equal("key101", obj["_key"]?.ToString());
    }

    [Fact]
    public void WriteJson_ShouldGenerateKeyIfMissing()
    {
        // SanityReference constructor generates a key, but let's test the converter logic
        var reference = new SanityReference<TestDocument>
        {
            Ref = "id123"
        };
        reference.SanityKey = null!; // Force null to test generator in converter

        var sw = new StringWriter();
        _converter.WriteJson(new JsonTextWriter(sw), reference, _serializer);
        var json = sw.ToString();

        var obj = JObject.Parse(json);
        Assert.False(string.IsNullOrEmpty(obj["_key"]?.ToString()));
    }

    [Fact]
    public void ReadJson_ShouldReturnNullForInvalidJson()
    {
        var json = @"""not an object""";
        var reader = new JsonTextReader(new StringReader(json));

        var result = _converter.ReadJson(reader, typeof(SanityReference<TestDocument>), null, _serializer);

        Assert.Null(result);
    }

    [Fact]
    public void WriteJson_ShouldSerializeNull()
    {
        var sw = new StringWriter();
        _converter.WriteJson(new JsonTextWriter(sw), null, _serializer);
        var json = sw.ToString();

        Assert.Equal("null", json);
    }

    [Fact]
    public void ReadJson_ShouldHandleMissingIdInExpandedReference()
    {
        // If _id is missing, Ref should be null
        var json = @"{ ""_type"": ""testDoc"", ""name"": ""Test Name"" }";
        var reader = new JsonTextReader(new StringReader(json));

        var result = _converter.ReadJson(reader, typeof(SanityReference<TestDocument>), null, _serializer) as SanityReference<TestDocument>;

        Assert.NotNull(result);
        Assert.Null(result.Ref);
        Assert.NotNull(result.Value);
        Assert.Equal("Test Name", result.Value.Name);
    }

    [Fact]
    public void WriteJson_ShouldHandleValueWithoutId()
    {
        // Document without _id property or attribute
        var reference = new SanityReference<DocumentWithoutId>
        {
            Value = new DocumentWithoutId { Name = "No ID" }
        };

        var sw = new StringWriter();
        _converter.WriteJson(new JsonTextWriter(sw), reference, _serializer);
        var json = sw.ToString();

        Assert.Equal("null", json);
    }

    [Fact]
    public void WriteJson_ShouldHandleWeakProperty()
    {
        var referenceTrue = new SanityReference<TestDocument> { Ref = "id1", Weak = true };
        var referenceFalse = new SanityReference<TestDocument> { Ref = "id2", Weak = false };
        var referenceNull = new SanityReference<TestDocument> { Ref = "id3", Weak = null };

        var swTrue = new StringWriter();
        _converter.WriteJson(new JsonTextWriter(swTrue), referenceTrue, _serializer);
        Assert.Contains(@"""_weak"":true", swTrue.ToString());

        var swFalse = new StringWriter();
        _converter.WriteJson(new JsonTextWriter(swFalse), referenceFalse, _serializer);
        Assert.Contains(@"""_weak"":false", swFalse.ToString());

        var swNull = new StringWriter();
        _converter.WriteJson(new JsonTextWriter(swNull), referenceNull, _serializer);
        Assert.DoesNotContain(@"""_weak""", swNull.ToString());
    }

    [Fact]
    public void WriteJson_ShouldPreferRefOverValueId()
    {
        var reference = new SanityReference<TestDocument>
        {
            Ref = "explicit-ref",
            Value = new TestDocument { Id = "value-id", Name = "Doc" }
        };

        var sw = new StringWriter();
        _converter.WriteJson(new JsonTextWriter(sw), reference, _serializer);
        var json = sw.ToString();

        var obj = JObject.Parse(json);
        Assert.Equal("explicit-ref", obj["_ref"]?.ToString());
    }

    [Fact]
    public void ReadJson_ShouldHandleEmptyObject()
    {
        var json = @"{}";
        var reader = new JsonTextReader(new StringReader(json));

        var result = _converter.ReadJson(reader, typeof(SanityReference<TestDocument>), null, _serializer) as SanityReference<TestDocument>;

        Assert.NotNull(result);
        Assert.Null(result.Ref);
    }

    [Fact]
    public void WriteJson_ShouldNotFindIdInField()
    {
        // Current implementation only looks for properties
        var reference = new SanityReference<DocumentWithIdField>
        {
            Value = new DocumentWithIdField { Id = "field-id" }
        };

        var sw = new StringWriter();
        _converter.WriteJson(new JsonTextWriter(sw), reference, _serializer);
        var json = sw.ToString();

        // Should be null because it can't find the ID in a field
        Assert.Equal("null", json);
    }

    public class TestDocument
    {
        [JsonProperty("_id")] public string? Id { get; set; }

        public string? Name { get; set; }
    }

    public class DocumentWithoutId
    {
        public string? Name { get; set; }
    }

    public class DocumentWithIdField
    {
        public string? Id;
    }
}