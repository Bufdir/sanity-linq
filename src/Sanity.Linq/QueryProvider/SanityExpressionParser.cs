// Copy-write 2018 Oslofjord Operations AS

// This file is part of Sanity LINQ (https://github.com/oslofjord/sanity-linq).

//  Sanity LINQ is free software: you can redistribute it and/or modify
//  it under the terms of the MIT License.

//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
//  MIT License for more details.

//  You should have received a copy of the MIT License
//  along with this program.

using System.Collections;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.Internal;

// ReSharper disable MemberCanBePrivate.Global

namespace Sanity.Linq.QueryProvider;

internal class SanityExpressionParser(Expression expression, Type docType, int maxNestingLevel, Type? resultType = null) : ExpressionVisitor
{
    private readonly HashSet<Expression> _visited = [];
    public Type DocType { get; } = docType;
    public Expression Expression { get; } = expression;
    public int MaxNestingLevel { get; set; } = maxNestingLevel;
    public Type ResultType { get; } = resultType != null ? TypeSystem.GetElementType(resultType) : docType;
    public bool ExpectsArray { get; } = resultType != null && resultType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(resultType);
    private SanityQueryBuilder QueryBuilder { get; set; } = new();

    public string BuildQuery(bool includeProjections = true)
    {
        //Initialize query builder
        QueryBuilder = new SanityQueryBuilder
        {
            // Add constraint for root type
            DocType = DocType,
            ResultType = ResultType,
            ExpectsArray = ExpectsArray
        };

        // Parse Query
        var expression = Evaluator.PartialEval(Expression);
        if (expression is MethodCallExpression or LambdaExpression)
            // Traverse expression to build query
            Visit(expression);

        // Build query
        var query = QueryBuilder.Build(includeProjections, MaxNestingLevel);
        return query;
    }

    public override Expression? Visit(Expression? expression)
    {
        if (expression == null || !_visited.Add(expression)) return expression;

        if (expression is not LambdaExpression lambda)
            return expression switch
            {
                BinaryExpression b => HandleVisitBinary(b),
                UnaryExpression u => HandleVisitUnary(u),
                MethodCallExpression m => HandleVisitMethodCall(m),
                _ => expression
            };

        var simplified = (LambdaExpression)Evaluator.PartialEval(lambda);
        if (simplified.Body is MethodCallExpression method) QueryBuilder.Constraints.Add(TransformMethodCallExpression(method));
        // If it's a lambda, we don't necessarily want to fall through to Visit binary/unary/etc
        // because those might add constraints that are already handled or not appropriate here.
        // But let's check what the original code did. It had two switch statements.

        return expression switch
        {
            BinaryExpression b => HandleVisitBinary(b),
            UnaryExpression u => HandleVisitUnary(u),
            MethodCallExpression m => HandleVisitMethodCall(m),
            _ => expression
        };
    }

    private BinaryExpression HandleVisitBinary(BinaryExpression b)
    {
        QueryBuilder.Constraints.Add(TransformBinaryExpression(b));
        return b;
    }

    private UnaryExpression HandleVisitUnary(UnaryExpression u)
    {
        QueryBuilder.Constraints.Add(TransformUnaryExpression(u));
        return u;
    }

    private MethodCallExpression HandleVisitMethodCall(MethodCallExpression m)
    {
        TransformMethodCallExpression(m);
        if (m.Arguments.Count > 0 && m.Arguments[0] is not ConstantExpression) Visit(m.Arguments[0]);
        return m;
    }

    private static string HandleGetValue(MethodCallExpression e)
    {
        if (e.Arguments.Count <= 0) throw new Exception("Could not evaluate GetValue method");

        var simplifiedExpression = Evaluator.PartialEval(e.Arguments[1]);
        if (simplifiedExpression is not ConstantExpression c || c.Type != typeof(string)) throw new Exception("Could not evaluate GetValue method");

        var fieldName = c.Value?.ToString() ?? "";
        return $"{fieldName}";
    }

    private static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private string HandleContains(MethodCallExpression e)
    {
        if (!TryGetContainsParts(e, out var collectionExpr, out var valueExpr) || collectionExpr == null || valueExpr == null)
            return HandleContainsLegacy(e);

        // Case 1: enumerable constant/list: titles.Contains(p.Title)
        if (HandleEnumerableContains(collectionExpr, valueExpr) is { } enumerableResult)
            return enumerableResult;

        // Case 2: property.Contains(constant)
        if (HandlePropertyContainsConstant(collectionExpr, valueExpr) is { } constantResult)
            return constantResult;

        // Case 3: property.Contains(otherProperty)
        if (HandlePropertyContainsProperty(collectionExpr, valueExpr) is { } propertyResult)
            return propertyResult;

        // Case 4: generic fallback
        var leftAny = TransformOperand(collectionExpr);
        var rightAny = TransformOperand(valueExpr);
        if (!string.IsNullOrEmpty(leftAny) && !string.IsNullOrEmpty(rightAny)) return $"{rightAny} in {leftAny}";

        throw new Exception("'Contains' is only supported for simple expressions with non-null values.");
    }

    private string? HandleEnumerableContains(Expression collectionExpr, Expression valueExpr)
    {
        var eval = TryEvaluate(collectionExpr);
        if (eval is not (IEnumerable ie and not string)) return null;

        var memberName = TransformOperand(valueExpr);
        return $"{memberName} in {JoinValues(ie)}";
    }

    private string? HandlePropertyContainsConstant(Expression collectionExpr, Expression valueExpr)
    {
        if (collectionExpr is not MemberExpression) return null;

        var simplifiedValue = Evaluator.PartialEval(valueExpr);
        if (simplifiedValue is not ConstantExpression { Value: not null } c2) return null;

        var memberName = TransformOperand(collectionExpr);
        var valStr = c2.Value.ToString() ?? "";
        if (c2.Type != typeof(string) && c2.Type != typeof(Guid)) return $"{valStr} in {memberName}";

        valStr = EscapeString(valStr);
        return $"\"{valStr}\" in {memberName}";
    }

    private string? HandlePropertyContainsProperty(Expression collectionExpr, Expression valueExpr)
    {
        var collUnwrapped = collectionExpr is UnaryExpression { NodeType: ExpressionType.Convert } uc ? uc.Operand : collectionExpr;
        var valUnwrapped = valueExpr is UnaryExpression { NodeType: ExpressionType.Convert } uv ? uv.Operand : valueExpr;

        if (collUnwrapped is MemberExpression && valUnwrapped is MemberExpression)
        {
            var left = TransformOperand(collUnwrapped);
            var right = TransformOperand(valUnwrapped);
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
        var first = arr[0]!;
        var t = first.GetType();
        if (t != typeof(string) && t != typeof(Guid)) return $"[{string.Join(",", arr)}]";

        // Quote string-like entries without adding backslashes to the output
        var escapedValues = arr.Select(o => o == null ? "" : EscapeString(o.ToString()!));
        return $"[\"{string.Join("\",\"", escapedValues)}\"]";
    }

    private string HandleContainsLegacy(MethodCallExpression e)
    {
        if (e.Arguments.Count != 2) 
            throw new Exception("'Contains' is only supported for simple expressions with non-null values.");

        var memberName = TransformOperand(e.Arguments[0]);
        var simplifiedValue = Evaluator.PartialEval(e.Arguments[1]);
        if (simplifiedValue is not ConstantExpression c2) 
            throw new Exception("'Contains' is only supported for simple expressions with non-null values.");

        var valueObj = c2.Value;
        if (valueObj == null) 
            throw new Exception("'Contains' is only supported for simple expressions with non-null values.");

        return c2.Type == typeof(string) || c2.Type == typeof(Guid)
            ? $"\"{valueObj}\" in {memberName}"
            : $"{valueObj} in {memberName}";
    }

    private string HandleCount(MethodCallExpression e)
    {
        Visit(e.Arguments[0]);
        const string function = "count";
        QueryBuilder.AggregateFunction = function;
        return function;
    }

    private string HandleImplicit(MethodCallExpression e)
    {
        // Treat implicit conversions as no-ops in the query translation
        if (e.Arguments is { Count: 1 }) return TransformOperand(e.Arguments[0]);
        if (e.Object != null) return TransformOperand(e.Object);
        if (e.Arguments is { Count: > 0 }) return TransformOperand(e.Arguments[0]);
        throw new Exception("op_Implicit method shape not supported.");
    }

    private string HandleAny(MethodCallExpression e)
    {
        Expression sourceExpr;
        if (e.Object != null)
        {
            if (e.Arguments.Count > 0) throw new Exception("Any with a predicate is not supported.");
            sourceExpr = e.Object;
        }
        else if (e.Arguments.Count == 1)
        {
            sourceExpr = e.Arguments[0];
        }
        else
        {
            throw new Exception("Any with a predicate is not supported.");
        }

        var source = TransformOperand(sourceExpr);
        return $"count({source}) > 0";
    }

    private string HandleInclude(MethodCallExpression e)
    {
        var lambda = ExtractIncludeLambda(e);
        var (body, selectors) = ExtractSelectors(lambda.Body);

        if (body is not MemberExpression { Member.MemberType: MemberTypes.Property } me)
            throw new Exception("Joins can only be applied to properties.");

        var currentPath = TransformOperand(me);
        var currentOriginalType = me.Type;

        // If we have selectors, we should also ensure parents are included
        foreach (var selector in selectors)
        {
            // The type of the current path is the parameter type of the next selector.
            // BUT if the currentOriginalType is a collection, we should represent it as a collection of the parameter types.
            var currentType = selector.Parameters[0].Type;
            AddInclude(currentPath, currentType, null, currentOriginalType);

            var selectorPath = TransformOperand(selector.Body);
            if (selectorPath.StartsWith("@")) selectorPath = selectorPath.Substring(1).TrimStart('.');

            if (!string.IsNullOrEmpty(selectorPath))
                currentPath = currentPath + (selectorPath.StartsWith("->") ? "" : ".") + selectorPath;

            currentOriginalType = selector.Body.Type;
        }

        // Finally, include
        var propertyType = e.Method.GetGenericArguments()[1];
        var sourceName = GetIncludeSourceName(e, currentPath.Split('.', '>').LastOrDefault() ?? string.Empty);
        return AddInclude(currentPath, propertyType, sourceName, currentOriginalType);
    }

    private static LambdaExpression ExtractIncludeLambda(MethodCallExpression e)
    {
        if (e.Arguments.Count < 2 || e.Arguments[1] is not UnaryExpression { Operand: LambdaExpression lambda })
            throw new Exception("Include must be a lambda expression.");
        return lambda;
    }

    private static (Expression body, List<LambdaExpression> selectors) ExtractSelectors(Expression body)
    {
        var selectors = new List<LambdaExpression>();
        while (body is MethodCallExpression { Method.Name: "Select" or "SelectMany" or "OfType" } mce)
        {
            if (mce.Method.Name is "Select" or "SelectMany" && mce.Arguments.Count > 1)
            {
                var arg = mce.Arguments[1];
                switch (arg)
                {
                    case UnaryExpression { Operand: LambdaExpression selector1 }:
                        selectors.Insert(0, selector1);
                        break;
                    case LambdaExpression selector2:
                        selectors.Insert(0, selector2);
                        break;
                }
            }

            body = mce.Arguments[0];
        }

        return (body, selectors);
    }

    private string AddInclude(string fieldPath, Type propertyType, string? sourceName, Type originalType)
    {
        var targetName = fieldPath.Split('.', '>').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? string.Empty;
        var finalSourceName = sourceName ?? targetName;

        // Wrap in IEnumerable if the original property was a collection but propertyType isn't.
        // This is necessary for OfType/Select/SelectMany scenarios where we work on elements.
        if (typeof(IEnumerable).IsAssignableFrom(originalType) && originalType != typeof(string)
                                                               && !typeof(IEnumerable).IsAssignableFrom(propertyType))
            propertyType = typeof(IEnumerable<>).MakeGenericType(propertyType);

        var projection = SanityQueryBuilder.GetJoinProjection(finalSourceName, targetName, propertyType, 0, MaxNestingLevel);
        QueryBuilder.Includes[fieldPath] = projection;
        return projection;
    }

    private static string GetIncludeSourceName(MethodCallExpression e, string targetName)
    {
        if (e.Arguments.Count <= 2 || e.Arguments[2] is not ConstantExpression c) return targetName;

        var value = c.Value?.ToString();
        return !string.IsNullOrEmpty(value) ? value : targetName;
    }

    private string HandleIsDefined(MethodCallExpression e)
    {
        var field = TransformOperand(e.Arguments[0]);
        return $"defined({field})";
    }

    private string HandleIsNullOrEmpty(MethodCallExpression e)
    {
        var field = TransformOperand(e.Arguments[0]);
        return $"{field} == null || {field} == \"\" || !(defined({field}))";
    }

    private string HandleOrdering(MethodCallExpression e, bool descending)
    {
        Visit(e.Arguments[0]);

        if (e.Arguments[1] is not UnaryExpression { Operand: LambdaExpression l }) throw new Exception(descending ? "Order by descending expression not supported." : "Order by expression not supported.");

        var declaringType = l.Parameters[0].Type;
        if (declaringType != DocType) throw new Exception($"Ordering is only supported on root document type {DocType.Name}");
        var ordering = TransformOperand(l.Body) + (descending ? " desc" : " asc");
        QueryBuilder.Orderings.Add(ordering);
        return ordering;
    }

    private string HandleSelect(MethodCallExpression e)
    {
        Visit(e.Arguments[0]);

        if (e.Arguments[1] is not UnaryExpression { Operand: LambdaExpression l }) throw new Exception("Syntax of Select expression not supported.");

        if (l.Body is MemberExpression m && (m.Type.IsPrimitive || m.Type == typeof(string))) throw new Exception($"Selecting '{m.Member.Name}' as a scalar value is not supported due to serialization limitations. Instead, create an anonymous object containing the '{m.Member.Name}' field. e.g. o => new {{ o.{m.Member.Name} }}.");
        var projection = TransformOperand(l.Body);
        QueryBuilder.Projection = projection;
        return projection;
    }

    private string HandleSkip(MethodCallExpression e)
    {
        Visit(e.Arguments[0]);
        if (e.Arguments[1] is not ConstantExpression { Value: int skip }) throw new Exception("Format for Skip expression not supported.");
        QueryBuilder.Skip = skip;
        return QueryBuilder.Skip.ToString();
    }

    private string HandleStartsWith(MethodCallExpression e)
    {
        if (e.Object is null) throw new Exception("StartsWith must be called on an instance.");
        var memberName = TransformOperand(e.Object);
        var simplifiedValue = Evaluator.PartialEval(e.Arguments[0]);
        if (simplifiedValue is not ConstantExpression c || c.Type != typeof(string)) throw new Exception("StartsWith is only supported for constant expressions");

        var value = EscapeString(c.Value?.ToString() ?? "");
        return $"{memberName} match \"{value}*\"";
    }

    private string HandleTake(MethodCallExpression e)
    {
        Visit(e.Arguments[0]);
        if (e.Arguments[1] is not ConstantExpression { Value: int take }) throw new Exception("Format for Take expression not supported.");
        QueryBuilder.Take = take;
        return QueryBuilder.Take.ToString();
    }

    private string HandleWhere(MethodCallExpression e)
    {
        var elementType = TypeSystem.GetElementType(e.Arguments[0].Type);
        if (elementType != DocType) throw new Exception("Where expressions are only supported on the root type.");
        Visit(e.Arguments[0]);

        if (e.Arguments[1] is not UnaryExpression { Operand: LambdaExpression l }) throw new Exception("Syntax of Select expression not supported.");

        var constraint = TransformOperand(l.Body);
        QueryBuilder.Constraints.Add(constraint);
        return constraint;
    }

    private string TransformBinaryExpression(BinaryExpression b)
    {
        var op = GetBinaryOperator(b.NodeType);
        var left = TransformOperand(b.Left);
        var right = TransformOperand(b.Right);

        if (left == "null" && op is "==" or "!=")
            // Swap left and right so null is always on the right for comparison logic
            (left, right) = (right, left);

        return right switch
        {
            "null" when op == "==" => $"(!(defined({left})) || {left} {op} {right})",
            "null" when op == "!=" => $"(defined({left}) && {left} {op} {right})",
            _ => $"{left} {op} {right}"
        };
    }

    private static string GetBinaryOperator(ExpressionType nodeType)
    {
        return nodeType switch
        {
            ExpressionType.Equal => "==",
            ExpressionType.AndAlso => "&&",
            ExpressionType.OrElse => "||",
            ExpressionType.LessThan => "<",
            ExpressionType.GreaterThan => ">",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.NotEqual => "!=",
            _ => throw new NotImplementedException($"Operator '{nodeType}' is not supported.")
        };
    }

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
                return HandleSelect(e);

            case "Include":
                return HandleInclude(e);

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
                Visit(e.Arguments[0]);
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

    private string TransformOperand(Expression e)
    {
        return e switch
        {
            MemberExpression m => HandleMemberExpression(m),
            NewExpression nw => HandleNewExpression(nw),
            BinaryExpression b => TransformBinaryExpression(b),
            UnaryExpression u => TransformUnaryExpression(u),
            MethodCallExpression mc => TransformMethodCallExpression(mc),
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
            NewArrayExpression na => "[" + string.Join(",", na.Expressions.Select(TransformOperand)) + "]",
            _ => throw new Exception($"Operands of type {e.GetType()} and nodeType {e.NodeType} not supported. ")
        };
    }

    private static bool IsNumericOrBoolType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType == typeof(int) ||
               underlyingType == typeof(double) ||
               underlyingType == typeof(float) ||
               underlyingType == typeof(short) ||
               underlyingType == typeof(byte) ||
               underlyingType == typeof(decimal) ||
               underlyingType == typeof(bool);
    }

    private string HandleMemberExpression(MemberExpression m)
    {
        var memberPath = new List<string>();
        var member = m.Member;

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

        if (m.Expression is MemberExpression inner) memberPath.Add(TransformOperand(inner));

        return memberPath
            .Aggregate((a1, a2) => a1 != "->" && a2 != "->" ? $"{a2}.{a1}" : $"{a2}{a1}")
            .Replace(".->", "->")
            .Replace("->.", "->");
    }

    private string HandleNewExpression(NewExpression nw)
    {
        var args = nw.Arguments
            .Select(arg => arg is NewExpression ? "{" + TransformOperand(arg) + "}" : TransformOperand(arg))
            .ToArray();
        var props = (nw.Members ?? Enumerable.Empty<MemberInfo>())
            .Select(prop => prop.Name.ToCamelCase())
            .ToArray();

        if (args.Length != props.Length) throw new Exception("Selections must be anonymous types without a constructor.");

        var projection = args
            .Select((t, i) => t.Equals(props[i]) ? t : $"\"{props[i]}\": {t}")
            .ToList();

        return string.Join(", ", projection);
    }

    private string TransformUnaryExpression(UnaryExpression u)
    {
        return u.NodeType switch
        {
            ExpressionType.Not => "!(" + TransformOperand(u.Operand) + ")",
            ExpressionType.Convert => TransformOperand(u.Operand),
            _ => throw new Exception(
                $"Unary expression of type {u.GetType()} and nodeType {u.NodeType} not supported. ")
        };
    }
}