using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.DTOs;
using Sanity.Linq.Enums;
using Sanity.Linq.Mutations;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityClientExtensionsTests
{
    private static SanityClient CreateTestClient(out TestSanityClient testClient)
    {
        testClient = new TestSanityClient(new SanityOptions
        {
            ProjectId = "proj",
            Dataset = "ds",
            UseCdn = false
        });
        return testClient;
    }

    [Fact]
    public async Task UploadImageAsync_Uppercase_Extension_Maps_To_Correct_Mime()
    {
        var client = CreateTestClient(out var testClient);
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var url = new Uri($"http://127.0.0.1:{server.Port}/img/PHOTO.PNG");
        _ = await client.UploadImageAsync(url, label: "x");

        Assert.True(testClient.UploadImageCalled);
        Assert.Equal("PHOTO.PNG", testClient.LastImageFileName);
        Assert.Equal("image/png", testClient.LastImageContentType);
    }

    [Fact]
    public async Task UploadImageAsync_MultiDot_FileName_Uses_Last_Extension()
    {
        var client = CreateTestClient(out var testClient);
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var url = new Uri($"http://127.0.0.1:{server.Port}/photos/holiday.profile.jpg");
        _ = await client.UploadImageAsync(url);

        Assert.True(testClient.UploadImageCalled);
        Assert.Equal("holiday.profile.jpg", testClient.LastImageFileName);
        Assert.Equal("image/jpeg", testClient.LastImageContentType);
    }

    [Fact]
    public async Task UploadFileAsync_MultiDot_FileName_Uses_Last_Extension()
    {
        var client = CreateTestClient(out var testClient);
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var url = new Uri($"http://127.0.0.1:{server.Port}/docs/report.final.pdf");
        _ = await client.UploadFileAsync(url);

        Assert.True(testClient.UploadFileCalled);
        Assert.Equal("report.final.pdf", testClient.LastFileFileName);
        Assert.Equal("application/pdf", testClient.LastFileContentType);
    }

    [Fact]
    public async Task UploadFileAsync_Unknown_Extension_Falls_Back_To_OctetStream()
    {
        var client = CreateTestClient(out var testClient);
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var url = new Uri($"http://127.0.0.1:{server.Port}/bin/blob.unknownext");
        _ = await client.UploadFileAsync(url);

        Assert.True(testClient.UploadFileCalled);
        Assert.Equal("blob.unknownext", testClient.LastFileFileName);
        Assert.Equal("application/octet-stream", testClient.LastFileContentType);
    }

    [Fact]
    public async Task UploadFileAsync_UrlEncoded_FileName_Is_Preserved_And_Mime_By_Ext()
    {
        var client = CreateTestClient(out var testClient);
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var url = new Uri($"http://127.0.0.1:{server.Port}/files/report%20final.pdf");
        _ = await client.UploadFileAsync(url);

        Assert.True(testClient.UploadFileCalled);
        Assert.Equal("report%20final.pdf", testClient.LastFileFileName);
        Assert.Equal("application/pdf", testClient.LastFileContentType);
    }

    [Fact]
    public async Task UploadImageAsync_Unicode_FileName_Mime_By_Extension()
    {
        var client = CreateTestClient(out var testClient);
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var url = new Uri($"http://127.0.0.1:{server.Port}/media/фото.jpg");
        _ = await client.UploadImageAsync(url);

        Assert.True(testClient.UploadImageCalled);
        // Uri.PathAndQuery returns percent-encoded segment, so filename is encoded
        Assert.Equal("%D1%84%D0%BE%D1%82%D0%BE.jpg", testClient.LastImageFileName);
        Assert.Equal("image/jpeg", testClient.LastImageContentType);
    }

    [Fact]
    public async Task UploadImageAsync_Trailing_Slash_Yields_Default_Mime_And_Empty_FileName()
    {
        var client = CreateTestClient(out var testClient);
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var url = new Uri($"http://127.0.0.1:{server.Port}/assets/");
        _ = await client.UploadImageAsync(url);

        Assert.True(testClient.UploadImageCalled);
        Assert.Equal(string.Empty, testClient.LastImageFileName);
        Assert.Equal("image/jpeg", testClient.LastImageContentType);
    }

    [Fact]
    public async Task UploadFileAsync_Trailing_Slash_Yields_Default_Mime_And_Empty_FileName()
    {
        var client = CreateTestClient(out var testClient);
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var url = new Uri($"http://127.0.0.1:{server.Port}/download/");
        _ = await client.UploadFileAsync(url);

        Assert.True(testClient.UploadFileCalled);
        Assert.Equal(string.Empty, testClient.LastFileFileName);
        Assert.Equal("application/octet-stream", testClient.LastFileContentType);
    }

    [Fact]
    public async Task Upload_Image_And_File_Respect_Custom_Label()
    {
        var client = CreateTestClient(out var testClient);
        // Server 1 for image
        using (var server1 = new MinimalHttpServer())
        {
            await server1.StartAsync();
            var img = new Uri($"http://127.0.0.1:{server1.Port}/p.png");
            _ = await client.UploadImageAsync(img, label: "IMG");
        }
        Assert.Equal("IMG", testClient.LastImageLabel);

        // Server 2 for file
        using (var server2 = new MinimalHttpServer())
        {
            await server2.StartAsync();
            var file = new Uri($"http://127.0.0.1:{server2.Port}/r.txt");
            _ = await client.UploadFileAsync(file, label: "FILE");
        }
        Assert.Equal("FILE", testClient.LastFileLabel);
    }

    [Fact]
    public void BeginTransaction_Builders_Are_Tied_To_Client()
    {
        var client = CreateTestClient(out var testClient);

        var b = client.BeginTransaction();
        Assert.NotNull(b);
        Assert.Same(client, b.Client);

        var g = client.BeginTransaction<DocWithId>();
        Assert.NotNull(g);
        Assert.Same(client, g.InnerBuilder.Client);
    }

    [Fact]
    public async Task CommitAsync_NonDefault_Flags_Pass_Through_And_Clear()
    {
        var client = CreateTestClient(out var testClient);
        var builder = new SanityMutationBuilder(client);
        builder.Create(new DocWithId { Id = "k1", Type = "docWithId", Title = "t" });

        var _ = await SanityClientExtensions.CommitAsync(builder, returnIds: true, returnDocuments: false, visibility: SanityMutationVisibility.Async);

        Assert.True(testClient.CommitCalled);
        Assert.True(testClient.LastReturnIds);
        Assert.False(testClient.LastReturnDocs);
        Assert.Equal(SanityMutationVisibility.Async, testClient.LastVisibility);
        Assert.Empty(builder.Mutations);
    }

    [Fact]
    public async Task UploadImageAsync_Throws_On_Null_Uri()
    {
        var client = CreateTestClient(out var testClient);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await client.UploadImageAsync((Uri)null!));
    }

    [Fact]
    public async Task UploadImageAsync_Parses_FileName_Mime_And_Default_Label()
    {
        var client = CreateTestClient(out var testClient);
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var url = new Uri($"http://127.0.0.1:{server.Port}/folder/photo.jpg?x=1#frag");
        var resp = await client.UploadImageAsync(url);

        Assert.True(testClient.UploadImageCalled);
        Assert.Equal("photo.jpg", testClient.LastImageFileName);
        Assert.Equal("image/jpeg", testClient.LastImageContentType);
        Assert.StartsWith("Source:", testClient.LastImageLabel);
        Assert.Contains(url.OriginalString, testClient.LastImageLabel);
        Assert.NotNull(resp);
    }

    [Fact]
    public async Task UploadImageAsync_Defaults_To_Jpeg_When_No_Extension()
    {
        var client = CreateTestClient(out var testClient);
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var url = new Uri($"http://127.0.0.1:{server.Port}/image");
        _ = await client.UploadImageAsync(url, label: null);

        Assert.True(testClient.UploadImageCalled);
        Assert.Equal("image", testClient.LastImageFileName);
        Assert.Equal("image/jpeg", testClient.LastImageContentType); // default from .jpg
    }

    [Fact]
    public async Task UploadFileAsync_Defaults_To_OctetStream_When_No_Extension()
    {
        var client = CreateTestClient(out var testClient);
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var url = new Uri($"http://127.0.0.1:{server.Port}/download");
        _ = await client.UploadFileAsync(url);

        Assert.True(testClient.UploadFileCalled);
        Assert.Equal("download", testClient.LastFileFileName);
        Assert.Equal("application/octet-stream", testClient.LastFileContentType);
    }

    [Fact]
    public async Task UploadFileAsync_Parses_FileName_And_MimeType_From_Extension()
    {
        var client = CreateTestClient(out var testClient);
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var url = new Uri($"http://127.0.0.1:{server.Port}/data/report.pdf?ver=2");
        _ = await client.UploadFileAsync(url, label: "custom");

        Assert.True(testClient.UploadFileCalled);
        Assert.Equal("report.pdf", testClient.LastFileFileName);
        Assert.Equal("application/pdf", testClient.LastFileContentType);
        Assert.Equal("custom", testClient.LastFileLabel);
    }

    private class DocWithId { [JsonProperty("_id")] public string? Id { get; set; } [JsonProperty("_type")] public string? Type { get; set; } public string? Title { get; set; } }

    [Fact]
    public async Task CreateAsync_Builds_And_Commits_With_Expected_Flags()
    {
        var client = CreateTestClient(out var testClient);
        var doc = new DocWithId { Id = "doc-1", Type = "docWithId", Title = "T" };

        var _ = await client.CreateAsync(doc);

        Assert.True(testClient.CommitCalled);
        Assert.False(testClient.LastReturnIds);
        Assert.True(testClient.LastReturnDocs);
        Assert.Equal(SanityMutationVisibility.Sync, testClient.LastVisibility);
        Assert.Contains("create", testClient.LastCommitPayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetAsync_Builds_And_Commits()
    {
        var client = CreateTestClient(out var testClient);
        var doc = new DocWithId { Id = "doc-2", Type = "docWithId", Title = "X" };

        var _ = await client.SetAsync(doc);

        Assert.True(testClient.CommitCalled);
        Assert.Contains("\"patch\"", testClient.LastCommitPayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"set\"", testClient.LastCommitPayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_Builds_And_Commits()
    {
        var client = CreateTestClient(out var testClient);

        var _ = await client.DeleteAsync("doc-1");

        Assert.True(testClient.CommitCalled);
        Assert.Contains("delete", testClient.LastCommitPayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommitAsync_NonGeneric_Clears_Builder_After_Commit()
    {
        var client = CreateTestClient(out var testClient);
        var builder = new SanityMutationBuilder(client);
        builder.Create(new DocWithId { Id = "x1", Type = "docWithId", Title = "a" });

        Assert.NotEmpty(builder.Mutations);
        var _ = await SanityClientExtensions.CommitAsync(builder);
        Assert.Empty(builder.Mutations); // cleared
        Assert.True(testClient.CommitCalled);
    }

    [Fact]
    public async Task CommitAsync_Generic_Clears_Only_Generic_Mutations()
    {
        var client = CreateTestClient(out var testClient);
        var builder = new SanityMutationBuilder(client);
        var g = builder.For<DocWithId>();
        g.Create(new DocWithId { Id = "y1", Type = "docWithId", Title = "T" });
        // also add a non-generic mutation
        builder.Create(new DocWithId { Id = "z1", Type = "docWithId", Title = "b" });

        Assert.True(builder.Mutations.Count > 1);

        var _ = await SanityClientExtensions.CommitAsync(g);

        // generic clear removes only DocWithId mutations, leaving the other
        Assert.Single(builder.Mutations);
        Assert.True(testClient.CommitCalled);
    }

    private sealed class TestSanityClient : SanityClient
    {
        public TestSanityClient(SanityOptions options) : base(options) { }

        // Upload image tracking
        public bool UploadImageCalled { get; private set; }
        public string? LastImageFileName { get; private set; }
        public string? LastImageContentType { get; private set; }
        public string? LastImageLabel { get; private set; }

        public bool UploadFileCalled { get; private set; }
        public string? LastFileFileName { get; private set; }
        public string? LastFileContentType { get; private set; }
        public string? LastFileLabel { get; private set; }

        public bool CommitCalled { get; private set; }
        public bool LastReturnIds { get; private set; }
        public bool LastReturnDocs { get; private set; }
        public SanityMutationVisibility LastVisibility { get; private set; }
        public string LastCommitPayloadJson { get; private set; } = string.Empty;

        public override Task<SanityDocumentResponse<SanityImageAsset>> UploadImageAsync(Stream stream, string fileName, string? contentType = null, string? label = null, CancellationToken cancellationToken = default)
        {
            UploadImageCalled = true;
            LastImageFileName = fileName;
            LastImageContentType = contentType;
            LastImageLabel = label;
            return Task.FromResult(new SanityDocumentResponse<SanityImageAsset> { Document = new SanityImageAsset() });
        }

        public override Task<SanityDocumentResponse<SanityFileAsset>> UploadFileAsync(Stream stream, string fileName, string? contentType = null, string? label = null, CancellationToken cancellationToken = default)
        {
            UploadFileCalled = true;
            LastFileFileName = fileName;
            LastFileContentType = contentType;
            LastFileLabel = label;
            return Task.FromResult(new SanityDocumentResponse<SanityFileAsset> { Document = new SanityFileAsset() });
        }

        public override Task<SanityMutationResponse> CommitMutationsAsync(object mutations, bool returnIds = false, bool returnDocuments = true, SanityMutationVisibility visibility = SanityMutationVisibility.Sync, CancellationToken cancellationToken = default)
        {
            TrackCommit(mutations, returnIds, returnDocuments, visibility);
            return Task.FromResult(new SanityMutationResponse());
        }

        public override Task<SanityMutationResponse<TDoc>> CommitMutationsAsync<TDoc>(object mutations, bool returnIds = false, bool returnDocuments = true, SanityMutationVisibility visibility = SanityMutationVisibility.Sync, CancellationToken cancellationToken = default)
        {
            TrackCommit(mutations, returnIds, returnDocuments, visibility);
            return Task.FromResult(new SanityMutationResponse<TDoc>());
        }

        private void TrackCommit(object mutations, bool returnIds, bool returnDocuments, SanityMutationVisibility visibility)
        {
            CommitCalled = true;
            LastReturnIds = returnIds;
            LastReturnDocs = returnDocuments;
            LastVisibility = visibility;
            LastCommitPayloadJson = mutations as string ?? JsonConvert.SerializeObject(mutations);
        }
    }

    private sealed class MinimalHttpServer : IDisposable
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        public int Port { get; private set; }

        public Task StartAsync()
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                    using var ns = client.GetStream();
                    // Read request (ignore content)
                    var buffer = new byte[1024];
                    _ = await ns.ReadAsync(buffer, 0, buffer.Length, _cts.Token).ConfigureAwait(false);

                    var body = Encoding.UTF8.GetBytes("ok");
                    var header = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                    var headerBytes = Encoding.ASCII.GetBytes(header);
                    await ns.WriteAsync(headerBytes, 0, headerBytes.Length, _cts.Token).ConfigureAwait(false);
                    await ns.WriteAsync(body, 0, body.Length, _cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            });

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch (Exception ex) { Console.Error.WriteLine($"MinimalHttpServer.Dispose: Failed to stop listener: {ex}"); }
            _cts?.Dispose();
        }
    }
}
