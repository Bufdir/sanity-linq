using System.Collections.ObjectModel;

namespace Sanity.Linq.QueryProvider;

internal sealed class SanityGroqTokenRegistry
{
    public static readonly SanityGroqTokenRegistry Instance = new();

    private SanityGroqTokenRegistry()
    {
        var tokens = new Dictionary<string, string>
        {
            { SanityConstants.DEREFERENCING_SWITCH, "__GTK_01__" },
            { SanityConstants.DEREFERENCING_OPERATOR, "__GTK_02__" },
            { SanityConstants.STRING_DELIMITER, "__GTK_03__" },
            { SanityConstants.COLON, "__GTK_04__" },
            { SanityConstants.SPREAD_OPERATOR, "__GTK_05__" },
            { SanityConstants.ARRAY_INDICATOR, "__GTK_06__" },
            { SanityConstants.OPEN_BRACKET, "__GTK_07__" },
            { SanityConstants.CLOSE_BRACKET, "__GTK_08__" },
            { SanityConstants.OPEN_PAREN, "__GTK_09__" },
            { SanityConstants.CLOSE_PAREN, "__GTK_10__" },
            { SanityConstants.AT, "__GTK_11__" },
            { SanityConstants.DOT, "__GTK_12__" },
            { SanityConstants.EQUALS, "__GTK_13__" },
            { SanityConstants.NOT_EQUALS, "__GTK_14__" },
            { SanityConstants.AND, "__GTK_15__" },
            { SanityConstants.OR, "__GTK_16__" },
            { SanityConstants.GREATER_THAN, "__GTK_17__" },
            { SanityConstants.LESS_THAN, "__GTK_18__" },
            { SanityConstants.GREATER_THAN_OR_EQUAL, "__GTK_19__" },
            { SanityConstants.LESS_THAN_OR_EQUAL, "__GTK_20__" }
        };

        Tokens = new ReadOnlyDictionary<string, string>(tokens);
        SortedTokenKeys = tokens.Keys.OrderByDescending(k => k.Length).ToList().AsReadOnly();
        ReverseTokens = new ReadOnlyDictionary<string, string>(tokens.ToDictionary(kvp => kvp.Value, kvp => kvp.Key));
    }

    public IReadOnlyDictionary<string, string> Tokens { get; }
    public IReadOnlyList<string> SortedTokenKeys { get; }
    public IReadOnlyDictionary<string, string> ReverseTokens { get; }
}
