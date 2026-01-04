using System.Collections;
using Sanity.Linq.CommonTypes;

namespace Sanity.Linq.QueryProvider;

internal static class SanityExpressionTransformerHelper
{
    /// <summary>
    ///     Formats an enumerable collection into a string representation suitable for use in Sanity queries.
    /// </summary>
    /// <param name="enumerable">The enumerable collection to format. Can be <c>null</c>.</param>
    /// <param name="type">The type of the enumerable collection.</param>
    /// <param name="transformOperand">
    ///     A function to transform an <see cref="Expression" /> into a string representation,
    ///     using handlers for method calls, binary expressions, unary expressions, and a coalesce fallback option.
    /// </param>
    /// <param name="methodCallHandler">A function to handle formatting of <see cref="MethodCallExpression" /> instances.</param>
    /// <param name="binaryExpressionHandler">A function to handle formatting of <see cref="BinaryExpression" /> instances.</param>
    /// <param name="unaryExpressionHandler">A function to handle formatting of <see cref="UnaryExpression" /> instances.</param>
    /// <param name="useCoalesceFallback">
    ///     A boolean value indicating whether to use a coalesce fallback when transforming operands.
    /// </param>
    /// <returns>
    ///     A string representation of the enumerable collection. If the enumerable is <c>null</c>,
    ///     the method returns a constant representing <c>null</c>. If the enumerable is of type
    ///     <see cref="SanityDocumentSet{TDoc}" />, it returns a constant representing a placeholder.
    /// </returns>
    /// <remarks>
    ///     The method handles various types of items within the enumerable, including strings,
    ///     booleans, numeric types, and other objects. Special characters are escaped as needed.
    /// </remarks>
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

    /// <summary>
    ///     Handles the transformation of a <see cref="MemberExpression" /> into its corresponding string representation
    ///     within the context of a Sanity query.
    /// </summary>
    /// <param name="m">The <see cref="MemberExpression" /> to be transformed.</param>
    /// <param name="transformOperand">
    ///     A function to recursively transform operands, taking an <see cref="Expression" /> and handlers for
    ///     <see cref="MethodCallExpression" />, <see cref="BinaryExpression" />, and <see cref="UnaryExpression" />,
    ///     as well as a flag indicating whether to use a coalesce fallback.
    /// </param>
    /// <param name="methodCallHandler">A function to handle <see cref="MethodCallExpression" /> transformations.</param>
    /// <param name="binaryExpressionHandler">A function to handle <see cref="BinaryExpression" /> transformations.</param>
    /// <param name="unaryExpressionHandler">A function to handle <see cref="UnaryExpression" /> transformations.</param>
    /// <param name="useCoalesceFallback">
    ///     A boolean flag indicating whether to apply a coalesce fallback for certain expressions.
    /// </param>
    /// <returns>
    ///     A string representation of the transformed <see cref="MemberExpression" /> suitable for use in a Sanity query.
    /// </returns>
    /// <remarks>
    ///     This method includes optimizations for handling nullable types and specific patterns involving
    ///     <c>SanityReference&lt;T&gt;</c>. It ensures that the resulting string adheres to the expected query format.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if any of the required parameters are null.
    /// </exception>
    internal static string HandleMemberExpression(MemberExpression m, Func<Expression, Func<MethodCallExpression, string>, Func<BinaryExpression, string>, Func<UnaryExpression, string>, bool, string> transformOperand, Func<MethodCallExpression, string> methodCallHandler, Func<BinaryExpression, string> binaryExpressionHandler, Func<UnaryExpression, string> unaryExpressionHandler, bool useCoalesceFallback)
    {
        var member = m.Member;

        // Skip .Value for Nullable types
        if (member is { Name: "Value", DeclaringType.IsGenericType: true } &&
            member.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>) &&
            m.Expression != null)
            return transformOperand(m.Expression, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);

        switch (useCoalesceFallback)
        {
            // Optimization: if we have SanityReference<T>.Value.Id, we can use coalesce(_ref, _key)
            case true when member.Name == "Id" && m.Expression is MemberExpression { Member: { Name: "Value", DeclaringType.IsGenericType: true } } innerM && innerM.Member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>):
            {
                var refPath = transformOperand(innerM.Expression!, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);
                return refPath == SanityConstants.AT ? $"{SanityConstants.COALESCE}({SanityConstants.REF}, {SanityConstants.KEY})" : $"{SanityConstants.COALESCE}({refPath}{SanityConstants.DOT}{SanityConstants.REF}, {refPath}{SanityConstants.DOT}{SanityConstants.KEY})";
            }
            // General fallback for denormalized properties on SanityReference.Value
            case true when m.Expression is MemberExpression { Member: { Name: "Value", DeclaringType.IsGenericType: true } } innerM2 && innerM2.Member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>):
            {
                var propName = member.GetJsonProperty();
                var refPath = transformOperand(innerM2.Expression!, methodCallHandler, binaryExpressionHandler, unaryExpressionHandler, useCoalesceFallback);

                return refPath == SanityConstants.AT
                    ? $"{SanityConstants.COALESCE}({SanityConstants.AT}{SanityConstants.DEREFERENCING_OPERATOR}{propName}, {propName})"
                    : $"{SanityConstants.COALESCE}({refPath}{SanityConstants.DEREFERENCING_OPERATOR}{propName}, {refPath}{SanityConstants.DOT}{propName})";
            }
        }

        string current;
        if (member is { Name: "Value", DeclaringType.IsGenericType: true } && member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>))
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