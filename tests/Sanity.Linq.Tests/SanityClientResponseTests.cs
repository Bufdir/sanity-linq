using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Sanity.Linq;
using Sanity.Linq.Exceptions;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityClientResponseTests
{
    private static SanityOptions CreateOptions() => new()
    {
        ProjectId = "testproj",
        Dataset = "testdata",
        UseCdn = false,
        ApiVersion = "v2021-03-25"
    };

    [Fact]
    public async Task HandleHttpResponse_Success_Deserializes_Object()
    {
        var client = new TestableSanityClient(CreateOptions());
        var http = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonConvert.SerializeObject(new { value = 123 }))
        };

        var result = await client.InvokeHandleAsync<TestDto>(http);

        Assert.NotNull(result);
        Assert.Equal(123, result.Value);
    }

    [Fact]
    public async Task HandleHttpResponse_Deserialize_Null_Throws_Typed()
    {
        var client = new TestableSanityClient(CreateOptions());
        var http = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.test/endpoint"),
            Content = new StringContent("null")
        };

        var ex = await Assert.ThrowsAsync<SanityDeserializationException>(() => client.InvokeHandleAsync<TestDto>(http));
        Assert.Contains("Failed to deserialize", ex.Message);
        Assert.Equal("null", ex.ResponsePreview);
        Assert.NotNull(ex.RequestUri);
    }

    [Fact]
    public async Task HandleHttpResponse_Malformed_Json_Throws_Typed_With_Preview()
    {
        var client = new TestableSanityClient(CreateOptions());
        var http = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.test/endpoint"),
            Content = new StringContent("{invalid")
        };

        var ex = await Assert.ThrowsAsync<SanityDeserializationException>(() => client.InvokeHandleAsync<TestDto>(http));
        Assert.NotNull(ex.InnerException);
        Assert.Equal("{invalid", ex.ResponsePreview);
        Assert.NotNull(ex.RequestUri);
    }

    [Fact]
    public async Task HandleHttpResponse_Http_Error_Throws_SanityHttpException()
    {
        var client = new TestableSanityClient(CreateOptions());
        var http = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad request body")
        };

        var ex = await Assert.ThrowsAsync<SanityHttpException>(() => client.InvokeHandleAsync<TestDto>(http));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("bad request body", ex.Content);
    }

    public class TestDto
    {
        [JsonProperty("value")] public int Value { get; set; }
    }

    private sealed class TestableSanityClient : SanityClient
    {
        public TestableSanityClient(SanityOptions options) : base(options) { }

        public Task<T> InvokeHandleAsync<T>(HttpResponseMessage response)
        {
            return HandleHttpResponseAsync<T>(response);
        }
    }
}
