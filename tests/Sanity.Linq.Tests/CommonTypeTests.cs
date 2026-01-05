using System;
using System.Threading.Tasks;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.Demo.Model;
using Xunit;

namespace Sanity.Linq.Tests;

public class CommonTypeTest : TestBase
{

    [Fact]
    public async Task SanityLocaleString_GetWithLanguageCode_ShouldReturnAValue()
    {
        var sanity = new SanityDataContext(Options);
        await ClearAllDataAsync(sanity);

        var page = new Page
        {
            Id = Guid.NewGuid().ToString(),
            Title = new SanityLocaleString(),
        };

        page.Title.Set("en", "My Page");
        page.Title.Set("no", "Min side");

        // Create page
        await sanity.DocumentSet<Page>().Create(page).CommitAsync();

        // Retrieve newly created page
        page = await sanity.DocumentSet<Page>().GetAsync(page.Id);

        Assert.NotNull(page);
        var enTitle = page.Title.Get("en");
        var noTitle = page.Title.Get("no");

        Assert.NotNull(enTitle);
        Assert.NotNull(noTitle);
        Assert.Equal("My Page", enTitle);
        Assert.Equal("Min side", noTitle);
    }

    [Fact]
    public async Task SanityLocaleT_GetWithLanguageCode_ShouldReturnAT()
    {
        var sanity = new SanityDataContext(Options);
        await ClearAllDataAsync(sanity);

        var page = new Page
        {
            Id = Guid.NewGuid().ToString(),
            Options = new SanityLocale<PageOptions>()
        };

        page.Options.Set("en", new PageOptions { ShowOnFrontPage = false, Subtitle = "Awesome page" });
        page.Options.Set("no", new PageOptions { ShowOnFrontPage = true, Subtitle = "Heftig bra side!" });

        // Create page
        await sanity.DocumentSet<Page>().Create(page).CommitAsync();

        // Retrieve newly created page
        page = await sanity.DocumentSet<Page>().GetAsync(page.Id);

        Assert.NotNull(page);
        var enOptions = page.Options.Get("en");
        var noOptions = page.Options.Get("no");

        Assert.NotNull(enOptions);
        Assert.NotNull(noOptions);
        Assert.Equal("Awesome page", enOptions.Subtitle);
        Assert.Equal("Heftig bra side!", noOptions.Subtitle);
        Assert.False(enOptions.ShowOnFrontPage);
        Assert.True(noOptions.ShowOnFrontPage);
    }


}