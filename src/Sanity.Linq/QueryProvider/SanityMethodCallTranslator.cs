using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.Internal;

namespace Sanity.Linq.QueryProvider;

internal class SanityMethodCallTranslator(
    SanityQueryBuilder queryBuilder,
    Func<Expression, string> transformOperand,
    Func<Expression, Expression?> visit,
    bool isTopLevel = false)
{
    private static string CountGreaterThanZeroPostFix => $"{SanityConstants.SPACE}{SanityConstants.GREATER_THAN}{SanityConstants.SPACE}0";

    private string TransformMethodCallExpression(MethodCallExpression e)
    {
        switch (e.Method.Name)
        {
            case "StartsWith":
                return HandleStartsWith(e);

            case "Contains":
                return HandleContains(e);

            case "GetValue`1":
            case "GetValue":
                return HandleGetValue(e);

            case "Where":
                return HandleWhere(e);

            case "Select":
            case "SelectMany":
                return HandleSelect(e);

            case "Include":
                return HandleInclude(e);

            case "OfType":
                return HandleOfType(e);

            case "IsNullOrEmpty":
                return HandleIsNullOrEmpty(e);

            case "_id":
            case "SanityId":
                return SanityConstants.ID;

            case "_createdAt":
            case "SanityCreatedAt":
                return SanityConstants.CREATED_AT;

            case "_updatedAt":
            case "SanityUpdatedAt":
                return SanityConstants.UPDATED_AT;

            case "_rev":
            case "SanityRevision":
                return SanityConstants.REVISION;

            case "_type":
            case "SanityType":
                return SanityConstants.TYPE;

            case "IsDefined":
                return HandleIsDefined(e);

            case "IsDraft":
                return $"{SanityConstants.ID} {SanityConstants.IN} {SanityConstants.PATH}({SanityConstants.STRING_DELIMITER}{SanityConstants.DRAFTS_PATH}{SanityConstants.STRING_DELIMITER})";

            case "Cast":
                visit(e.Arguments[0]);
                return string.Empty;

            case "OrderBy":
            case "ThenBy":
                return HandleOrdering(e, false);

            case "OrderByDescending":
            case "ThenByDescending":
                return HandleOrdering(e, true);

            case "Count":
            case "LongCount":
                return HandleCount(e);

            case "Max":
                return HandleMaxMin(e, SanityConstants.MAX);

            case "Min":
                return HandleMaxMin(e, SanityConstants.MIN);

            case "Take":
                return HandleTake(e);

            case "Skip":
                return HandleSkip(e);

            case "op_Implicit":
                return HandleImplicit(e);

            case "Any":
                return HandleAny(e);

            default:
                throw new Exception($"Method call {e.Method.Name} not supported.");
        }
    }

    public string Translate(MethodCallExpression e)
    {
        return TransformMethodCallExpression(e);
    }

    private string HandleWhere(MethodCallExpression e)
    {
        VisitSourceIfTopLevel(e);

        if (!TryGetLambda(e, 1, out var lambda)) return transformOperand(e.Arguments[0]);

        var filter = transformOperand(lambda!.Body);

        if (isTopLevel && !queryBuilder.IsSilent)
        {
            if (!string.IsNullOrEmpty(queryBuilder.Projection))
                queryBuilder.AddPostFilter(filter);
            else
                queryBuilder.AddConstraint(filter);

            return transformOperand(e.Arguments[0]);
        }

        var operand = transformOperand(e.Arguments[0]);
        return $"{operand}[{filter}]";
    }

    private string HandleSelect(MethodCallExpression e)
    {
        VisitSourceIfTopLevel(e);

        if (!TryGetLambda(e, 1, out var lambda)) return transformOperand(e.Arguments[0]);

        if (lambda!.Body is MemberExpression m && (m.Type.IsPrimitive || m.Type == typeof(string)))
            throw new Exception($"Selecting '{m.Member.Name}' as a scalar value is not supported due to serialization limitations. Instead, create an anonymous object containing the '{m.Member.Name}' field. e.g. o => new {{ o.{m.Member.Name} }}.");

        if (!isTopLevel || queryBuilder.IsSilent) return transformOperand(e.Arguments[0]);

        var projection = transformOperand(lambda.Body);
        if (lambda.Body is NewExpression)
            projection = $"{SanityConstants.OPEN_BRACE}{projection}{SanityConstants.CLOSE_BRACE}";

        var isComplex = lambda.Body is MemberExpression && !lambda.Body.Type.IsPrimitive && lambda.Body.Type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(lambda.Body.Type);

        queryBuilder.AddProjection(projection);
        if (isComplex) queryBuilder.FlattenProjection = true;

        return transformOperand(e.Arguments[0]);
    }

    private string HandleInclude(MethodCallExpression e)
    {
        VisitSourceIfTopLevel(e);
        if (!TryGetLambda(e, 1, out var lambda)) throw new Exception("Include method second argument must be a lambda expression.");

        var (body, selectors) = ExtractSelectors(lambda!.Body);

        var wasSilent = queryBuilder.IsSilent;
        var wasFallback = queryBuilder.UseCoalesceFallback;
        queryBuilder.IsSilent = true;
        queryBuilder.UseCoalesceFallback = false;
        try
        {
            var fieldPath = transformOperand(body).TrimStart('@').TrimStart('.');
            var sourceName = GetIncludeSourceName(e, fieldPath);
            var includePath = AddInclude(fieldPath, body.Type, sourceName);

            selectors.Reverse();
            ProcessIncludeSelectors(selectors, includePath, body.Type);
        }
        finally
        {
            queryBuilder.IsSilent = wasSilent;
            queryBuilder.UseCoalesceFallback = wasFallback;
        }

        return string.Empty;
    }

    private void ProcessIncludeSelectors(List<Expression> selectors, string includePath, Type currentType)
    {
        var currentIncludePath = includePath;
        foreach (var selectorExpr in selectors)
            if (selectorExpr is MethodCallExpression { Method.Name: "OfType" } mc)
                currentIncludePath = HandleIncludeOfType(mc, currentIncludePath, ref currentType);
            else
                currentIncludePath = HandleIncludePath(selectorExpr, currentIncludePath, out currentType);
    }

    private string HandleIncludeOfType(MethodCallExpression mc, string currentIncludePath, ref Type currentType)
    {
        var targetType = mc.Method.GetGenericArguments()[0];
        var currentElementType = TypeSystem.GetElementType(currentType);

        if (currentType != targetType && currentElementType != targetType)
        {
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(SanityReference<>))
            {
                currentIncludePath += $"{SanityConstants.OPEN_BRACKET}{SanityConstants.TYPE} {SanityConstants.EQUALS} {SanityConstants.STRING_DELIMITER}{SanityConstants.REFERENCE}{SanityConstants.STRING_DELIMITER}{SanityConstants.CLOSE_BRACKET}";
            }
            else
            {
                var sanityType = targetType.GetSanityTypeName();
                currentIncludePath += $"{SanityConstants.OPEN_BRACKET}{SanityConstants.TYPE} {SanityConstants.EQUALS} {SanityConstants.STRING_DELIMITER}{sanityType}{SanityConstants.STRING_DELIMITER}{SanityConstants.CLOSE_BRACKET}";
            }
        }

        AddInclude(currentIncludePath, mc.Type, null);
        currentType = targetType;
        return currentIncludePath;
    }

    private string HandleIncludePath(Expression selectorExpr, string currentIncludePath, out Type currentType)
    {
        var selector = (LambdaExpression)(selectorExpr is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : selectorExpr);
        var rawPath = transformOperand(selector.Body);
        var selectorPath = rawPath.Replace(SanityConstants.DEREFERENCING_OPERATOR, string.Empty).Replace(SanityConstants.AT, string.Empty).Replace(SanityConstants.ARRAY_INDICATOR, string.Empty).Trim(SanityConstants.DOT[0]);
        if (string.IsNullOrEmpty(selectorPath))
        {
            if (rawPath.Contains(SanityConstants.DEREFERENCING_OPERATOR))
            {
                selectorPath = SanityConstants.DEREFERENCING_OPERATOR;
            }
            else
            {
                // Update the currentType but don't add redundant include for the same path
                currentType = selector.Body.Type;
                return currentIncludePath;
            }
        }

        currentIncludePath = selectorPath.StartsWith(SanityConstants.OPEN_BRACKET) ? $"{currentIncludePath}{selectorPath}" : $"{currentIncludePath}{SanityConstants.DOT}{selectorPath}".TrimEnd(SanityConstants.DOT[0]);
        AddInclude(currentIncludePath, selector.Body.Type, null);
        currentType = selector.Body.Type;
        return currentIncludePath;
    }


    private static (Expression body, List<Expression> selectors) ExtractSelectors(Expression body)
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

    private string AddInclude(string fieldPath, Type propertyType, string? sourceName)
    {
        if (queryBuilder.Includes.ContainsKey(fieldPath)) return fieldPath;

        var targetName = fieldPath.Split('.').Last();
        var actualSourceName = sourceName ?? targetName;
        var includeValue = SanityQueryBuilder.GetJoinProjection(actualSourceName, targetName, propertyType, 0, 0, true);
        queryBuilder.Includes.Add(fieldPath, includeValue);
        return fieldPath;
    }

    private static string GetIncludeSourceName(MethodCallExpression e, string targetName)
    {
        if (e.Arguments is [_, _, ConstantExpression { Value: string s }, ..]) return s;

        return targetName.Split('.').Last();
    }

    private string HandleOfType(MethodCallExpression e)
    {
        visit(e.Arguments[0]);
        var operand = transformOperand(e.Arguments[0]);
        var targetType = e.Method.GetGenericArguments()[0];

        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(SanityReference<>))
            // Only include items that are actual references.
            // We include anything with _type == "reference" even if _ref is missing,
            // as some datasets use _key as the identifier for inline-expanded references.
            return $"{operand}{SanityConstants.OPEN_BRACKET}{SanityConstants.TYPE} {SanityConstants.EQUALS} {SanityConstants.STRING_DELIMITER}{SanityConstants.REFERENCE}{SanityConstants.STRING_DELIMITER}{SanityConstants.CLOSE_BRACKET}";

        var sanityType = targetType.GetSanityTypeName();
        return $"{operand}{SanityConstants.OPEN_BRACKET}{SanityConstants.TYPE} {SanityConstants.EQUALS} {SanityConstants.STRING_DELIMITER}{sanityType}{SanityConstants.STRING_DELIMITER}{SanityConstants.CLOSE_BRACKET}";
    }

    private static string HandleGetValue(MethodCallExpression e)
    {
        if (e.Arguments.Count <= 0) throw new Exception("Could not evaluate GetValue method");

        if (e.Arguments[1] is not ConstantExpression c || c.Type != typeof(string)) throw new Exception("Could not evaluate GetValue method");

        var fieldName = c.Value?.ToString() ?? string.Empty;
        return $"{fieldName}";
    }

    private string HandleContains(MethodCallExpression e)
    {
        if (!TryGetContainsParts(e, out var collectionExpr, out var valueExpr) || collectionExpr == null || valueExpr == null)
            return HandleContainsLegacy(e);

        if (HandleEnumerableContains(collectionExpr, valueExpr) is { } enumerableResult) return enumerableResult;
        if (HandlePropertyContainsConstant(collectionExpr, valueExpr) is { } constantResult) return constantResult;
        if (HandlePropertyContainsProperty(collectionExpr, valueExpr) is { } propertyResult) return propertyResult;

        var leftAny = transformOperand(collectionExpr);
        var rightAny = transformOperand(valueExpr);

        if (string.IsNullOrEmpty(leftAny) || string.IsNullOrEmpty(rightAny))
            throw new Exception("'Contains' is only supported for simple expressions with non-null values.");

        if (leftAny == SanityConstants.AT) return $"{rightAny} {SanityConstants.IN} {SanityConstants.AT}";
        return $"{rightAny} {SanityConstants.IN} {leftAny}";
    }

    private string? HandleEnumerableContains(Expression collectionExpr, Expression valueExpr)
    {
        var eval = TryEvaluate(collectionExpr);
        if (eval is not (IEnumerable ie and not string)) return null;

        var memberName = transformOperand(valueExpr);
        var values = JoinValues(ie);
        if (values == SanityConstants.OPEN_BRACKET + SanityConstants.CLOSE_BRACKET) return SanityConstants.FALSE;

        // If the memberName is @->id or something similar, it means we are checking a reference's ID
        // In GROQ, "refID in [ids]" is more efficient than "count([ids][@ == refID]) > 0"
        return $"{memberName} {SanityConstants.IN} {values}";
    }

    private string? HandlePropertyContainsConstant(Expression collectionExpr, Expression valueExpr)
    {
        if (collectionExpr is not MemberExpression) return null;

        if (valueExpr is not ConstantExpression { Value: not null } c2) return null;

        var memberName = transformOperand(collectionExpr);
        var valStr = c2.Value.ToString() ?? string.Empty;
        if (c2.Type != typeof(string) && c2.Type != typeof(Guid)) return $"{valStr} {SanityConstants.IN} {memberName}";

        valStr = SanityExpressionTransformer.EscapeString(valStr);
        return $"{SanityConstants.STRING_DELIMITER}{valStr}{SanityConstants.STRING_DELIMITER} {SanityConstants.IN} {memberName}";
    }

    private string? HandlePropertyContainsProperty(Expression collectionExpr, Expression valueExpr)
    {
        var collUnwrapped = collectionExpr is UnaryExpression { NodeType: ExpressionType.Convert } uc ? uc.Operand : collectionExpr;
        var valUnwrapped = valueExpr is UnaryExpression { NodeType: ExpressionType.Convert } uv ? uv.Operand : valueExpr;

        if (collUnwrapped is MemberExpression && valUnwrapped is MemberExpression)
        {
            var left = transformOperand(collUnwrapped);
            var right = transformOperand(valUnwrapped);
            return $"{right} {SanityConstants.IN} {left}";
        }

        return null;
    }

    private static object? TryEvaluate(Expression expr)
    {
        return expr is ConstantExpression c ? c.Value : null;
    }

    private static bool TryGetContainsParts(MethodCallExpression call, out Expression? coll, out Expression? val)
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

    private static string JoinValues(IEnumerable values)
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
                    sb.Append(SanityConstants.STRING_DELIMITER).Append(SanityExpressionTransformer.EscapeString(s)).Append(SanityConstants.STRING_DELIMITER);
                    break;
                case Guid g:
                    sb.Append(SanityConstants.STRING_DELIMITER).Append(g).Append(SanityConstants.STRING_DELIMITER);
                    break;
                case bool b:
                    sb.Append(b ? SanityConstants.TRUE : SanityConstants.FALSE);
                    break;
                case int or long or double or float or decimal:
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0}", v);
                    break;
                default:
                    sb.Append(SanityConstants.STRING_DELIMITER).Append(SanityExpressionTransformer.EscapeString(v.ToString() ?? string.Empty)).Append(SanityConstants.STRING_DELIMITER);
                    break;
            }
        }

        sb.Append(SanityConstants.CHAR_CLOSE_BRACKET);
        return sb.ToString();
    }

    private string HandleContainsLegacy(MethodCallExpression e)
    {
        if (e.Object == null || e.Arguments.Count <= 0) throw new Exception("'Contains' is only supported for simple expressions with non-null values.");

        var value = transformOperand(e.Arguments[0]);
        var member = transformOperand(e.Object);

        return $"{value} {SanityConstants.IN} {member}";
    }

    private string HandleCount(MethodCallExpression e)
    {
        if (isTopLevel)
        {
            VisitSourceIfTopLevel(e);
            if (!queryBuilder.IsSilent) queryBuilder.AggregateFunction = SanityConstants.COUNT;
        }

        var operand = e.Arguments.Count > 1 ? HandleWhere(e) : e.Arguments.Count > 0 ? transformOperand(e.Arguments[0]) : SanityConstants.AT;

        return $"{SanityConstants.COUNT}({operand})";
    }

    private string HandleMaxMin(MethodCallExpression e, string function)
    {
        var direction = function == SanityConstants.MAX ? SanityConstants.DESC : SanityConstants.ASC;
        var orderPipe = $"{SanityConstants.SPACE}{SanityConstants.PIPE}{SanityConstants.SPACE}{SanityConstants.ORDER}{SanityConstants.OPEN_PAREN}{SanityConstants.AT}{SanityConstants.SPACE}{direction}{SanityConstants.CLOSE_PAREN}{SanityConstants.OPEN_BRACKET}0{SanityConstants.CLOSE_BRACKET}";

        if (isTopLevel)
        {
            VisitSourceIfTopLevel(e);

            if (!queryBuilder.IsSilent)
            {
                if (TryGetLambda(e, 1, out var lambda))
                    queryBuilder.AddProjection(transformOperand(lambda!.Body));

                queryBuilder.AggregateFunction = string.Empty;
                queryBuilder.AggregatePostFix = orderPipe;
                queryBuilder.ExpectsArray = false;
            }
        }

        var source = transformOperand(e.Arguments[0]);
        var selector = TryGetLambda(e, 1, out var l) ? transformOperand(l!.Body) : SanityConstants.AT;

        return selector == SanityConstants.AT
            ? $"({source}){orderPipe}"
            : $"({source}){SanityConstants.DOT}{selector}{orderPipe}";
    }

    private string HandleImplicit(MethodCallExpression e)
    {
        VisitSourceIfTopLevel(e);
        return e.Arguments.Count > 0 ? transformOperand(e.Arguments[0]) : string.Empty;
    }

    private string HandleAny(MethodCallExpression e)
    {
        if (isTopLevel)
        {
            VisitSourceIfTopLevel(e);
            if (!queryBuilder.IsSilent)
            {
                queryBuilder.AggregateFunction = SanityConstants.COUNT;
                queryBuilder.AggregatePostFix = CountGreaterThanZeroPostFix;
            }
        }

        if (TryGetLambda(e, 1, out var lambda))
        {
            if (TryHandleAnyConstantCollection(e, lambda!, out var constantResult))
                return constantResult;

            var collectionExpr = e.Arguments[0];
            if (collectionExpr.Type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(collectionExpr.Type))
            {
                // Collection.Any(predicate) -> count(Collection[predicate]) > 0
                var filter = HandleWhere(e);
                return WrapWithCountGreaterThanZero(filter);
            }
        }

        var op = e.Arguments.Count > 0 ? transformOperand(e.Arguments[0]) : SanityConstants.AT;
        return WrapWithCountGreaterThanZero(op);
    }

    private bool TryHandleAnyConstantCollection(MethodCallExpression e, LambdaExpression lambda, [NotNullWhen(true)] out string? result)
    {
        result = null;
        var collectionExpr = e.Arguments[0];

        // Handle topicsWithSuperTopic.Any(ts => ts == t.Value.Id)
        // If the collection can be evaluated locally, use HandleContains logic
        if (collectionExpr is ConstantExpression expression &&
            lambda.Body is BinaryExpression { NodeType: ExpressionType.Equal } be)
        {
            // We need to find the MemberExpression in lambda.Body that corresponds to the 't' parameter.
            // Example: ts == t.Value.Id. Here 'ts' is the collection element, 't.Value.Id' is the target.
            Expression? target = null;
            if (be.Left == lambda.Parameters[0]) target = be.Right;
            else if (be.Right == lambda.Parameters[0]) target = be.Left;

            if (target != null)
            {
                var collection = expression.Value as IEnumerable ?? Array.Empty<object>();
                var targetOperand = transformOperand(target);
                var values = JoinValues(collection);
                result = $"{targetOperand} {SanityConstants.IN} {values}";
                return true;
            }
        }

        return false;
    }

    private static string WrapWithCountGreaterThanZero(string operand)
    {
        return $"{SanityConstants.COUNT}{SanityConstants.OPEN_PAREN}{operand}{SanityConstants.CLOSE_PAREN}{CountGreaterThanZeroPostFix}";
    }

    private string HandleIsDefined(MethodCallExpression e)
    {
        return e.Arguments.Count > 0 ? $"{SanityConstants.DEFINED}{SanityConstants.OPEN_PAREN}{transformOperand(e.Arguments[0])}{SanityConstants.CLOSE_PAREN}" : string.Empty;
    }

    private string HandleIsNullOrEmpty(MethodCallExpression e)
    {
        if (e.Arguments.Count == 0) return string.Empty;

        var operand = transformOperand(e.Arguments[0]);
        return $"{SanityConstants.OPEN_PAREN}{operand}{SanityConstants.SPACE}{SanityConstants.EQUALS}{SanityConstants.SPACE}{SanityConstants.NULL}{SanityConstants.SPACE}{SanityConstants.OR}{SanityConstants.SPACE}{operand}{SanityConstants.SPACE}{SanityConstants.EQUALS}{SanityConstants.SPACE}{SanityConstants.STRING_DELIMITER}{SanityConstants.STRING_DELIMITER}{SanityConstants.SPACE}{SanityConstants.OR}{SanityConstants.SPACE}{SanityConstants.NOT}{SanityConstants.OPEN_PAREN}{SanityConstants.DEFINED}{SanityConstants.OPEN_PAREN}{operand}{SanityConstants.CLOSE_PAREN}{SanityConstants.CLOSE_PAREN}{SanityConstants.CLOSE_PAREN}";
    }

    private string HandleOrdering(MethodCallExpression e, bool descending)
    {
        VisitSourceIfTopLevel(e);

        if (!TryGetLambda(e, 1, out var lambda)) return string.Empty;

        if (isTopLevel && !queryBuilder.IsSilent)
        {
            if (e.Method.Name.StartsWith("OrderBy")) queryBuilder.Orderings.Clear();
            queryBuilder.AddOrdering(transformOperand(lambda!.Body) + (descending ? SanityConstants.SPACE + SanityConstants.DESC : SanityConstants.SPACE + SanityConstants.ASC));
        }

        return string.Empty;
    }

    private string HandleSkip(MethodCallExpression e)
    {
        VisitSourceIfTopLevel(e);

        if (e.Arguments.Count <= 1) return string.Empty;

        if (e.Arguments[1] is not ConstantExpression c) return string.Empty;

        if (isTopLevel && !queryBuilder.IsSilent)
            queryBuilder.Skip = (int)c.Value!;

        return string.Empty;
    }

    private string HandleTake(MethodCallExpression e)
    {
        VisitSourceIfTopLevel(e);

        if (e.Arguments.Count <= 1) return string.Empty;

        if (e.Arguments[1] is not ConstantExpression c) return string.Empty;

        if (isTopLevel && !queryBuilder.IsSilent)
            queryBuilder.Take = (int)c.Value!;

        return string.Empty;
    }

    private string HandleStartsWith(MethodCallExpression e)
    {
        if (e.Object == null || e.Arguments.Count == 0) return string.Empty;

        var member = transformOperand(e.Object);
        var valueExpr = e.Arguments[0];
        if (valueExpr is ConstantExpression { Value: string s }) return $"{member}{SanityConstants.SPACE}{SanityConstants.MATCH}{SanityConstants.SPACE}{SanityConstants.STRING_DELIMITER}{SanityExpressionTransformer.EscapeString(s)}{SanityConstants.STAR}{SanityConstants.STRING_DELIMITER}";

        var value = transformOperand(e.Arguments[0]);
        return $"{member}{SanityConstants.SPACE}{SanityConstants.MATCH}{SanityConstants.SPACE}{value}{SanityConstants.SPACE}{SanityConstants.PLUS}{SanityConstants.SPACE}{SanityConstants.STRING_DELIMITER}{SanityConstants.STAR}{SanityConstants.STRING_DELIMITER}";
    }

    private void VisitSourceIfTopLevel(MethodCallExpression e)
    {
        if (isTopLevel && e.Arguments.Count > 0 && e.Arguments[0] is not ConstantExpression) visit(e.Arguments[0]);
    }

    private static bool TryGetLambda(MethodCallExpression e, int index, out LambdaExpression? lambda)
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
}