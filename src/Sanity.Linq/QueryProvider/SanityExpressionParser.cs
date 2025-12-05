// Copy-write 2018 Oslofjord Operations AS

// This file is part of Sanity LINQ (https://github.com/oslofjord/sanity-linq).

//  Sanity LINQ is free software: you can redistribute it and/or modify
//  it under the terms of the MIT Licence.

//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
//  MIT Licence for more details.

//  You should have received a copy of the MIT Licence
//  along with this program.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            ResultType = ResultType ?? DocType
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

            case LambdaExpression l:
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

    /// <summary>
    /// Constructs a projection string for a property to be included or joined in a query.
    /// Handles various property types, including primitive types, strings,
    /// nested objects, and collections of <see cref="SanityReference{T}"/>.
    /// </summary>
    /// <param name="sourceName">The name of the source field in the query.</param>
    /// <param name="targetName">The name of the target field in the query.</param>
    /// <param name="propertyType">The type of the property being projected.</param>
    /// <param name="nestingLevel">The current nesting level of the projection.</param>
    /// <param name="maxNestingLevel">The maximum allowed nesting level for projections.</param>
    /// <returns>A string representing the projection for the specified property.</returns>
    private static string GetJoinProjection(string sourceName, string targetName, Type propertyType, int nestingLevel, int maxNestingLevel)
    {
        var projection = "";
        var fieldRef = sourceName;
        if (sourceName != targetName && !string.IsNullOrEmpty(targetName))
        {
            fieldRef = $"\"{targetName}\":{sourceName}";
        }

        // String or primitive
        if (propertyType == typeof(string) || propertyType.IsPrimitive)
        {
            return fieldRef;
        }

        var isSanityReferenceType = propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(SanityReference<>);
        if (isSanityReferenceType)
        {
            // CASE 1: SanityReference<T>
            var fields = GetPropertyProjectionList(propertyType.GetGenericArguments()[0], nestingLevel, maxNestingLevel);
            var fieldList = fields.Aggregate((c, n) => c + "," + n);
            projection = $"{fieldRef}->{{ {fieldList} }}";
        }
        else
        {
            var listOfSanityReferenceType = propertyType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>) && i.GetGenericArguments()[0].IsGenericType && i.GetGenericArguments()[0].GetGenericTypeDefinition() == typeof(SanityReference<>));
            var isListOfSanityReference = listOfSanityReferenceType != null;
            if (isListOfSanityReference)
            {
                // CASE 2: IEnumerable<SanityReference<T>>
                var elementType = listOfSanityReferenceType!.GetGenericArguments()[0].GetGenericArguments()[0];
                var fields = GetPropertyProjectionList(elementType, nestingLevel, maxNestingLevel);
                var fieldList = fields.Aggregate((c, n) => c + "," + n);
                projection = $"{fieldRef}[]->{{{fieldList}}}";
            }
            else
            {
                var nestedProperties = propertyType.GetProperties();
                var sanityImageAssetProperty = nestedProperties.FirstOrDefault(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(SanityReference<>) && (p.Name.ToLower() == "asset" || ((p.GetCustomAttributes<JsonPropertyAttribute>(true).FirstOrDefault())?.PropertyName?.Equals("asset")).GetValueOrDefault()));
                var isSanityImage = sanityImageAssetProperty != null;
                if (isSanityImage)
                {
                    // CASE 3: Image.Asset
                    //var propertyName = nestedProperty.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName ?? nestedProperty.Name.ToCamelCase();
                    var fields = GetPropertyProjectionList(propertyType, nestingLevel, maxNestingLevel);
                    var nestedFields = GetPropertyProjectionList(sanityImageAssetProperty!.PropertyType, nestingLevel, maxNestingLevel);

                    // Nested Reference
                    var fieldList = fields.Select(f => f.StartsWith("asset") ? $"asset->{(nestedFields.Count > 0 ? ("{" + nestedFields.Aggregate((a, b) => a + "," + b) + "}") : "")}" : f).Aggregate((c, n) => c + "," + n);
                    projection = $"{fieldRef}{{{fieldList}}}";
                }
                else
                {
                    var nestedSanityReferenceProperty = nestedProperties.FirstOrDefault(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(SanityReference<>));
                    if (nestedSanityReferenceProperty is { } nsr)
                    {
                        // CASE 4: Property->SanityReference<T> (generalization of Case 3)
                        var propertyName = nsr.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName ?? nsr.Name.ToCamelCase();
                        var fields = GetPropertyProjectionList(propertyType, nestingLevel, maxNestingLevel);
                        var elementType = nsr.PropertyType.GetGenericArguments()[0];
                        var nestedFields = GetPropertyProjectionList(elementType, nestingLevel + 1, maxNestingLevel);

                        // Nested Reference
                        var fieldList = fields.Select(f => f == propertyName ? $"{propertyName}->{(nestedFields.Count > 0 ? ("{" + nestedFields.Aggregate((a, b) => a + "," + b) + "}") : "")}" : f).Aggregate((c, n) => c + "," + n);
                        projection = $"{fieldRef}{{{fieldList}}}";
                    }
                    else
                    {
                        var nestedListOfSanityReferenceType = nestedProperties.FirstOrDefault(p => p.PropertyType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>) && i.GetGenericArguments()[0].IsGenericType && i.GetGenericArguments()[0].GetGenericTypeDefinition() == typeof(SanityReference<>)));
                        if (nestedListOfSanityReferenceType is { } nlsr)
                        {
                            // CASE 5: Property->List<SanityReference<T>>
                            var propertyName = nlsr.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName ?? nlsr.Name.ToCamelCase();
                            var fields = GetPropertyProjectionList(propertyType, nestingLevel, maxNestingLevel);
                            var collectionType = nlsr.PropertyType.GetGenericArguments()[0];
                            var elementType = collectionType.GetGenericArguments()[0];
                            var nestedFields = GetPropertyProjectionList(elementType, nestingLevel + 1, maxNestingLevel);

                            // Nested Reference
                            var fieldList = fields.Select(f => f == propertyName ? $"{propertyName}[]->{(nestedFields.Count > 0 ? ("{" + nestedFields.Aggregate((a, b) => a + "," + b) + "}") : "")}" : f).Aggregate((c, n) => c + "," + n);
                            projection = $"{fieldRef}{{{fieldList}}}";
                        }
                        else
                        {
                            var listOfSanityImagesType = propertyType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>) && i.GetGenericArguments()[0].GetProperties().Any(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(SanityReference<>) && (p.Name.ToLower() == "asset" || ((p.GetCustomAttributes<JsonPropertyAttribute>(true).FirstOrDefault())?.PropertyName?.Equals("asset")).GetValueOrDefault())));
                            if (listOfSanityImagesType is { } lois)
                            {
                                // CASE 6: Array of objects with "asset" field (e.g. images)
                                var elementType = lois.GetGenericArguments()[0];
                                var fields = GetPropertyProjectionList(elementType, nestingLevel, maxNestingLevel);

                                // Nested Reference
                                var fieldList = fields.Select(f => f.StartsWith("asset") ? $"asset->{{{SanityConstants.SPREAD_OPERATOR}}}" : f).Aggregate((c, n) => c + "," + n);
                                projection = $"{fieldRef}[]{{{fieldList}}}";
                            }
                        }
                    }
                }
            }
        }

        // CASE 7: Fallback case: not nested / not strongly typed
        if (!string.IsNullOrEmpty(projection))
        {
            return projection;
        }

        var enumerableType = propertyType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        var isEnumerable = enumerableType != null;
        if (isEnumerable)
        {
            var elemType = enumerableType!.GetGenericArguments()[0];
            var fields = GetPropertyProjectionList(elemType, nestingLevel, maxNestingLevel);
            if (fields.Count > 0)
            {
                // Other strongly typed includes
                var fieldList = fields.Aggregate((c, n) => c + "," + n);
                projection = $"{fieldRef}[]{{{fieldList},{SanityConstants.DEREFERENCING_SWITCH + "{" + fieldList + "}"}}}";
            }
            else
            {
                // "object" without any fields defined
                projection = $"{fieldRef}[]->";
            }
        }
        else
        {
            var fields = GetPropertyProjectionList(propertyType, nestingLevel, maxNestingLevel);
            if (fields.Count > 0)
            {
                // Other strongly typed includes
                var fieldList = fields.Aggregate((c, n) => c + "," + n);
                projection = $"{fieldRef}{{{fieldList},{SanityConstants.DEREFERENCING_SWITCH + "{" + fieldList + "}"}}}";
            }
            else
            {
                // "object" without any fields defined
                projection = $"{fieldRef}{{{SanityConstants.SPREAD_OPERATOR},{SanityConstants.DEREFERENCING_SWITCH + "{" + SanityConstants.SPREAD_OPERATOR + "}"}}}";
            }
        }

        return projection;
    }

    private static List<string> GetPropertyProjectionList(Type type, int nestingLevel, int maxNestingLevel)
    {
        var props = type.GetProperties().Where(p => p.CanWrite);
        if (nestingLevel == maxNestingLevel)
        {
            return ["..."];
        }

        // "Include all" primitive types with a simple ...
        var result = new List<string> { "..." };

        foreach (var prop in props)
        {
            var isIgnored = prop.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Length > 0;
            if (isIgnored)
            {
                continue;
            }

            var targetName = (prop.GetCustomAttributes(typeof(JsonPropertyAttribute), true).FirstOrDefault() as JsonPropertyAttribute)?.PropertyName ?? prop.Name.ToCamelCase();
            var includeAttr = prop.GetCustomAttributes<IncludeAttribute>(true).FirstOrDefault();
            var sourceName = !string.IsNullOrEmpty(includeAttr?.FieldName) ? includeAttr.FieldName : targetName;
            var fieldRef = targetName == sourceName ? sourceName : $"\"{targetName}\": {sourceName}";
            var isIncluded = includeAttr != null;
            if (isIncluded)
            {
                // Add a join projection for [Include]d properties
                result.Add(GetJoinProjection(sourceName, targetName, prop.PropertyType, nestingLevel + 1, maxNestingLevel));
            }
            else if (prop.PropertyType.IsClass && prop.PropertyType != typeof(string))
            {
                var listInterface = prop.PropertyType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
                var isList = listInterface != null;
                if (isList)
                {
                    // Array Case: Recursively add projection list for class types
                    var elementType = listInterface!.GetGenericArguments()[0];

                    // Avoid recursion for special case of JObject
                    if (elementType == typeof(JObject))
                    {
                        continue;
                    }

                    var fieldList = GetPropertyProjectionList(elementType, nestingLevel + 1, maxNestingLevel);
                    var listItemProjection = fieldList.Aggregate((c, n) => c + "," + n);
                    if (listItemProjection != "...")
                    {
                        result.Add($"{fieldRef}[]{{{listItemProjection}}}");
                    }
                }
                else
                {
                    if (prop.PropertyType == typeof(JObject))
                    {
                        result.Add($"{fieldRef}{{...}}");
                    }
                    // Object Case: Recursively add projection list for class types
                    var fieldList = GetPropertyProjectionList(prop.PropertyType, nestingLevel + 1, maxNestingLevel);
                    result.Add($"{fieldRef}{{{fieldList.Aggregate((c, n) => c + "," + n)}}}");
                }
            }
        }
        return result;
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
                {
                    if (e.Object is null)
                    {
                        throw new Exception("StartsWith must be called on an instance.");
                    }
                    var memberName = TransformOperand(e.Object);
                    var value = "";
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
            case "Contains":
                {
                    // Support both instance method (list.Contains(x)) and static Enumerable.Contains(list, x)
                    Expression? collectionExpr = null;
                    Expression? valueExpr = null;
                    if (e is { Object: not null, Arguments.Count: 1 })
                    {
                        collectionExpr = e.Object;
                        valueExpr = e.Arguments[0];
                    }
                    else if (e.Object == null && e.Arguments.Count == 2)
                    {
                        collectionExpr = e.Arguments[0];
                        valueExpr = e.Arguments[1];
                    }

                    if (collectionExpr != null && valueExpr != null)
                    {
                        // Try evaluate collectionExpr to a constant IEnumerable (closure/local/constant)
                        object? enumerableObj = null;
                        var simplified = Evaluator.PartialEval(collectionExpr);
                        if (simplified is ConstantExpression c1)
                        {
                            enumerableObj = c1.Value;
                        }
                        else
                        {
                            try { enumerableObj = Expression.Lambda(simplified).Compile().DynamicInvoke(); } catch { }
                        }

                        if (enumerableObj is IEnumerable ie1 and not string)
                        {
                            // Case: titles.Contains(p.Title)
                            var memberName = TransformOperand(valueExpr);
                            var values = ie1.Cast<object?>().Where(o => o != null).ToArray();
                            if (values.Length == 0)
                            {
                                return $"{memberName} in []";
                            }
                            var first = values[0]!;
                            var t = first.GetType();
                            if (t == typeof(string) || t == typeof(Guid))
                            {
                                return $"{memberName} in [\"{string.Join("\",\"", values)}\"]";
                            }
                            return $"{memberName} in [{string.Join(",", values)}]";
                        }

                        // Fallback: property.Contains(constant) represented as Enumerable.Contains(property, constant)
                        if (collectionExpr is MemberExpression && valueExpr is not MemberExpression)
                        {
                            var memberName = TransformOperand(collectionExpr);
                            var simplifiedValue = Evaluator.PartialEval(valueExpr);
                            if (simplifiedValue is ConstantExpression { Value: not null } c2)
                            {
                                if (c2.Type == typeof(string) || c2.Type == typeof(Guid))
                                {
                                    return $"\"{c2.Value}\" in {memberName}";
                                }

                                return $"{c2.Value} in {memberName}";
                            }
                        }

                        // New: property.Contains(otherProperty) -> "otherProperty in property"
                        // Also handle boxing/unboxing conversions (UnaryExpression Convert)
                        var collUnwrapped = collectionExpr is UnaryExpression { NodeType: ExpressionType.Convert } uc ? uc.Operand : collectionExpr;
                        var valUnwrapped = valueExpr is UnaryExpression { NodeType: ExpressionType.Convert } uv ? uv.Operand : valueExpr;
                        if (collUnwrapped is MemberExpression && valUnwrapped is MemberExpression)
                        {
                            var left = TransformOperand(collUnwrapped);
                            var right = TransformOperand(valUnwrapped);
                            return $"{right} in {left}";
                        }

                        // Generic fallback: attempt to translate both operands
                        // and emit `<value> in <collection>` when both sides are resolvable.
                        var leftAny = TransformOperand(collectionExpr);
                        var rightAny = TransformOperand(valueExpr);
                        if (!string.IsNullOrEmpty(leftAny) && !string.IsNullOrEmpty(rightAny))
                        {
                            return $"{rightAny} in {leftAny}";
                        }
                    }
                    // .Where(p => p.Tags.Contains("Alians"))
                    // *[tags[] match "Aliens"]
                    else if (e.Arguments.Count == 2)
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

                        if (c2.Type == typeof(string) || c2.Type == typeof(Guid))
                        {
                            return $"\"{valueObj}\" in {memberName}";
                        }

                        return $"{valueObj} in {memberName}";
                    }
                    else if (e.Arguments.Count == 2)
                    {
                        var memberName1 = TransformOperand(e.Arguments[0]);
                        var memberName2 = TransformOperand(e.Arguments[1]);
                        return $"{memberName2} in {memberName1}";
                    }
                    throw new Exception("'Contains' is only supported for simple expressions with non-null values.");
                }
            case "GetValue`1":
            case "GetValue":
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
            case "Where":
                {
                    //Arg 0: Source
                    var elementType = TypeSystem.GetElementType(e.Arguments[0].Type);
                    if (elementType != DocType)
                    {
                        throw new Exception("Where expressions are only supported on the root type.");
                    }
                    Visit(e.Arguments[0]);

                    //Arg 1: Query / lambda
                    if (e.Arguments[1] is not UnaryExpression { Operand: LambdaExpression l })
                    {
                        throw new Exception("Syntax of Select expression not supported.");
                    }

                    var constraint = TransformOperand(l.Body);
                    QueryBuilder.Constraints.Add(constraint);
                    return constraint;
                }
            case "Select":
                {
                    //Arg 0: Source
                    Visit(e.Arguments[0]);

                    //Arg 1: Select expression
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
            case "Include":
                {
                    //Arg 0: Source

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
                    var projection = GetJoinProjection(sourceName, targetName, propertyType, 0, MaxNestingLevel);
                    QueryBuilder.Includes[fieldPath] = projection;
                    return projection;
                }
            case "IsNullOrEmpty":
                {
                    var field = TransformOperand(e.Arguments[0]);
                    return $"{field} == null || {field} == \"\" || !(defined({field}))";
                }
            case "_id":
            case "SanityId":
                {
                    return "_id";
                }
            case "_createdAt":
            case "SanityCreatedAt":
                {
                    return "_createdAt";
                }
            case "_updatedAt":
            case "SanityUpdatedAt":
                {
                    return "_updatedAt";
                }
            case "_rev":
            case "SanityRevision":
                {
                    return "_rev";
                }
            case "_type":
            case "SanityType":
                {
                    return "_type";
                }
            case "IsDefined":
                {
                    var field = TransformOperand(e.Arguments[0]);
                    return $"defined({field})";
                }
            case "IsDraft":
                {
                    return "_id in path(\"drafts.**\")";
                }
            case "Cast":
                {
                    //Arg 0: Source
                    Visit(e.Arguments[0]);
                    return "";
                }
            case "OrderBy":
            case "ThenBy":
                {
                    //Arg 0: Source
                    Visit(e.Arguments[0]);

                    // Args[1] Order expression
                    if (e.Arguments[1] is not UnaryExpression { Operand: LambdaExpression l })
                    {
                        throw new Exception("Order by expression not supported.");
                    }

                    var declaringType = l.Parameters[0].Type;
                    if (declaringType != DocType)
                    {
                        throw new Exception($"Ordering is only supported on root document type {DocType.Name ?? ""}");
                    }
                    var ordering = TransformOperand(l.Body) + " asc";
                    QueryBuilder.Orderings.Add(ordering);
                    return ordering;
                }
            case "OrderByDescending":
            case "ThenByDescending":
                {
                    //Arg 0: Source
                    Visit(e.Arguments[0]);

                    // Args[1] Order expression
                    if (e.Arguments[1] is not UnaryExpression { Operand: LambdaExpression l })
                    {
                        throw new Exception("Order by descending expression not supported.");
                    }

                    var declaringType = l.Parameters[0].Type;
                    if (declaringType != DocType)
                    {
                        throw new Exception($"Ordering is only supported on root document type {DocType.Name ?? ""}");
                    }
                    var ordering = TransformOperand(l.Body) + " desc";
                    QueryBuilder.Orderings.Add(ordering);
                    return ordering;
                }
            case "Count":
            case "LongCount":
                {
                    //Arg 0: Source
                    Visit(e.Arguments[0]);
                    const string function = "count";
                    QueryBuilder.AggregateFunction = function;
                    return function;
                }
            case "Take":
                {
                    //Arg 0: Source
                    Visit(e.Arguments[0]);

                    //Arg 1: take
                    if (e.Arguments[1] is not ConstantExpression { Value: int take })
                    {
                        throw new Exception("Format for Take expression not supported.");
                    }

                    QueryBuilder.Take = take;
                    return QueryBuilder.Take.ToString();
                }
            case "Skip":
                {
                    //Arg 0: Source
                    Visit(e.Arguments[0]);

                    //Arg 1: take
                    if (e.Arguments[1] is not ConstantExpression { Value: int skip })
                    {
                        throw new Exception("Format for Skip expression not supported.");
                    }

                    QueryBuilder.Skip = skip;
                    return QueryBuilder.Skip.ToString();
                }
            case "op_Implicit":
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
                    // Best effort: fall back to visiting all args and returning the first
                    if (e.Arguments is { Count: > 0 })
                    {
                        return TransformOperand(e.Arguments[0]);
                    }
                    throw new Exception("op_Implicit method shape not supported.");
                }
            default:
                {
                    throw new Exception($"Method call {e.Method.Name} not supported.");
                }
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
                    var props = (nw.Members ?? System.Linq.Enumerable.Empty<MemberInfo>()).Select(prop => prop.Name.ToCamelCase()).ToArray();
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
            case ConstantExpression c when c.Value == null:
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
        if (u.NodeType == ExpressionType.Not)
        {
            return "!(" + TransformOperand(u.Operand) + ")";
        }
        if (u.NodeType == ExpressionType.Convert)
        {
            return TransformOperand(u.Operand);
        }
        throw new Exception($"Unary expression of type {u.GetType()} and nodeType {u.NodeType} not supported. ");
    }

    private static class SanityConstants
    {
        public const string ARRAY_INDICATOR = "[]";
        public const string COLON = ":";
        public const string DEREFERENCING_OPERATOR = "->";
        public const string DEREFERENCING_SWITCH = "_type=='reference'=>@->";
        public const string SPREAD_OPERATOR = "...";
        public const string STRING_DELIMITOR = "\"";
    }

    private sealed class SanityQueryBuilder
    {
        private readonly Dictionary<string, string> GroqTokens = new()
        {
            { SanityConstants.DEREFERENCING_SWITCH, "__0001__" },
            { SanityConstants.DEREFERENCING_OPERATOR, "__0002__" },
            { SanityConstants.STRING_DELIMITOR, "__0003__" },
            { SanityConstants.COLON, "__0004__" },
            { SanityConstants.SPREAD_OPERATOR, "__0005__" },
            { SanityConstants.ARRAY_INDICATOR, "__0006__" },
        };

        public string AggregateFunction { get; set; } = "";
        public List<string> Constraints { get; } = [];
        public Type? DocType { get; set; }
        public Dictionary<string, string> Includes { get; set; } = new();
        public List<string> Orderings { get; set; } = [];
        public string Projection { get; set; } = "";
        public Type? ResultType { get; set; }
        public int Skip { get; set; }
        public int Take { get; set; }

        public string Build(bool includeProjections, int maxNestingLevel)
        {
            var sb = new StringBuilder();
            // Select all
            sb.Append("*");

            // Add document type contraint
            if (DocType != null && DocType != typeof(object) && DocType != typeof(SanityDocument))
            {
                var rootTypeName = DocType!.GetSanityTypeName();
                try
                {
                    var dummyDoc = Activator.CreateInstance(DocType);
                    var typeName = dummyDoc?.SanityType();
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        rootTypeName = typeName;
                    }
                }
                catch
                {
                    // ignored
                }

                Constraints.Insert(0, $"_type == \"{rootTypeName}\"");
            }

            // Add constraints
            if (Constraints.Count > 0)
            {
                sb.Append('[');
                sb.Append(Constraints.Distinct().Aggregate((c, n) => $"({c}) && ({n})"));
                sb.Append(']');
            }

            if (includeProjections)
            {
                var projection = Projection;

                // NOTE: This block is most likely redundant (already covered by GetPropertyProjectionList below)
                // Attribute based includes ([Include])
                // TODO: Note similar logic in TransformMethodCallExpression -- could be refactored to consolidate
                //if (DocType != null)
                //{
                //    var properties = DocType.GetProperties();
                //    var includedProps = properties.Where(p => p.GetCustomAttributes<IncludeAttribute>(true).FirstOrDefault() != null).ToList();
                //    foreach (var prop in includedProps)
                //    {
                //        var includeAttr = prop.GetCustomAttributes<IncludeAttribute>(true).FirstOrDefault();
                //        var targetName = (prop.GetCustomAttributes(typeof(JsonPropertyAttribute), true).FirstOrDefault() as JsonPropertyAttribute)?.PropertyName ?? prop.Name.ToCamelCase();
                //        var sourceName = !string.IsNullOrEmpty(includeAttr.FieldName) ? includeAttr.FieldName : targetName;
                //        if (!Includes.ContainsKey(targetName))
                //        {
                //            Includes.Add(targetName, GetJoinProjection(sourceName, targetName, prop.PropertyType));
                //        }
                //    }
                //}

                // Add joins / includes
                if (string.IsNullOrEmpty(projection))
                {
                    // Joins require an explicit projection
                    var propertyList = GetPropertyProjectionList(ResultType ?? DocType ?? typeof(object), 0, maxNestingLevel);
                    projection = propertyList.Count switch
                    {
                        > 0 => propertyList.Aggregate((c, n) => c + "," + n),
                        _ => Includes.Keys.Aggregate((c, n) => c + "," + n)
                    };
                }

                // Add projection
                if (!string.IsNullOrEmpty(projection))
                {
                    projection = ExpandIncludesInProjection(projection, Includes);
                    projection = projection.Replace($"{{{SanityConstants.SPREAD_OPERATOR}}}", ""); // Remove redundant {...} to simplify query
                    if (projection != $"{{{SanityConstants.SPREAD_OPERATOR}}}") // Don't need to add an empty projection
                    {
                        // Ensure outer braces present; ExpandIncludesInProjection returns braces only when includes > 0
                        var hasBraces = projection.StartsWith('{') && projection.EndsWith('}');
                        if (!hasBraces)
                        {
                            sb.Append(" {");
                            sb.Append(projection);
                            sb.Append('}');
                        }
                        else
                        {
                            sb.Append(projection);
                        }
                    }
                }
            }

            // Add orderings
            if (Orderings.Count > 0)
            {
                sb.Append(" | order(" + Orderings.Aggregate((c, n) => $"{c}, {n}") + ")");
            }

            // Add slices
            if (Take > 0)
            {
                if (Take == 1)
                {
                    sb.Append($" [{Skip}]");
                }
                else
                {
                    sb.Append($" [{Skip}..{Skip + Take - 1}]");
                }
            }
            else
            {
                if (Skip > 0)
                {
                    sb.Append($" [{Skip}..{int.MaxValue}]");
                }
            }

            // Wrap with Aggregate function
            if (!string.IsNullOrEmpty(AggregateFunction))
            {
                sb.Insert(0, AggregateFunction + "(");
                sb.Append(')');
            }

            return sb.ToString();
        }

        private string ExpandIncludesInProjection(string projection, Dictionary<string, string> includes)
        {
            // Finds and replaces includes in projection by converting projection (GROQ) to an equivelant JSON representation,
            // modifying the JSON replacement and then converting back to GROQ.
            //
            // The reason for converting to JSON is simply to be able to work with the query in a hierarchical structure.
            // This could also be done creating some sort of query tree object, which might be a more appropriate / cleaner solution.

            var jsonProjection = GroqToJson($"{{{projection}}}");
            if (JsonConvert.DeserializeObject(jsonProjection) is not JObject jObjectProjection || includes.Count == 0)
            {
                return projection;
            }

            foreach (var includeKey in Includes.Keys.OrderBy(k => k))
            {
                var jsonInclude = GroqToJson($"{{{Includes[includeKey]}}}");
                var jObjectInclude = JsonConvert.DeserializeObject(jsonInclude) as JObject;
                if (jObjectInclude is null)
                {
                    continue;
                }

                var pathParts = includeKey
                    .Replace(SanityConstants.COLON, GroqTokens[SanityConstants.COLON])
                    .Replace(SanityConstants.STRING_DELIMITOR, GroqTokens[SanityConstants.STRING_DELIMITOR])
                    .Replace(SanityConstants.ARRAY_INDICATOR, GroqTokens[SanityConstants.ARRAY_INDICATOR])
                    .Replace(SanityConstants.DEREFERENCING_SWITCH, GroqTokens[SanityConstants.DEREFERENCING_SWITCH])
                    .Replace(SanityConstants.DEREFERENCING_OPERATOR, ".")
                    .TrimEnd('.').Split('.');

                var obj = jObjectProjection;
                for (var i = 0; i < pathParts.Length; i++)
                {
                    var part = pathParts[i];
                    var isLast = i == pathParts.Length - 1;
                    if (!isLast)
                    {
                        // Traverse / construct path to property
                        var propertyExists = false;
                        foreach (var property in obj)
                        {
                            if (property.Key != part
                                && !property.Key.StartsWith(
                                    $"{GroqTokens[SanityConstants.STRING_DELIMITOR]}{part}{GroqTokens[SanityConstants.STRING_DELIMITOR]}")
                                && !property.Key.StartsWith(part + GroqTokens[SanityConstants.ARRAY_INDICATOR])
                                && !property.Key.StartsWith(part + GroqTokens[SanityConstants.DEREFERENCING_OPERATOR]))
                            {
                                continue;
                            }

                            if (obj[property.Key] is not JObject next)
                            {
                                continue;
                            }

                            obj = next;
                            propertyExists = true;
                            break;
                            // if not a JObject, continue searching
                        }

                        if (propertyExists)
                        {
                            continue;
                        }

                        var nextJObject = new JObject();
                        obj[part] = nextJObject;
                        obj = nextJObject;
                    }
                    else
                    {
                        // Remove previous representations of field (typically without a projection)
                        var fieldsToReplace = new List<string>();
                        foreach (var property in obj)
                        {
                            if (property.Key == part
                                || property.Key.StartsWith($"{GroqTokens[SanityConstants.STRING_DELIMITOR]}{part}{GroqTokens[SanityConstants.STRING_DELIMITOR]}")
                                || property.Key.StartsWith(part + GroqTokens[SanityConstants.ARRAY_INDICATOR])
                                || property.Key.StartsWith(part + GroqTokens[SanityConstants.DEREFERENCING_OPERATOR]))
                            {
                                fieldsToReplace.Add(property.Key);
                            }
                        }
                        foreach (var key in fieldsToReplace)
                        {
                            obj.Remove(key);
                        }

                        // Set field to new projection
                        foreach (var include in jObjectInclude)
                        {
                            if (include.Key != part
                                && !include.Key.StartsWith(
                                    $"{GroqTokens[SanityConstants.STRING_DELIMITOR]}{part}{GroqTokens[SanityConstants.STRING_DELIMITOR]}")
                                && !include.Key.StartsWith(part + GroqTokens[SanityConstants.ARRAY_INDICATOR])
                                && !include.Key.StartsWith(part + GroqTokens[SanityConstants.DEREFERENCING_OPERATOR]))
                            {
                                continue;
                            }

                            obj[include.Key] = include.Value;
                            break;
                        }
                    }
                }
            }

            // Convert back to JSON
            jsonProjection = jObjectProjection.ToString(Formatting.None);
            // Convert JSON back to GROQ query
            projection = JsonToGroq(jsonProjection);

            return projection;
        }

        private string GroqToJson(string groq)
        {
            var json = groq.Replace(" ", "");
            foreach (var token in GroqTokens.Keys.OrderBy(k => GroqTokens[k]))
            {
                json = json.Replace(token, GroqTokens[token]);
            }
            json = json.Replace("{", ":{").TrimStart(':');

            // Replace variable names with valid json (e.g. convert myField to "myField":true)
            var reVariables = new Regex("(,|{)([^\"}:,]+)(,|})");
            var reMatches = reVariables.Matches(json);
            while (reMatches.Count > 0)
            {
                foreach (Match match in reMatches)
                {
                    var fieldName = match.Groups[2].Value;
                    var fieldReplacement = $"\"{fieldName}\":true";
                    json = json.Replace(match.Value, match.Value.Replace(fieldName, fieldReplacement));
                }

                reMatches = reVariables.Matches(json);
            }

            return json;
        }

        private string JsonToGroq(string json)
        {
            var groq = json
                .Replace(":{", "{")
                .Replace(":true", "")
                .Replace("\"", "");
            foreach (var token in GroqTokens.Keys)
            {
                groq = groq.Replace(GroqTokens[token], token);
            }
            return groq;
        }
    }
}