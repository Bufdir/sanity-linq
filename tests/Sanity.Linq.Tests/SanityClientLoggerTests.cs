using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityClientLoggerTests
{
    private static SanityOptions CreateOptions(bool debug = false)
    {
        return new SanityOptions
        {
            ProjectId = "testproj",
            Dataset = "testdata",
            UseCdn = false,
            ApiVersion = "v2021-03-25",
            Debug = debug
        };
    }

    [Fact]
    public async Task HandleHttpResponse_Logs_Debug_When_Debug_Enabled()
    {
        var logger = new TestLogger();
        var client = new TestableSanityClient(CreateOptions(true), logger);

        var payload = new { value = 42 };
        var http = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonConvert.SerializeObject(payload))
        };

        var _ = await client.InvokeHandleAsync<SanityClientResponseTests.TestDto>(http);

        Assert.Contains(logger.Messages, m =>
            m.level == LogLevel.Debug &&
            m.message.Contains("\"response\":") &&
            m.message.Contains("\"content\":") &&
            m.message.Contains("\"value\": 42") &&
            !m.message.Contains("\n"));
    }

    [Fact]
    public async Task HandleHttpResponse_Does_Not_Log_Debug_When_Debug_Disabled()
    {
        var logger = new TestLogger();
        var client = new TestableSanityClient(CreateOptions(false), logger);

        var http = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"value\":1}")
        };

        var _ = await client.InvokeHandleAsync<SanityClientResponseTests.TestDto>(http);

        Assert.DoesNotContain(logger.Messages, m => m.level == LogLevel.Debug);
    }

    [Fact]
    public async Task HandleHttpResponse_Logs_Raw_When_Content_Is_Not_Json()
    {
        var logger = new TestLogger();
        var client = new TestableSanityClient(CreateOptions(true), logger);

        const string raw = "not json at all";
        var http = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(raw)
        };

        try
        {
            var _ = await client.InvokeHandleAsync<SanityClientResponseTests.TestDto>(http);
        }
        catch
        {
            // Ignore deserialization error; we're testing logging behavior only
        }

        Assert.Contains(logger.Messages, m =>
            m.level == LogLevel.Debug &&
            m.message.Contains("\"response\":") &&
            m.message.Contains("\"content\":") &&
            m.message.Contains(raw) &&
            !m.message.Contains("\n"));
    }

    private sealed class TestableSanityClient(SanityOptions options, ILogger logger) : SanityClient(options, null, null, null, logger)
    {
        public Task<T> InvokeHandleAsync<T>(HttpResponseMessage response)
        {
            return HandleHttpResponseAsync<T>(response);
        }
    }

    private sealed class TestLogger : ILogger
    {
        public readonly List<(LogLevel level, string message)> Messages = new();

        IDisposable? ILogger.BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            Messages.Add((logLevel, msg));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}