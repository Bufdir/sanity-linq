using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Sanity.Linq.JsonConverters;

namespace Sanity.Linq.BlockContent;

public class SanityHtmlBuilder
{
    private readonly SanityOptions _options;
    private readonly SanityHtmlBuilderOptions _htmlBuilderOptions;
    public Dictionary<string, Func<JToken, SanityOptions, object?, Task<string>>> Serializers { get; } = new();
    private readonly SanityTreeBuilder _treeBuilder = new();

    public JsonSerializerSettings SerializerSettings { get; }

    public SanityHtmlBuilder(SanityOptions options,
        Dictionary<string, Func<JToken, SanityOptions, object?, Task<string>>>? customSerializers = null,
        JsonSerializerSettings? serializerSettings = null,
        SanityHtmlBuilderOptions? htmlBuilderOptions = null)
    {
        _options = options;
        SerializerSettings = serializerSettings ?? new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new List<JsonConverter> { new SanityReferenceTypeConverter() }
        };
        if (customSerializers != null)
        {
            InitSerializers(customSerializers);
        }
        else
        {
            InitSerializers();
        }
        if (htmlBuilderOptions != null)
        {
            _htmlBuilderOptions = htmlBuilderOptions;
        }
        else
        {
            _htmlBuilderOptions = new SanityHtmlBuilderOptions();
        }
    }

    public virtual void AddSerializer(string type, Func<JToken, SanityOptions, Task<string>> serializeFn)
    {
        Serializers[type] = SerializerFn;
        return;
        Task<string> SerializerFn(JToken token, SanityOptions options, object? context) => serializeFn(token, options);
    }

    public virtual void AddSerializer(string type, Func<JToken, SanityOptions, object?, Task<string>> serializeFn)
    {
        Serializers[type] = serializeFn;
    }

    public virtual Task<string> BuildAsync(object content, object? buildContext = null)
    {
        switch (content)
        {
            case null:
                throw new ArgumentNullException(nameof(content));
            case JArray array:
                return BuildAsync(array, buildContext);
            case JToken token:
                return SerializeBlockAsync(token, buildContext);
            // JSON String
            case string s:
                return Build(s, buildContext);
            default:
            {
                // Strongly typed object
                var json = JsonConvert.SerializeObject(content, SerializerSettings);
                return Build(json, buildContext);
            }
        }
    }

    protected virtual async Task<string> BuildAsync(JArray content, object? buildContext)
    {
        if (content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        var html = new StringBuilder();

        //build list items (if any)
        content = _treeBuilder.Build(content);

        //serialize each block with their respective serializers
        foreach (var block in content)
        {
            html.Append(await SerializeBlockAsync(block, buildContext).ConfigureAwait(false));
        }

        return html.ToString();
    }

    protected virtual Task<string> Build(string content, object? buildContext)
    {
        var nodes = JsonConvert.DeserializeObject(content, SerializerSettings) as JToken;
        if (nodes == null)
        {
            throw new ArgumentNullException(nameof(content));
        }
        if (nodes is JArray array)
        {
            // Block array (ie. block content)
            return BuildAsync(array, buildContext);
        }

        // Single block
        return SerializeBlockAsync(nodes, buildContext);
    }

    private Task<string> SerializeBlockAsync(JToken block, object? buildContext)
    {
        var type = block["_type"]?.ToString();
        if (string.IsNullOrEmpty(type))
        {
            throw new Exception("Could not convert block to HTML; _type was not defined on block content.");
        }
            
        if (!Serializers.TryGetValue(type, out var serializer))
        {
            // TODO: Add options for ignoring/skipping specific types.
            return _htmlBuilderOptions.IgnoreAllUnknownTypes
                ? Task.FromResult("")
                : throw new Exception($"No serializer for type '{type}' could be found. Consider providing a custom serializer or setting HtmlBuilderOptions.IgnoreAllUnknownTypes.");
        }
            
        return serializer(block, _options, buildContext);
    }

    private void InitSerializers() //with default serializers
    {
        LoadDefaultSerializers();
    }

    private void InitSerializers(Dictionary<string, Func<JToken, SanityOptions, object?, Task<string>>> customSerializers) //with default and custom serializers
    {
        LoadDefaultSerializers();
        foreach (var customSerializer in customSerializers)
        {
            Serializers[customSerializer.Key] = customSerializer.Value;
        }
    }

    public void LoadDefaultSerializers()
    {
        var serializers = new SanityHtmlSerializers();
        AddSerializer("block", serializers.SerializeDefaultBlockAsync);
        AddSerializer("image", serializers.SerializeImageAsync);
    }
}