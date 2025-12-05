using Sanity.Linq.CommonTypes;
using Sanity.Linq.Mutations.Model;
using System;
using System.Linq;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityDocumentSetExtensionsTests
{
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