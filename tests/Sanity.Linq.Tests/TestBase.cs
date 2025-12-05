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

        // Delete all images
        await sanity.Images.Delete().CommitAsync();

        // Wait for eventual consistency: ensure all collections are empty
        await WaitUntilAsync(async () => (await sanity.DocumentSet<Post>().ToListAsync()).Count == 0, maxRetries: 40, delayMs: 500);
        await WaitUntilAsync(async () => (await sanity.DocumentSet<Author>().ToListAsync()).Count == 0, maxRetries: 40, delayMs: 500);
        await WaitUntilAsync(async () => (await sanity.DocumentSet<Category>().ToListAsync()).Count == 0, maxRetries: 40, delayMs: 500);
    }

    protected static async Task WaitUntilAsync(Func<Task<bool>> condition, int maxRetries = 40, int delayMs = 500)
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

    protected static async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxRetries = 3, int delayMs = 1000)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (ex.GetType().FullName == "Sanity.Linq.Exceptions.SanityHttpException")
            {
                last = ex;
                await Task.Delay(delayMs);
            }
        }
        throw last ?? new Exception("Unknown error during retry operation");
    }
}