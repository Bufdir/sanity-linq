using Microsoft.Extensions.Logging;

namespace Sanity.Linq;

internal static class HttpResponseMessageLogExtensions
{
    public static async Task<string> GetResponseContentAndDebugAsync(this HttpResponseMessage response, bool debug, ILogger? logger)
    {
        // Return early when debug is off or logger is not enabled for Debug
        if (!debug || logger?.IsEnabled(LogLevel.Debug) != true)
            return await GetContentSafeAsync(response.Content, logger).ConfigureAwait(false);

        if (response.Content != null) await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);

        var responseContent = await GetContentSafeAsync(response.Content, logger).ConfigureAwait(false);

        try
        {
            // Buffer request content as well to allow reading after send
            if (response.RequestMessage?.Content != null)
                await response.RequestMessage.Content.LoadIntoBufferAsync().ConfigureAwait(false);

            var requestBody = await GetContentSafeAsync(response.RequestMessage?.Content, logger).ConfigureAwait(false);
            var (requestUrl, queryString) = ExtractRequestDetails(response);

            var envelope = BuildLogEnvelope(requestUrl, queryString, requestBody, responseContent);
            // Keep spaces after colons but remove newlines to satisfy single-line expectations
            var pretty = envelope.ToString(Formatting.Indented);
            var singleLine = pretty.Replace("\r\n", string.Empty).Replace("\n", string.Empty);
            logger?.LogDebug(singleLine);
        }
        catch
        {
            // swallow envelope logging errors to avoid affecting flow
        }

        return responseContent;
    }

    private static (string RequestUrl, string QueryString) ExtractRequestDetails(HttpResponseMessage response)
    {
        var request = response.RequestMessage;
        var requestUrl = request?.RequestUri?.ToString() ?? string.Empty;
        var queryString = request?.RequestUri?.Query ?? string.Empty;
        if (queryString.StartsWith("?", StringComparison.Ordinal))
            queryString = queryString[1..];
        return (requestUrl, queryString);
    }

    private static JObject BuildLogEnvelope(string requestUrl, string queryString, string requestBody, string responseContent)
    {
        return new JObject
        {
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
            ["request"] = new JObject
            {
                ["url"] = requestUrl,
                ["querystring"] = queryString,
                ["body"] = ToJsonOrString(requestBody)
            },
            ["response"] = new JObject
            {
                ["content"] = ToJsonOrString(responseContent)
            }
        };
    }

    private static JToken ToJsonOrString(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return JValue.CreateString(string.Empty);
        try
        {
            return JToken.Parse(text);
        }
        catch
        {
            return JValue.CreateString(text);
        }
    }

    private static async Task<string> GetContentSafeAsync(HttpContent? content, ILogger? logger)
    {
        if (content == null) return string.Empty;

        try
        {
            return await content.ReadAsStringAsync().ConfigureAwait(false) ?? string.Empty;
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to read content as string");
            return string.Empty;
        }
    }
}