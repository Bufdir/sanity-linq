using System.Diagnostics.CodeAnalysis;
using Sanity.Linq.CommonTypes;

// ReSharper disable MemberCanBePrivate.Global

namespace Sanity.Linq.QueryProvider;

internal sealed partial class SanityQueryBuilder
{
    private static readonly ConcurrentDictionary<(Type, int, int), string[]> ProjectionCache = new();
    private static readonly ConcurrentDictionary<Type, string> DocTypeCache = new();
    public string AggregateFunction { get; set; } = string.Empty;

    public string AggregatePostFix { get; set; } = string.Empty;

    public List<string> Constraints { get; } = [];
    public List<string> PostFilters { get; } = [];

    public Type? DocType { get; set; }

    public Dictionary<string, string> Includes { get; set; } = new();

    public List<string> Orderings { get; set; } = [];

    public string Projection { get; set; } = string.Empty;

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
            : $"{SanityConstants.STRING_DELIMITER}{targetName}{SanityConstants.STRING_DELIMITER}{SanityConstants.COLON}{sourceName}";

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
        return $"{fieldRef}{SanityConstants.OPEN_BRACE}{fieldList}{SanityConstants.COMMA}{SanityConstants.DEREFERENCING_SWITCH}{SanityConstants.OPEN_BRACE}{fieldList}{SanityConstants.CLOSE_BRACE}{SanityConstants.CLOSE_BRACE}";
    }

    private static string HandleListOfSanityReferenceCase(string fieldRef, Type refElement, int nestingLevel, int maxNestingLevel)
    {
        var fields = GetPropertyProjectionList(refElement, nestingLevel, maxNestingLevel);
        var fieldList = JoinComma(fields);

        var indicator = fieldRef.Contains(SanityConstants.ARRAY_INDICATOR) ? string.Empty : SanityConstants.ARRAY_INDICATOR;
        var filter = fieldRef.Contains(SanityConstants.DEFINED) ? string.Empty : SanityConstants.ARRAY_FILTER;

        return $"{fieldRef}{indicator}{filter}{SanityConstants.OPEN_BRACE}{fieldList}{SanityConstants.COMMA}{SanityConstants.DEREFERENCING_SWITCH}{SanityConstants.OPEN_BRACE}{fieldList}{SanityConstants.CLOSE_BRACE}{SanityConstants.CLOSE_BRACE}";
    }

    private static string HandleImageAssetCase(string fieldRef, Type propertyType, PropertyInfo assetProp, int nestingLevel, int maxNestingLevel)
    {
        var fields = GetPropertyProjectionList(propertyType, nestingLevel, maxNestingLevel);
        var nestedFields = GetPropertyProjectionList(assetProp.PropertyType, nestingLevel, maxNestingLevel);
        var projectedFields = fields
            .Select(f => f.StartsWith(SanityConstants.ASSET)
                ? $"{SanityConstants.ASSET}{SanityConstants.DEREFERENCING_OPERATOR}{(nestedFields.Count > 0 ? SanityConstants.OPEN_BRACE + JoinComma(nestedFields) + SanityConstants.CLOSE_BRACE : string.Empty)}"
                : f);
        var fieldList = JoinComma(projectedFields);
        return $"{fieldRef}{SanityConstants.OPEN_BRACE}{fieldList}{SanityConstants.CLOSE_BRACE}";
    }

    private static string HandleListOfSanityImagesCase(string fieldRef, Type imgElementType, int nestingLevel, int maxNestingLevel)
    {
        var fields = GetPropertyProjectionList(imgElementType, nestingLevel, maxNestingLevel);
        var projectedFields = fields.Select(f => f.StartsWith(SanityConstants.ASSET) ? SanityConstants.ASSET + SanityConstants.DEREFERENCING_OPERATOR + SanityConstants.OPEN_BRACE + SanityConstants.SPREAD_OPERATOR + SanityConstants.CLOSE_BRACE : f);
        var fieldList = JoinComma(projectedFields);
        var indicator = fieldRef.Contains(SanityConstants.ARRAY_INDICATOR) ? string.Empty : SanityConstants.ARRAY_INDICATOR;
        var filter = fieldRef.Contains(SanityConstants.DEFINED) ? string.Empty : SanityConstants.ARRAY_FILTER;
        return $"{fieldRef}{indicator}{filter}{SanityConstants.OPEN_BRACE}{fieldList}{SanityConstants.CLOSE_BRACE}";
    }

    private static string HandleGenericObjectCase(string fieldRef, Type propertyType, int nestingLevel, int maxNestingLevel, bool isExplicit)
    {
        var isEnumerable = TryGetEnumerableElementType(propertyType, out var enumerableType);
        var targetType = isEnumerable ? enumerableType! : propertyType;
        var fields = GetPropertyProjectionList(targetType, nestingLevel, maxNestingLevel);
        var indicator = isEnumerable && !fieldRef.Contains(SanityConstants.ARRAY_INDICATOR) ? SanityConstants.ARRAY_INDICATOR : string.Empty;
        var filter = isEnumerable && !fieldRef.Contains(SanityConstants.DEFINED) ? SanityConstants.ARRAY_FILTER : string.Empty;
        var suffix = indicator + filter;

        if (fields.Count <= 0)
        {
            if (isExplicit) return $"{fieldRef}{suffix}{SanityConstants.OPEN_BRACE}{SanityConstants.SPREAD_OPERATOR}{SanityConstants.COMMA}{SanityConstants.DEREFERENCING_SWITCH}{SanityConstants.OPEN_BRACE}{SanityConstants.SPREAD_OPERATOR}{SanityConstants.CLOSE_BRACE}{SanityConstants.CLOSE_BRACE}";

            return $"{fieldRef}{suffix}{SanityConstants.OPEN_BRACE}{SanityConstants.SPREAD_OPERATOR}{SanityConstants.CLOSE_BRACE}";
        }

        var fieldList = JoinComma(fields);
        if (isExplicit) return $"{fieldRef}{suffix}{SanityConstants.OPEN_BRACE}{fieldList}{SanityConstants.COMMA}{SanityConstants.DEREFERENCING_SWITCH}{SanityConstants.OPEN_BRACE}{fieldList}{SanityConstants.CLOSE_BRACE}{SanityConstants.CLOSE_BRACE}";

        return $"{fieldRef}{suffix}{SanityConstants.OPEN_BRACE}{fieldList}{SanityConstants.CLOSE_BRACE}";
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
        if (ProjectionCache.TryGetValue((type, nestingLevel, maxNestingLevel), out var cached)) return [..cached];

        if (nestingLevel == maxNestingLevel) return [SanityConstants.SPREAD_OPERATOR];

        // "Include all" primitive types with a simple ...
        var result = new List<string> { SanityConstants.SPREAD_OPERATOR };

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

        ProjectionCache.TryAdd((type, nestingLevel, maxNestingLevel), result.ToArray());
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
        sb.Append(SanityConstants.CHAR_STAR);

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

        sb.Append(SanityConstants.CHAR_OPEN_BRACKET);
        var first = true;
        foreach (var filter in PostFilters)
        {
            if (!first) sb.Append(SanityConstants.CHAR_SPACE).Append(SanityConstants.AND).Append(SanityConstants.CHAR_SPACE);
            sb.Append(SanityConstants.CHAR_OPEN_PAREN).Append(filter).Append(SanityConstants.CHAR_CLOSE_PAREN);
            first = false;
        }

        sb.Append(SanityConstants.CHAR_CLOSE_BRACKET);
    }

    public void AddProjection(string projection)
    {
        if (string.IsNullOrWhiteSpace(projection)) return;
        if (string.IsNullOrEmpty(Projection))
        {
            Projection = projection;
            return;
        }

        if (projection.StartsWith(SanityConstants.OPEN_BRACE))
            Projection = $"{Projection}{SanityConstants.SPACE}{projection}";
        else
            Projection = $"{Projection}{SanityConstants.DOT}{projection}";
    }

    public void AddConstraint(string constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint)) return;
        if (!Constraints.Contains(constraint)) Constraints.Add(constraint);
    }

    public void AddPostFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return;
        if (!PostFilters.Contains(filter)) PostFilters.Add(filter);
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
        return string.Join(SanityConstants.COMMA, parts);
    }

    private static bool KeyMatchesPart(string key, string part)
    {
        if (key == part) return true;

        // Simplify key: untokenize it and remove spaces
        var k = Untokenize(key).Replace(SanityConstants.SPACE, string.Empty);

        // Simplify part: remove spaces
        var p = part.Replace(SanityConstants.SPACE, string.Empty);

        if (k == p) return true;

        // Base name match (e.g. "topic" matches "topic[...]").
        // We only care about the part before the first [ or -> or . (though dots shouldn't be here)
        var kBase = k.Split(SanityConstants.CHAR_OPEN_BRACKET, SanityConstants.CHAR_HYPHEN)[0];
        var pBase = p.Split(SanityConstants.CHAR_OPEN_BRACKET, SanityConstants.CHAR_HYPHEN)[0];

        return kBase == pBase && !string.IsNullOrEmpty(kBase);
    }

    private static string[] ParseIncludePath(string includeKey)
    {
        return includeKey
            .Replace(SanityConstants.DEREFERENCING_OPERATOR, SanityConstants.DOT)
            .TrimEnd(SanityConstants.CHAR_DOT)
            .Split(SanityConstants.CHAR_DOT, StringSplitOptions.RemoveEmptyEntries);
    }

    private static void ReplaceFieldWithInclude(JObject parent, string part, JObject includeObject)
    {
        var tokens = SanityGroqTokenRegistry.Instance.Tokens;
        var targets = GetMergeTargets(parent, tokens);

        foreach (var targetObj in targets)
            if (TryFindProperty(includeObject, part, out var newKey, out var newValue))
                PerformMergeOrUpdate(targetObj, part, newKey, newValue);
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

    private static void PerformMergeOrUpdate(JObject targetObj, string part, string newKey, JToken newValue)
    {
        if (TryFindProperty(targetObj, part, out var existingKey, out var existingValue))
        {
            if (existingValue is JObject existingObj && newValue is JObject newObj)
            {
                // Merge them!
                existingObj.Merge(newObj, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Union });

                // Prefer the new key if it has a filter and the existing one doesn't (or has a simpler one)
                var untokenizedExisting = Untokenize(existingKey);
                var untokenizedNew = Untokenize(newKey);

                if (untokenizedNew.Contains(SanityConstants.CHAR_OPEN_BRACKET) && (!untokenizedExisting.Contains(SanityConstants.CHAR_OPEN_BRACKET) || untokenizedNew.Length > untokenizedExisting.Length))
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

    private static string Untokenize(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        var reverseTokens = SanityGroqTokenRegistry.Instance.ReverseTokens;

        var index = key.IndexOf(SanityConstants.TOKEN_PREFIX, StringComparison.Ordinal);
        if (index == -1) return key;

        var sb = new StringBuilder();
        var lastIndex = 0;
        while (index != -1)
        {
            sb.Append(key, lastIndex, index - lastIndex);
            if (index + 10 <= key.Length)
            {
                var token = key.Substring(index, 10);
                if (reverseTokens.TryGetValue(token, out var val))
                {
                    sb.Append(val);
                    lastIndex = index + 10;
                }
                else
                {
                    sb.Append(SanityConstants.TOKEN_PREFIX);
                    lastIndex = index + 6;
                }
            }
            else
            {
                sb.Append(SanityConstants.TOKEN_PREFIX);
                lastIndex = index + 6;
            }

            index = key.IndexOf(SanityConstants.TOKEN_PREFIX, lastIndex, StringComparison.Ordinal);
        }

        sb.Append(key, lastIndex, key.Length - lastIndex);
        return sb.ToString();
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

        var rootTypeName = DocTypeCache.GetOrAdd(DocType, type =>
        {
            var name = type.GetSanityTypeName();
            try
            {
                var dummyDoc = Activator.CreateInstance(type);
                var typeName = dummyDoc?.SanityType();
                if (!string.IsNullOrEmpty(typeName)) name = typeName;
            }
            catch
            {
                // ignored
            }

            return name;
        });

        Constraints.Insert(0, $"{SanityConstants.TYPE}{SanityConstants.SPACE}{SanityConstants.EQUALS}{SanityConstants.SPACE}{SanityConstants.STRING_DELIMITER}{rootTypeName}{SanityConstants.STRING_DELIMITER}");
    }

    private void AppendConstraints(StringBuilder sb)
    {
        if (Constraints.Count <= 0) return;

        sb.Append(SanityConstants.CHAR_OPEN_BRACKET);
        var first = true;
        foreach (var constraint in Constraints)
        {
            if (!first) sb.Append(SanityConstants.CHAR_SPACE).Append(SanityConstants.AND).Append(SanityConstants.CHAR_SPACE);
            sb.Append(SanityConstants.CHAR_OPEN_PAREN).Append(constraint).Append(SanityConstants.CHAR_CLOSE_PAREN);
            first = false;
        }

        sb.Append(SanityConstants.CHAR_CLOSE_BRACKET);
    }

    private void AppendOrderings(StringBuilder sb)
    {
        if (Orderings.Count == 0) return;

        sb.Append(SanityConstants.CHAR_SPACE).Append(SanityConstants.PIPE).Append(SanityConstants.CHAR_SPACE).Append(SanityConstants.ORDER).Append(SanityConstants.OPEN_PAREN);
        var first = true;
        foreach (var ordering in Orderings)
        {
            if (!first) sb.Append(SanityConstants.CHAR_COMMA).Append(SanityConstants.CHAR_SPACE);
            sb.Append(ordering);
            first = false;
        }

        sb.Append(SanityConstants.CHAR_CLOSE_PAREN);
    }

    public void AddOrdering(string ordering)
    {
        if (string.IsNullOrWhiteSpace(ordering)) return;
        if (!Orderings.Contains(ordering)) Orderings.Add(ordering);
    }

    private void AppendProjection(StringBuilder sb, string projection)
    {
        if (string.IsNullOrEmpty(projection)) return;

        // Replace @ (parameter reference) with ... (spread operator) for full entity selection
        var normalized = projection == SanityConstants.AT ? SanityConstants.SPREAD_OPERATOR : projection;

        var expanded = ExpandIncludesInProjection(normalized, Includes);

        if (expanded == SanityConstants.OPEN_BRACE + SanityConstants.SPREAD_OPERATOR + SanityConstants.CLOSE_BRACE) return; // Don't add an empty projection

        if (expanded.StartsWith(SanityConstants.OPEN_BRACE))
        {
            if (FlattenProjection && expanded.EndsWith(SanityConstants.CLOSE_BRACE) && !expanded.StartsWith(SanityConstants.OPEN_BRACE + SanityConstants.SPREAD_OPERATOR))
            {
                sb.Append(SanityConstants.OPEN_BRACE + SanityConstants.SPREAD_OPERATOR + expanded.Substring(1));
                return;
            }

            sb.Append(expanded);
            return;
        }

        if ((!string.IsNullOrEmpty(AggregateFunction) || !string.IsNullOrEmpty(AggregatePostFix)) && !expanded.Contains(SanityConstants.CHAR_COMMA))
            sb.Append(SanityConstants.DOT).Append(expanded);
        else if (FlattenProjection)
            sb.Append(SanityConstants.SPACE + SanityConstants.OPEN_BRACE + SanityConstants.SPREAD_OPERATOR + expanded + SanityConstants.CLOSE_BRACE);
        else
            sb.Append(SanityConstants.SPACE + SanityConstants.OPEN_BRACE + expanded + SanityConstants.CLOSE_BRACE);
    }

    private void AppendSlices(StringBuilder sb)
    {
        if (!Take.HasValue)
        {
            if (Skip > 0) sb.Append(SanityConstants.SPACE + SanityConstants.OPEN_BRACKET + Skip + SanityConstants.RANGE + int.MaxValue + SanityConstants.CLOSE_BRACKET);
            return;
        }

        switch (Take.Value)
        {
            case 1:
                sb.Append(ExpectsArray ? SanityConstants.SPACE + SanityConstants.OPEN_BRACKET + Skip + SanityConstants.RANGE + Skip + SanityConstants.CLOSE_BRACKET : SanityConstants.SPACE + SanityConstants.OPEN_BRACKET + Skip + SanityConstants.CLOSE_BRACKET);
                break;
            case 0:
                sb.Append(SanityConstants.SPACE + SanityConstants.OPEN_BRACKET + Skip + SanityConstants.INCLUSIVE_RANGE + Skip + SanityConstants.CLOSE_BRACKET);
                break;
            default:
                sb.Append(SanityConstants.SPACE + SanityConstants.OPEN_BRACKET + Skip + SanityConstants.RANGE + (Skip + Take.Value - 1) + SanityConstants.CLOSE_BRACKET);
                break;
        }
    }

    private static string ExpandIncludesInProjection(string projection, Dictionary<string, string>? includes)
    {
        if (includes == null || includes.Count == 0) return projection;

        // Finds and replaces includes in a projection by converting projection (GROQ) to an equivalent JSON representation,
        // modifying the JSON replacement and then converting back to GROQ.
        //
        // The reason for converting to JSON is simply to be able to work with the query in a hierarchical structure.
        // This could also be done creating some sort of query tree object, which might be a more appropriate / cleaner solution.

        var jsonProjection = GroqToJson(SanityConstants.OPEN_BRACE + projection + SanityConstants.CLOSE_BRACE);
        if (JsonConvert.DeserializeObject(jsonProjection) is not JObject jObjectProjection)
            return projection;

        // Use the includes provided via parameter, not the instance field
        foreach (var (includeKey, includeValue) in includes.OrderBy(k => k.Key))
        {
            var jsonInclude = GroqToJson(SanityConstants.OPEN_BRACE + includeValue + SanityConstants.CLOSE_BRACE);
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
        if (string.IsNullOrEmpty(groq)) return groq;

        var registry = SanityGroqTokenRegistry.Instance;
        var tokens = registry.Tokens;
        var sortedKeys = registry.SortedTokenKeys;

        var sb = new StringBuilder(groq.Length);
        var inQuotes = false;
        char? quoteChar = null;
        var isEscaped = false;

        for (var i = 0; i < groq.Length; i++)
        {
            var c = groq[i];

            // 1. Try match token
            string? matchedKey = null;
            string? matchedToken = null;
            foreach (var key in sortedKeys)
                if (i + key.Length <= groq.Length)
                {
                    var match = !key.Where((t, j) => groq[i + j] != t).Any();

                    if (!match) continue;
                    matchedKey = key;
                    matchedToken = tokens[key];
                    break;
                }

            if (matchedKey != null)
            {
                if (matchedKey == SanityConstants.STRING_DELIMITER)
                {
                    if (!inQuotes)
                    {
                        inQuotes = true;
                        quoteChar = '"';
                        isEscaped = false;
                    }
                    else if (quoteChar == '"' && !isEscaped)
                    {
                        inQuotes = false;
                        quoteChar = null;
                    }
                }

                sb.Append(matchedToken);
                i += matchedKey.Length - 1;
                if (inQuotes) isEscaped = false;
                continue;
            }

            // 2. Handle non-token characters
            if (c == SanityConstants.STRING_DELIMITER[0] || c == SanityConstants.SINGLE_QUOTE[0])
            {
                if (!inQuotes)
                {
                    inQuotes = true;
                    quoteChar = c;
                    isEscaped = false;
                }
                else if (c == quoteChar && !isEscaped)
                {
                    inQuotes = false;
                    quoteChar = null;
                }
            }

            if (c == SanityConstants.SPACE[0] && !inQuotes) continue;

            sb.Append(c);

            if (!inQuotes) continue;
            if (c == '\\') isEscaped = !isEscaped;
            else isEscaped = false;
        }

        var json = sb.ToString();
        json = json.Replace(SanityConstants.OPEN_BRACE, SanityConstants.COLON + SanityConstants.OPEN_BRACE);
        if (json.StartsWith(SanityConstants.COLON)) json = json.Substring(1);

        // Replace variable names with valid JSON (e.g., convert myField to "myField": true)
        var reVariables = MyRegex();
        json = reVariables.Replace(json, m => $"{m.Groups[1].Value}{SanityConstants.STRING_DELIMITER}{m.Groups[2].Value}{SanityConstants.STRING_DELIMITER}{SanityConstants.COLON}{SanityConstants.TRUE}{m.Groups[3].Value}");
        // Second pass to handle overlapping matches like {a,b,c}
        json = reVariables.Replace(json, m => $"{m.Groups[1].Value}{SanityConstants.STRING_DELIMITER}{m.Groups[2].Value}{SanityConstants.STRING_DELIMITER}{SanityConstants.COLON}{SanityConstants.TRUE}{m.Groups[3].Value}");

        return json;
    }

    private static string JsonToGroq(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;

        var groq = json
            .Replace(SanityConstants.COLON + SanityConstants.OPEN_BRACE, SanityConstants.OPEN_BRACE)
            .Replace(SanityConstants.COLON + SanityConstants.TRUE, string.Empty)
            .Replace(SanityConstants.STRING_DELIMITER, string.Empty);

        return Untokenize(groq);
    }

    private string ResolveProjection(int maxNestingLevel)
    {
        if (!string.IsNullOrEmpty(Projection)) return Projection;

        // Joins require an explicit projection
        var propertyList = GetPropertyProjectionList(ResultType ?? DocType ?? typeof(object), 0, maxNestingLevel);
        if (propertyList.Count > 0) return JoinComma(propertyList);

        if (Includes.Keys.Count > 0) return string.Join(SanityConstants.COMMA, Includes.Keys);

        return string.Empty;
    }

    private void WrapWithAggregate(StringBuilder sb)
    {
        if (!string.IsNullOrEmpty(AggregateFunction))
        {
            sb.Insert(0, AggregateFunction + SanityConstants.OPEN_PAREN);
            sb.Append(SanityConstants.CHAR_CLOSE_PAREN);
        }

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