using System.Collections;
using Sanity.Linq.CommonTypes;

namespace Sanity.Linq.QueryProvider;

internal static class SanityExpressionTransformer
{
    public static string TransformOperand(Expression e, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler, bool useCoalesceFallback = true)
    {
        return e switch
        {
            MemberExpression m => HandleMemberExpression(m, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback),
            NewExpression nw => HandleNewExpression(nw, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback),
            BinaryExpression b => binaryExpressionHandler(b),
            UnaryExpression u => unaryExpressionHandler(u),
            MethodCallExpression mc => methodCallHandler(mc),
            ConstantExpression { Value: null } => SanityConstants.NULL,
            ConstantExpression c when c.Type == typeof(string) => $"{SanityConstants.STRING_DELIMITER}{EscapeString(c.Value?.ToString() ?? string.Empty)}{SanityConstants.STRING_DELIMITER}",
            ConstantExpression c when IsNumericOrBoolType(c.Type) => string.Format(CultureInfo.InvariantCulture, "{0}", c.Value).ToLower(),
            ConstantExpression { Value: DateTime dt } => dt == dt.Date
                ? $"{SanityConstants.STRING_DELIMITER}{dt:yyyy-MM-dd}{SanityConstants.STRING_DELIMITER}"
                : $"{SanityConstants.STRING_DELIMITER}{dt:O}{SanityConstants.STRING_DELIMITER}",
            ConstantExpression { Value: DateTimeOffset dto } => $"{SanityConstants.STRING_DELIMITER}{dto:O}{SanityConstants.STRING_DELIMITER}",
            ConstantExpression c when c.Type == typeof(Guid) => $"{SanityConstants.STRING_DELIMITER}{EscapeString(c.Value?.ToString() ?? string.Empty)}{SanityConstants.STRING_DELIMITER}",
            ConstantExpression c when c.Type.IsArray || (c.Type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(c.Type)) => FormatEnumerable(c.Value as IEnumerable, c.Type),
            ConstantExpression c => c.Value?.ToString() ?? SanityConstants.NULL,
            ParameterExpression => SanityConstants.AT,
            NewArrayExpression na => SanityConstants.OPEN_BRACKET + string.Join(SanityConstants.COMMA, na.Expressions.Select(expr => TransformOperand(expr, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback))) + SanityConstants.CLOSE_BRACKET,
            _ => throw new Exception($"Operands of type {e.GetType()} and nodeType {e.NodeType} not supported. ")
        };
    }

    private static string FormatEnumerable(IEnumerable? enumerable, Type type)
    {
        if (enumerable == null) return SanityConstants.NULL;
        if (typeof(IQueryable).IsAssignableFrom(type) && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SanityDocumentSet<>))
            // This is a subquery. If we are here, Evaluator.PartialEval decided it's NOT evaluatable locally (correct).
            // But somehow it reached here as a ConstantExpression? That would be strange.
            // If it's a subquery, we should probably return its GROQ.
            // But for now, let's just avoid the crash.
            return SanityConstants.AT;

        var sb = new StringBuilder();
        sb.Append(SanityConstants.OPEN_BRACKET);
        var first = true;
        foreach (var item in enumerable)
        {
            if (!first) sb.Append(SanityConstants.COMMA);
            first = false;

            switch (item)
            {
                case null:
                    sb.Append(SanityConstants.NULL);
                    break;
                case string s:
                    sb.Append(SanityConstants.STRING_DELIMITER).Append(EscapeString(s)).Append(SanityConstants.STRING_DELIMITER);
                    break;
                case bool b:
                    sb.Append(b ? SanityConstants.TRUE : SanityConstants.FALSE);
                    break;
                case int or long or double or float or decimal:
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0}", item);
                    break;
                default:
                    sb.Append(SanityConstants.STRING_DELIMITER).Append(EscapeString(item.ToString() ?? string.Empty)).Append(SanityConstants.STRING_DELIMITER);
                    break;
            }
        }

        sb.Append(SanityConstants.CLOSE_BRACKET);
        return sb.ToString();
    }

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

    private static bool IsNumericOrBoolType(Type type)
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

    private static string HandleMemberExpression(MemberExpression m, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler, bool useCoalesceFallback)
    {
        var member = m.Member;

        // Skip .Value for Nullable types
        if (member is { Name: "Value", DeclaringType.IsGenericType: true } &&
            member.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>) &&
            m.Expression != null)
            return TransformOperand(m.Expression, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);

        // Optimization: if we have SanityReference<T>.Value.Id, we can use coalesce(_ref, _key)
        if (member.Name == "Id" && m.Expression is MemberExpression { Member: { Name: "Value", DeclaringType.IsGenericType: true } } innerM &&
            innerM.Member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>))
        {
            var refPath = TransformOperand(innerM.Expression!, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);
            return refPath == SanityConstants.AT ? $"{SanityConstants.COALESCE}({SanityConstants.REF}, {SanityConstants.KEY})" : $"{SanityConstants.COALESCE}({refPath}{SanityConstants.DOT}{SanityConstants.REF}, {refPath}{SanityConstants.DOT}{SanityConstants.KEY})";
        }

        // General fallback for denormalized properties on SanityReference.Value
        if (useCoalesceFallback && m.Expression is MemberExpression { Member: { Name: "Value", DeclaringType.IsGenericType: true } } innerM2 &&
            innerM2.Member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>))
        {
            var propName = member.GetJsonProperty();
            var refPath = TransformOperand(innerM2.Expression!, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);

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
        var parent = TransformOperand(inner, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler);

        if (parent == SanityConstants.DEREFERENCING_OPERATOR || current == SanityConstants.DEREFERENCING_OPERATOR || parent.EndsWith(SanityConstants.DEREFERENCING_OPERATOR) || current.StartsWith(SanityConstants.DEREFERENCING_OPERATOR))
            return parent + current;

        return parent + SanityConstants.DOT + current;
    }

    private static string HandleNewExpression(NewExpression nw, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler, bool useCoalesceFallback)
    {
        var members = nw.Members;
        if (members == null || nw.Arguments.Count != members.Count)
            throw new Exception("Selections must be anonymous types without a constructor.");

        var sb = new StringBuilder();
        for (var i = 0; i < nw.Arguments.Count; i++)
        {
            if (i > 0) sb.Append(SanityConstants.COMMA).Append(SanityConstants.SPACE);

            var arg = nw.Arguments[i];
            var propName = members[i].Name.ToCamelCase();

            var transformedArg = arg is NewExpression
                ? SanityConstants.OPEN_BRACE + TransformOperand(arg, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback) + SanityConstants.CLOSE_BRACE
                : TransformOperand(arg, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);

            if (transformedArg == propName)
                sb.Append(transformedArg);
            else
                sb.Append(SanityConstants.STRING_DELIMITER).Append(propName).Append(SanityConstants.STRING_DELIMITER)
                    .Append(SanityConstants.COLON).Append(SanityConstants.SPACE).Append(transformedArg);
        }

        return sb.ToString();
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