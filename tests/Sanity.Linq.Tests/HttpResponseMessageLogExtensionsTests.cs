using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Sanity.Linq.Tests;

public class HttpResponseMessageLogExtensionsTests
{
    [Fact]
    public async Task Returns_Content_And_Does_Not_Log_When_Debug_Disabled()
    {
        var logger = new TestLogger();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("hello world");

        var text = await response.GetResponseContentAndDebugAsync(false, logger);

        Assert.Equal("hello world", text);
        Assert.DoesNotContain(logger.Messages, m => m.level == LogLevel.Debug);
    }

    [Fact]
    public async Task Logs_Single_Line_Envelope_With_Json_When_Debug_Enabled()
    {
        var logger = new TestLogger();

        var requestJson = "{\"a\": 1}";
        var responseJson = "{\"value\": 42}";

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/test?x=1&y=2")
        {
            Content = new StringContent(requestJson)
        };
        response.Content = new StringContent(responseJson);

        var text = await response.GetResponseContentAndDebugAsync(true, logger);

        Assert.Equal(responseJson, text);

        var debug = Assert.Single(logger.Messages, m => m.level == LogLevel.Debug);
        var msg = debug.message;

        // Should be single-line
        Assert.DoesNotContain("\n", msg);
        Assert.DoesNotContain("\r", msg);

        // Contains expected envelope parts
        Assert.Contains("\"request\":", msg);
        Assert.Contains("\"response\":", msg);
        Assert.Contains("\"url\": \"https://example.com/api/test?x=1&y=2\"", msg);
        Assert.Contains("\"querystring\": \"x=1&y=2\"", msg);
        // Body parsed as JSON object (allow flexible whitespace)
        Assert.Contains("\"body\":", msg);
        Assert.Contains("\"a\": 1", msg);
        // Response parsed as JSON object (allow flexible whitespace)
        Assert.Contains("\"content\":", msg);
        Assert.Contains("\"value\": 42", msg);
    }

    [Fact]
    public async Task Logs_String_When_Not_Json()
    {
        var logger = new TestLogger();

        const string requestRaw = "not json";
        const string responseRaw = "also not json";

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?q=ok")
        {
            Content = new StringContent(requestRaw)
        };
        response.Content = new StringContent(responseRaw);

        var _ = await response.GetResponseContentAndDebugAsync(true, logger);

        var debug = Assert.Single(logger.Messages, m => m.level == LogLevel.Debug);
        var msg = debug.message;

        // Non-JSON should be quoted strings in envelope
        Assert.Contains("\"body\": \"not json\"", msg);
        Assert.Contains("\"content\": \"also not json\"", msg);
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

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
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