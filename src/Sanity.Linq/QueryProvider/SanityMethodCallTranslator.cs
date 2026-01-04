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
                return SanityMethodCallTranslatorHelper.HandleGetValue(e);

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

        if (!SanityMethodCallTranslatorHelper.TryGetLambda(e, 1, out var lambda)) return transformOperand(e.Arguments[0]);

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

        if (!SanityMethodCallTranslatorHelper.TryGetLambda(e, 1, out var lambda)) return transformOperand(e.Arguments[0]);

        if (lambda!.Body is MemberExpression m && (m.Type.IsPrimitive || m.Type == typeof(string)))
            throw new Exception($"Selecting '{m.Member.Name}' as a scalar value is not supported due to serialization limitations. Instead, create an anonymous object containing the '{m.Member.Name}' field. e.g. o => new {{ o.{m.Member.Name} }}.");

        if (!isTopLevel || queryBuilder.IsSilent)
        {
            var wasFallback = queryBuilder.UseCoalesceFallback;
            queryBuilder.UseCoalesceFallback = false;
            try
            {
                var operand = transformOperand(e.Arguments[0]);
                var selector = transformOperand(lambda.Body);

                if (selector == SanityConstants.AT) return operand;

                if (selector.StartsWith(SanityConstants.AT)) selector = selector.Substring(SanityConstants.AT.Length);

                string result;
                if (selector.StartsWith(SanityConstants.DEREFERENCING_OPERATOR))
                    result = $"{operand}{selector}";
                else if (operand == SanityConstants.AT)
                    result = selector;
                else
                    result = $"{operand}.{selector}";

                if (e.Method.Name == "SelectMany" && !result.EndsWith(SanityConstants.ARRAY_INDICATOR)) result += SanityConstants.ARRAY_INDICATOR;

                return result;
            }
            finally
            {
                queryBuilder.UseCoalesceFallback = wasFallback;
            }
        }

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
        if (!SanityMethodCallTranslatorHelper.TryGetLambda(e, 1, out var lambda)) throw new Exception("Include method second argument must be a lambda expression.");

        var (body, selectors) = SanityMethodCallTranslatorHelper.ExtractSelectors(lambda!.Body);

        var wasSilent = queryBuilder.IsSilent;
        var wasFallback = queryBuilder.UseCoalesceFallback;
        queryBuilder.IsSilent = true;
        queryBuilder.UseCoalesceFallback = false;
        try
        {
            var fieldPath = transformOperand(body).TrimStart('@').TrimStart('.');
            var sourceName = SanityMethodCallTranslatorHelper.GetIncludeSourceName(e, fieldPath);
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
        var originalType = currentType; // Save the original type before filtering

        var filter = string.Empty;
        if (currentType != targetType && currentElementType != targetType)
        {
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(SanityReference<>))
            {
                filter = $"{SanityConstants.OPEN_BRACKET}{SanityConstants.TYPE} {SanityConstants.EQUALS} {SanityConstants.STRING_DELIMITER}{SanityConstants.REFERENCE}{SanityConstants.STRING_DELIMITER}{SanityConstants.CLOSE_BRACKET}";
            }
            else
            {
                var sanityType = targetType.GetSanityTypeName();
                filter = $"{SanityConstants.OPEN_BRACKET}{SanityConstants.TYPE} {SanityConstants.EQUALS} {SanityConstants.STRING_DELIMITER}{sanityType}{SanityConstants.STRING_DELIMITER}{SanityConstants.CLOSE_BRACKET}";
            }
        }

        var fullPathWithFilter = currentIncludePath + filter;

        AddInclude(fullPathWithFilter, mc.Type, null, originalType);
        currentType = targetType;
        return fullPathWithFilter;
    }

    private string HandleIncludePath(Expression selectorExpr, string currentIncludePath, out Type currentType)
    {
        var selector = (LambdaExpression)(selectorExpr is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : selectorExpr);
        var rawPath = transformOperand(selector.Body);
        var selectorPath = rawPath.Replace(SanityConstants.AT, string.Empty).Replace(SanityConstants.ARRAY_INDICATOR, string.Empty).Trim(SanityConstants.DOT[0]);
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

        currentIncludePath = selectorPath.StartsWith(SanityConstants.OPEN_BRACKET) || selectorPath.StartsWith(SanityConstants.DEREFERENCING_OPERATOR)
            ? $"{currentIncludePath}{selectorPath}"
            : $"{currentIncludePath}{SanityConstants.DOT}{selectorPath}".TrimEnd(SanityConstants.DOT[0]);
        AddInclude(currentIncludePath, selector.Body.Type, null);
        currentType = selector.Body.Type;
        return currentIncludePath;
    }


    private string AddInclude(string fieldPath, Type propertyType, string? sourceName, Type? originalType = null)
    {
        var targetName = fieldPath.Split('.', '-').Last().TrimStart('>');
        var actualSourceName = (sourceName ?? targetName).Split('.', '-').Last().TrimStart('>');

        var includeValue = SanityQueryBuilder.GetJoinProjection(actualSourceName, targetName, propertyType, 0, 0, true);

        if (queryBuilder.Includes.TryGetValue(fieldPath, out var existingValue))
            if (existingValue == includeValue)
                return fieldPath;
        // If the path exists but with different value, we might need to merge them later in projection expansion
        // For now, let's allow overwriting or appending if it's a filtered include
        queryBuilder.Includes[fieldPath] = includeValue;

        // If this is a filtered include (contains [filter]), ensure the base field is also included
        // This is necessary for proper deserialization of union types
        var openBracketIndex = targetName.IndexOf(SanityConstants.CHAR_OPEN_BRACKET);
        if (openBracketIndex > 0)
        {
            var baseFieldName = targetName.Substring(0, openBracketIndex);
            var lastDotIndex = fieldPath.LastIndexOf('.');
            var baseFieldPath = lastDotIndex >= 0
                ? fieldPath.Substring(0, lastDotIndex + 1) + baseFieldName
                : baseFieldName;

            if (!queryBuilder.Includes.ContainsKey(baseFieldPath) && originalType != null)
            {
                // Add the base field with the original (unfiltered) type
                var baseIncludeValue = SanityQueryBuilder.GetJoinProjection(baseFieldName, baseFieldName, originalType, 0, 0);
                queryBuilder.Includes[baseFieldPath] = baseIncludeValue;
            }
        }

        return fieldPath;
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


    private string HandleContains(MethodCallExpression e)
    {
        if (!SanityMethodCallTranslatorHelper.TryGetContainsParts(e, out var collectionExpr, out var valueExpr) || collectionExpr == null || valueExpr == null)
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
        var eval = SanityMethodCallTranslatorHelper.TryEvaluate(collectionExpr);
        if (eval is not (IEnumerable ie and not string)) return null;

        var memberName = transformOperand(valueExpr);
        var values = SanityMethodCallTranslatorHelper.JoinValues(ie);
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
                if (SanityMethodCallTranslatorHelper.TryGetLambda(e, 1, out var lambda))
                    queryBuilder.AddProjection(transformOperand(lambda!.Body));

                queryBuilder.AggregateFunction = string.Empty;
                queryBuilder.AggregatePostFix = orderPipe;
                queryBuilder.ExpectsArray = false;
            }
        }

        var source = transformOperand(e.Arguments[0]);
        var selector = SanityMethodCallTranslatorHelper.TryGetLambda(e, 1, out var l) ? transformOperand(l!.Body) : SanityConstants.AT;

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
                queryBuilder.AggregatePostFix = SanityMethodCallTranslatorHelper.CountGreaterThanZeroPostFix;
            }
        }

        if (SanityMethodCallTranslatorHelper.TryGetLambda(e, 1, out var lambda))
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

        // Handle topicsWithSuperTopic.Any(ts => ts == t.Value.Id) or topics.Any(t => t.Id == target)
        // If the collection can be evaluated locally, use HandleContains logic
        if (collectionExpr is ConstantExpression expression &&
            lambda.Body is BinaryExpression { NodeType: ExpressionType.Equal } be)
        {
            Expression? paramSide = null;
            Expression? target = null;

            if (IsParameterOrMemberOfParameter(be.Left, lambda.Parameters[0]))
            {
                paramSide = be.Left;
                target = be.Right;
            }
            else if (IsParameterOrMemberOfParameter(be.Right, lambda.Parameters[0]))
            {
                paramSide = be.Right;
                target = be.Left;
            }

            if (paramSide != null && target != null)
            {
                var collection = expression.Value as IEnumerable;
                if (collection == null || collection is IQueryable) return false;

                // Extract values from collection based on paramSide
                var valuesList = new List<object?>();
                var param = lambda.Parameters[0];

                // Create a lambda to evaluate paramSide
                var evaluator = Expression.Lambda(paramSide, param).Compile();

                foreach (var item in collection)
                    try
                    {
                        var val = evaluator.DynamicInvoke(item);
                        valuesList.Add(val);
                    }
                    catch
                    {
                        // Ignore items that fail evaluation
                    }

                var targetOperand = transformOperand(target);
                var valuesGroq = SanityMethodCallTranslatorHelper.JoinValues(valuesList);
                result = $"{targetOperand} {SanityConstants.IN} {valuesGroq}";
                return true;
            }
        }

        return false;
    }

    private static bool IsParameterOrMemberOfParameter(Expression e, ParameterExpression p)
    {
        var current = e;
        while (current is MemberExpression me) current = me.Expression;
        while (current is UnaryExpression ue) current = ue.Operand;
        return current == p;
    }

    private string WrapWithCountGreaterThanZero(string operand)
    {
        return SanityMethodCallTranslatorHelper.WrapWithCountGreaterThanZero(operand);
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

        if (!SanityMethodCallTranslatorHelper.TryGetLambda(e, 1, out var lambda)) return string.Empty;

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
}