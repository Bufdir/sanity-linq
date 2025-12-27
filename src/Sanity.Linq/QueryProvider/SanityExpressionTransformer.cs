using Sanity.Linq.CommonTypes;

namespace Sanity.Linq.QueryProvider;

internal static class SanityExpressionTransformer
{
    public static string TransformOperand(Expression e, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler)
    {
        return e switch
        {
            MemberExpression m => HandleMemberExpression(m, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler),
            NewExpression nw => HandleNewExpression(nw, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler),
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
            ConstantExpression c => c.Value?.ToString() ?? "null",
            ParameterExpression => "@",
            NewArrayExpression na => "[" + string.Join(",", na.Expressions.Select(expr => TransformOperand(expr, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler))) + "]",
            _ => throw new Exception($"Operands of type {e.GetType()} and nodeType {e.NodeType} not supported. ")
        };
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

    private static string HandleMemberExpression(MemberExpression m, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler)
    {
        var member = m.Member;

        // Skip .Value for Nullable types
        if (member is { Name: "Value", DeclaringType.IsGenericType: true } &&
            member.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>) &&
            m.Expression != null)
        {
            return TransformOperand(m.Expression, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler);
        }

        var memberPath = new List<string>();

        if (member is { Name: "Value", DeclaringType.IsGenericType: true } &&
            member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>))
        {
            memberPath.Add("->");
        }
        else
        {
            var jsonProperty = member.GetCustomAttributes(typeof(JsonPropertyAttribute), true)
                .Cast<JsonPropertyAttribute>().FirstOrDefault();
            memberPath.Add(jsonProperty?.PropertyName ?? member.Name.ToCamelCase());
        }

        if (m.Expression is MemberExpression inner) memberPath.Add(TransformOperand(inner, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler));

        return memberPath
            .Aggregate((a1, a2) => a1 != "->" && a2 != "->" ? $"{a2}.{a1}" : $"{a2}{a1}")
            .Replace(".->", "->")
            .Replace("->.", "->");
    }

    private static string HandleNewExpression(NewExpression nw, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler)
    {
        var args = nw.Arguments
            .Select(arg => arg is NewExpression ? "{" + TransformOperand(arg, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler) + "}" : TransformOperand(arg, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler))
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