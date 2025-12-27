using System.Collections;
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
                return "_id";

            case "_createdAt":
            case "SanityCreatedAt":
                return "_createdAt";

            case "_updatedAt":
            case "SanityUpdatedAt":
                return "_updatedAt";

            case "_rev":
            case "SanityRevision":
                return "_rev";

            case "_type":
            case "SanityType":
                return "_type";

            case "IsDefined":
                return HandleIsDefined(e);

            case "IsDraft":
                return "_id in path(\"drafts.**\")";

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
        if (isTopLevel) visit(e.Arguments[0]);

        if (e.Arguments.Count <= 1) return transformOperand(e.Arguments[0]);

        var arg = e.Arguments[1];
        if (arg is UnaryExpression { NodeType: ExpressionType.Quote } quote) arg = quote.Operand;

        var simplifiedExpression = Evaluator.PartialEval(arg);
        if (simplifiedExpression is not LambdaExpression lambda) return transformOperand(e.Arguments[0]);

        if (isTopLevel && !queryBuilder.IsSilent)
            queryBuilder.Constraints.Add(transformOperand(lambda.Body));

        return transformOperand(e.Arguments[0]);
    }

    private string HandleSelect(MethodCallExpression e)
    {
        if (isTopLevel) visit(e.Arguments[0]);

        if (e.Arguments.Count <= 1) return transformOperand(e.Arguments[0]);

        var arg = e.Arguments[1];
        if (arg is UnaryExpression { NodeType: ExpressionType.Quote } quote) arg = quote.Operand;

        var simplifiedExpression = Evaluator.PartialEval(arg);
        if (simplifiedExpression is not LambdaExpression lambda) return transformOperand(e.Arguments[0]);

        if (lambda.Body is MemberExpression m && (m.Type.IsPrimitive || m.Type == typeof(string)))
            throw new Exception($"Selecting '{m.Member.Name}' as a scalar value is not supported due to serialization limitations. Instead, create an anonymous object containing the '{m.Member.Name}' field. e.g. o => new {{ o.{m.Member.Name} }}.");

        if (isTopLevel && !queryBuilder.IsSilent) queryBuilder.Projection = transformOperand(lambda.Body);

        return transformOperand(e.Arguments[0]);
    }

    private string HandleInclude(MethodCallExpression e)
    {
        if (isTopLevel) visit(e.Arguments[0]);
        var lambda = ExtractIncludeLambda(e);

        var (body, selectors) = ExtractSelectors(lambda.Body);

        var wasSilent = queryBuilder.IsSilent;
        queryBuilder.IsSilent = true;
        try
        {
            var fieldPath = transformOperand(body);
            var sourceName = GetIncludeSourceName(e, fieldPath);
            var propertyType = body.Type;
            var originalType = lambda.Parameters[0].Type;

            var includePath = AddInclude(fieldPath, propertyType, sourceName, originalType);

            selectors.Reverse();
            var currentIncludePath = includePath;
            var currentType = propertyType;

            foreach (var selector in selectors)
            {
                var selectorPath = transformOperand(selector.Body).Replace("->", "").Replace("@", "");
                if (string.IsNullOrEmpty(selectorPath) || selectorPath is "." or "[@]")
                    continue;

                selectorPath = selectorPath.Trim('.');

                currentIncludePath = $"{currentIncludePath}.{selectorPath}";
                AddInclude(currentIncludePath, selector.Body.Type, null, currentType);
                currentType = selector.Body.Type;
            }
        }
        finally
        {
            queryBuilder.IsSilent = wasSilent;
        }

        return string.Empty;
    }

    private static LambdaExpression ExtractIncludeLambda(MethodCallExpression e)
    {
        if (e.Arguments.Count < 2) throw new Exception("Include method must have at least two arguments.");

        var arg = e.Arguments[1];
        if (arg is UnaryExpression { NodeType: ExpressionType.Quote } quote) arg = quote.Operand;
        if (arg is UnaryExpression { NodeType: ExpressionType.Convert } convert) arg = convert.Operand;

        if (arg is not LambdaExpression lambda)
            throw new Exception("Include method second argument must be a lambda expression.");

        return lambda;
    }

    private static (Expression body, List<LambdaExpression> selectors) ExtractSelectors(Expression body)
    {
        var selectors = new List<LambdaExpression>();
        while (body is MethodCallExpression m && (m.Method.Name == "Select" || m.Method.Name == "SelectMany" || m.Method.Name == "OfType"))
        {
            if (m.Arguments.Count >= 2)
            {
                var arg = m.Arguments[1];
                if (arg is UnaryExpression { NodeType: ExpressionType.Quote } quote) arg = quote.Operand;
                if (arg is LambdaExpression selector) selectors.Add(selector);
            }

            body = m.Arguments[0];
        }

        return (body, selectors);
    }

    private string AddInclude(string fieldPath, Type propertyType, string? sourceName, Type originalType)
    {
        if (queryBuilder.Includes.ContainsKey(fieldPath)) return fieldPath;

        var targetName = fieldPath.Split('.').Last();
        var actualSourceName = sourceName ?? targetName;
        var includeValue = SanityQueryBuilder.GetJoinProjection(actualSourceName, targetName, propertyType, 0, 0);
        queryBuilder.Includes.Add(fieldPath, includeValue);
        return fieldPath;
    }

    private static string GetIncludeSourceName(MethodCallExpression e, string targetName)
    {
        if (e.Arguments.Count >= 3)
        {
            var sourceNameExpr = Evaluator.PartialEval(e.Arguments[2]);
            if (sourceNameExpr is ConstantExpression { Value: string s }) return s;
        }

        return targetName.Split('.').Last();
    }

    private string HandleOfType(MethodCallExpression e)
    {
        visit(e.Arguments[0]);
        return transformOperand(e.Arguments[0]);
    }

    private static string HandleGetValue(MethodCallExpression e)
    {
        if (e.Arguments.Count <= 0) throw new Exception("Could not evaluate GetValue method");

        var simplifiedExpression = Evaluator.PartialEval(e.Arguments[1]);
        if (simplifiedExpression is not ConstantExpression c || c.Type != typeof(string)) throw new Exception("Could not evaluate GetValue method");

        var fieldName = c.Value?.ToString() ?? "";
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
        if (!string.IsNullOrEmpty(leftAny) && !string.IsNullOrEmpty(rightAny)) return $"{rightAny} in {leftAny}";

        throw new Exception("'Contains' is only supported for simple expressions with non-null values.");
    }

    private string? HandleEnumerableContains(Expression collectionExpr, Expression valueExpr)
    {
        var eval = TryEvaluate(collectionExpr);
        if (eval is not (IEnumerable ie and not string)) return null;

        var memberName = transformOperand(valueExpr);
        return $"{memberName} in {JoinValues(ie)}";
    }

    private string? HandlePropertyContainsConstant(Expression collectionExpr, Expression valueExpr)
    {
        if (collectionExpr is not MemberExpression) return null;

        var simplifiedValue = Evaluator.PartialEval(valueExpr);
        if (simplifiedValue is not ConstantExpression { Value: not null } c2) return null;

        var memberName = transformOperand(collectionExpr);
        var valStr = c2.Value.ToString() ?? "";
        if (c2.Type != typeof(string) && c2.Type != typeof(Guid)) return $"{valStr} in {memberName}";

        valStr = SanityExpressionTransformer.EscapeString(valStr);
        return $"\"{valStr}\" in {memberName}";
    }

    private string? HandlePropertyContainsProperty(Expression collectionExpr, Expression valueExpr)
    {
        var collUnwrapped = collectionExpr is UnaryExpression { NodeType: ExpressionType.Convert } uc ? uc.Operand : collectionExpr;
        var valUnwrapped = valueExpr is UnaryExpression { NodeType: ExpressionType.Convert } uv ? uv.Operand : valueExpr;

        if (collUnwrapped is MemberExpression && valUnwrapped is MemberExpression)
        {
            var left = transformOperand(collUnwrapped);
            var right = transformOperand(valUnwrapped);
            return $"{right} in {left}";
        }

        return null;
    }

    private static object? TryEvaluate(Expression expr)
    {
        var simplified = Evaluator.PartialEval(expr);
        if (simplified is ConstantExpression c) return c.Value;

        try
        {
            return Expression.Lambda(simplified).Compile().DynamicInvoke();
        }
        catch
        {
            return null;
        }
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
        var arr = values.Cast<object?>().Where(o => o != null).ToArray();
        if (arr.Length == 0) return "[]";

        var formatted = arr.Select(v => v switch
        {
            string s => $"\"{SanityExpressionTransformer.EscapeString(s)}\"",
            Guid g => $"\"{g}\"",
            bool b => b.ToString().ToLower(),
            _ => v?.ToString() ?? "null"
        });

        return "[" + string.Join(",", formatted) + "]";
    }

    private string HandleContainsLegacy(MethodCallExpression e)
    {
        if (e.Object == null || e.Arguments.Count <= 0) throw new Exception("'Contains' is only supported for simple expressions with non-null values.");

        var value = transformOperand(e.Arguments[0]);
        var member = transformOperand(e.Object);

        return $"{value} in {member}";
    }

    private string HandleCount(MethodCallExpression e)
    {
        if (isTopLevel)
        {
            if (e.Arguments.Count > 0 && e.Arguments[0] is not ConstantExpression) visit(e.Arguments[0]);
            if (!queryBuilder.IsSilent) queryBuilder.AggregateFunction = "count";
        }

        var operand = e.Arguments.Count > 0 ? transformOperand(e.Arguments[0]) : "@";
        if (e.Arguments.Count > 1) HandleWhere(e);

        return $"count({operand})";
    }

    private string HandleImplicit(MethodCallExpression e)
    {
        if (isTopLevel && e.Arguments.Count > 0) visit(e.Arguments[0]);
        return e.Arguments.Count > 0 ? transformOperand(e.Arguments[0]) : string.Empty;
    }

    private string HandleAny(MethodCallExpression e)
    {
        if (isTopLevel)
        {
            if (e.Arguments.Count > 0 && e.Arguments[0] is not ConstantExpression) visit(e.Arguments[0]);
            if (!queryBuilder.IsSilent)
            {
                queryBuilder.AggregateFunction = "count";
                queryBuilder.AggregatePostFix = " > 0";
            }
        }

        var operand = e.Arguments.Count > 0 ? transformOperand(e.Arguments[0]) : "@";
        if (e.Arguments.Count > 1) HandleWhere(e);

        return $"count({operand}) > 0";
    }

    private string HandleIsDefined(MethodCallExpression e)
    {
        return e.Arguments.Count > 0 ? $"defined({transformOperand(e.Arguments[0])})" : string.Empty;
    }

    private string HandleIsNullOrEmpty(MethodCallExpression e)
    {
        if (e.Arguments.Count == 0) return string.Empty;

        var operand = transformOperand(e.Arguments[0]);
        return $"({operand} == null || {operand} == \"\" || !(defined({operand})))";
    }

    private string HandleOrdering(MethodCallExpression e, bool descending)
    {
        if (isTopLevel) visit(e.Arguments[0]);

        if (e.Arguments.Count <= 1) return string.Empty;

        var arg = e.Arguments[1];
        if (arg is UnaryExpression { NodeType: ExpressionType.Quote } quote) arg = quote.Operand;

        var simplifiedExpression = Evaluator.PartialEval(arg);
        if (simplifiedExpression is not LambdaExpression lambda) return string.Empty;
        if (isTopLevel && !queryBuilder.IsSilent)
            queryBuilder.Orderings.Add(transformOperand(lambda.Body) + (descending ? " desc" : " asc"));

        return string.Empty;
    }

    private string HandleSkip(MethodCallExpression e)
    {
        if (isTopLevel) visit(e.Arguments[0]);

        if (e.Arguments.Count <= 1) return string.Empty;

        var simplifiedExpression = Evaluator.PartialEval(e.Arguments[1]);
        if (simplifiedExpression is not ConstantExpression c) return string.Empty;

        if (isTopLevel && !queryBuilder.IsSilent)
            queryBuilder.Skip = (int)c.Value!;

        return string.Empty;
    }

    private string HandleTake(MethodCallExpression e)
    {
        if (isTopLevel) visit(e.Arguments[0]);

        if (e.Arguments.Count <= 1) return string.Empty;

        var simplifiedExpression = Evaluator.PartialEval(e.Arguments[1]);
        if (simplifiedExpression is not ConstantExpression c) return string.Empty;

        if (isTopLevel && !queryBuilder.IsSilent)
            queryBuilder.Take = (int)c.Value!;

        return string.Empty;
    }

    private string HandleStartsWith(MethodCallExpression e)
    {
        if (e.Object == null || e.Arguments.Count == 0) return string.Empty;

        var member = transformOperand(e.Object);
        var valueExpr = Evaluator.PartialEval(e.Arguments[0]);
        if (valueExpr is ConstantExpression { Value: string s }) return $"{member} match \"{SanityExpressionTransformer.EscapeString(s)}*\"";

        var value = transformOperand(e.Arguments[0]);
        return $"{member} match {value} + \"*\"";
    }
}