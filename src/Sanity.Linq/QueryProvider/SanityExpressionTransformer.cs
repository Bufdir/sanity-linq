using System.Collections;
using Sanity.Linq.Internal;

namespace Sanity.Linq.QueryProvider;

internal static class SanityExpressionTransformer
{
    public static string EscapeString(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var needsEscaping = false;
        foreach (var c in value)
            if (c is SanityConstants.CHAR_BACKSLASH or SanityConstants.CHAR_QUOTE)
            {
                needsEscaping = true;
                break;
            }

        if (!needsEscaping) return value;

        var sb = new StringBuilder(value.Length + 4);
        foreach (var c in value)
        {
            if (c is SanityConstants.CHAR_BACKSLASH or SanityConstants.CHAR_QUOTE) sb.Append(SanityConstants.CHAR_BACKSLASH);
            sb.Append(c);
        }

        return sb.ToString();
    }

    public static string TransformOperand(Expression e, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler, bool useCoalesceFallback = true)
    {
        return e switch
        {
            MemberExpression m => SanityExpressionTransformerHelper.HandleMemberExpression(m, TransformOperand, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback),
            NewExpression nw => SanityExpressionTransformerHelper.HandleNewExpression(nw, TransformOperand, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback),
            BinaryExpression b => binaryExpressionHandler(b),
            UnaryExpression u => unaryExpressionHandler(u),
            MethodCallExpression mc => methodCallHandler(mc),
            ConstantExpression { Value: null } => SanityConstants.NULL,
            ConstantExpression c when c.Type == typeof(string) => $"{SanityConstants.STRING_DELIMITER}{EscapeString(c.Value?.ToString() ?? string.Empty)}{SanityConstants.STRING_DELIMITER}",
            ConstantExpression c when c.Type.IsNumericOrBoolType() => string.Format(CultureInfo.InvariantCulture, "{0}", c.Value).ToLower(),
            ConstantExpression { Value: DateTime dt } => dt == dt.Date
                ? $"{SanityConstants.STRING_DELIMITER}{dt:yyyy-MM-dd}{SanityConstants.STRING_DELIMITER}"
                : $"{SanityConstants.STRING_DELIMITER}{dt:O}{SanityConstants.STRING_DELIMITER}",
            ConstantExpression { Value: DateTimeOffset dto } => $"{SanityConstants.STRING_DELIMITER}{dto:O}{SanityConstants.STRING_DELIMITER}",
            ConstantExpression c when c.Type == typeof(Guid) => $"{SanityConstants.STRING_DELIMITER}{EscapeString(c.Value?.ToString() ?? string.Empty)}{SanityConstants.STRING_DELIMITER}",
            ConstantExpression c when c.Type.IsArray || (c.Type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(c.Type)) => SanityExpressionTransformerHelper.FormatEnumerable(c.Value as IEnumerable, c.Type, TransformOperand, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback),
            ConstantExpression c => c.Value?.ToString() ?? SanityConstants.NULL,
            ParameterExpression => SanityConstants.AT,
            NewArrayExpression na => SanityConstants.OPEN_BRACKET + string.Join(SanityConstants.COMMA, na.Expressions.Select(expr => TransformOperand(expr, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback))) + SanityConstants.CLOSE_BRACKET,
            _ => throw new Exception($"Operands of type {e.GetType()} and nodeType {e.NodeType} not supported. ")
        };
    }

    public static string TransformUnaryExpression(UnaryExpression u, Func<Expression, string> operandTransformer)
    {
        return u.NodeType switch
        {
            ExpressionType.Not => SanityConstants.NOT + SanityConstants.OPEN_PAREN + operandTransformer(u.Operand) + SanityConstants.CLOSE_PAREN,
            ExpressionType.Convert => operandTransformer(u.Operand),
            _ => throw new Exception($"Unary expression of type {u.GetType()} and nodeType {u.NodeType} not supported. ")
        };
    }
}