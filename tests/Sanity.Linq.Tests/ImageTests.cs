using System;
using System.Linq;
using System.Threading.Tasks;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.Demo.Model;
using Xunit;

namespace Sanity.Linq.Tests;

public class ImageTests : TestBase
{
        

    //[Fact]
    public async Task Image_Test()
    {

        var sanity = new SanityDataContext(Options);

        // Clear existing records
        await sanity.DocumentSet<Post>().Delete().CommitAsync();
        await sanity.DocumentSet<Author>().Delete().CommitAsync();
        await sanity.DocumentSet<Category>().Delete().CommitAsync();

        // Delete images
        await sanity.Images.Delete().CommitAsync();

        // Wait until the dataset is consistent (no docs/images)
        await WaitUntilAsync(async () => (await sanity.DocumentSet<Category>().ToListAsync()).Count == 0);
        await WaitUntilAsync(async () => (await sanity.DocumentSet<Author>().ToListAsync()).Count == 0);

        // Upload new image
        var imageUri = new Uri("https://www.sanity.io/static/images/opengraph/social.png");
        var image = (await sanity.Images.UploadAsync(imageUri)).Document;

        var category = new Category
        {
            // Use a unique id to avoid conflicts in eventually-consistent CI
            CategoryId = Guid.NewGuid().ToString(),
            Description = "Category for popular authors",
            Title = "Popular Authors",
            MainImage = new SanityImage
            {
                Asset = new SanityReference<SanityImageAsset> { Ref = image.Id },
            }
        };
        await sanity.DocumentSet<Category>().Create(category).CommitAsync();

        // Link image to new author
        var author = new Author
        {
            Name = "Joe Bloggs",
            Images =
            [
                new SanityImage
                {
                    Asset = new SanityReference<SanityImageAsset> { Ref = image.Id },
                }
            ],
            FavoriteCategories =
            [
                new SanityReference<Category>
                {
                    Value = category
                }
            ]
        };

        await sanity.DocumentSet<Author>().Create(author).CommitAsync();
        // Wait until the created author is queryable
        await WaitUntilAsync(async () => (await sanity.DocumentSet<Author>().ToListAsync()).Count >= 1, maxRetries: 40, delayMs: 500);
        // Also ensure category is visible (eventual consistency)
        await WaitUntilAsync(async () => (await sanity.DocumentSet<Category>().ToListAsync()).Count >= 1, maxRetries: 40, delayMs: 500);
        // Wait until dereferencing of image asset and category image succeeds
        await WaitUntilAsync(async () =>
        {
            var list = await sanity.DocumentSet<Author>()
                .Include(a => a.Images)
                .Include(a => a.FavoriteCategories)
                .ToListAsync();
            var first = list.FirstOrDefault();
            var hasImageExt = first?.Images?.FirstOrDefault()?.Asset?.Value?.Extension != null;
            var hasCatImageExt = first?.FavoriteCategories?.FirstOrDefault()?.Value?.MainImage?.Asset?.Value?.Extension != null;
            return hasImageExt && hasCatImageExt;
        }, maxRetries: 60, delayMs: 1000);

        var retrievedDoc = await sanity.DocumentSet<Author>().ToListAsync();

        Assert.True(retrievedDoc.FirstOrDefault()?.Images?.FirstOrDefault()?.Asset?.Value?.Extension != null);

        Assert.True(retrievedDoc.FirstOrDefault()?.FavoriteCategories?.FirstOrDefault()?.Value?.MainImage?.Asset?.Value?.Extension != null);

    }
}