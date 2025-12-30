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
            ConstantExpression { Value: null } => "null",
            ConstantExpression c when c.Type == typeof(string) => $"\"{EscapeString(c.Value?.ToString() ?? "")}\"",
            ConstantExpression c when IsNumericOrBoolType(c.Type) => string.Format(CultureInfo.InvariantCulture, "{0}", c.Value).ToLower(),
            ConstantExpression { Value: DateTime dt } => dt == dt.Date
                ? $"\"{dt:yyyy-MM-dd}\""
                : $"\"{dt:O}\"",
            ConstantExpression { Value: DateTimeOffset dto } => $"\"{dto:O}\"",
            ConstantExpression c when c.Type == typeof(Guid) => $"\"{EscapeString(c.Value?.ToString() ?? "")}\"",
            ConstantExpression c when c.Type.IsArray || (c.Type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(c.Type)) => FormatEnumerable(c.Value as IEnumerable, c.Type),
            ConstantExpression c => c.Value?.ToString() ?? "null",
            ParameterExpression => "@",
            NewArrayExpression na => "[" + string.Join(",", na.Expressions.Select(expr => TransformOperand(expr, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback))) + "]",
            _ => throw new Exception($"Operands of type {e.GetType()} and nodeType {e.NodeType} not supported. ")
        };
    }

    private static string FormatEnumerable(IEnumerable? enumerable, Type type)
    {
        if (enumerable == null) return "null";
        if (typeof(IQueryable).IsAssignableFrom(type) && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SanityDocumentSet<>))
            // This is a subquery. If we are here, Evaluator.PartialEval decided it's NOT evaluatable locally (correct).
            // But somehow it reached here as a ConstantExpression? That would be strange.
            // If it's a subquery, we should probably return its GROQ.
            // But for now, let's just avoid the crash.
            return "@";

        var items = new List<string>();
        foreach (var item in enumerable)
            switch (item)
            {
                case null:
                    items.Add("null");
                    break;
                case string s:
                    items.Add($"\"{EscapeString(s)}\"");
                    break;
                case bool b:
                    items.Add(b.ToString().ToLower());
                    break;
                case int or long or double or float or decimal:
                    items.Add(string.Format(CultureInfo.InvariantCulture, "{0}", item));
                    break;
                default:
                    items.Add($"\"{EscapeString(item.ToString() ?? "")}\"");
                    break;
            }

        return "[" + string.Join(",", items) + "]";
    }

    public static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
            return refPath == "@" ? "coalesce(_ref, _key)" : $"coalesce({refPath}._ref, {refPath}._key)";
        }

        // General fallback for denormalized properties on SanityReference.Value
        if (useCoalesceFallback && m.Expression is MemberExpression { Member: { Name: "Value", DeclaringType.IsGenericType: true } } innerM2 &&
            innerM2.Member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>))
        {
            var propName = member.GetJsonProperty();
            var refPath = TransformOperand(innerM2.Expression!, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);

            return refPath == "@"
                ? $"coalesce(@->{propName}, {propName})"
                : $"coalesce({refPath}->{propName}, {refPath}.{propName})";
        }

        var memberPath = new List<string>();

        if (member is { Name: "Value", DeclaringType.IsGenericType: true } &&
            member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>))
        {
            memberPath.Add(m.Expression is ParameterExpression ? "@->" : "->");
        }
        else
        {
            memberPath.Add(member.GetJsonProperty());
        }

        if (m.Expression is MemberExpression inner)
            memberPath.Add(TransformOperand(inner, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler));

        return memberPath
            .Aggregate((a1, a2) => a1 != "->" && a2 != "->" ? $"{a2}.{a1}" : $"{a2}{a1}")
            .Replace(".->", "->")
            .Replace("->.", "->");
    }

    private static string HandleNewExpression(NewExpression nw, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler, bool useCoalesceFallback)
    {
        var args = nw.Arguments
            .Select(arg => arg is NewExpression ? "{" + TransformOperand(arg, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback) + "}" : TransformOperand(arg, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback))
            .ToArray();
        var props = (nw.Members ?? Enumerable.Empty<MemberInfo>())
            .Select(prop => prop.Name.ToCamelCase())
            .ToArray();

        if (args.Length != props.Length)
            throw new Exception("Selections must be anonymous types without a constructor.");

        var projection = args
            .Select((t, i) => t.Equals(props[i]) ? t : $"\"{props[i]}\": {t}")
            .ToList();

        return string.Join(", ", projection);
    }

    public static string TransformUnaryExpression(UnaryExpression u, Func<Expression, string> operandTransformer)
    {
        return u.NodeType switch
        {
            ExpressionType.Not => "!(" + operandTransformer(u.Operand) + ")",
            ExpressionType.Convert => operandTransformer(u.Operand),
            _ => throw new Exception($"Unary expression of type {u.GetType()} and nodeType {u.NodeType} not supported. ")
        };
    }

    
}