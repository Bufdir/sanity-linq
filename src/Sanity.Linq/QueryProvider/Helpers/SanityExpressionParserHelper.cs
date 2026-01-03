namespace Sanity.Linq.QueryProvider;

internal static class SanityExpressionParserHelper
{
    private static string GetBinaryOperator(ExpressionType nodeType)
    {
        return nodeType switch
        {
            ExpressionType.Equal => SanityConstants.EQUALS,
            ExpressionType.AndAlso => SanityConstants.AND,
            ExpressionType.OrElse => SanityConstants.OR,
            ExpressionType.LessThan => SanityConstants.LESS_THAN,
            ExpressionType.GreaterThan => SanityConstants.GREATER_THAN,
            ExpressionType.LessThanOrEqual => SanityConstants.LESS_THAN_OR_EQUAL,
            ExpressionType.GreaterThanOrEqual => SanityConstants.GREATER_THAN_OR_EQUAL,
            ExpressionType.NotEqual => SanityConstants.NOT_EQUALS,
            _ => throw new NotImplementedException($"Operator '{nodeType}' is not supported.")
        };
    }

    public static string TransformBinaryExpression(BinaryExpression b, Func<Expression, string> transformOperand)
    {
        var op = GetBinaryOperator(b.NodeType);
        var left = transformOperand(b.Left);
        var right = transformOperand(b.Right);

        if (left == SanityConstants.NULL && op is SanityConstants.EQUALS or SanityConstants.NOT_EQUALS)
            // Swap left and right so null is always on the right for comparison logic
            (left, right) = (right, left);

        return right switch
        {
            SanityConstants.NULL when op == SanityConstants.EQUALS => $"{SanityConstants.OPEN_PAREN}{SanityConstants.NOT}{SanityConstants.OPEN_PAREN}{SanityConstants.DEFINED}{SanityConstants.OPEN_PAREN}{left}{SanityConstants.CLOSE_PAREN}{SanityConstants.CLOSE_PAREN}{SanityConstants.SPACE}{SanityConstants.OR}{SanityConstants.SPACE}{left}{SanityConstants.SPACE}{op}{SanityConstants.SPACE}{right}{SanityConstants.CLOSE_PAREN}",
            SanityConstants.NULL when op == SanityConstants.NOT_EQUALS => $"{SanityConstants.OPEN_PAREN}{SanityConstants.DEFINED}{SanityConstants.OPEN_PAREN}{left}{SanityConstants.CLOSE_PAREN}{SanityConstants.SPACE}{SanityConstants.AND}{SanityConstants.SPACE}{left}{SanityConstants.SPACE}{op}{SanityConstants.SPACE}{right}{SanityConstants.CLOSE_PAREN}",
            _ => $"{left}{SanityConstants.SPACE}{op}{SanityConstants.SPACE}{right}"
        };
    }
}