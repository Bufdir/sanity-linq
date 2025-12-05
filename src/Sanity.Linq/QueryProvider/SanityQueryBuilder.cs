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
        // Build field reference (alias if needed)
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

        // CASE 1: SanityReference<T>
        if (IsSanityReferenceType(propertyType))
        {
            var fields = GetPropertyProjectionList(propertyType.GetGenericArguments()[0], nestingLevel, maxNestingLevel);
            var fieldList = JoinComma(fields);
            return $"{fieldRef}->{{ {fieldList} }}";
        }

        // CASE 2: IEnumerable<SanityReference<T>>
        if (IsListOfSanityReference(propertyType, out var refElement))
        {
            var fields = GetPropertyProjectionList(refElement!, nestingLevel, maxNestingLevel);
            var fieldList = JoinComma(fields);
            return $"{fieldRef}[]->{{{fieldList}}}";
        }

        var nestedProperties = propertyType.GetProperties();

        // CASE 3: Image.Asset
        if (HasSanityImageAsset(nestedProperties, out var sanityImageAssetProperty))
        {
            var fields = GetPropertyProjectionList(propertyType, nestingLevel, maxNestingLevel);
            var assetProp = sanityImageAssetProperty!;
            var nestedFields = GetPropertyProjectionList(assetProp.PropertyType, nestingLevel, maxNestingLevel);
            var fieldList = fields
                .Select(f => f.StartsWith("asset")
                    ? $"asset->{(nestedFields.Count > 0 ? ("{" + JoinComma(nestedFields) + "}") : "")}"
                    : f)
                .Aggregate((c, n) => c + "," + n);
            return $"{fieldRef}{{{fieldList}}}";
        }

        // CASE 4: Property->SanityReference<T>
        var nestedSanityReferenceProperty = FindNestedSanityReference(nestedProperties);
        if (nestedSanityReferenceProperty is { } nsr)
        {
            var propertyName = nsr.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName ?? nsr.Name.ToCamelCase();
            var fields = GetPropertyProjectionList(propertyType, nestingLevel, maxNestingLevel);
            var nsrElementType = nsr.PropertyType.GetGenericArguments()[0];
            var nestedFields = GetPropertyProjectionList(nsrElementType, nestingLevel + 1, maxNestingLevel);
            var fieldList = fields
                .Select(f => f == propertyName
                    ? $"{propertyName}->{(nestedFields.Count > 0 ? ("{" + JoinComma(nestedFields) + "}") : "")}"
                    : f)
                .Aggregate((c, n) => c + "," + n);
            return $"{fieldRef}{{{fieldList}}}";
        }

        // CASE 5: Property->List<SanityReference<T>>
        if (FindNestedListOfSanityReference(nestedProperties, out var propertyInfo, out var listElementType))
        {
            var listProp = propertyInfo!;
            var elemType = listElementType!;
            var propertyName = listProp.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName ?? listProp.Name.ToCamelCase();
            var fields = GetPropertyProjectionList(propertyType, nestingLevel, maxNestingLevel);
            var nestedFields = GetPropertyProjectionList(elemType, nestingLevel + 1, maxNestingLevel);
            var fieldList = fields
                .Select(f => f == propertyName
                    ? $"{propertyName}[]->{(nestedFields.Count > 0 ? ("{" + JoinComma(nestedFields) + "}") : "")}"
                    : f)
                .Aggregate((c, n) => c + "," + n);
            return $"{fieldRef}{{{fieldList}}}";
        }

        // CASE 6: Array of objects with "asset" field (e.g., images)
        if (IsListOfSanityImages(propertyType, out var imgElementType))
        {
            var fields = GetPropertyProjectionList(imgElementType!, nestingLevel, maxNestingLevel);
            var fieldList = fields.Select(f => f.StartsWith("asset") ? $"asset->{{{SanityConstants.SPREAD_OPERATOR}}}" : f)
                                  .Aggregate((c, n) => c + "," + n);
            return $"{fieldRef}[]{{{fieldList}}}";
        }

        // CASE 7: Fallback case: not nested / not strongly typed
        if (TryGetEnumerableElementType(propertyType, out var enumerableType))
        {
            var fields = GetPropertyProjectionList(enumerableType!, nestingLevel, maxNestingLevel);
            if (fields.Count <= 0)
            {
                return $"{fieldRef}[]->";
            }

            var fieldList = JoinComma(fields);
            return $"{fieldRef}[]{{{fieldList},{SanityConstants.DEREFERENCING_SWITCH + "{" + fieldList + "}"}}}";
        }

        // Non-enumerable object fallback
        {
            var fields = GetPropertyProjectionList(propertyType, nestingLevel, maxNestingLevel);
            if (fields.Count <= 0)
            {
                return $"{fieldRef}{{{SanityConstants.SPREAD_OPERATOR},{SanityConstants.DEREFERENCING_SWITCH + "{" + SanityConstants.SPREAD_OPERATOR + "}"}}}";
            }

            var fieldList = JoinComma(fields);
            return $"{fieldRef}{{{fieldList},{SanityConstants.DEREFERENCING_SWITCH + "{" + fieldList + "}"}}}";
        }
    }

    /// <summary>
    /// Generates a list of property projections for a given type, considering nesting levels and maximum allowed nesting depth.
    /// </summary>
    /// <param name="type">The type for which property projections are to be generated.</param>
    /// <param name="nestingLevel">The current nesting level in the projection hierarchy.</param>
    /// <param name="maxNestingLevel">The maximum allowed nesting level for projections.</param>
    /// <returns>A list of strings representing the property projections for the specified type.</returns>
    /// <remarks>
    /// This method recursively processes the properties of the specified type, applying rules for inclusion, 
    /// handling complex types, collections, and attributes such as <see cref="JsonIgnoreAttribute"/> and <see cref="JsonPropertyAttribute"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if the <paramref name="type"/> parameter is null.</exception>
    public static List<string> GetPropertyProjectionList(Type type, int nestingLevel, int maxNestingLevel)
    {
        if (nestingLevel == maxNestingLevel)
        {
            return ["..."];
        }

        // "Include all" primitive types with a simple ...
        var result = new List<string> { "..." };

        foreach (var prop in type.GetProperties().Where(p => p.CanWrite))
        {
            // Skip ignored
            if (prop.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Length > 0)
            {
                continue;
            }

            var targetName = (prop.GetCustomAttributes(typeof(JsonPropertyAttribute), true).FirstOrDefault() as JsonPropertyAttribute)?.PropertyName
                             ?? prop.Name.ToCamelCase();
            var includeAttr = prop.GetCustomAttributes<IncludeAttribute>(true).FirstOrDefault();
            var sourceName = !string.IsNullOrEmpty(includeAttr?.FieldName) ? includeAttr.FieldName : targetName;
            var fieldRef = targetName == sourceName ? sourceName : $"\"{targetName}\": {sourceName}";

            // Explicit include → delegate to GetJoinProjection
            if (includeAttr != null)
            {
                result.Add(GetJoinProjection(sourceName, targetName, prop.PropertyType, nestingLevel + 1, maxNestingLevel));
                continue;
            }

            // Only complex classes (non-string) need further processing
            if (!(prop.PropertyType.IsClass && prop.PropertyType != typeof(string)))
            {
                continue;
            }

            // Handle lists/arrays of complex types
            if (TryGetEnumerableElementType(prop.PropertyType, out var elementType))
            {
                // Avoid recursion for a special case of JObject
                if (elementType == typeof(JObject))
                {
                    continue;
                }

                var fieldList = GetPropertyProjectionList(elementType!, nestingLevel + 1, maxNestingLevel);
                var listItemProjection = JoinComma(fieldList);
                if (listItemProjection != "...")
                {
                    result.Add($"{fieldRef}[]{{{listItemProjection}}}");
                }
                continue;
            }

            // JObject special handling: do not expand explicit projection, rely on top-level "..."
            if (prop.PropertyType == typeof(JObject))
            {
                continue;
            }

            // Object case: recursively add a projection list
            {
                var fieldList = GetPropertyProjectionList(prop.PropertyType, nestingLevel + 1, maxNestingLevel);
                result.Add($"{fieldRef}{{{JoinComma(fieldList)}}}");
            }
        }

        return result;
    }

    /// <summary>
    /// Constructs a query string based on the current state of the query builder.
    /// </summary>
    /// <param name="includeProjections">
    /// A boolean value indicating whether to include projections in the query.
    /// </param>
    /// <param name="maxNestingLevel">
    /// The maximum level of nesting allowed for projections.
    /// </param>
    /// <returns>
    /// A string representing the constructed query.
    /// </returns>
    /// <remarks>
    /// This method builds the query by combining constraints, projections, orderings, slices, 
    /// and an optional aggregate function. The resulting query is formatted as a string.
    /// </remarks>
    public string Build(bool includeProjections, int maxNestingLevel)
    {
        var sb = new StringBuilder();
        // Select all
        sb.Append("*");

        AddDocTypeConstraintIfAny();
        AppendConstraints(sb);

        if (includeProjections)
        {
            var projection = ResolveProjection(maxNestingLevel);
            AppendProjection(sb, projection);
        }

        AppendOrderings(sb);
        AppendSlices(sb);
        WrapWithAggregate(sb);

        return sb.ToString();
    }

    private static void EnsurePath(JObject root, IReadOnlyList<string> parts, Dictionary<string, string> tokens, out JObject parent)
    {
        var obj = root;
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var isLast = i == parts.Count - 1;
            if (isLast)
            {
                break;
            }

            if (TryFindChildObject(obj, part, tokens, out var next))
            {
                obj = next!;
                continue;
            }

            var nextJObject = new JObject();
            obj[part] = nextJObject;
            obj = nextJObject;
        }

        parent = obj;
    }

    private static bool FindNestedListOfSanityReference(PropertyInfo[] props, out PropertyInfo? prop, out Type? elementType)
    {
        prop = props.FirstOrDefault(p => p.PropertyType.GetInterfaces().Any(i => i.IsGenericType
                                                                                 && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                                                                                 && i.GetGenericArguments()[0].IsGenericType
                                                                                 && i.GetGenericArguments()[0].GetGenericTypeDefinition() == typeof(SanityReference<>)));
        if (prop != null)
        {
            var collectionType = prop.PropertyType.GetGenericArguments()[0];
            elementType = collectionType.GetGenericArguments()[0];
            return true;
        }
        elementType = null;
        return false;
    }

    private static PropertyInfo? FindNestedSanityReference(PropertyInfo[] props)
        => props.FirstOrDefault(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(SanityReference<>));

    private static bool HasSanityImageAsset(PropertyInfo[] props, out PropertyInfo? assetProp)
    {
        assetProp = props.FirstOrDefault(p => p.PropertyType.IsGenericType
                                              && p.PropertyType.GetGenericTypeDefinition() == typeof(SanityReference<>)
                                              && (p.Name.ToLower() == "asset"
                                                  || ((p.GetCustomAttributes<JsonPropertyAttribute>(true).FirstOrDefault())?.PropertyName?.Equals("asset")).GetValueOrDefault()));
        return assetProp != null;
    }

    private static bool IsListOfSanityImages(Type t, out Type? elementType)
    {
        elementType = null;
        var type = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType
                                                          && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                                                          && i.GetGenericArguments()[0].GetProperties().Any(p => p.PropertyType.IsGenericType
                                                              && p.PropertyType.GetGenericTypeDefinition() == typeof(SanityReference<>)
                                                              && (p.Name.ToLower() == "asset"
                                                                  || ((p.GetCustomAttributes<JsonPropertyAttribute>(true).FirstOrDefault())?.PropertyName?.Equals("asset")).GetValueOrDefault())));
        if (type == null)
        {
            return false;
        }

        elementType = type.GetGenericArguments()[0];
        return true;
    }

    private static bool IsListOfSanityReference(Type t, out Type? element)
    {
        element = null;
        if (!TryGetEnumerableElementType(t, out var et) || et is not { IsGenericType: true } || et.GetGenericTypeDefinition() != typeof(SanityReference<>))
        {
            return false;
        }

        element = et.GetGenericArguments()[0];
        return true;
    }

    private static bool IsSanityReferenceType(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(SanityReference<>);

    private static string JoinComma(IEnumerable<string> parts) => string.Join(",", parts);

    private static bool KeyMatchesPart(string key, string part, Dictionary<string, string> tokens)
    {
        // Matches: exact key, quoted key, array variant (part[]), or dereferencing (part->)
        return key == part
               || key.StartsWith($"{tokens[SanityConstants.STRING_DELIMITER]}{part}{tokens[SanityConstants.STRING_DELIMITER]}")
               || key.StartsWith(part + tokens[SanityConstants.ARRAY_INDICATOR])
               || key.StartsWith(part + tokens[SanityConstants.DEREFERENCING_OPERATOR]);
    }

    private static string[] ParseIncludePath(string includeKey, Dictionary<string, string> tokens)
    {
        return includeKey
            .Replace(SanityConstants.COLON, tokens[SanityConstants.COLON])
            .Replace(SanityConstants.STRING_DELIMITER, tokens[SanityConstants.STRING_DELIMITER])
            .Replace(SanityConstants.ARRAY_INDICATOR, tokens[SanityConstants.ARRAY_INDICATOR])
            .Replace(SanityConstants.DEREFERENCING_SWITCH, tokens[SanityConstants.DEREFERENCING_SWITCH])
            .Replace(SanityConstants.DEREFERENCING_OPERATOR, ".")
            .TrimEnd('.')
            .Split('.');
    }

    private static void ReplaceFieldWithInclude(JObject parent, string part, JObject includeObject, Dictionary<string, string> tokens)
    {
        // Remove previous representations of a field (typically without a projection)
        var fieldsToReplace = new List<string>();
        foreach (var property in parent)
        {
            if (KeyMatchesPart(property.Key, part, tokens))
            {
                fieldsToReplace.Add(property.Key);
            }
        }
        foreach (var key in fieldsToReplace)
        {
            parent.Remove(key);
        }

        // Set field to new projection (match key variant inside the include object)
        foreach (var include in includeObject)
        {
            if (!KeyMatchesPart(include.Key, part, tokens))
            {
                continue;
            }

            parent[include.Key] = include.Value;
            break;
        }
    }

    private static bool TryFindChildObject(JObject current, string part, Dictionary<string, string> tokens, out JObject? next)
    {
        foreach (var property in current)
        {
            if (!KeyMatchesPart(property.Key, part, tokens))
            {
                continue;
            }

            if (current[property.Key] is not JObject obj)
            {
                continue;
            }

            next = obj;
            return true;
        }

        next = null;
        return false;
    }

    private static bool TryGetEnumerableElementType(Type t, out Type? elementType)
    {
        var type = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (type != null)
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }
        elementType = null;
        return false;
    }

    private void AddDocTypeConstraintIfAny()
    {
        if (DocType == null || DocType == typeof(object) || DocType == typeof(SanityDocument))
        {
            return;
        }

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

    private void AppendConstraints(StringBuilder sb)
    {
        if (Constraints.Count <= 0)
        {
            return;
        }

        sb.Append('[');
        sb.Append(Constraints.Distinct().Aggregate((c, n) => $"({c}) && ({n})"));
        sb.Append(']');
    }

    private void AppendOrderings(StringBuilder sb)
    {
        if (Orderings.Count > 0)
        {
            sb.Append(" | order(" + Orderings.Aggregate((c, n) => $"{c}, {n}") + ")");
        }
    }

    private void AppendProjection(StringBuilder sb, string projection)
    {
        if (string.IsNullOrEmpty(projection))
        {
            return;
        }

        var expanded = ExpandIncludesInProjection(projection, Includes)
            .Replace($"{{{SanityConstants.SPREAD_OPERATOR}}}", "");

        if (expanded == $"{{{SanityConstants.SPREAD_OPERATOR}}}")
        {
            return; // Don't add an empty projection
        }

        var hasBraces = expanded.StartsWith('{') && expanded.EndsWith('}');
        if (!hasBraces)
        {
            sb.Append(" {");
            sb.Append(expanded);
            sb.Append('}');
        }
        else
        {
            sb.Append(expanded);
        }
    }

    private void AppendSlices(StringBuilder sb)
    {
        if (Take > 0)
        {
            sb.Append(Take == 1 ? $" [{Skip}]" : $" [{Skip}..{Skip + Take - 1}]");
        }
        else if (Skip > 0)
        {
            sb.Append($" [{Skip}..{int.MaxValue}]");
        }
    }

    private string ExpandIncludesInProjection(string projection, Dictionary<string, string> includes)
    {
        // Finds and replaces includes in a projection by converting projection (GROQ) to an equivalent JSON representation,
        // modifying the JSON replacement and then converting back to GROQ.
        //
        // The reason for converting to JSON is simply to be able to work with the query in a hierarchical structure.
        // This could also be done creating some sort of query tree object, which might be a more appropriate / cleaner solution.

        var jsonProjection = GroqToJson($"{{{projection}}}");
        if (JsonConvert.DeserializeObject(jsonProjection) is not JObject jObjectProjection || includes.Count == 0)
        {
            return projection;
        }

        // Use the includes provided via parameter, not the instance field
        foreach (var includeKey in includes.Keys.OrderBy(k => k))
        {
            var jsonInclude = GroqToJson($"{{{includes[includeKey]}}}");
            if (JsonConvert.DeserializeObject(jsonInclude) is not JObject jObjectInclude)
            {
                continue;
            }

            var pathParts = ParseIncludePath(includeKey, _groqTokens);

            // Traverse to parent
            EnsurePath(jObjectProjection, pathParts, _groqTokens, out var parent);

            // Replace or set the last segment
            var lastPart = pathParts[^1];
            ReplaceFieldWithInclude(parent, lastPart, jObjectInclude, _groqTokens);
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

        // Replace variable names with valid JSON (e.g., convert myField to "myField": true)
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

    private string ResolveProjection(int maxNestingLevel)
    {
        var projection = Projection;

        if (!string.IsNullOrEmpty(projection))
        {
            return projection;
        }

        // Joins require an explicit projection
        var propertyList = GetPropertyProjectionList(ResultType ?? DocType ?? typeof(object), 0, maxNestingLevel);
        projection = propertyList.Count > 0
            ? JoinComma(propertyList)
            : (Includes.Keys.Count > 0 ? Includes.Keys.Aggregate((c, n) => c + "," + n) : "");

        return projection;
    }

    private void WrapWithAggregate(StringBuilder sb)
    {
        if (string.IsNullOrEmpty(AggregateFunction))
        {
            return;
        }

        sb.Insert(0, AggregateFunction + "(");
        sb.Append(')');
    }
}