using System.Diagnostics.CodeAnalysis;
using Sanity.Linq.CommonTypes;

// ReSharper disable MemberCanBePrivate.Global

namespace Sanity.Linq.QueryProvider;

internal sealed partial class SanityQueryBuilder
{
    public string AggregateFunction { get; set; } = "";

    public string AggregatePostFix { get; set; } = "";

    public List<string> Constraints { get; } = [];
    public List<string> PostFilters { get; } = [];

    public Type? DocType { get; set; }

    public Dictionary<string, string> Includes { get; set; } = new();

    public List<string> Orderings { get; set; } = [];

    public string Projection { get; set; } = "";

    public Type? ResultType { get; set; }

    public bool ExpectsArray { get; set; }
    public bool FlattenProjection { get; set; }
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
        var filter = fieldRef.Contains(SanityConstants.DEFINED) ? "" : SanityConstants.ARRAY_FILTER;

        return $"{fieldRef}{indicator}{filter}{{{fieldList},{SanityConstants.DEREFERENCING_SWITCH + "{" + fieldList + "}"}}}";
    }

    private static string HandleImageAssetCase(string fieldRef, Type propertyType, PropertyInfo assetProp, int nestingLevel, int maxNestingLevel)
    {
        var fields = GetPropertyProjectionList(propertyType, nestingLevel, maxNestingLevel);
        var nestedFields = GetPropertyProjectionList(assetProp.PropertyType, nestingLevel, maxNestingLevel);
        var projectedFields = fields
            .Select(f => f.StartsWith(SanityConstants.ASSET)
                ? $"{SanityConstants.ASSET}{SanityConstants.DEREFERENCING_OPERATOR}{(nestedFields.Count > 0 ? "{" + JoinComma(nestedFields) + "}" : "")}"
                : f);
        var fieldList = JoinComma(projectedFields);
        return $"{fieldRef}{{{fieldList}}}";
    }

    private static string HandleListOfSanityImagesCase(string fieldRef, Type imgElementType, int nestingLevel, int maxNestingLevel)
    {
        var fields = GetPropertyProjectionList(imgElementType, nestingLevel, maxNestingLevel);
        var projectedFields = fields.Select(f => f.StartsWith(SanityConstants.ASSET) ? $"{SanityConstants.ASSET}{SanityConstants.DEREFERENCING_OPERATOR}{{{SanityConstants.SPREAD_OPERATOR}}}" : f);
        var fieldList = JoinComma(projectedFields);
        var indicator = fieldRef.Contains(SanityConstants.ARRAY_INDICATOR) ? "" : SanityConstants.ARRAY_INDICATOR;
        var filter = fieldRef.Contains(SanityConstants.DEFINED) ? "" : SanityConstants.ARRAY_FILTER;
        return $"{fieldRef}{indicator}{filter}{{{fieldList}}}";
    }

    private static string HandleGenericObjectCase(string fieldRef, Type propertyType, int nestingLevel, int maxNestingLevel, bool isExplicit)
    {
        var isEnumerable = TryGetEnumerableElementType(propertyType, out var enumerableType);
        var targetType = isEnumerable ? enumerableType! : propertyType;
        var fields = GetPropertyProjectionList(targetType, nestingLevel, maxNestingLevel);
        var indicator = isEnumerable && !fieldRef.Contains(SanityConstants.ARRAY_INDICATOR) ? SanityConstants.ARRAY_INDICATOR : "";
        var filter = isEnumerable && !fieldRef.Contains(SanityConstants.DEFINED) ? SanityConstants.ARRAY_FILTER : "";
        var suffix = indicator + filter;

        if (fields.Count <= 0)
        {
            if (isExplicit) return $"{fieldRef}{suffix}{{{SanityConstants.SPREAD_OPERATOR},{SanityConstants.DEREFERENCING_SWITCH + "{" + SanityConstants.SPREAD_OPERATOR + "}"}}}";

            return $"{fieldRef}{suffix}{{{SanityConstants.SPREAD_OPERATOR}}}";
        }

        var fieldList = JoinComma(fields);
        if (isExplicit) return $"{fieldRef}{suffix}{{{fieldList},{SanityConstants.DEREFERENCING_SWITCH + "{" + fieldList + "}"}}}";

        return $"{fieldRef}{suffix}{{{fieldList}}}";
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

        var properties = type.GetProperties().Where(p => p.CanWrite);
        foreach (var prop in properties)
        {
            if (ShouldSkipProperty(prop)) continue;

            var (sourceName, targetName) = ResolvePropertyNames(prop);

            if (TryHandleExplicitInclude(prop, sourceName, targetName, nestingLevel, maxNestingLevel, out var projection))
            {
                result.Add(projection);
                continue;
            }

            if (!ShouldExpandComplexType(prop)) continue;

            result.Add(GetJoinProjection(sourceName, targetName, prop.PropertyType, nestingLevel + 1, maxNestingLevel));
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
        var targetName = prop.GetJsonProperty();
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
        sb.Append(SanityConstants.STAR);

        AddDocTypeConstraintIfAny();
        AppendConstraints(sb);

        if (includeProjections)
        {
            var projection = ResolveProjection(maxNestingLevel);
            AppendProjection(sb, projection);
        }

        AppendPostFilters(sb);
        AppendOrderings(sb);
        AppendSlices(sb);
        WrapWithAggregate(sb);

        return sb.ToString();
    }

    private void AppendPostFilters(StringBuilder sb)
    {
        if (PostFilters.Count <= 0) return;

        sb.Append(SanityConstants.OPEN_BRACKET);
        sb.Append(PostFilters.Distinct().Aggregate((c, n) => $"({c}) {SanityConstants.AND} ({n})"));
        sb.Append(SanityConstants.CLOSE_BRACKET);
    }

    private static void EnsurePath(JObject root, IReadOnlyList<string> parts, out List<JObject> parents)
    {
        var tokens = SanityGroqTokenRegistry.Instance.Tokens;
        var currentObjects = new List<JObject> { root };
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (i == parts.Count - 1) break;

            var nextObjects = new List<JObject>();
            foreach (var obj in currentObjects)
            {
                var matches = FindAllChildObjects(obj, part);
                if (matches.Count > 0)
                {
                    nextObjects.AddRange(matches);
                }
                else
                {
                    var nextJObject = new JObject
                    {
                        [tokens[SanityConstants.SPREAD_OPERATOR]] = true
                    };

                    obj[part] = nextJObject;
                    nextObjects.Add(nextJObject);
                }
            }

            currentObjects = nextObjects.Distinct().ToList();
        }

        parents = currentObjects;
    }

    private static List<JObject> FindAllChildObjects(JObject current, string part)
    {
        var tokens = SanityGroqTokenRegistry.Instance.Tokens;
        var matches = new List<JObject>();
        foreach (var property in current)
        {
            if (KeyMatchesPart(property.Key, part) && property.Value is JObject obj)
            {
                matches.Add(obj);

                // Also check for a deref section inside this child
                foreach (var (childKey, childValue) in obj)
                {
                    var isDeref = childKey.Contains(tokens[SanityConstants.DEREFERENCING_SWITCH]) ||
                                  childKey.Contains(tokens[SanityConstants.DEREFERENCING_OPERATOR]);

                    if (isDeref && childValue is JObject derefObj) matches.Add(derefObj);
                }
            }

            var hasDerefToken = property.Key.Contains(tokens[SanityConstants.DEREFERENCING_SWITCH]) ||
                                property.Key.Contains(tokens[SanityConstants.DEREFERENCING_OPERATOR]);

            if (hasDerefToken && property.Value is JObject derefObj2) matches.AddRange(FindAllChildObjects(derefObj2, part));
        }

        return matches;
    }

    private static bool HasSanityImageAsset(PropertyInfo[] props, out PropertyInfo? assetProp)
    {
        assetProp = props.FirstOrDefault(p => p.PropertyType.IsGenericType
                                              && p.PropertyType.GetGenericTypeDefinition() == typeof(SanityReference<>)
                                              && p.GetJsonProperty() == SanityConstants.ASSET);
        return assetProp != null;
    }

    private static bool IsListOfSanityImages(Type t, out Type? elementType)
    {
        elementType = null;
        var type = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType
                                                         && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                                                         && i.GetGenericArguments()[0].GetProperties().Any(p => p.PropertyType.IsGenericType
                                                                                                                && p.PropertyType.GetGenericTypeDefinition() == typeof(SanityReference<>)
                                                                                                                && p.GetJsonProperty() == SanityConstants.ASSET));
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

    private static bool KeyMatchesPart(string key, string part)
    {
        if (key == part) return true;

        var registry = SanityGroqTokenRegistry.Instance;
        // Simplify key: untokenize it and remove spaces
        var k = registry.ReverseTokens.Aggregate(key, (current, token) => current.Replace(token.Key, token.Value));
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

    private static string[] ParseIncludePath(string includeKey)
    {
        return includeKey
            .Replace(SanityConstants.DEREFERENCING_OPERATOR, SanityConstants.DOT)
            .TrimEnd(SanityConstants.DOT[0])
            .Split(SanityConstants.DOT[0], StringSplitOptions.RemoveEmptyEntries);
    }

    private static void ReplaceFieldWithInclude(JObject parent, string part, JObject includeObject)
    {
        var tokens = SanityGroqTokenRegistry.Instance.Tokens;
        var targets = GetMergeTargets(parent, tokens);

        foreach (var targetObj in targets)
            if (TryFindProperty(includeObject, part, out var newKey, out var newValue))
                PerformMergeOrUpdate(targetObj, part, newKey, newValue, tokens);
    }

    private static List<JObject> GetMergeTargets(JObject parent, IReadOnlyDictionary<string, string> tokens)
    {
        var targets = new List<JObject> { parent };

        foreach (var property in parent)
        {
            var isDeref = property.Key.Contains(tokens[SanityConstants.DEREFERENCING_SWITCH]) ||
                          property.Key.Contains(tokens[SanityConstants.DEREFERENCING_OPERATOR]);

            if (isDeref && property.Value is JObject derefObj) targets.Add(derefObj);
        }

        return targets;
    }

    private static bool TryFindProperty(JObject obj, string part, [NotNullWhen(true)] out string? key, [NotNullWhen(true)] out JToken? value)
    {
        foreach (var property in obj)
            if (KeyMatchesPart(property.Key, part))
            {
                key = property.Key;
                value = property.Value!;
                return true;
            }

        key = null;
        value = null;
        return false;
    }

    private static void PerformMergeOrUpdate(JObject targetObj, string part, string newKey, JToken newValue, IReadOnlyDictionary<string, string> tokens)
    {
        if (TryFindProperty(targetObj, part, out var existingKey, out var existingValue))
        {
            if (existingValue is JObject existingObj && newValue is JObject newObj)
            {
                // Merge them!
                existingObj.Merge(newObj, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Union });

                // Prefer the new key if it has a filter and the existing one doesn't (or has a simpler one)
                var untokenizedExisting = Untokenize(existingKey, tokens);
                var untokenizedNew = Untokenize(newKey, tokens);

                if (untokenizedNew.Contains('[') && (!untokenizedExisting.Contains('[') || untokenizedNew.Length > untokenizedExisting.Length))
                {
                    targetObj.Remove(existingKey);
                    targetObj[newKey] = existingValue;
                }
            }
            else
            {
                // Update value but keep the existing key (preserving suffixes like [])
                targetObj[existingKey] = newValue;
            }
        }
        else
        {
            // Add new
            targetObj[newKey] = newValue;
        }
    }

    private static string Untokenize(string key, IReadOnlyDictionary<string, string> tokens)
    {
        return tokens.Aggregate(key, (current, token) => current.Replace(token.Value, token.Key));
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

        Constraints.Insert(0, $"{SanityConstants.TYPE} {SanityConstants.EQUALS} \"{rootTypeName}\"");
    }

    private void AppendConstraints(StringBuilder sb)
    {
        if (Constraints.Count <= 0) return;

        sb.Append(SanityConstants.OPEN_BRACKET);
        sb.Append(Constraints.Distinct().Aggregate((c, n) => $"({c}) {SanityConstants.AND} ({n})"));
        sb.Append(SanityConstants.CLOSE_BRACKET);
    }

    private void AppendOrderings(StringBuilder sb)
    {
        if (Orderings.Count == 0) return;

        var distinctOrderings = string.Join(", ", Orderings.Distinct());
        sb.Append($" | {SanityConstants.ORDER}({distinctOrderings})");
    }

    private void AppendProjection(StringBuilder sb, string projection)
    {
        if (string.IsNullOrEmpty(projection)) return;

        // Replace @ (parameter reference) with ... (spread operator) for full entity selection
        var normalized = projection == "@" ? SanityConstants.SPREAD_OPERATOR : projection;

        var expanded = ExpandIncludesInProjection(normalized, Includes);

        if (expanded == $"{{{SanityConstants.SPREAD_OPERATOR}}}") return; // Don't add an empty projection

        if (expanded.StartsWith('{') && expanded.EndsWith('}'))
        {
            if (FlattenProjection && !expanded.StartsWith("{..."))
            {
                sb.Append("{..." + expanded.Substring(1));
                return;
            }

            sb.Append(expanded);
            return;
        }

        if (FlattenProjection)
            sb.Append($" {{...{expanded}}}");
        else
            sb.Append($" {{{expanded}}}");
    }

    private void AppendSlices(StringBuilder sb)
    {
        if (!Take.HasValue)
        {
            if (Skip > 0) sb.Append($" [{Skip}..{int.MaxValue}]");
            return;
        }

        switch (Take.Value)
        {
            case 1:
                sb.Append(ExpectsArray ? $" [{Skip}..{Skip}]" : $" [{Skip}]");
                break;
            case 0:
                sb.Append($" [{Skip}...{Skip}]");
                break;
            default:
                sb.Append($" [{Skip}..{Skip + Take.Value - 1}]");
                break;
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

        // Use the includes provided via parameter, not the instance field
        foreach (var (includeKey, includeValue) in includes.OrderBy(k => k.Key))
        {
            var jsonInclude = GroqToJson($"{{{includeValue}}}");
            if (JsonConvert.DeserializeObject(jsonInclude) is not JObject jObjectInclude) continue;

            var pathParts = ParseIncludePath(includeKey);

            // Traverse to parent
            EnsurePath(jObjectProjection, pathParts, out var parents);

            // Replace or set the last segment in all parents
            var lastPart = pathParts[^1];
            foreach (var parent in parents) ReplaceFieldWithInclude(parent, lastPart, jObjectInclude);
        }

        // Convert back to JSON
        jsonProjection = jObjectProjection.ToString(Formatting.None);
        // Convert JSON back to GROQ query
        return JsonToGroq(jsonProjection);
    }

    private static string GroqToJson(string groq)
    {
        var json = groq.Replace(" ", "");

        var registry = SanityGroqTokenRegistry.Instance;
        json = registry.SortedTokenKeys.Aggregate(json, (current, token) => current.Replace(token, registry.Tokens[token]));
        json = json.Replace(SanityConstants.OPEN_BRACE, SanityConstants.COLON + SanityConstants.OPEN_BRACE).TrimStart(SanityConstants.COLON[0]);

        // Replace variable names with valid JSON (e.g., convert myField to "myField": true)
        var reVariables = MyRegex();
        var reMatches = reVariables.Matches(json);
        while (reMatches.Count > 0)
        {
            foreach (Match match in reMatches)
            {
                var fieldName = match.Groups[2].Value;
                var fieldReplacement = $"{SanityConstants.STRING_DELIMITER}{fieldName}{SanityConstants.STRING_DELIMITER}{SanityConstants.COLON}true";
                json = json.Replace(match.Value, match.Value.Replace(fieldName, fieldReplacement));
            }

            reMatches = reVariables.Matches(json);
        }

        return json;
    }

    private static string JsonToGroq(string json)
    {
        var groq = json
            .Replace(SanityConstants.COLON + SanityConstants.OPEN_BRACE, SanityConstants.OPEN_BRACE)
            .Replace(SanityConstants.COLON + "true", "")
            .Replace(SanityConstants.STRING_DELIMITER, "");
        var registry = SanityGroqTokenRegistry.Instance;
        return registry.ReverseTokens.Aggregate(groq, (current, token) => current.Replace(token.Key, token.Value));
    }

    private string ResolveProjection(int maxNestingLevel)
    {
        if (!string.IsNullOrEmpty(Projection)) return Projection;

        // Joins require an explicit projection
        var propertyList = GetPropertyProjectionList(ResultType ?? DocType ?? typeof(object), 0, maxNestingLevel);
        if (propertyList.Count > 0) return JoinComma(propertyList);

        if (Includes.Keys.Count > 0) return string.Join(",", Includes.Keys);

        return string.Empty;
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