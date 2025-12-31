// ReSharper disable InconsistentNaming
namespace Sanity.Linq.QueryProvider;

internal static class SanityConstants
{
    public const string ARRAY_INDICATOR = "[]";
    public const string COLON = ":";
    public const string COMMA = ",";
    public const string DEREFERENCING_OPERATOR = "->";
    public const string SPREAD_OPERATOR = "...";
    public const string STRING_DELIMITER = "\"";
    public const string OPEN_BRACKET = "[";
    public const string CLOSE_BRACKET = "]";
    public const string OPEN_PAREN = "(";
    public const string CLOSE_PAREN = ")";
    public const string OPEN_BRACE = "{";
    public const string CLOSE_BRACE = "}";
    public const string AT = "@";
    public const string DOT = ".";
    public const string EQUALS = "==";
    public const string NOT_EQUALS = "!=";
    public const string AND = "&&";
    public const string OR = "||";
    public const string GREATER_THAN = ">";
    public const string LESS_THAN = "<";
    public const string GREATER_THAN_OR_EQUAL = ">=";
    public const string LESS_THAN_OR_EQUAL = "<=";
    public const string NOT = "!";

    public const string STAR = "*";
    public const string NULL = "null";
    public const string TYPE = "_type";
    public const string ID = "_id";
    public const string REVISION = "_rev";
    public const string CREATED_AT = "_createdAt";
    public const string UPDATED_AT = "_updatedAt";
    public const string MATCH = "match";
    public const string IN = "in";
    public const string ORDER = "order";
    public const string ASC = "asc";
    public const string DESC = "desc";
    public const string COUNT = "count";
    public const string DEFINED = "defined";
    public const string REFERENCES = "references";
    public const string PATH = "path";
    public const string REFERENCE = "reference";
    public const string REF = "_ref";
    public const string KEY = "_key";
    public const string COALESCE = "coalesce";
    public const string ASSET = "asset";

    public const string ARRAY_FILTER = OPEN_BRACKET + DEFINED + "(" + AT + ")" + CLOSE_BRACKET;
    public const string DEREFERENCING_SWITCH = TYPE + "=='" + REFERENCE + "'=>" + AT + DEREFERENCING_OPERATOR;
}