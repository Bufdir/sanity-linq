using Sanity.Linq.CommonTypes;
using Sanity.Linq.Mutations.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sanity.Linq.DTOs;
using Sanity.Linq.Enums;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityDocumentSetExtensionsTests
{
    private sealed class TestSanityClient : SanityClient
    {
        public TestSanityClient() : base(new SanityOptions { ProjectId = "p", Dataset = "d" }) { }

        public bool FetchAsyncCalled { get; private set; }
        public object? FetchResult { get; set; }

        public bool CommitMutationsCalled { get; private set; }
        public bool UploadImageCalled { get; private set; }
        public bool UploadFileCalled { get; private set; }

        public override Task<SanityQueryResponse<TResult>> FetchAsync<TResult>(string query, object? parameters = null, CancellationToken cancellationToken = default)
        {
            FetchAsyncCalled = true;
            return Task.FromResult(new SanityQueryResponse<TResult> { Result = (TResult)FetchResult! });
        }

        public override Task<SanityMutationResponse<TDoc>> CommitMutationsAsync<TDoc>(object mutations, bool returnIds = false, bool returnDocuments = true, SanityMutationVisibility visibility = SanityMutationVisibility.Sync, CancellationToken cancellationToken = default)
        {
            CommitMutationsCalled = true;
            return Task.FromResult(new SanityMutationResponse<TDoc>());
        }

        public override Task<SanityDocumentResponse<SanityImageAsset>> UploadImageAsync(Stream stream, string fileName, string? contentType = null, string? label = null, CancellationToken cancellationToken = default)
        {
            UploadImageCalled = true;
            return Task.FromResult(new SanityDocumentResponse<SanityImageAsset>());
        }

        public override Task<SanityDocumentResponse<SanityFileAsset>> UploadFileAsync(Stream stream, string fileName, string? contentType = null, string? label = null, CancellationToken cancellationToken = default)
        {
            UploadFileCalled = true;
            return Task.FromResult(new SanityDocumentResponse<SanityFileAsset>());
        }
    }

    private static SanityDataContext CreateContext(SanityClient client)
    {
        var options = new SanityOptions
        {
            ProjectId = "testProject",
            Dataset = "testDataset",
            UseCdn = true,
            ApiVersion = "v2021-10-21"
        };
        var context = new SanityDataContext(options);
        // We need to inject the client. SanityDataContext.Client is read-only but it is set in constructor.
        // However, we can use a trick if we can't inject it.
        // Looking at SanityDataContext, Client is public and read-only.
        // Let's see if we can use a private field via reflection if needed, or if there is a better way.
        var field = typeof(SanityDataContext).GetField("<Client>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(context, client);
        return context;
    }

    [Fact]
    public async Task ToListAsync_Returns_List()
    {
        // Arrange
        var testClient = new TestSanityClient();
        testClient.FetchResult = new List<MyDoc> { new MyDoc { Title = "Test" } };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);

        // Act
        var result = await set.ToListAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("Test", result[0].Title);
        Assert.True(testClient.FetchAsyncCalled);
    }

    [Fact]
    public async Task ToArrayAsync_Returns_Array()
    {
        // Arrange
        var testClient = new TestSanityClient();
        testClient.FetchResult = new[] { new MyDoc { Title = "Test" } };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);

        // Act
        var result = await set.ToArrayAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("Test", result[0].Title);
        Assert.True(testClient.FetchAsyncCalled);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_Returns_First_Element()
    {
        // Arrange
        var testClient = new TestSanityClient();
        testClient.FetchResult = new MyDoc { Title = "First" };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);

        // Act
        var result = await set.FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("First", result.Title);
        Assert.True(testClient.FetchAsyncCalled);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Enumerable()
    {
        // Arrange
        var testClient = new TestSanityClient();
        testClient.FetchResult = new List<MyDoc> { new MyDoc { Title = "Test" } };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);

        // Act
        var result = await set.ExecuteAsync();

        // Assert
        Assert.Single(result);
        Assert.True(testClient.FetchAsyncCalled);
    }

    [Fact]
    public async Task ExecuteSingleAsync_Returns_Single_Element()
    {
        // Arrange
        var testClient = new TestSanityClient();
        testClient.FetchResult = new MyDoc { Title = "Single" };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);

        // Act
        var result = await set.ExecuteSingleAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Single", result.Title);
        Assert.True(testClient.FetchAsyncCalled);
    }

    [Fact]
    public async Task CountAsync_Returns_Count()
    {
        // Arrange
        var testClient = new TestSanityClient();
        testClient.FetchResult = 5;

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);

        // Act
        var result = await set.CountAsync();

        // Assert
        Assert.Equal(5, result);
        Assert.True(testClient.FetchAsyncCalled);
    }

    [Fact]
    public async Task LongCountAsync_Returns_LongCount()
    {
        // Arrange
        var testClient = new TestSanityClient();
        testClient.FetchResult = 10L;

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);

        // Act
        var result = await set.LongCountAsync();

        // Assert
        Assert.Equal(10L, result);
        Assert.True(testClient.FetchAsyncCalled);
    }

    [Fact]
    public async Task CommitChangesAsync_Calls_Client_Commit()
    {
        // Arrange
        var testClient = new TestSanityClient();
        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        var doc = new MyDoc { Title = "T" };
        doc.SetSanityId("1");
        set.Update(doc);

        // Act
        var result = await set.CommitChangesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(testClient.CommitMutationsCalled);
    }

    [Fact]
    public async Task UploadAsync_Image_Stream_Calls_Client()
    {
        // Arrange
        var testClient = new TestSanityClient();
        var context = CreateContext(testClient);
        var set = context.Images;

        // Act
        using var stream = new MemoryStream();
        var result = await set.UploadAsync(stream, "test.png");

        // Assert
        Assert.NotNull(result);
        Assert.True(testClient.UploadImageCalled);
    }

    [Fact]
    public async Task UploadAsync_File_Stream_Calls_Client()
    {
        // Arrange
        var testClient = new TestSanityClient();
        var context = CreateContext(testClient);
        var set = context.Files;

        // Act
        using var stream = new MemoryStream();
        var result = await set.UploadAsync(stream, "test.txt");

        // Assert
        Assert.NotNull(result);
        Assert.True(testClient.UploadFileCalled);
    }

    [Fact]
    public async Task UploadAsync_Image_FileInfo_Calls_Client()
    {
        // Arrange
        var testClient = new TestSanityClient();
        var context = CreateContext(testClient);
        var set = context.Images;
        var fileInfo = new FileInfo("test.png");

        // Act (we expect it to fail if the file doesn't exist, but we just want to see if it calls the client)
        // Actually UploadAsync(FileInfo) opens the file, so we might need a real file or mock FileInfo if possible.
        // Since FileInfo is not easily mockable, let's create a temp file.
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0, 1, 2 });
            var fi = new FileInfo(tempFile);
            var result = await set.UploadAsync(fi, "test.png");
            Assert.NotNull(result);
            Assert.True(testClient.UploadImageCalled);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadAsync_Image_Uri_Calls_Client()
    {
        // Arrange
        var testClient = new TestSanityClient();
        var context = CreateContext(testClient);
        var set = context.Images;
        
        // Start a local server to handle the image download
        using var server = new MinimalHttpServer();
        await server.StartAsync();
        
        var uri = new Uri($"http://127.0.0.1:{server.Port}/image.png");

        // Act
        var result = await set.UploadAsync(uri);

        // Assert
        Assert.NotNull(result);
        Assert.True(testClient.UploadImageCalled);
    }

    private sealed class MinimalHttpServer : IDisposable
    {
        private System.Net.Sockets.TcpListener? _listener;
        private CancellationTokenSource? _cts;
        public int Port { get; private set; }

        public Task StartAsync()
        {
            _cts = new CancellationTokenSource();
            _listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
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

                    var body = System.Text.Encoding.UTF8.GetBytes("ok");
                    var header = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                    var headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
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
            try { _listener?.Stop(); } catch { }
            _cts?.Dispose();
        }
    }
    [Fact]
    public void ClearChanges_Removes_Only_For_Specific_Doc_Type()
    {
        // Arrange
        var context = CreateContext();
        var set1 = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        var set2 = new SanityDocumentSet<OtherDoc>(context, maxNestingLevel: 3);

        var d1 = new MyDoc { Title = "A" };
        d1.SetSanityId("m1");
        var d2 = new OtherDoc { Name = "B" };
        d2.SetSanityId("o1");

        set1.Create(d1);
        set2.Create(d2);

        Assert.Contains(context.Mutations.Mutations, m => m.DocType == typeof(MyDoc));
        Assert.Contains(context.Mutations.Mutations, m => m.DocType == typeof(OtherDoc));

        // Act
        set1.ClearChanges();

        // Assert
        Assert.DoesNotContain(context.Mutations.Mutations, m => m.DocType == typeof(MyDoc));
        Assert.Contains(context.Mutations.Mutations, m => m.DocType == typeof(OtherDoc));
    }

    [Fact]
    public void Delete_By_Query_Adds_DeleteByQuery_Mutation()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        IQueryable<MyDoc> queryable = set.Where(d => d.Title == null);

        // Act
        var builder = queryable.Delete();

        // Assert
        Assert.NotNull(builder);
        var del = context.Mutations.Mutations.OfType<SanityDeleteByQueryMutation>().FirstOrDefault(m => m.DocType == typeof(MyDoc));
        Assert.NotNull(del);
    }

    [Fact]
    public void Delete_Throws_On_Null_Source()
    {
        IQueryable<MyDoc>? queryable = null;
        Assert.Throws<ArgumentNullException>(() => queryable!.Delete());
    }

    [Fact]
    public void DocumentSet_DeleteByQuery_And_PatchByQuery_Add_Mutations()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);

        // Act
        set.DeleteByQuery(d => d.Title == null);
        set.PatchByQuery(d => d.Title != null, p => p.Unset = (string[])["title"]);

        // Assert
        Assert.Contains(context.Mutations.Mutations, m => m.DocType == typeof(MyDoc) && m.GetType().Name.Contains("DeleteByQuery"));
        Assert.Contains(context.Mutations.Mutations, m => m.DocType == typeof(MyDoc) && m.GetType().Name.Contains("Patch"));
    }

    [Fact]
    public void DocumentSet_Shortcuts_Create_Update_Delete_ClearChanges_Work()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        var doc = new MyDoc { Title = "T" };
        doc.SetSanityId("doc-1");

        // Act
        set.Create(doc);
        set.Update(doc);
        set.DeleteById("doc-1");
        set.PatchById("doc-1", p => p.Unset = (string[])["obsolete"]);

        // Assert mutations present
        Assert.Contains(context.Mutations.Mutations, m => m.DocType == typeof(MyDoc));

        // Act clear changes via extension
        set.ClearChanges();

        // Assert cleared for this doc type
        Assert.DoesNotContain(context.Mutations.Mutations, m => m.DocType == typeof(MyDoc));
    }

    [Fact]
    public void GetSanityQuery_On_BaseSet_Returns_Type_Filter_Only()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        IQueryable<MyDoc> queryable = set; // no where/predicate

        // Act
        var groq = queryable.GetSanityQuery();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(groq));
        Assert.Contains("_type == \"myDoc\"", groq, StringComparison.Ordinal);
        Assert.DoesNotContain("title ==", groq, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSanityQuery_Returns_Query_For_SanityDocumentSet()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        IQueryable<MyDoc> queryable = set.Where(d => d.Title == "Hello");

        // Act
        var groq = queryable.GetSanityQuery();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(groq));
        Assert.Contains("_type == \"myDoc\"", groq, StringComparison.Ordinal);
        Assert.Contains("title == \"Hello\"", groq, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSanityQuery_Throws_On_Non_Sanity_IQueryable()
    {
        // Arrange
        var queryable = new[] { 1, 2, 3 }.AsQueryable();

        // Act + Assert
        var ex = Assert.Throws<Exception>(() => queryable.GetSanityQuery());
        Assert.Equal("Queryable source must be a SanityDbSet<T>.", ex.Message);
    }

    [Fact]
    public void GetSanityQuery_Throws_On_Null_Source()
    {
        IQueryable<MyDoc>? queryable = null;
        Assert.Throws<ArgumentNullException>(() => queryable!.GetSanityQuery());
    }

    [Fact]
    public void Include_On_Non_Sanity_IQueryable_Throws()
    {
        // Arrange
        IQueryable<MyDoc> queryable = Array.Empty<MyDoc>().AsQueryable();

        // Act + Assert
        var ex1 = Assert.Throws<Exception>(() => queryable.Include(d => d.Author!));
        Assert.Equal("Queryable source must be a SanityDbSet<T>.", ex1.Message);

        var ex2 = Assert.Throws<Exception>(() => queryable.Include(d => d.Author!, sourceName: "src"));
        Assert.Equal("Queryable source must be a SanityDbSet<T>.", ex2.Message);
    }

    [Fact]
    public void Include_Overload_With_SourceName_Produces_Queryable()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        IQueryable<MyDoc> queryable = set;

        // Act
        var result = queryable.Include(d => d.Author, sourceName: "authorRef");

        // Assert
        Assert.NotNull(result);
        var groq = result.GetSanityQuery();
        Assert.False(string.IsNullOrWhiteSpace(groq));
    }

    [Fact]
    public void Include_Overload_Without_SourceName_Produces_Queryable()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        IQueryable<MyDoc> queryable = set;

        // Act
        var result = queryable.Include(d => d.Author);

        // Assert
        Assert.NotNull(result);
        // Should be able to build a query after Include
        var groq = result.Where(d => d.Title != null).GetSanityQuery();
        Assert.Contains("_type == \"myDoc\"", groq, StringComparison.Ordinal);
    }

    [Fact]
    public void Include_Throws_On_Null_Source()
    {
        IQueryable<MyDoc>? queryable = null;
        Assert.Throws<ArgumentNullException>(() => queryable!.Include(d => d.Author!));
    }

    [Fact]
    public void Include_With_SourceName_Throws_On_Null_Source()
    {
        IQueryable<MyDoc>? queryable = null;
        Assert.Throws<ArgumentNullException>(() => queryable!.Include(d => d.Author!, sourceName: "authorRef"));
    }

    [Fact]
    public void Patch_By_Query_Adds_Patch_Mutation()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        IQueryable<MyDoc> queryable = set.Where(d => d.Title != null);

        // Act
        var builder = queryable.Patch(p => p.Set = new { title = "New" });

        // Assert
        Assert.NotNull(builder);
        // Underlying mutations include a SanityPatchMutation for MyDoc
        var patchMutation = context.Mutations.Mutations.OfType<SanityPatchMutation>().FirstOrDefault(m => m.DocType == typeof(MyDoc));
        Assert.NotNull(patchMutation);
    }

    [Fact]
    public void Patch_Throws_On_Null_Source()
    {
        IQueryable<MyDoc>? queryable = null;
        Assert.Throws<ArgumentNullException>(() => queryable!.Patch(p => p.Set = new { title = "X" }));
    }

    [Fact]
    public void Patch_Throws_When_Action_Is_Null()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        IQueryable<MyDoc> queryable = set.Where(d => d.Title != null);

        // Act + Assert (Mutations layer will invoke null delegate -> NullReferenceException)
        Assert.Throws<NullReferenceException>(() => queryable.Patch(null!));
    }

    [Fact]
    public void Update_Throws_When_Id_Is_Missing()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, maxNestingLevel: 3);
        var doc = new MyDoc { Title = "T" }; // no _id set

        // Act + Assert
        var ex = Assert.Throws<Exception>(() => set.Update(doc));
        Assert.Equal("Id must be specified when updating document.", ex.Message);
    }

    private static SanityDataContext CreateContext()
    {
        var options = new SanityOptions
        {
            ProjectId = "testProject",
            Dataset = "testDataset",
            UseCdn = true,
            ApiVersion = "v2021-10-21"
        };
        return new SanityDataContext(options);
    }

    private sealed class MyDoc : SanityDocument
    {
        public Person? Author { get; set; }
        public string? Title { get; set; }
    }

    private sealed class OtherDoc : SanityDocument
    {
        public string? Name { get; set; }
    }

    private sealed class Person
    {
        public string? Name { get; set; }
    }
}