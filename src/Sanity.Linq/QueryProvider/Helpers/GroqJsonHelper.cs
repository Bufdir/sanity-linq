namespace Sanity.Linq.QueryProvider;

internal static partial class GroqJsonHelper
{
    public static string GroqToJson(string groq)
    {
        if (string.IsNullOrEmpty(groq)) return groq;

        var registry = SanityGroqTokenRegistry.Instance;
        var tokens = registry.Tokens;
        var sortedKeys = registry.SortedTokenKeys;

        var sb = new StringBuilder(groq.Length);
        var inQuotes = false;
        char? quoteChar = null;
        var isEscaped = false;

        for (var i = 0; i < groq.Length; i++)
        {
            var c = groq[i];

            // 1. Try match token
            string? matchedKey = null;
            string? matchedToken = null;
            foreach (var key in sortedKeys)
                if (i + key.Length <= groq.Length)
                {
                    var match = !key.Where((t, j) => groq[i + j] != t).Any();

                    if (!match) continue;
                    matchedKey = key;
                    matchedToken = tokens[key];
                    break;
                }

            if (matchedKey != null)
            {
                if (matchedKey == SanityConstants.STRING_DELIMITER)
                {
                    if (!inQuotes)
                    {
                        inQuotes = true;
                        quoteChar = '"';
                        isEscaped = false;
                    }
                    else if (quoteChar == '"' && !isEscaped)
                    {
                        inQuotes = false;
                        quoteChar = null;
                    }
                }

                sb.Append(matchedToken);
                i += matchedKey.Length - 1;
                if (inQuotes) isEscaped = false;
                continue;
            }

            // 2. Handle non-token characters
            if (c == SanityConstants.STRING_DELIMITER[0] || c == SanityConstants.SINGLE_QUOTE[0])
            {
                if (!inQuotes)
                {
                    inQuotes = true;
                    quoteChar = c;
                    isEscaped = false;
                }
                else if (c == quoteChar && !isEscaped)
                {
                    inQuotes = false;
                    quoteChar = null;
                }
            }

            if (c == SanityConstants.SPACE[0] && !inQuotes) continue;

            sb.Append(c);

            if (!inQuotes) continue;
            if (c == '\\') isEscaped = !isEscaped;
            else isEscaped = false;
        }

        var json = sb.ToString();
        json = json.Replace(SanityConstants.OPEN_BRACE, SanityConstants.COLON + SanityConstants.OPEN_BRACE);
        if (json.StartsWith(SanityConstants.COLON)) json = json.Substring(1);

        // Replace variable names with valid JSON (e.g., convert myField to "myField": true)
        var reVariables = MyRegex();
        json = reVariables.Replace(json, m => $"{m.Groups[1].Value}{SanityConstants.STRING_DELIMITER}{m.Groups[2].Value}{SanityConstants.STRING_DELIMITER}{SanityConstants.COLON}{SanityConstants.TRUE}{m.Groups[3].Value}");
        // Second pass to handle overlapping matches like {a,b,c}
        json = reVariables.Replace(json, m => $"{m.Groups[1].Value}{SanityConstants.STRING_DELIMITER}{m.Groups[2].Value}{SanityConstants.STRING_DELIMITER}{SanityConstants.COLON}{SanityConstants.TRUE}{m.Groups[3].Value}");

        return json;
    }

    public static string JsonToGroq(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;

        var groq = json
            .Replace(SanityConstants.COLON + SanityConstants.OPEN_BRACE, SanityConstants.OPEN_BRACE)
            .Replace(SanityConstants.COLON + SanityConstants.TRUE, string.Empty)
            .Replace(SanityConstants.STRING_DELIMITER, string.Empty);

        return Untokenize(groq);
    }

    public static string Untokenize(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        var reverseTokens = SanityGroqTokenRegistry.Instance.ReverseTokens;

        var index = key.IndexOf(SanityConstants.TOKEN_PREFIX, StringComparison.Ordinal);
        if (index == -1) return key;

        var sb = new StringBuilder();
        var lastIndex = 0;
        while (index != -1)
        {
            sb.Append(key, lastIndex, index - lastIndex);
            if (index + 10 <= key.Length)
            {
                var token = key.Substring(index, 10);
                if (reverseTokens.TryGetValue(token, out var val))
                {
                    sb.Append(val);
                    lastIndex = index + 10;
                }
                else
                {
                    sb.Append(SanityConstants.TOKEN_PREFIX);
                    lastIndex = index + 6;
                }
            }
            else
            {
                sb.Append(SanityConstants.TOKEN_PREFIX);
                lastIndex = index + 6;
            }

            index = key.IndexOf(SanityConstants.TOKEN_PREFIX, lastIndex, StringComparison.Ordinal);
        }

        sb.Append(key, lastIndex, key.Length - lastIndex);
        return sb.ToString();
    }


#if NET7_0_OR_GREATER
    [GeneratedRegex("(,|{)([^\"}:,]+)(,|})")]
    internal static partial Regex MyRegex();
#else
    private static readonly Regex _myRegex = new Regex("(,|{)([^\"}:,]+)(,|})", RegexOptions.Compiled);
    internal static Regex MyRegex() => _myRegex;
#endif
}