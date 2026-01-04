using System.Collections.ObjectModel;

namespace Sanity.Linq.QueryProvider;

internal sealed class SanityGroqTokenRegistry
{
    public static readonly SanityGroqTokenRegistry Instance = new();

    private SanityGroqTokenRegistry()
    {
        var tokens = new Dictionary<string, string>
        {
            { SanityConstants.DEREFERENCING_SWITCH, SanityConstants.TOKEN_PREFIX + "01__" },
            { SanityConstants.DEREFERENCING_OPERATOR, SanityConstants.TOKEN_PREFIX + "02__" },
            { SanityConstants.STRING_DELIMITER, SanityConstants.TOKEN_PREFIX + "03__" },
            { SanityConstants.COLON, SanityConstants.TOKEN_PREFIX + "04__" },
            { SanityConstants.SPREAD_OPERATOR, SanityConstants.TOKEN_PREFIX + "05__" },
            { SanityConstants.ARRAY_INDICATOR, SanityConstants.TOKEN_PREFIX + "06__" },
            { SanityConstants.OPEN_BRACKET, SanityConstants.TOKEN_PREFIX + "07__" },
            { SanityConstants.CLOSE_BRACKET, SanityConstants.TOKEN_PREFIX + "08__" },
            { SanityConstants.OPEN_PAREN, SanityConstants.TOKEN_PREFIX + "09__" },
            { SanityConstants.CLOSE_PAREN, SanityConstants.TOKEN_PREFIX + "10__" },
            { SanityConstants.AT, SanityConstants.TOKEN_PREFIX + "11__" },
            { SanityConstants.DOT, SanityConstants.TOKEN_PREFIX + "12__" },
            { SanityConstants.EQUALS, SanityConstants.TOKEN_PREFIX + "13__" },
            { SanityConstants.NOT_EQUALS, SanityConstants.TOKEN_PREFIX + "14__" },
            { SanityConstants.AND, SanityConstants.TOKEN_PREFIX + "15__" },
            { SanityConstants.OR, SanityConstants.TOKEN_PREFIX + "16__" },
            { SanityConstants.GREATER_THAN, SanityConstants.TOKEN_PREFIX + "17__" },
            { SanityConstants.LESS_THAN, SanityConstants.TOKEN_PREFIX + "18__" },
            { SanityConstants.GREATER_THAN_OR_EQUAL, SanityConstants.TOKEN_PREFIX + "19__" },
            { SanityConstants.LESS_THAN_OR_EQUAL, SanityConstants.TOKEN_PREFIX + "20__" }
        };

        Tokens = new ReadOnlyDictionary<string, string>(tokens);
        SortedTokenKeys = tokens.Keys.OrderByDescending(k => k.Length).ToList().AsReadOnly();
        ReverseTokens = new ReadOnlyDictionary<string, string>(tokens.ToDictionary(kvp => kvp.Value, kvp => kvp.Key));
    }

    public IReadOnlyDictionary<string, string> ReverseTokens { get; }
    public IReadOnlyList<string> SortedTokenKeys { get; }
    public IReadOnlyDictionary<string, string> Tokens { get; }
}