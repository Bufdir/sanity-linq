using System.Collections;
using Sanity.Linq.CommonTypes;

namespace Sanity.Linq.QueryProvider;

internal static class SanityExpressionTransformerHelper
{
    internal static string FormatEnumerable(IEnumerable? enumerable, Type type, Func<Expression, Func<MethodCallExpression, string>, Func<BinaryExpression, string>, Func<UnaryExpression, string>, bool, string> transformOperand, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler, bool useCoalesceFallback)
    {
        if (enumerable == null) return SanityConstants.NULL;
        if (typeof(IQueryable).IsAssignableFrom(type) && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SanityDocumentSet<>))
            return SanityConstants.AT;

        var sb = new StringBuilder();
        sb.Append(SanityConstants.CHAR_OPEN_BRACKET);
        var first = true;
        foreach (var item in enumerable)
        {
            if (!first) sb.Append(SanityConstants.CHAR_COMMA);
            first = false;

            switch (item)
            {
                case null:
                    sb.Append(SanityConstants.NULL);
                    break;
                case string s:
                    sb.Append(SanityConstants.CHAR_STRING_DELIMITER).Append(SanityExpressionTransformer.EscapeString(s)).Append(SanityConstants.CHAR_STRING_DELIMITER);
                    break;
                case bool b:
                    sb.Append(b ? SanityConstants.TRUE : SanityConstants.FALSE);
                    break;
                case int or long or double or float or decimal:
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0}", item);
                    break;
                default:
                    sb.Append(SanityConstants.CHAR_STRING_DELIMITER).Append(SanityExpressionTransformer.EscapeString(item.ToString() ?? string.Empty)).Append(SanityConstants.CHAR_STRING_DELIMITER);
                    break;
            }
        }

        sb.Append(SanityConstants.CHAR_CLOSE_BRACKET);
        return sb.ToString();
    }

    internal static bool IsNumericOrBoolType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType == typeof(int) ||
               underlyingType == typeof(long) ||
               underlyingType == typeof(double) ||
               underlyingType == typeof(float) ||
               underlyingType == typeof(short) ||
               underlyingType == typeof(byte) ||
               underlyingType == typeof(decimal) ||
               underlyingType == typeof(bool);
    }

    internal static string HandleMemberExpression(MemberExpression m, Func<Expression, Func<MethodCallExpression, string>, Func<BinaryExpression, string>, Func<UnaryExpression, string>, bool, string> transformOperand, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler, bool useCoalesceFallback)
    {
        var member = m.Member;

        // Skip .Value for Nullable types
        if (member is { Name: "Value", DeclaringType.IsGenericType: true } &&
            member.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>) &&
            m.Expression != null)
            return transformOperand(m.Expression, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);

        // Optimization: if we have SanityReference<T>.Value.Id, we can use coalesce(_ref, _key)
        if (useCoalesceFallback && member.Name == "Id" && m.Expression is MemberExpression { Member: { Name: "Value", DeclaringType.IsGenericType: true } } innerM &&
            innerM.Member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>))
        {
            var refPath = transformOperand(innerM.Expression!, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);
            return refPath == SanityConstants.AT ? $"{SanityConstants.COALESCE}({SanityConstants.REF}, {SanityConstants.KEY})" : $"{SanityConstants.COALESCE}({refPath}{SanityConstants.DOT}{SanityConstants.REF}, {refPath}{SanityConstants.DOT}{SanityConstants.KEY})";
        }

        // General fallback for denormalized properties on SanityReference.Value
        if (useCoalesceFallback && m.Expression is MemberExpression { Member: { Name: "Value", DeclaringType.IsGenericType: true } } innerM2 &&
            innerM2.Member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>))
        {
            var propName = member.GetJsonProperty();
            var refPath = transformOperand(innerM2.Expression!, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);

            return refPath == SanityConstants.AT
                ? $"{SanityConstants.COALESCE}({SanityConstants.AT}{SanityConstants.DEREFERENCING_OPERATOR}{propName}, {propName})"
                : $"{SanityConstants.COALESCE}({refPath}{SanityConstants.DEREFERENCING_OPERATOR}{propName}, {refPath}{SanityConstants.DOT}{propName})";
        }

        string current;
        if (member is { Name: "Value", DeclaringType.IsGenericType: true } &&
            member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>))
            current = m.Expression is ParameterExpression ? SanityConstants.AT + SanityConstants.DEREFERENCING_OPERATOR : SanityConstants.DEREFERENCING_OPERATOR;
        else
            current = member.GetJsonProperty();

        if (m.Expression is not MemberExpression inner) return current;
        var parent = transformOperand(inner, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);

        if (parent == SanityConstants.DEREFERENCING_OPERATOR || current == SanityConstants.DEREFERENCING_OPERATOR || parent.EndsWith(SanityConstants.DEREFERENCING_OPERATOR) || current.StartsWith(SanityConstants.DEREFERENCING_OPERATOR))
            return parent + current;

        return parent + SanityConstants.DOT + current;
    }

    internal static string HandleNewExpression(NewExpression nw, Func<Expression, Func<MethodCallExpression, string>, Func<BinaryExpression, string>, Func<UnaryExpression, string>, bool, string> transformOperand, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler, bool useCoalesceFallback)
    {
        var members = nw.Members;
        if (members == null || nw.Arguments.Count != members.Count)
            throw new Exception("Selections must be anonymous types without a constructor.");

        var sb = new StringBuilder();
        for (var i = 0; i < nw.Arguments.Count; i++)
        {
            if (i > 0) sb.Append(SanityConstants.CHAR_COMMA).Append(SanityConstants.CHAR_SPACE);

            var arg = nw.Arguments[i];
            var propName = members[i].Name.ToCamelCase();

            var transformedArg = arg is NewExpression
                ? SanityConstants.OPEN_BRACE + transformOperand(arg, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback) + SanityConstants.CLOSE_BRACE
                : transformOperand(arg, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);

            if (transformedArg == propName)
                sb.Append(transformedArg);
            else
                sb.Append(SanityConstants.CHAR_STRING_DELIMITER).Append(propName).Append(SanityConstants.CHAR_STRING_DELIMITER)
                    .Append(SanityConstants.CHAR_COLON).Append(SanityConstants.CHAR_SPACE).Append(transformedArg);
        }

        return sb.ToString();
    }
}