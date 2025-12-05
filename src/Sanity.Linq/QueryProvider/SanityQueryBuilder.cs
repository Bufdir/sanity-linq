using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sanity.Linq.CommonTypes;

namespace Sanity.Linq.QueryProvider;

internal sealed class SanityQueryBuilder
{
    private readonly Dictionary<string, string> _groqTokens = new()
    {
        { SanityConstants.DEREFERENCING_SWITCH, "__0001__" },
        { SanityConstants.DEREFERENCING_OPERATOR, "__0002__" },
        { SanityConstants.STRING_DELIMITER, "__0003__" },
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
    public static string GetJoinProjection(string sourceName, string targetName, Type propertyType, int nestingLevel, int maxNestingLevel)
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

    public static List<string> GetPropertyProjectionList(Type type, int nestingLevel, int maxNestingLevel)
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

    public string Build(bool includeProjections, int maxNestingLevel)
    {
        var sb = new StringBuilder();
        // Select all
        sb.Append("*");

        // Add document type constraint
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
                .Replace(SanityConstants.COLON, _groqTokens[SanityConstants.COLON])
                .Replace(SanityConstants.STRING_DELIMITER, _groqTokens[SanityConstants.STRING_DELIMITER])
                .Replace(SanityConstants.ARRAY_INDICATOR, _groqTokens[SanityConstants.ARRAY_INDICATOR])
                .Replace(SanityConstants.DEREFERENCING_SWITCH, _groqTokens[SanityConstants.DEREFERENCING_SWITCH])
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
                                $"{_groqTokens[SanityConstants.STRING_DELIMITER]}{part}{_groqTokens[SanityConstants.STRING_DELIMITER]}")
                            && !property.Key.StartsWith(part + _groqTokens[SanityConstants.ARRAY_INDICATOR])
                            && !property.Key.StartsWith(part + _groqTokens[SanityConstants.DEREFERENCING_OPERATOR]))
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
                            || property.Key.StartsWith($"{_groqTokens[SanityConstants.STRING_DELIMITER]}{part}{_groqTokens[SanityConstants.STRING_DELIMITER]}")
                            || property.Key.StartsWith(part + _groqTokens[SanityConstants.ARRAY_INDICATOR])
                            || property.Key.StartsWith(part + _groqTokens[SanityConstants.DEREFERENCING_OPERATOR]))
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
                                $"{_groqTokens[SanityConstants.STRING_DELIMITER]}{part}{_groqTokens[SanityConstants.STRING_DELIMITER]}")
                            && !include.Key.StartsWith(part + _groqTokens[SanityConstants.ARRAY_INDICATOR])
                            && !include.Key.StartsWith(part + _groqTokens[SanityConstants.DEREFERENCING_OPERATOR]))
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
        foreach (var token in _groqTokens.Keys.OrderBy(k => _groqTokens[k]))
        {
            json = json.Replace(token, _groqTokens[token]);
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
        foreach (var token in _groqTokens.Keys)
        {
            groq = groq.Replace(_groqTokens[token], token);
        }
        return groq;
    }
}