namespace Sanity.Linq.QueryProvider;

public static class SanityResponseProcessor
{
    /// <summary>
    ///     Extracts the content of the "result" property from a Sanity API JSON response.
    ///     It handles both direct responses and responses wrapped in a "response" object (common in some logging formats).
    /// </summary>
    /// <param name="json">The JSON string to extract from.</param>
    /// <returns>The content of the "result" property as a JSON string, or null if not found.</returns>
    public static string? ExtractResult(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var jObject = JObject.Parse(json);

            var resultToken = jObject["result"]
                              ?? jObject["response"]?["content"]?["result"]
                              ?? jObject["response"]?["result"];

            return resultToken?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Parses the "query" and "result" properties from a Sanity API JSON response.
    ///     It handles both direct responses and responses wrapped in a "response" object (common in some logging formats).
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <returns>
    ///     A tuple containing the "query" property as the first element and the "result" property as the second element.
    ///     Both elements will be empty strings if the properties are not found or the JSON is invalid.
    /// </returns>
    public static (string Query, string Result) ParseQueryAndResult(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (string.Empty, string.Empty);

        try
        {
            var jObject = JObject.Parse(json);

            var resultToken = (jObject["result"]
                               ?? jObject["response"]?["content"]?["result"]
                               ?? jObject["response"]?["result"])?.ToString() ?? string.Empty;

            var queryToken = jObject["query"]?.ToString() ?? string.Empty;

            return (queryToken, resultToken);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }
}

public static class SanityResponseExtensions
{
    /// <summary>
    ///     Extension method to extract the content of the "result" property from a Sanity API JSON response string.
    /// </summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The result content as a JSON string, or null.</returns>
    public static string? ExtractSanityResult(this string json)
    {
        return SanityResponseProcessor.ExtractResult(json);
    }
}