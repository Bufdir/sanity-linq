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

namespace Sanity.Linq.QueryProvider;

internal class SanityExpressionParser(Expression expression, Type docType, int maxNestingLevel, Type? resultType = null) : ExpressionVisitor
{
    public Type DocType { get; } = docType;
    public Expression Expression { get; } = expression;
    public int MaxNestingLevel { get; set; } = maxNestingLevel;
    public Type ResultType { get; } = resultType != null ? TypeSystem.GetElementType(resultType) : docType;
    private SanityQueryBuilder QueryBuilder { get; set; } = new();

    public string BuildQuery(bool includeProjections = true)
    {
        //Initialize query builder
        QueryBuilder = new SanityQueryBuilder
        {
            // Add constraint for root type
            DocType = DocType,
            ResultType = ResultType
        };

        // Parse Query
        if (Expression is MethodCallExpression or LambdaExpression)
        {
            // Traverse expression to build query
            Visit(Expression);
        }

        // Build query
        var query = QueryBuilder.Build(includeProjections, MaxNestingLevel);
        return query;
    }

    public override Expression? Visit(Expression? expression)
    {
        switch (expression)
        {
            case null:
                return null;

            case LambdaExpression:
                {
                    //Simplify lambda
                    expression = (LambdaExpression)Evaluator.PartialEval(expression);
                    if (((LambdaExpression)expression).Body is MethodCallExpression method)
                    {
                        QueryBuilder.Constraints.Add(TransformMethodCallExpression(method));
                    }

                    break;
                }
        }

        switch (expression)
        {
            case BinaryExpression b:
                QueryBuilder.Constraints.Add(TransformBinaryExpression(b));
                return b;

            case UnaryExpression u:
                QueryBuilder.Constraints.Add(TransformUnaryExpression(u));
                return u;

            case MethodCallExpression m:
                {
                    TransformMethodCallExpression(m);
                    if (m.Arguments[0] is not ConstantExpression)
                    {
                        Visit(m.Arguments[0]);
                    }
                    return expression;
                }

            default:
                return base.Visit(expression);
        }
    }

    private static string HandleGetValue(MethodCallExpression e)
    {
        if (e.Arguments.Count <= 0)
        {
            throw new Exception("Could not evaluate GetValue method");
        }

        var simplifiedExpression = Evaluator.PartialEval(e.Arguments[1]);
        if (simplifiedExpression is not ConstantExpression c || c.Type != typeof(string))
        {
            throw new Exception("Could not evaluate GetValue method");
        }

        var fieldName = c.Value?.ToString() ?? "";
        return $"{fieldName}";
    }

    private string HandleContains(MethodCallExpression e)
    {
        // Try resolve standard shapes: instance and static Contains
        if (TryGetParts(e, out var collectionExpr, out var valueExpr) && collectionExpr != null && valueExpr != null)
        {
            // Case 1: enumerable constant/list: titles.Contains(p.Title)
            var eval = TryEvaluate(collectionExpr);
            if (eval is IEnumerable ie and not string)
            {
                var memberName = TransformOperand(valueExpr);
                return $"{memberName} in {JoinValues(ie)}";
            }

            // Case 2: property.Contains(constant)
            if (collectionExpr is MemberExpression && valueExpr is not MemberExpression)
            {
                var memberName = TransformOperand(collectionExpr);
                var simplifiedValue = Evaluator.PartialEval(valueExpr);
                if (simplifiedValue is ConstantExpression { Value: not null } c2)
                {
                    return (c2.Type == typeof(string) || c2.Type == typeof(Guid))
                        ? $"\"{c2.Value}\" in {memberName}"
                        : $"{c2.Value} in {memberName}";
                }
            }

            // Case 3: property.Contains(otherProperty)
            var collUnwrapped = collectionExpr is UnaryExpression { NodeType: ExpressionType.Convert } uc ? uc.Operand : collectionExpr;
            var valUnwrapped = valueExpr is UnaryExpression { NodeType: ExpressionType.Convert } uv ? uv.Operand : valueExpr;
            if (collUnwrapped is MemberExpression && valUnwrapped is MemberExpression)
            {
                var left = TransformOperand(collUnwrapped);
                var right = TransformOperand(valUnwrapped);
                return $"{right} in {left}";
            }

            // Case 4: generic fallback
            var leftAny = TransformOperand(collectionExpr);
            var rightAny = TransformOperand(valueExpr);
            if (!string.IsNullOrEmpty(leftAny) && !string.IsNullOrEmpty(rightAny))
            {
                return $"{rightAny} in {leftAny}";
            }
        }

        // Legacy branch: two-arg form when parsing fallback
        if (e.Arguments.Count == 2)
        {
            var memberName = TransformOperand(e.Arguments[0]);
            var simplifiedValue = Evaluator.PartialEval(e.Arguments[1]);
            if (simplifiedValue is not ConstantExpression c2)
            {
                throw new Exception("'Contains' is only supported for simple expressions with non-null values.");
            }

            var valueObj = c2.Value;
            if (valueObj == null)
            {
                throw new Exception("'Contains' is only supported for simple expressions with non-null values.");
            }

            return (c2.Type == typeof(string) || c2.Type == typeof(Guid))
                ? $"\"{valueObj}\" in {memberName}"
                : $"{valueObj} in {memberName}";
        }

        throw new Exception("'Contains' is only supported for simple expressions with non-null values.");

        static object? TryEvaluate(Expression expr)
        {
            var simplified = Evaluator.PartialEval(expr);
            if (simplified is ConstantExpression c)
            {
                return c.Value;
            }

            try { return Expression.Lambda(simplified).Compile().DynamicInvoke(); }
            catch { return null; }
        }

        // Local helpers to reduce nesting
        static bool TryGetParts(MethodCallExpression call, out Expression? coll, out Expression? val)
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

        static string JoinValues(IEnumerable values)
        {
            var arr = values.Cast<object?>().Where(o => o != null).ToArray();
            if (arr.Length == 0) return "[]";
            var first = arr[0]!;
            var t = first.GetType();
            if (t == typeof(string) || t == typeof(Guid))
            {
                // Quote string-like entries without adding backslashes to the output
                return $"[\"{string.Join("\",\"", arr)}\"]";
            }
            return $"[{string.Join(",", arr)}]";
        }
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
        if (e.Arguments is { Count: 1 })
        {
            return TransformOperand(e.Arguments[0]);
        }
        if (e.Object != null)
        {
            return TransformOperand(e.Object);
        }
        if (e.Arguments is { Count: > 0 })
        {
            return TransformOperand(e.Arguments[0]);
        }
        throw new Exception("op_Implicit method shape not supported.");
    }

    private string HandleInclude(MethodCallExpression e)
    {
        // Arg 1: Field to join
        if (e.Arguments[1] is not UnaryExpression { Operand: LambdaExpression { Body: MemberExpression { Member.MemberType: MemberTypes.Property } } l })
        {
            throw new Exception("Joins can only be applied to properties.");
        }

        var fieldPath = TransformOperand(l.Body);
        var propertyType = l.Body.Type;
        var targetName = fieldPath.Split('.', '>').LastOrDefault() ?? string.Empty;
        var sourceName = targetName;

        // Arg 2: fieldName
        if (e.Arguments.Count > 2 && e.Arguments[2] is ConstantExpression c)
        {
            sourceName = c.Value?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(sourceName))
            {
                sourceName = targetName;
            }
        }
        var projection = SanityQueryBuilder.GetJoinProjection(sourceName, targetName, propertyType, 0, MaxNestingLevel);
        QueryBuilder.Includes[fieldPath] = projection;
        return projection;
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

        if (e.Arguments[1] is not UnaryExpression { Operand: LambdaExpression l })
        {
            throw new Exception(descending ? "Order by descending expression not supported." : "Order by expression not supported.");
        }

        var declaringType = l.Parameters[0].Type;
        if (declaringType != DocType)
        {
            throw new Exception($"Ordering is only supported on root document type {DocType.Name}");
        }
        var ordering = TransformOperand(l.Body) + (descending ? " desc" : " asc");
        QueryBuilder.Orderings.Add(ordering);
        return ordering;
    }

    private string HandleSelect(MethodCallExpression e)
    {
        Visit(e.Arguments[0]);

        if (e.Arguments[1] is not UnaryExpression { Operand: LambdaExpression l })
        {
            throw new Exception("Syntax of Select expression not supported.");
        }

        if (l.Body is MemberExpression m && (m.Type.IsPrimitive || m.Type == typeof(string)))
        {
            throw new Exception($"Selecting '{m.Member.Name}' as a scalar value is not supported due to serialization limitations. Instead, create an anonymous object containing the '{m.Member.Name}' field. e.g. o => new {{ o.{m.Member.Name} }}.");
        }
        var projection = TransformOperand(l.Body);
        QueryBuilder.Projection = projection;
        return projection;
    }

    private string HandleSkip(MethodCallExpression e)
    {
        Visit(e.Arguments[0]);
        if (e.Arguments[1] is not ConstantExpression { Value: int skip })
        {
            throw new Exception("Format for Skip expression not supported.");
        }
        QueryBuilder.Skip = skip;
        return QueryBuilder.Skip.ToString();
    }

    private string HandleStartsWith(MethodCallExpression e)
    {
        if (e.Object is null)
        {
            throw new Exception("StartsWith must be called on an instance.");
        }
        var memberName = TransformOperand(e.Object);
        string value;
        if (e.Arguments[0] is ConstantExpression c && c.Type == typeof(string))
        {
            value = c.Value?.ToString() ?? "";
        }
        else
        {
            throw new Exception("StartsWith is only supported for constant expressions");
        }

        return $"{memberName} match \"{value}*\"";
    }

    private string HandleTake(MethodCallExpression e)
    {
        Visit(e.Arguments[0]);
        if (e.Arguments[1] is not ConstantExpression { Value: int take })
        {
            throw new Exception("Format for Take expression not supported.");
        }
        QueryBuilder.Take = take;
        return QueryBuilder.Take.ToString();
    }

    private string HandleWhere(MethodCallExpression e)
    {
        var elementType = TypeSystem.GetElementType(e.Arguments[0].Type);
        if (elementType != DocType)
        {
            throw new Exception("Where expressions are only supported on the root type.");
        }
        Visit(e.Arguments[0]);

        if (e.Arguments[1] is not UnaryExpression { Operand: LambdaExpression l })
        {
            throw new Exception("Syntax of Select expression not supported.");
        }

        var constraint = TransformOperand(l.Body);
        QueryBuilder.Constraints.Add(constraint);
        return constraint;
    }

    private string TransformBinaryExpression(BinaryExpression b)
    {
        var op = b.NodeType switch
        {
            ExpressionType.Equal => "==",
            ExpressionType.AndAlso => "&&",
            ExpressionType.OrElse => "||",
            ExpressionType.LessThan => "<",
            ExpressionType.GreaterThan => ">",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.NotEqual => "!=",
            _ => throw new NotImplementedException($"Operator '{b.NodeType}' is not supported.")
        };

        var left = TransformOperand(b.Left);
        var right = TransformOperand(b.Right);

        return right switch
        {
            // Handle comparison to null
            "null" when op == "==" => $"(!(defined({left})) || {left} {op} {right})",
            "null" when op == "!=" => $"(defined({left}) && {left} {op} {right})",
            _ => $"({left} {op} {right})"
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
                return HandleOrdering(e, descending: false);

            case "OrderByDescending":
            case "ThenByDescending":
                return HandleOrdering(e, descending: true);

            case "Count":
            case "LongCount":
                return HandleCount(e);

            case "Take":
                return HandleTake(e);

            case "Skip":
                return HandleSkip(e);

            case "op_Implicit":
                return HandleImplicit(e);

            default:
                throw new Exception($"Method call {e.Method.Name} not supported.");
        }
    }

    private string TransformOperand(Expression e)
    {
        // Attempt to simplify
        e = Evaluator.PartialEval(e);

        switch (e)
        {
            // Member access
            case MemberExpression m:
                {
                    var memberPath = new List<string>();
                    var member = m.Member;

                    if (member is { Name: "Value", DeclaringType.IsGenericType: true } && member.DeclaringType.GetGenericTypeDefinition() == typeof(SanityReference<>))
                    {
                        memberPath.Add("->");
                    }
                    else
                    {
                        if (member.GetCustomAttributes(typeof(JsonPropertyAttribute), true).FirstOrDefault() is JsonPropertyAttribute jsonProperty)
                        {
                            memberPath.Add(jsonProperty.PropertyName ?? member.Name.ToCamelCase());
                        }
                        else
                        {
                            memberPath.Add(member.Name.ToCamelCase());
                        }
                    }
                    if (m.Expression is MemberExpression inner)
                    {
                        memberPath.Add(TransformOperand(inner));
                    }

                    return memberPath.Aggregate((a1, a2) => a1 != "->" && a2 != "->" ? $"{a2}.{a1}" : $"{a2}{a1}").Replace(".->", "->").Replace("->.", "->");
                }
            case NewExpression nw:
                {
                    // New expression with support for nested news
                    var args = nw.Arguments.Select(arg => arg is NewExpression ? "{" + TransformOperand(arg) + "}" : TransformOperand(arg)).ToArray();
                    var props = (nw.Members ?? Enumerable.Empty<MemberInfo>()).Select(prop => prop.Name.ToCamelCase()).ToArray();
                    if (args.Length != props.Length)
                    {
                        throw new Exception("Selections must be anonymous types without a constructor.");
                    }

                    var projection = new List<string>();
                    for (var i = 0; i < args.Length; i++)
                    {
                        if (args[i].Equals(props[i]))
                        {
                            projection.Add(args[i]);
                        }
                        else
                        {
                            projection.Add($"\"{props[i]}\": {args[i]}");
                        }
                    }
                    return projection.Aggregate((pc, pn) => $"{pc}, {pn}");
                }
            // Binary
            case BinaryExpression b:
                return TransformBinaryExpression(b);
            // Unary
            case UnaryExpression u:
                return TransformUnaryExpression(u);
            // Method call
            case MethodCallExpression mc:
                return TransformMethodCallExpression(mc);
            // Constant
            case ConstantExpression { Value: null }:
                return "null";

            case ConstantExpression c when c.Type == typeof(string):
                return $"\"{c.Value}\"";

            case ConstantExpression c when c.Type == typeof(int) ||
                                           c.Type == typeof(int?) ||
                                           c.Type == typeof(double) ||
                                           c.Type == typeof(double?) ||
                                           c.Type == typeof(float) ||
                                           c.Type == typeof(float?) ||
                                           c.Type == typeof(short) ||
                                           c.Type == typeof(short?) ||
                                           c.Type == typeof(byte) ||
                                           c.Type == typeof(byte?) ||
                                           c.Type == typeof(decimal) ||
                                           c.Type == typeof(decimal?):
                return $"{string.Format(CultureInfo.InvariantCulture, "{0}", c.Value).ToLower()}";

            case ConstantExpression c when c.Type == typeof(bool) ||
                                           c.Type == typeof(bool?):
                return $"{string.Format(CultureInfo.InvariantCulture, "{0}", c.Value).ToLower()}";

            case ConstantExpression { Value: DateTime dt }:
                {
                    if (dt == dt.Date) //No time component
                    {
                        return $"\"{dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\"";
                    }

                    return $"\"{dt.ToString("O", CultureInfo.InvariantCulture)}\"";
                }
            case ConstantExpression { Value: DateTimeOffset dto }:
                return $"\"{dto.ToString("O", CultureInfo.InvariantCulture)}\"";

            case ConstantExpression c:
                return $"\"{c.Value}\"";

            default:
                throw new Exception($"Operands of type {e.GetType()} and nodeType {e.NodeType} not supported. ");
        }
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