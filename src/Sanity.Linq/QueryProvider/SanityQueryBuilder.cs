using System.Diagnostics.CodeAnalysis;
using Sanity.Linq.CommonTypes;

namespace Sanity.Linq.QueryProvider;

internal sealed partial class SanityQueryBuilder
{
    private readonly Dictionary<string, string> _groqTokens = new()
    {
        { SanityConstants.DEREFERENCING_SWITCH, "__0001__" },
        { SanityConstants.DEREFERENCING_OPERATOR, "__0002__" },
        { SanityConstants.STRING_DELIMITER, "__0003__" },
        { SanityConstants.COLON, "__0004__" },
        { SanityConstants.SPREAD_OPERATOR, "__0005__" },
        { SanityConstants.ARRAY_INDICATOR, "__0006__" },
        { "[", "__0007__" },
        { "]", "__0008__" },
        { "(", "__0009__" },
        { ")", "__0010__" },
        { "@", "__0011__" },
        { ".", "__0012__" },
        { "==", "__0013__" },
        { "!=", "__0014__" },
        { "&&", "__0015__" },
        { "||", "__0016__" },
        { ">", "__0017__" },
        { "<", "__0018__" },
        { ">=", "__0019__" },
        { "<=", "__0020__" }
    };

    public string AggregateFunction { get; set; } = "";

    public string AggregatePostFix { get; set; } = "";

    public List<string> Constraints { get; } = [];

    public Type? DocType { get; set; }

    public Dictionary<string, string> Includes { get; set; } = new();

    public List<string> Orderings { get; set; } = [];

    public string Projection { get; set; } = "";

    public Type? ResultType { get; set; }

    public bool ExpectsArray { get; set; }

    public bool IsSilent { get; set; }

    public bool UseCoalesceFallback { get; set; } = true;

    public int Skip { get; set; }

    public int? Take { get; set; }

    /// <summary>
    ///     Constructs a projection string for a property to be included or joined in a query.
    ///     Handles various property types, including primitive types, strings,
    ///     nested objects, and collections of <see cref="SanityReference{T}" />.
    /// </summary>
    /// <param name="sourceName">The name of the source field in the query.</param>
    /// <param name="targetName">The name of the target field in the query.</param>
    /// <param name="propertyType">The type of the property being projected.</param>
    /// <param name="nestingLevel">The current nesting level of the projection.</param>
    /// <param name="maxNestingLevel">The maximum allowed nesting level for projections.</param>
    /// <param name="isExplicit">Indicates if the join is explicitly requested.</param>
    /// <returns>A string representing the projection for the specified property.</returns>
    public static string GetJoinProjection(string sourceName, string targetName, Type propertyType, int nestingLevel, int maxNestingLevel, bool isExplicit = false)
    {
        // Build field reference (alias if needed)
        var fieldRef = sourceName == targetName || string.IsNullOrEmpty(targetName)
            ? sourceName
            : $"\"{targetName}\":{sourceName}";

        // String or primitive
        if (propertyType == typeof(string) || propertyType.IsPrimitive) return fieldRef;

        // CASE 1: SanityReference<T>
        if (IsSanityReferenceType(propertyType)) return HandleSanityReferenceCase(fieldRef, propertyType, nestingLevel, maxNestingLevel);

        // CASE 2: IEnumerable<SanityReference<T>>
        if (IsListOfSanityReference(propertyType, out var refElement)) return HandleListOfSanityReferenceCase(fieldRef, refElement!, nestingLevel, maxNestingLevel);

        var nestedProperties = propertyType.GetProperties();

        // CASE 3: Image.Asset
        if (HasSanityImageAsset(nestedProperties, out var sanityImageAssetProperty)) return HandleImageAssetCase(fieldRef, propertyType, sanityImageAssetProperty!, nestingLevel, maxNestingLevel);

        // CASE 4: Array of objects with "asset" field (e.g., images)
        if (IsListOfSanityImages(propertyType, out var imgElementType)) return HandleListOfSanityImagesCase(fieldRef, imgElementType!, nestingLevel, maxNestingLevel);

        // CASE 5: Fallback case: enumerable or non-enumerable object
        return HandleGenericObjectCase(fieldRef, propertyType, nestingLevel, maxNestingLevel, isExplicit);
    }

    private static string HandleSanityReferenceCase(string fieldRef, Type propertyType, int nestingLevel, int maxNestingLevel)
    {
        var fields = GetPropertyProjectionList(propertyType.GetGenericArguments()[0], nestingLevel, maxNestingLevel);
        var fieldList = JoinComma(fields);
        return $"{fieldRef}{{{fieldList},{SanityConstants.DEREFERENCING_SWITCH + "{" + fieldList + "}"}}}";
    }

    private static string HandleListOfSanityReferenceCase(string fieldRef, Type refElement, int nestingLevel, int maxNestingLevel)
    {
        var fields = GetPropertyProjectionList(refElement, nestingLevel, maxNestingLevel);
        var fieldList = JoinComma(fields);
        
        var indicator = fieldRef.Contains(SanityConstants.ARRAY_INDICATOR) ? "" : SanityConstants.ARRAY_INDICATOR;
        var filter = fieldRef.Contains("defined") ? "" : SanityConstants.ARRAY_FILTER;
        
        return $"{fieldRef}{indicator}{filter}{{{fieldList},{SanityConstants.DEREFERENCING_SWITCH + "{" + fieldList + "}"}}}";
    }

    private static string HandleImageAssetCase(string fieldRef, Type propertyType, PropertyInfo assetProp, int nestingLevel, int maxNestingLevel)
    {
        var fields = GetPropertyProjectionList(propertyType, nestingLevel, maxNestingLevel);
        var nestedFields = GetPropertyProjectionList(assetProp.PropertyType, nestingLevel, maxNestingLevel);
        var projectedFields = fields
            .Select(f => f.StartsWith("asset")
                ? $"asset->{(nestedFields.Count > 0 ? "{" + JoinComma(nestedFields) + "}" : "")}"
                : f);
        var fieldList = JoinComma(projectedFields);
        return $"{fieldRef}{{{fieldList}}}";
    }

    private static string HandleListOfSanityImagesCase(string fieldRef, Type imgElementType, int nestingLevel, int maxNestingLevel)
    {
        var fields = GetPropertyProjectionList(imgElementType, nestingLevel, maxNestingLevel);
        var projectedFields = fields.Select(f => f.StartsWith("asset") ? $"asset->{{{SanityConstants.SPREAD_OPERATOR}}}" : f);
        var fieldList = JoinComma(projectedFields);
        var indicator = fieldRef.Contains(SanityConstants.ARRAY_INDICATOR) ? "" : SanityConstants.ARRAY_INDICATOR;
        var filter = fieldRef.Contains("defined") ? "" : SanityConstants.ARRAY_FILTER;
        return $"{fieldRef}{indicator}{filter}{{{fieldList}}}";
    }

    private static string HandleGenericObjectCase(string fieldRef, Type propertyType, int nestingLevel, int maxNestingLevel, bool isExplicit)
    {
        var isEnumerable = TryGetEnumerableElementType(propertyType, out var enumerableType);
        var targetType = isEnumerable ? enumerableType! : propertyType;
        var fields = GetPropertyProjectionList(targetType, nestingLevel, maxNestingLevel);
        var indicator = isEnumerable && !fieldRef.Contains(SanityConstants.ARRAY_INDICATOR) ? SanityConstants.ARRAY_INDICATOR : "";
        var filter = isEnumerable && !fieldRef.Contains("defined") ? SanityConstants.ARRAY_FILTER : "";
        var suffix = indicator + filter;

        if (fields.Count <= 0)
        {
            var projection = $"{fieldRef}{suffix}{{{SanityConstants.SPREAD_OPERATOR}}}";
            if (isExplicit)
            {
                projection = $"{fieldRef}{suffix}{{{SanityConstants.SPREAD_OPERATOR},{SanityConstants.DEREFERENCING_SWITCH + "{" + SanityConstants.SPREAD_OPERATOR + "}"}}}";
            }
            return projection;
        }

        var fieldList = JoinComma(fields);
        var baseProjection = $"{fieldRef}{suffix}{{{fieldList}}}";
        if (isExplicit)
        {
            baseProjection = $"{fieldRef}{suffix}{{{fieldList},{SanityConstants.DEREFERENCING_SWITCH + "{" + fieldList + "}"}}}";
        }
        return baseProjection;
    }

    /// <summary>
    ///     Generates a list of property projections for a given type, considering nesting levels and maximum allowed nesting
    ///     depth.
    /// </summary>
    /// <param name="type">The type for which property projections are to be generated.</param>
    /// <param name="nestingLevel">The current nesting level in the projection hierarchy.</param>
    /// <param name="maxNestingLevel">The maximum allowed nesting level for projections.</param>
    /// <returns>A list of strings representing the property projections for the specified type.</returns>
    /// <remarks>
    ///     This method recursively processes the properties of the specified type, applying rules for inclusion,
    ///     handling complex types, collections, and attributes such as <see cref="JsonIgnoreAttribute" /> and
    ///     <see cref="JsonPropertyAttribute" />.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if the <paramref name="type" /> parameter is null.</exception>
    public static List<string> GetPropertyProjectionList(Type type, int nestingLevel, int maxNestingLevel)
    {
        if (nestingLevel == maxNestingLevel) return ["..."];

        // "Include all" primitive types with a simple ...
        var result = new List<string> { "..." };

        foreach (var prop in type.GetProperties().Where(p => p.CanWrite))
        {
            if (ShouldSkipProperty(prop)) continue;

            var (sourceName, targetName) = ResolvePropertyNames(prop);

            if (TryHandleExplicitInclude(prop, sourceName, targetName, nestingLevel, maxNestingLevel, out var projection))
            {
                result.Add(projection);
                continue;
            }

            if (ShouldExpandComplexType(prop)) result.Add(GetJoinProjection(sourceName, targetName, prop.PropertyType, nestingLevel + 1, maxNestingLevel));
        }

        return result;
    }

    private static bool ShouldSkipProperty(PropertyInfo prop)
    {
        // Skip ignored
        if (prop.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Length > 0) 
            return true;

        // JObject special handling: do not expand explicit projection, rely on top-level "..."
        if (prop.PropertyType == typeof(JObject) || (TryGetEnumerableElementType(prop.PropertyType, out var et) && et == typeof(JObject))) 
            return true;

        return false;
    }

    private static (string SourceName, string TargetName) ResolvePropertyNames(PropertyInfo prop)
    {
        var targetName = (prop.GetCustomAttributes(typeof(JsonPropertyAttribute), true).FirstOrDefault() as JsonPropertyAttribute)?.PropertyName
                         ?? prop.Name.ToCamelCase();
        var includeAttr = prop.GetCustomAttributes<IncludeAttribute>(true).FirstOrDefault();
        var sourceName = !string.IsNullOrEmpty(includeAttr?.FieldName) ? includeAttr.FieldName : targetName;
        return (sourceName, targetName);
    }

    private static bool TryHandleExplicitInclude(PropertyInfo prop, string sourceName, string targetName, int nestingLevel, int maxNestingLevel, [NotNullWhen(true)] out string? projection)
    {
        var includeAttr = prop.GetCustomAttributes<IncludeAttribute>(true).FirstOrDefault();
        if (includeAttr != null)
        {
            projection = GetJoinProjection(sourceName, targetName, prop.PropertyType, nestingLevel + 1, maxNestingLevel, true);
            return true;
        }

        projection = null;
        return false;
    }

    private static bool ShouldExpandComplexType(PropertyInfo prop)
    {
        // Only complex classes (non-string) need further processing
        if (!prop.PropertyType.IsClass || prop.PropertyType == typeof(string)) return false;

        // Skip auto-expansion for Sanity references and images as they involve dereferencing, 
        // which should be controlled by [Include] or explicit .Include()
        if (IsSanityReferenceType(prop.PropertyType) || IsListOfSanityReference(prop.PropertyType, out _))
            return false;

        if (HasSanityImageAsset(prop.PropertyType.GetProperties(), out _) || IsListOfSanityImages(prop.PropertyType, out _))
            return false;

        return true;
    }

    /// <summary>
    ///     Constructs a query string based on the current state of the query builder.
    /// </summary>
    /// <param name="includeProjections">
    ///     A boolean value indicating whether to include projections in the query.
    /// </param>
    /// <param name="maxNestingLevel">
    ///     The maximum level of nesting allowed for projections.
    /// </param>
    /// <returns>
    ///     A string representing the constructed query.
    /// </returns>
    /// <remarks>
    ///     This method builds the query by combining constraints, projections, orderings, slices,
    ///     and an optional aggregate function. The resulting query is formatted as a string.
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

    private static void EnsurePath(JObject root, IReadOnlyList<string> parts, Dictionary<string, string> tokens, out List<JObject> parents)
    {
        var currentObjects = new List<JObject> { root };
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (i == parts.Count - 1) break;

            var nextObjects = new List<JObject>();
            foreach (var obj in currentObjects)
            {
                var matches = FindAllChildObjects(obj, part, tokens);
                if (matches.Count > 0)
                {
                    nextObjects.AddRange(matches);
                }
                else
                {
                    // throw new Exception($"ENSURE PATH FAILED to find {part} in {obj.ToString(Formatting.None)}");
                    var nextJObject = new JObject();
                    obj[part] = nextJObject;
                    nextObjects.Add(nextJObject);
                }
            }

            currentObjects = nextObjects.Distinct().ToList();
        }

        parents = currentObjects;
    }

    private static List<JObject> FindAllChildObjects(JObject current, string part, Dictionary<string, string> tokens)
    {
        var matches = new List<JObject>();
        foreach (var property in current)
        {
            if (KeyMatchesPart(property.Key, part, tokens))
                if (current[property.Key] is JObject obj)
                {
                    matches.Add(obj);

                    // Also check for a deref section inside this child
                    foreach (var childProp in obj)
                        if (childProp.Key.Contains(tokens[SanityConstants.DEREFERENCING_SWITCH]) ||
                            childProp.Key.Contains(tokens[SanityConstants.DEREFERENCING_OPERATOR]))
                        {
                            if (childProp.Value is JObject derefObj1)
                                matches.Add(derefObj1);
                        }
                }

            if (!property.Key.Contains(tokens[SanityConstants.DEREFERENCING_SWITCH]) &&
                !property.Key.Contains(tokens[SanityConstants.DEREFERENCING_OPERATOR]))
            {
                continue;
            }

            if (property.Value is JObject derefObj2)
                matches.AddRange(FindAllChildObjects(derefObj2, part, tokens));
        }

        return matches;
    }

    private static bool HasSanityImageAsset(PropertyInfo[] props, out PropertyInfo? assetProp)
    {
        assetProp = props.FirstOrDefault(p => p.PropertyType.IsGenericType
                                              && p.PropertyType.GetGenericTypeDefinition() == typeof(SanityReference<>)
                                              && (p?.Name.ToLower() == "asset"
                                                  || (p?.GetCustomAttributes<JsonPropertyAttribute>(true).FirstOrDefault()?.PropertyName?.Equals("asset")).GetValueOrDefault()));
        return assetProp != null;
    }

    private static bool IsListOfSanityImages(Type t, out Type? elementType)
    {
        elementType = null;
        var type = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType
                                                         && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                                                         && i.GetGenericArguments()[0].GetProperties().Any(p => p.PropertyType.IsGenericType
                                                                                                                && p.PropertyType.GetGenericTypeDefinition() == typeof(SanityReference<>)
                                                                                                                && (p?.Name.ToLower() == "asset" || (p?.GetCustomAttributes<JsonPropertyAttribute>(true).FirstOrDefault()?.PropertyName?.Equals("asset")).GetValueOrDefault())));
        if (type == null) return false;

        elementType = type.GetGenericArguments()[0];
        return true;
    }

    private static bool IsListOfSanityReference(Type t, out Type? element)
    {
        element = null;
        if (!TryGetEnumerableElementType(t, out var et) || et is not { IsGenericType: true } || et.GetGenericTypeDefinition() != typeof(SanityReference<>)) return false;

        element = et.GetGenericArguments()[0];
        return true;
    }

    private static bool IsSanityReferenceType(Type t)
    {
        return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(SanityReference<>);
    }

    private static string JoinComma(IEnumerable<string> parts)
    {
        return string.Join(",", parts);
    }

    private static bool KeyMatchesPart(string key, string part, Dictionary<string, string> tokens)
    {
        if (key == part) return true;

        // Simplify key: untokenize it and remove spaces
        var k = key;
        foreach (var token in tokens) k = k.Replace(token.Value, token.Key);
        k = k.Replace(" ", "");

        // Simplify part: remove spaces
        var p = part.Replace(" ", "");

        if (k == p) return true;

        // Base name match (e.g. "topic" matches "topic[...]").
        // We only care about the part before the first [ or -> or . (though dots shouldn't be here)
        var kBase = k.Split('[', '-')[0]; 
        var pBase = p.Split('[', '-')[0];

        return kBase == pBase && !string.IsNullOrEmpty(kBase);
    }

    private static string[] ParseIncludePath(string includeKey, Dictionary<string, string> tokens)
    {
        return includeKey
            .Replace(SanityConstants.DEREFERENCING_OPERATOR, ".")
            .TrimEnd('.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries);
    }

    private static void ReplaceFieldWithInclude(JObject parent, string part, JObject includeObject, Dictionary<string, string> tokens)
    {
        var targets = new List<JObject> { parent };

        // Also add a dereferenced section if it exists
        foreach (var property in parent)
            if (property.Key.Contains(tokens[SanityConstants.DEREFERENCING_SWITCH]) ||
                property.Key.Contains(tokens[SanityConstants.DEREFERENCING_OPERATOR]))
                if (property.Value is JObject derefObj)
                    targets.Add(derefObj);

        foreach (var targetObj in targets)
        {
            // Find existing field that matches 'part'
            string? existingKey = null;
            foreach (var property in targetObj)
            {
                if (KeyMatchesPart(property.Key, part, tokens))
                {
                    existingKey = property.Key;
                    break;
                }
            }

            // Find new field in includeObject that matches 'part'
            string? newKey = null;
            JToken? newValue = null;
            foreach (var include in includeObject)
            {
                if (KeyMatchesPart(include.Key, part, tokens))
                {
                    newKey = include.Key;
                    newValue = include.Value;
                    break;
                }
            }

            if (newValue == null) continue;

            if (existingKey != null)
            {
                if (targetObj[existingKey] is JObject existingObj && newValue is JObject newObj)
                {
                    // Merge them!
                    existingObj.Merge(newObj, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Union });

                    // Prefer the new key if it has a filter and the existing one doesn't (or has a simpler one)
                    var untokenizedExisting = existingKey;
                    var untokenizedNew = newKey ?? part;
                    foreach (var token in tokens)
                    {
                        untokenizedExisting = untokenizedExisting.Replace(token.Value, token.Key);
                        untokenizedNew = untokenizedNew.Replace(token.Value, token.Key);
                    }

                    if (untokenizedNew.Contains('[') && (!untokenizedExisting.Contains('[') || untokenizedNew.Length > untokenizedExisting.Length))
                    {
                        var val = targetObj[existingKey];
                        targetObj.Remove(existingKey);
                        targetObj[newKey ?? part] = val;
                    }
                }
                else
                {
                    // Update value but keep existing key (preserving suffixes like [])
                    targetObj[existingKey] = newValue;
                }
            }
            else
            {
                // Add new
                targetObj[newKey ?? part] = newValue;
            }
        }
    }


    private static bool TryGetEnumerableElementType(Type t, out Type? elementType)
    {
        var type = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? t
            : t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

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
        if (DocType == null || DocType == typeof(object) || DocType == typeof(SanityDocument)) return;

        var rootTypeName = DocType!.GetSanityTypeName();
        try
        {
            var dummyDoc = Activator.CreateInstance(DocType);
            var typeName = dummyDoc?.SanityType();
            if (!string.IsNullOrEmpty(typeName)) rootTypeName = typeName;
        }
        catch
        {
            // ignored
        }

        Constraints.Insert(0, $"_type == \"{rootTypeName}\"");
    }

    private void AppendConstraints(StringBuilder sb)
    {
        if (Constraints.Count <= 0) return;

        sb.Append('[');
        sb.Append(Constraints.Distinct().Aggregate((c, n) => $"({c}) && ({n})"));
        sb.Append(']');
    }

    private void AppendOrderings(StringBuilder sb)
    {
        if (Orderings.Count == 0) return;

        var distinctOrderings = string.Join(", ", Orderings.Distinct());
        sb.Append($" | order({distinctOrderings})");
    }

    private void AppendProjection(StringBuilder sb, string projection)
    {
        if (string.IsNullOrEmpty(projection)) return;

        // Replace @ (parameter reference) with ... (spread operator) for full entity selection
        var normalized = projection == "@" ? SanityConstants.SPREAD_OPERATOR : projection;

        var expanded = ExpandIncludesInProjection(normalized, Includes);

        if (expanded == $"{{{SanityConstants.SPREAD_OPERATOR}}}") return; // Don't add an empty projection

        var hasBraces = expanded.StartsWith('{') && expanded.EndsWith('}');
        if (!hasBraces)
            sb.Append($" {{{expanded}}}");
        else
            sb.Append(expanded);
    }

    private void AppendSlices(StringBuilder sb)
    {
        if (Take.HasValue)
        {
            switch (Take)
            {
                case 1:
                    if (ExpectsArray)
                        sb.Append($" [{Skip}..{Skip}]");
                    else
                        sb.Append($" [{Skip}]");
                    break;
                case 0:
                    sb.Append($" [{Skip}...{Skip}]");
                    break;
                default:
                    sb.Append($" [{Skip}..{Skip + Take.Value - 1}]");
                    break;
            }
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
            return projection;

        // if (DateTime.Now.Year > 2000) throw new Exception($"DEBUG BEFORE: {jsonProjection}");

        // Use the includes provided via parameter, not the instance field
        foreach (var includeKey in includes.Keys.OrderBy(k => k))
        {
            var jsonInclude = GroqToJson($"{{{includes[includeKey]}}}");
            if (JsonConvert.DeserializeObject(jsonInclude) is not JObject jObjectInclude) continue;

            var pathParts = ParseIncludePath(includeKey, _groqTokens);

            // Traverse to parent
            EnsurePath(jObjectProjection, pathParts, _groqTokens, out var parents);

            // Replace or set the last segment in all parents
            var lastPart = pathParts[^1];
            foreach (var parent in parents) ReplaceFieldWithInclude(parent, lastPart, jObjectInclude, _groqTokens);
        }

        // Convert back to JSON
        jsonProjection = jObjectProjection.ToString(Formatting.None);
        // if (DateTime.Now.Year > 2000) throw new Exception($"DEBUG AFTER: {jsonProjection}");
        // Convert JSON back to GROQ query
        projection = JsonToGroq(jsonProjection);

        return projection;
    }

    private string GroqToJson(string groq)
    {
        var json = groq.Replace(" ", "");

        // Order by length descending to avoid partial matches
        var tokens = _groqTokens.Keys.OrderByDescending(k => k.Length);
        foreach (var token in tokens) json = json.Replace(token, _groqTokens[token]);
        json = json.Replace("{", ":{").TrimStart(':');

        // Replace variable names with valid JSON (e.g., convert myField to "myField": true)
        var reVariables = MyRegex();
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
        foreach (var token in _groqTokens.Keys) groq = groq.Replace(_groqTokens[token], token);
        return groq;
    }

    private string ResolveProjection(int maxNestingLevel)
    {
        if (!string.IsNullOrEmpty(Projection)) return Projection;

        // Joins require an explicit projection
        var propertyList = GetPropertyProjectionList(ResultType ?? DocType ?? typeof(object), 0, maxNestingLevel);
        if (propertyList.Count > 0) return JoinComma(propertyList);

        return Includes.Keys.Count > 0
            ? string.Join(",", Includes.Keys)
            : string.Empty;
    }

    private void WrapWithAggregate(StringBuilder sb)
    {
        if (string.IsNullOrEmpty(AggregateFunction)) return;

        sb.Insert(0, AggregateFunction + "(");
        sb.Append(')');
        if (!string.IsNullOrEmpty(AggregatePostFix)) sb.Append(AggregatePostFix);
    }

#if NET7_0_OR_GREATER
    [GeneratedRegex("(,|{)([^\"}:,]+)(,|})")]
    internal static partial Regex MyRegex();
#else
    private static readonly Regex _myRegex = new Regex("(,|{)([^\"}:,]+)(,|})", RegexOptions.Compiled);
    internal static Regex MyRegex() => _myRegex;
#endif
}