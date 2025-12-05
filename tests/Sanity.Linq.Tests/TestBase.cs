using System;
using System.Threading.Tasks;
using Sanity.Linq.Demo.Model;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Sanity.Linq.Tests;

public class TestBase
{
    public SanityOptions Options => new()
    {
        ProjectId = "dnjvf98k",
        Dataset = "test",
        Token = "skTl7VigmZzLpK4d6qE3hgeBqnTHILh5v9nSOX689Nk3bcd2Xs1Mm1rXt7JxWSBsBcrXmc5omCHd63kjYUDaCs0k1DNTz1qrIT5MX6I66Lsr9XD1Ln3NNaomZWFIBoIw1Y0bnwVSTgsDUR4BRqfO8bCXTfzFRvIBZdgwJxcRV8isJzbFmQJ7",
        UseCdn = false
    };

    public async Task ClearAllDataAsync(SanityDataContext sanity)
    {
        // Clear existing records in single transaction
        sanity.DocumentSet<Post>().Delete();
        sanity.DocumentSet<Author>().Delete();
        sanity.DocumentSet<Category>().Delete();
        await sanity.CommitAsync();

        await sanity.Images.Delete().CommitAsync();
    }

    protected static async Task WaitUntilAsync(Func<Task<bool>> condition, int maxRetries = 15, int delayMs = 300)
    {
        for (var i = 0; i < maxRetries; i++)
        {
            if (await condition()) return;
            await Task.Delay(delayMs);
        }
        // One last attempt before giving up
        if (!await condition())
            throw new TimeoutException("Condition not met within the allotted retries");
    }
}