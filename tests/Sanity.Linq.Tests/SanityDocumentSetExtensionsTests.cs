using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.DTOs;
using Sanity.Linq.Enums;
using Sanity.Linq.Mutations.Model;
using Sanity.Linq.QueryProvider;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityDocumentSetExtensionsTests
{
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
        var field = typeof(SanityDataContext).GetField("<Client>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(context, client);
        return context;
    }

    [Fact]
    public async Task ToListAsync_Returns_List()
    {
        // Arrange
        var testClient = new TestSanityClient
        {
            FetchResult = new List<MyDoc> { new() { Title = "Test" } }
        };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, 3);

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
        var testClient = new TestSanityClient
        {
            FetchResult = new[] { new MyDoc { Title = "Test" } }
        };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, 3);

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
        var testClient = new TestSanityClient
        {
            FetchResult = new MyDoc { Title = "First" }
        };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, 3);

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
        var testClient = new TestSanityClient
        {
            FetchResult = new List<MyDoc> { new() { Title = "Test" } }
        };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, 3);

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
        var testClient = new TestSanityClient
        {
            FetchResult = new MyDoc { Title = "Single" }
        };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, 3);

        // Act
        var result = await set.ExecuteSingleAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Single", result.Title);
        Assert.True(testClient.FetchAsyncCalled);
    }

    [Fact]
    public async Task ExecuteSingleAsync_With_FirstOrDefault_Returns_Single_Element()
    {
        // Arrange
        var testClient = new TestSanityClient
        {
            FetchResult = new MyDoc { Title = "First" }
        };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, 3);

        // Act
        var result = await set.FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("First", result.Title);
        Assert.True(testClient.FetchAsyncCalled);
        
        // Verify that the query sent to the client had [0]
        Assert.EndsWith("[0]", testClient.LastQuery.Trim());
    }

    [Fact]
    public async Task ExecuteSingleAsync_With_Take_One_FirstOrDefault_Returns_Single_Element()
    {
        // Arrange
        var testClient = new TestSanityClient
        {
            FetchResult = new MyDoc { Title = "FirstTake" }
        };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, 3);

        // Act
        var result = await set.Take(1).FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("FirstTake", result.Title);
        Assert.True(testClient.FetchAsyncCalled);

        // Take(1).FirstOrDefault() should still result in [0]
        Assert.EndsWith("[0]", testClient.LastQuery.Trim());
    }

    [Fact]
    public async Task CountAsync_Returns_Count()
    {
        // Arrange
        var testClient = new TestSanityClient
        {
            FetchResult = 5
        };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, 3);

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
        var testClient = new TestSanityClient
        {
            FetchResult = 10L
        };

        var context = CreateContext(testClient);
        var set = new SanityDocumentSet<MyDoc>(context, 3);

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
        var set = new SanityDocumentSet<MyDoc>(context, 3);
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

    [Fact]
    public async Task UploadAsync_File_FileInfo_Calls_Client()
    {
        // Arrange
        var testClient = new TestSanityClient();
        var context = CreateContext(testClient);
        var set = context.Files;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0, 1, 2 });
            var fi = new FileInfo(tempFile);

            // Act
            var result = await set.UploadAsync(fi, "test.txt");

            // Assert
            Assert.NotNull(result);
            Assert.True(testClient.UploadFileCalled);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadAsync_File_Uri_Calls_Client()
    {
        // Arrange
        var testClient = new TestSanityClient();
        var context = CreateContext(testClient);
        var set = context.Files;

        // Start a local server to handle the file download
        using var server = new MinimalHttpServer();
        await server.StartAsync();

        var uri = new Uri($"http://127.0.0.1:{server.Port}/file.txt");

        // Act
        var result = await set.UploadAsync(uri);

        // Assert
        Assert.NotNull(result);
        Assert.True(testClient.UploadFileCalled);
    }

    [Fact]
    public async Task ToListAsync_Throws_On_Null_Source()
    {
        IQueryable<MyDoc>? source = null;
        await Assert.ThrowsAsync<ArgumentNullException>(() => source!.ToListAsync());
    }

    [Fact]
    public async Task ToArrayAsync_Throws_On_Null_Source()
    {
        IQueryable<MyDoc>? source = null;
        await Assert.ThrowsAsync<ArgumentNullException>(() => source!.ToArrayAsync());
    }

    [Fact]
    public async Task FirstOrDefaultAsync_Throws_On_Null_Source()
    {
        IQueryable<MyDoc>? source = null;
        await Assert.ThrowsAsync<ArgumentNullException>(() => source!.FirstOrDefaultAsync());
    }

    [Fact]
    public async Task ExecuteAsync_Throws_On_Null_Source()
    {
        IQueryable<MyDoc>? source = null;
        await Assert.ThrowsAsync<ArgumentNullException>(() => source!.ExecuteAsync());
    }

    [Fact]
    public async Task ExecuteSingleAsync_Throws_On_Null_Source()
    {
        IQueryable<MyDoc>? source = null;
        await Assert.ThrowsAsync<ArgumentNullException>(() => source!.ExecuteSingleAsync());
    }

    [Fact]
    public async Task CountAsync_Throws_On_Null_Source()
    {
        IQueryable<MyDoc>? source = null;
        await Assert.ThrowsAsync<ArgumentNullException>(() => source!.CountAsync());
    }

    [Fact]
    public async Task LongCountAsync_Throws_On_Null_Source()
    {
        IQueryable<MyDoc>? source = null;
        await Assert.ThrowsAsync<ArgumentNullException>(() => source!.LongCountAsync());
    }

    [Fact]
    public void PatchById_Adds_Patch_Mutation()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);

        // Act
        set.PatchById("doc-123", p => p.Set = new { title = "New Title" });

        // Assert
        Assert.Contains(context.Mutations.Mutations, m => m.DocType == typeof(MyDoc) && m.GetType().Name.Contains("Patch"));
    }

    [Fact]
    public void DeleteById_Adds_Delete_Mutation()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);

        // Act
        set.DeleteById("doc-123");

        // Assert
        Assert.Contains(context.Mutations.Mutations, m => m.DocType == typeof(MyDoc) && m.GetType().Name.Contains("DeleteById"));
    }

    [Fact]
    public void ClearChanges_Removes_Only_For_Specific_Doc_Type()
    {
        // Arrange
        var context = CreateContext();
        var set1 = new SanityDocumentSet<MyDoc>(context, 3);
        var set2 = new SanityDocumentSet<OtherDoc>(context, 3);

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
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var queryable = set.Where(d => d.Title == null);

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
        var set = new SanityDocumentSet<MyDoc>(context, 3);

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
        var set = new SanityDocumentSet<MyDoc>(context, 3);
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
        var set = new SanityDocumentSet<MyDoc>(context, 3);
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
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var queryable = set.Where(d => d.Title == "Hello");

        // Act
        var groq = queryable.GetSanityQuery();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(groq));
        Assert.Contains("_type == \"myDoc\"", groq, StringComparison.Ordinal);
        Assert.Contains("title == \"Hello\"", groq, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSanityQuery_With_Complex_Query_Returns_Expected_Groq()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var queryable = set
            .Where(d => d.Title != null)
            .OrderBy(d => d.Title)
            .Skip(10)
            .Take(5);

        // Act
        var groq = queryable.GetSanityQuery();

        // Assert
        Assert.Contains("_type == \"myDoc\"", groq);
        Assert.Contains("defined(title)", groq);
        Assert.Contains("order(title asc)", groq);
        Assert.Contains("[10..14]", groq);
    }

    [Fact]
    public void GetSanityQuery_With_Select_Returns_Expected_Groq()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var queryable = set
            .Where(d => d.Title != null)
            .Select(d => new { d.Title });

        // Act
        var groq = queryable.GetSanityQuery();

        // Assert
        Assert.Contains("_type == \"myDoc\"", groq);
        Assert.Contains("title", groq);
        // Depending on implementation, it might look like "{title}" or just include "title" in the projection
    }

    [Fact]
    public void GetSanityQuery_With_Multiple_Where_Returns_Expected_Groq()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var queryable = set
            .Where(d => d.Title != null)
            .Where(d => d.Title == "Test");

        // Act
        var groq = queryable.GetSanityQuery();

        // Assert
        Assert.Contains("_type == \"myDoc\"", groq);
        Assert.Contains("defined(title)", groq);
        Assert.Contains("title == \"Test\"", groq);
    }

    [Fact]
    public void GetSanityQuery_With_Include_Returns_Expected_Groq()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        // We use a property that is NOT a SanityReference to see what it generates
        // In MyDoc, Author is Person.
        var queryable = set.Include(d => d.Author!);

        // Act
        var groq = queryable.GetSanityQuery();

        // Assert
        Assert.Contains("_type == \"myDoc\"", groq);
        Assert.Contains("author", groq);
        // For non-SanityReference, it should just include the fields of the nested object
        // Based on the failed test output, it was author{...,_type="...
        Assert.Contains("author{", groq);
    }

    [Fact]
    public void GetSanityQuery_With_Count_Returns_Expected_Groq()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        
        // Count() is a terminal operation, but we want to see the query it would generate.
        // Actually, CountAsync calls provider.ExecuteAsync<int>(exp).
        // provider.ExecuteAsync calls GetSanityQuery<TResult>(expression).
        
        // We can't easily call .Count() and get the query because it executes.
        // But we can manually use the provider to get the query for a Count expression.
        var countMethod = typeof(Queryable).GetMethods().First(m => m.Name == "Count" && m.GetParameters().Length == 1).MakeGenericMethod(typeof(MyDoc));
        var countExpression = Expression.Call(null, countMethod, set.Expression);
        
        var provider = (SanityQueryProvider)set.Provider;
        var groq = provider.GetSanityQuery<int>(countExpression);

        // Assert
        Assert.StartsWith("count(", groq);
        Assert.EndsWith(")", groq);
        Assert.Contains("_type == \"myDoc\"", groq);
    }

    [Fact]
    public void GetSanityQuery_With_FirstOrDefault_Returns_Expected_Groq()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        
        // FirstOrDefault() is a terminal operation. 
        // We can simulate it by calling Take(1).
        var queryable = set.Where(d => d.Title != null).Take(1);

        // Act
        var groq = queryable.GetSanityQuery();

        // Assert
        Assert.Contains("_type == \"myDoc\"", groq);
        Assert.Contains("[0]", groq);
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
        var queryable = Array.Empty<MyDoc>().AsQueryable();

        // Act + Assert
        var ex1 = Assert.Throws<Exception>(() => queryable.Include(d => d.Author!));
        Assert.Equal("Queryable source must be a SanityDbSet<T>.", ex1.Message);

        var ex2 = Assert.Throws<Exception>(() => queryable.Include(d => d.Author!, "src"));
        Assert.Equal("Queryable source must be a SanityDbSet<T>.", ex2.Message);
    }

    [Fact]
    public void Include_Overload_With_SourceName_Produces_Queryable()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        IQueryable<MyDoc> queryable = set;

        // Act
        var result = queryable.Include(d => d.Author, "authorRef");

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
        var set = new SanityDocumentSet<MyDoc>(context, 3);
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
        Assert.Throws<ArgumentNullException>(() => queryable!.Include(d => d.Author!, "authorRef"));
    }

    [Fact]
    public void Patch_By_Query_Adds_Patch_Mutation()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var queryable = set.Where(d => d.Title != null);

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
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var queryable = set.Where(d => d.Title != null);

        // Act + Assert (Mutations layer will invoke null delegate -> NullReferenceException)
        Assert.Throws<NullReferenceException>(() => queryable.Patch(null!));
    }

    [Fact]
    public void Update_Throws_When_Id_Is_Missing()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var doc = new MyDoc { Title = "T" }; // no _id set

        // Act + Assert
        var ex = Assert.Throws<Exception>(() => set.Update(doc));
        Assert.Equal("Id must be specified when updating document.", ex.Message);
    }

    [Fact]
    public void SetValues_Adds_Patch_Mutation()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var doc = new MyDoc { Title = "T" };
        doc.SetSanityId("doc-1");

        // Act
        var builder = set.SetValues(doc);

        // Assert
        Assert.NotNull(builder);
        var patchMutation = context.Mutations.Mutations.OfType<SanityPatchMutation>().FirstOrDefault(m => m.DocType == typeof(MyDoc));
        Assert.NotNull(patchMutation);
        var patchById = patchMutation.Patch as SanityPatchById<MyDoc>;
        Assert.NotNull(patchById);
        Assert.Equal("doc-1", patchById.Id);
        Assert.Equal(doc, patchById.Set);
    }

    [Fact]
    public void SetValues_Throws_When_Id_Is_Missing()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var doc = new MyDoc { Title = "T" }; // no _id set

        // Act + Assert
        var ex = Assert.Throws<Exception>(() => set.SetValues(doc));
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

    private sealed class TestSanityClient : SanityClient
    {
        public TestSanityClient() : base(new SanityOptions { ProjectId = "p", Dataset = "d" })
        {
        }

        public bool FetchAsyncCalled { get; private set; }
        public object? FetchResult { get; set; }

        public bool CommitMutationsCalled { get; private set; }
        public bool UploadImageCalled { get; private set; }
        public bool UploadFileCalled { get; private set; }
        public string LastQuery { get; private set; } = string.Empty;

        public override Task<SanityQueryResponse<TResult>> FetchAsync<TResult>(string query, object? parameters = null, ContentCallback? callback = null, CancellationToken cancellationToken = default)
        {
            FetchAsyncCalled = true;
            LastQuery = query;
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

    private sealed class MinimalHttpServer : IDisposable
    {
        private CancellationTokenSource? _cts;
        private TcpListener? _listener;
        public int Port { get; private set; }

        public void Dispose()
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }

            try
            {
                _listener?.Stop();
            }
            catch
            {
            }

            _cts?.Dispose();
        }

        public Task StartAsync()
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

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