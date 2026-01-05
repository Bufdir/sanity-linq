using System.Collections;

namespace Sanity.Linq.QueryProvider;

internal static class SanityMethodCallTranslatorHelper
{
    internal static string CountGreaterThanZeroPostFix => $"{SanityConstants.SPACE}{SanityConstants.GREATER_THAN}{SanityConstants.SPACE}0";

    internal static (Expression body, List<Expression> selectors) ExtractSelectors(Expression body)
    {
        var selectors = new List<Expression>();
        while (body is MethodCallExpression { Method.Name: "Select" or "SelectMany" or "OfType" } m)
        {
            if (m.Arguments.Count >= 2)
                selectors.Add(m.Arguments[1]);
            else if (m.Method.Name == "OfType") selectors.Add(m);

            body = m.Arguments[0];
        }

        return (body, selectors);
    }

    internal static string GetIncludeSourceName(MethodCallExpression e, string targetName)
    {
        if (e.Arguments is [_, _, ConstantExpression { Value: string s }, ..]) return s;

        return targetName.Split('.').Last();
    }

    internal static string HandleGetValue(MethodCallExpression e)
    {
        if (e.Arguments.Count <= 0 || e.Arguments[1] is not ConstantExpression c || c.Type != typeof(string))
            throw new Exception("Could not evaluate GetValue method");

        var fieldName = c.Value?.ToString() ?? string.Empty;
        return $"{fieldName}";
    }

    internal static string JoinValues(IEnumerable values)
    {
        var sb = new StringBuilder();
        sb.Append(SanityConstants.CHAR_OPEN_BRACKET);
        var first = true;
        foreach (var v in values)
        {
            if (v == null) continue;
            if (!first) sb.Append(SanityConstants.CHAR_COMMA);
            first = false;

            switch (v)
            {
                case string s:
                    sb.Append(SanityConstants.CHAR_STRING_DELIMITER).Append(SanityExpressionTransformer.EscapeString(s)).Append(SanityConstants.CHAR_STRING_DELIMITER);
                    break;
                case Guid g:
                    sb.Append(SanityConstants.CHAR_STRING_DELIMITER).Append(g).Append(SanityConstants.CHAR_STRING_DELIMITER);
                    break;
                case bool b:
                    sb.Append(b ? SanityConstants.TRUE : SanityConstants.FALSE);
                    break;
                case int or long or double or float or decimal:
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0}", v);
                    break;
                default:
                    sb.Append(SanityConstants.CHAR_STRING_DELIMITER).Append(SanityExpressionTransformer.EscapeString(v.ToString() ?? string.Empty)).Append(SanityConstants.CHAR_STRING_DELIMITER);
                    break;
            }
        }

        sb.Append(SanityConstants.CHAR_CLOSE_BRACKET);
        return sb.ToString();
    }

    internal static object? TryEvaluate(Expression expr)
    {
        return expr is ConstantExpression c ? c.Value : null;
    }

    internal static bool TryGetContainsParts(MethodCallExpression call, out Expression? coll, out Expression? val)
    {
        if (call is { Object: not null, Arguments.Count: 1 })
        {
            coll = call.Object;
            val = call.Arguments[0];
            return true;
        }

        if (call.Object == null && call.Arguments.Count == 2)
        {
            coll = call.Arguments[0];
            val = call.Arguments[1];
            return true;
        }

        coll = null;
        val = null;
        return false;
    }

    internal static bool TryGetLambda(MethodCallExpression e, int index, out LambdaExpression? lambda)
    {
        lambda = null;
        if (e.Arguments.Count <= index) return false;

        var arg = e.Arguments[index];
        while (arg is UnaryExpression { NodeType: ExpressionType.Quote or ExpressionType.Convert } unary)
            arg = unary.Operand;

        if (arg is LambdaExpression l)
        {
            lambda = l;
            return true;
        }

        return false;
    }

    internal static string WrapWithCountGreaterThanZero(string operand)
    {
        return $"{SanityConstants.COUNT}{SanityConstants.OPEN_PAREN}{operand}{SanityConstants.CLOSE_PAREN}{CountGreaterThanZeroPostFix}";
    }
}