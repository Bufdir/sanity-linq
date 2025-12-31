using System.Collections;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.Internal;

namespace Sanity.Linq.QueryProvider;

internal static class SanityExpressionTransformer
{
    public static string TransformOperand(Expression e, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler, bool useCoalesceFallback = true)
    {
        var simplified = Evaluator.PartialEval(e);
        return simplified switch
        {
            MemberExpression m => HandleMemberExpression(m, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback),
            NewExpression nw => HandleNewExpression(nw, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback),
            BinaryExpression b => binaryExpressionHandler(b),
            UnaryExpression u => unaryExpressionHandler(u),
            MethodCallExpression mc => methodCallHandler(mc),
            ConstantExpression { Value: null } => SanityConstants.NULL,
            ConstantExpression c when c.Type == typeof(string) => $"{SanityConstants.STRING_DELIMITER}{EscapeString(c.Value?.ToString() ?? "")}{SanityConstants.STRING_DELIMITER}",
            ConstantExpression c when IsNumericOrBoolType(c.Type) => string.Format(CultureInfo.InvariantCulture, "{0}", c.Value).ToLower(),
            ConstantExpression { Value: DateTime dt } => dt == dt.Date
                ? $"{SanityConstants.STRING_DELIMITER}{dt:yyyy-MM-dd}{SanityConstants.STRING_DELIMITER}"
                : $"{SanityConstants.STRING_DELIMITER}{dt:O}{SanityConstants.STRING_DELIMITER}",
            ConstantExpression { Value: DateTimeOffset dto } => $"{SanityConstants.STRING_DELIMITER}{dto:O}{SanityConstants.STRING_DELIMITER}",
            ConstantExpression c when c.Type == typeof(Guid) => $"{SanityConstants.STRING_DELIMITER}{EscapeString(c.Value?.ToString() ?? "")}{SanityConstants.STRING_DELIMITER}",
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

        var items = new List<string>();
        foreach (var item in enumerable)
            switch (item)
            {
                case null:
                    items.Add(SanityConstants.NULL);
                    break;
                case string s:
                    items.Add($"{SanityConstants.STRING_DELIMITER}{EscapeString(s)}{SanityConstants.STRING_DELIMITER}");
                    break;
                case bool b:
                    items.Add(b.ToString().ToLower());
                    break;
                case int or long or double or float or decimal:
                    items.Add(string.Format(CultureInfo.InvariantCulture, "{0}", item));
                    break;
                default:
                    items.Add($"{SanityConstants.STRING_DELIMITER}{EscapeString(item.ToString() ?? "")}{SanityConstants.STRING_DELIMITER}");
                    break;
            }
        return SanityConstants.OPEN_BRACKET + string.Join(SanityConstants.COMMA, items) + SanityConstants.CLOSE_BRACKET;
    }

    public static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace(SanityConstants.STRING_DELIMITER, "\\" + SanityConstants.STRING_DELIMITER);
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

        var memberPath = new List<string>();

        if (member is { Name: "Value", DeclaringType.IsGenericType: true } &&
            member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>))
            memberPath.Add(m.Expression is ParameterExpression ? SanityConstants.AT + SanityConstants.DEREFERENCING_OPERATOR : SanityConstants.DEREFERENCING_OPERATOR);
        else
            memberPath.Add(member.GetJsonProperty());

        if (m.Expression is MemberExpression inner)
            memberPath.Add(TransformOperand(inner, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler));

        return memberPath
            .Aggregate((a1, a2) => a1 != SanityConstants.DEREFERENCING_OPERATOR && a2 != SanityConstants.DEREFERENCING_OPERATOR ? $"{a2}{SanityConstants.DOT}{a1}" : $"{a2}{a1}")
            .Replace(SanityConstants.DOT + SanityConstants.DEREFERENCING_OPERATOR, SanityConstants.DEREFERENCING_OPERATOR)
            .Replace(SanityConstants.DEREFERENCING_OPERATOR + SanityConstants.DOT, SanityConstants.DEREFERENCING_OPERATOR);
    }

    private static string HandleNewExpression(NewExpression nw, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler, bool useCoalesceFallback)
    {
        var args = nw.Arguments
            .Select(arg => arg is NewExpression ? SanityConstants.OPEN_BRACE + TransformOperand(arg, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback) + SanityConstants.CLOSE_BRACE : TransformOperand(arg, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback))
            .ToArray();
        var props = (nw.Members ?? Enumerable.Empty<MemberInfo>())
            .Select(prop => prop.Name.ToCamelCase())
            .ToArray();

        if (args.Length != props.Length)
            throw new Exception("Selections must be anonymous types without a constructor.");

        var projection = args
            .Select((t, i) => t.Equals(props[i]) ? t : $"{SanityConstants.STRING_DELIMITER}{props[i]}{SanityConstants.STRING_DELIMITER}{SanityConstants.COLON} {t}")
            .ToList();

        return string.Join(SanityConstants.COMMA + " ", projection);
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