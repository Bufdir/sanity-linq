using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.DTOs;
using Sanity.Linq.QueryProvider;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityQueryProviderTests
{
    [Fact]
    public void Constructor_Sets_Properties()
    {
        // Arrange
        var context = CreateContext();

        // Act
        var provider = new SanityQueryProvider(typeof(MyDoc), context, 4);

        // Assert
        Assert.Equal(typeof(MyDoc), provider.DocType);
        Assert.Same(context, provider.Context);
        Assert.Equal(4, provider.MaxNestingLevel);
    }

    [Fact]
    public void CreateQuery_Typed_Returns_SanityDocumentSet()
    {
        // Arrange
        var context = CreateContext();
        var provider = new SanityQueryProvider(typeof(MyDoc), context, 3);
        var baseSet = new SanityDocumentSet<MyDoc>(context, 3);
        var expression = baseSet.Expression; // a valid IQueryable<T> expression

        // Act
        var result = provider.CreateQuery<MyDoc>(expression);

        // Assert
        var ds = Assert.IsType<SanityDocumentSet<MyDoc>>(result);
        Assert.Same(provider, ds.Provider);
        Assert.Same(expression, ((SanityDocumentSet<MyDoc>)ds).Expression);
    }

    [Fact]
    public void CreateQuery_Untyped_Returns_SanityDocumentSet()
    {
        // Arrange
        var context = CreateContext();
        var provider = new SanityQueryProvider(typeof(MyDoc), context, 3);
        var baseSet = new SanityDocumentSet<MyDoc>(context, 3);
        var expression = baseSet.Expression;

        // Act
        var queryable = provider.CreateQuery(expression);

        // Assert
        Assert.IsAssignableFrom<IQueryable>(queryable);
        Assert.IsType<SanityDocumentSet<MyDoc>>(queryable);
        var ds = (SanityDocumentSet<MyDoc>)queryable;
        Assert.Same(provider, ds.Provider);
        Assert.Same(expression, ds.Expression);
    }

    [Fact]
    public void GetSanityQuery_Builds_Query_With_Type_Filter_And_Predicate()
    {
        // Arrange
        var context = CreateContext();
        var set = new SanityDocumentSet<MyDoc>(context, 2);
        var queryable = set.Where(d => d.Title == "Hello");
        var provider = (SanityQueryProvider)queryable.Provider;

        // Act
        var groq = provider.GetSanityQuery<IEnumerable<MyDoc>>(queryable.Expression);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(groq));
        Assert.Contains("_type == \"myDoc\"", groq, StringComparison.Ordinal);
        Assert.Contains("title == \"Hello\"", groq, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Calls_Client_FetchAsync()
    {
        // Arrange
        var expectedDoc = new MyDoc { Title = "AsyncResult" };
        var testClient = new TestSanityClient
        {
            FetchResult = expectedDoc
        };
        var context = CreateContext(testClient);
        var provider = new SanityQueryProvider(typeof(MyDoc), context, 3);
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var expression = set.Where(d => d.Title == "Test").Expression;

        // Act
        var result = await provider.ExecuteAsync<MyDoc>(expression);

        // Assert
        Assert.Same(expectedDoc, result);
        Assert.True(testClient.FetchAsyncCalled);
        Assert.Contains("title == \"Test\"", testClient.LastQuery);
    }

    [Fact]
    public void Execute_Typed_Calls_ExecuteAsync()
    {
        // Arrange
        var expectedDoc = new MyDoc { Title = "SyncResult" };
        var testClient = new TestSanityClient
        {
            FetchResult = expectedDoc
        };
        var context = CreateContext(testClient);
        var provider = new SanityQueryProvider(typeof(MyDoc), context, 3);
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var expression = set.Where(d => d.Title == "Test").Expression;

        // Act
        var result = provider.Execute<MyDoc>(expression);

        // Assert
        Assert.Same(expectedDoc, result);
        Assert.True(testClient.FetchAsyncCalled);
    }

    [Fact]
    public void Execute_Untyped_Calls_ExecuteAsync()
    {
        // Arrange
        var expectedDoc = new MyDoc { Title = "UntypedResult" };
        var testClient = new TestSanityClient
        {
            FetchResult = expectedDoc
        };
        var context = CreateContext(testClient);
        var provider = new SanityQueryProvider(typeof(MyDoc), context, 3);
        var set = new SanityDocumentSet<MyDoc>(context, 3);
        var expression = set.Where(d => d.Title == "Test").Expression;

        // Act
        var result = provider.Execute(expression);

        // Assert
        Assert.Same(expectedDoc, result);
        Assert.True(testClient.FetchAsyncCalled);
    }

    [Theory]
    [InlineData("*[_type == \"movie\"]", "*[_type == \"movie\"]")]
    [InlineData("*[_type == \"movie\"]{title, year}", "*[_type == \"movie\"]{\n  title,\n  year\n}")]
    [InlineData("*[_type == \"movie\"]{title, \"actor\": actors[]->name}", "*[_type == \"movie\"]{\n  title,\n  \"actor\": actors[]->name\n}")]
    public void PrettyPrintQuery_Formats_Query(string input, string expected)
    {
        // Act
        var result = SanityQueryFormatter.Format(input);

        // Assert
        // Standardize line endings for comparison
        Assert.Equal(expected.Replace("\r\n", "\n"), result.Replace("\r\n", "\n"));
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

    private static SanityDataContext CreateContext(SanityClient client)
    {
        var context = CreateContext();
        var field = typeof(SanityDataContext).GetField("<Client>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(context, client);
        return context;
    }

    private sealed class MyDoc : SanityDocument
    {
        public string? Title { get; set; }
    }

    private sealed class TestSanityClient() : SanityClient(new SanityOptions { ProjectId = "p", Dataset = "d" })
    {
        public object? FetchResult { get; set; }
        public bool FetchAsyncCalled { get; private set; }
        public string? LastQuery { get; private set; }

        public override Task<SanityQueryResponse<TResult>> FetchAsync<TResult>(string query, object? parameters = null, ClientCallback? callback = null, CancellationToken cancellationToken = default)
        {
            FetchAsyncCalled = true;
            LastQuery = query;
            return Task.FromResult(new SanityQueryResponse<TResult>
            {
                Result = (TResult)FetchResult!
            });
        }
    }
}