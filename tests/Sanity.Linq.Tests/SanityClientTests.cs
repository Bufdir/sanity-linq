using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.DTOs;
using Sanity.Linq.Enums;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityClientTests
{
    private readonly SanityOptions _options = new()
    {
        ProjectId = "testproj",
        Dataset = "testdata",
        Token = "testtoken",
        ApiVersion = "v1"
    };

    private IHttpClientFactory CreateFakeFactory(HttpResponseMessage response, Action<HttpRequestMessage>? verifyRequest = null)
    {
        var handler = new FakeHttpMessageHandler(response, verifyRequest);
        var httpClient = new HttpClient(handler);
        return new FakeHttpClientFactory(httpClient);
    }

    [Fact]
    public async Task FetchAsync_SendsCorrectRequest()
    {
        // Arrange
        var query = "*[_type == 'post']";
        var expectedResponse = new SanityQueryResponse<List<object>>
        {
            Result = [new { title = "Test" }]
        };
        var responseJson = JsonConvert.SerializeObject(expectedResponse);

        HttpRequestMessage? capturedRequest = null;
        var factory = CreateFakeFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson)
        }, req => capturedRequest = req);

        var client = new SanityClient(_options, factory);

        // Act
        var result = await client.FetchAsync<List<object>>(query);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Result);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://testproj.api.sanity.io/v1/data/query/testdata", capturedRequest.RequestUri?.ToString());

        var content = await capturedRequest.Content!.ReadAsStringAsync();
        Assert.Contains(query, content);
    }

    [Fact]
    public async Task GetDocumentAsync_SendsCorrectRequest()
    {
        // Arrange
        var id = "doc123";
        var expectedResponse = new SanityDocumentsResponse<object>
        {
            Documents = [new { id = "doc123" }]
        };
        var responseJson = JsonConvert.SerializeObject(expectedResponse);

        HttpRequestMessage? capturedRequest = null;
        var factory = CreateFakeFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson)
        }, req => capturedRequest = req);

        var client = new SanityClient(_options, factory);

        // Act
        var result = await client.GetDocumentAsync<object>(id);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Documents);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest.Method);
        Assert.Equal("https://testproj.api.sanity.io/v1/data/doc/testdata/doc123", capturedRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task CommitMutationsAsync_SendsCorrectRequest()
    {
        // Arrange
        var mutations = new { create = new { _type = "post", title = "New Post" } };
        var expectedResponse = new SanityMutationResponse
        {
            TransactionId = "tx123"
        };
        var responseJson = JsonConvert.SerializeObject(expectedResponse);

        HttpRequestMessage? capturedRequest = null;
        var factory = CreateFakeFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson)
        }, req => capturedRequest = req);

        var client = new SanityClient(_options, factory);

        // Act
        var result = await client.CommitMutationsAsync(mutations, true, visibility: SanityMutationVisibility.Async);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("tx123", result.TransactionId);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        var uri = capturedRequest.RequestUri?.ToString();
        Assert.Contains("data/mutate/testdata", uri);
        Assert.Contains("returnIds=true", uri);
        Assert.Contains("visibility=async", uri);

        var content = await capturedRequest.Content!.ReadAsStringAsync();
        Assert.Contains("New Post", content);
    }

    [Fact]
    public async Task UploadFileAsync_SendsCorrectRequest()
    {
        // Arrange
        var fileName = "test.txt";
        var content = "hello world";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var expectedResponse = new SanityDocumentResponse<SanityFileAsset>
        {
            Document = new SanityFileAsset { AssetId = "file123" }
        };
        var responseJson = JsonConvert.SerializeObject(expectedResponse);

        HttpRequestMessage? capturedRequest = null;
        var factory = CreateFakeFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson)
        }, req => capturedRequest = req);

        var client = new SanityClient(_options, factory);

        // Act
        var result = await client.UploadFileAsync(stream, fileName, "text/plain", "mylabel");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("file123", result.Document.AssetId);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        var uri = capturedRequest.RequestUri?.ToString();
        Assert.Contains("assets/files/testdata", uri);
        Assert.Contains("filename=test.txt", uri);
        Assert.Contains("label=mylabel", uri);
        Assert.Equal("text/plain", capturedRequest.Content?.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UploadImageAsync_SendsCorrectRequest()
    {
        // Arrange
        var fileName = "test.png";
        var content = "fake image data";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var expectedResponse = new SanityDocumentResponse<SanityImageAsset>
        {
            Document = new SanityImageAsset { AssetId = "image123" }
        };
        var responseJson = JsonConvert.SerializeObject(expectedResponse);

        HttpRequestMessage? capturedRequest = null;
        var factory = CreateFakeFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson)
        }, req => capturedRequest = req);

        var client = new SanityClient(_options, factory);

        // Act
        var result = await client.UploadImageAsync(stream, fileName, "image/png", "imglabel");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("image123", result.Document.AssetId);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        var uri = capturedRequest.RequestUri?.ToString();
        Assert.Contains("assets/images/testdata", uri);
        Assert.Contains("filename=test.png", uri);
        Assert.Contains("label=imglabel", uri);
        Assert.Equal("image/png", capturedRequest.Content?.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task FetchAsync_ThrowsOnEmptyQuery()
    {
        var client = new SanityClient(_options);
        await Assert.ThrowsAsync<ArgumentException>(() => client.FetchAsync<object>(""));
    }

    [Fact]
    public async Task GetDocumentAsync_ThrowsOnEmptyId()
    {
        var client = new SanityClient(_options);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetDocumentAsync<object>(""));
    }

    private class FakeHttpMessageHandler(HttpResponseMessage response, Action<HttpRequestMessage>? verifyRequest = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            verifyRequest?.Invoke(request);
            return Task.FromResult(response);
        }
    }

    private class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return client;
        }
    }
}