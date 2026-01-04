using System.Diagnostics.CodeAnalysis;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.Internal;

namespace Sanity.Linq.QueryProvider;

internal static class SanityQueryBuilderHelper
{
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

        var indicator = isEnumerable && !fieldRef.Contains(SanityConstants.ARRAY_INDICATOR) ? SanityConstants.ARRAY_INDICATOR : string.Empty;
        var filter = isEnumerable && !fieldRef.Contains(SanityConstants.DEFINED) ? SanityConstants.ARRAY_FILTER : string.Empty;
        var suffix = indicator + filter;

        if (targetType.IsSimpleType()) return $"{fieldRef}{suffix}";

        var fields = GetPropertyProjectionList(targetType, nestingLevel, maxNestingLevel);

        if (fields.Count <= 0)
        {
            if (isExplicit) return $"{fieldRef}{suffix}{SanityConstants.OPEN_BRACE}{SanityConstants.SPREAD_OPERATOR}{SanityConstants.COMMA}{SanityConstants.DEREFERENCING_SWITCH}{SanityConstants.OPEN_BRACE}{SanityConstants.SPREAD_OPERATOR}{SanityConstants.CLOSE_BRACE}{SanityConstants.CLOSE_BRACE}";

            return $"{fieldRef}{suffix}{SanityConstants.OPEN_BRACE}{SanityConstants.SPREAD_OPERATOR}{SanityConstants.CLOSE_BRACE}";
        }

        var fieldList = JoinComma(fields);
        if (isExplicit) return $"{fieldRef}{suffix}{SanityConstants.OPEN_BRACE}{fieldList}{SanityConstants.COMMA}{SanityConstants.DEREFERENCING_SWITCH}{SanityConstants.OPEN_BRACE}{fieldList}{SanityConstants.CLOSE_BRACE}{SanityConstants.CLOSE_BRACE}";

        return $"{fieldRef}{suffix}{SanityConstants.OPEN_BRACE}{fieldList}{SanityConstants.CLOSE_BRACE}";
    }

    public static List<string> GetPropertyProjectionList(Type type, int nestingLevel, int maxNestingLevel)
    {
        if (ProjectionCache.Instance.TryGetValue(type, nestingLevel, maxNestingLevel, out var cached)) return [.. cached!];

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

        ProjectionCache.Instance.TryAdd(type, nestingLevel, maxNestingLevel, result.ToArray());
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
        var isDerefPart = part == SanityConstants.DEREFERENCING_OPERATOR;

        foreach (var property in current)
        {
            var hasDerefToken = property.Key.Contains(tokens[SanityConstants.DEREFERENCING_SWITCH]) ||
                                property.Key.Contains(tokens[SanityConstants.DEREFERENCING_OPERATOR]);

            if (isDerefPart && hasDerefToken && property.Value is JObject obj)
                matches.Add(obj);
            else if (KeyMatchesPart(property.Key, part) && property.Value is JObject obj2) matches.Add(obj2);

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

    public static string JoinComma(IEnumerable<string> parts)
    {
        return string.Join(SanityConstants.COMMA, parts);
    }

    private static bool KeyMatchesPart(string key, string part)
    {
        if (key == part) return true;

        var tokens = SanityGroqTokenRegistry.Instance.Tokens;
        if (part == SanityConstants.DEREFERENCING_OPERATOR)
            if (key.Contains(tokens[SanityConstants.DEREFERENCING_SWITCH]) || key.Contains(tokens[SanityConstants.DEREFERENCING_OPERATOR]))
                return true;

        // Simplify key: un-tokenize it and remove spaces
        var k = GroqJsonHelper.Untokenize(key).Replace(SanityConstants.SPACE, string.Empty);

        // Simplify part: remove spaces
        var p = part.Replace(SanityConstants.SPACE, string.Empty);

        if (k == p) return true;

        // Base name match (e.g. "topic" matches "topic[...]").
        // We only care about the part before the first [ or -> or .
        var kBase = k.Split(SanityConstants.CHAR_OPEN_BRACKET, SanityConstants.CHAR_HYPHEN)[0].Split(SanityConstants.CHAR_DOT)[0];
        var pBase = p.Split(SanityConstants.CHAR_OPEN_BRACKET, SanityConstants.CHAR_HYPHEN)[0].Split(SanityConstants.CHAR_DOT)[0];

        if (kBase == pBase && !string.IsNullOrEmpty(kBase)) return true;

        // Handle tokenized match for base field
        var untokenizedKey = GroqJsonHelper.Untokenize(key);
        var untokenizedPart = GroqJsonHelper.Untokenize(part);
        if (untokenizedKey.StartsWith(untokenizedPart) && (untokenizedKey.Length == untokenizedPart.Length || untokenizedKey[untokenizedPart.Length] == SanityConstants.CHAR_OPEN_BRACKET || untokenizedKey[untokenizedPart.Length] == SanityConstants.CHAR_HYPHEN || untokenizedKey[untokenizedPart.Length] == SanityConstants.CHAR_DOT))
            return true;

        return false;
    }

    private static string[] ParseIncludePath(string includeKey)
    {
        return includeKey
            .Replace(SanityConstants.DEREFERENCING_OPERATOR, SanityConstants.DOT + SanityConstants.DEREFERENCING_OPERATOR + SanityConstants.DOT)
            .TrimEnd(SanityConstants.CHAR_DOT)
            .Split(SanityConstants.CHAR_DOT, StringSplitOptions.RemoveEmptyEntries);
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


    public static string ExpandIncludesInProjection(string projection, Dictionary<string, string>? includes)
    {
        if (includes == null || includes.Count == 0) return projection;

        var tokens = SanityGroqTokenRegistry.Instance.Tokens;
        var normalizedProjection = projection.Trim();
        var toConvert = normalizedProjection.StartsWith(SanityConstants.OPEN_BRACE) && normalizedProjection.EndsWith(SanityConstants.CLOSE_BRACE)
            ? normalizedProjection
            : SanityConstants.OPEN_BRACE + normalizedProjection + SanityConstants.CLOSE_BRACE;

        var jsonProjection = GroqJsonHelper.GroqToJson(toConvert);
        JObject jObjectProjection;
        try
        {
            if (JsonConvert.DeserializeObject(jsonProjection) is not JObject obj)
                return projection;
            jObjectProjection = obj;
        }
        catch (Exception)
        {
            return projection;
        }

        // Apply includes one by one
        foreach (var (includeKey, includeValue) in includes.OrderBy(k => k.Key.Length).ThenBy(k => k.Key))
        {
            var pathParts = ParseIncludePath(includeKey);
            EnsurePath(jObjectProjection, pathParts, out var parents);

            var lastPart = pathParts[^1];
            var jsonInclude = GroqJsonHelper.GroqToJson(SanityConstants.OPEN_BRACE + includeValue + SanityConstants.CLOSE_BRACE);
            if (JsonConvert.DeserializeObject(jsonInclude) is not JObject jObjectInclude) continue;

            foreach (var parent in parents)
            foreach (var incProp in jObjectInclude.Properties())
            {
                var incKey = incProp.Name;
                var incValue = incProp.Value;

                var untokenizedIncKey = GroqJsonHelper.Untokenize(incKey);
                var openBrkt = untokenizedIncKey.IndexOf(SanityConstants.CHAR_OPEN_BRACKET);
                var closeBrkt = untokenizedIncKey.IndexOf(SanityConstants.CHAR_CLOSE_BRACKET);
                var isFiltered = openBrkt >= 0 && closeBrkt > openBrkt + 1;
                var baseName = isFiltered ? untokenizedIncKey.Substring(0, openBrkt) : untokenizedIncKey;

                // 1. Find or create existing base object
                string? existingKey = null;
                JObject? existingObj = null;
                foreach (var prop in parent.Properties())
                    if (KeyMatchesPart(prop.Name, baseName))
                    {
                        existingKey = prop.Name;
                        if (prop.Value is JObject obj) existingObj = obj;
                        break;
                    }

                if (existingObj == null)
                {
                    // No existing property, add new or create base
                    if (isFiltered || lastPart.Contains(SanityConstants.OPEN_BRACKET))
                    {
                        existingKey = baseName + SanityConstants.ARRAY_INDICATOR + SanityConstants.ARRAY_FILTER;
                        existingObj = new JObject { [tokens[SanityConstants.SPREAD_OPERATOR]] = true };
                        parent[existingKey] = existingObj;
                    }
                    else
                    {
                        parent[incKey] = incValue;
                        existingKey = incKey;
                        if (incValue is JObject obj2) existingObj = obj2;
                    }
                }

                // 2. Merge properties from include into existing object
                if (existingObj != null)
                {
                    if (isFiltered)
                    {
                        var filter = untokenizedIncKey.Substring(openBrkt + 1, closeBrkt - openBrkt - 1);
                        var normalizedFilter = filter.Replace(SanityConstants.STRING_DELIMITER, SanityConstants.SINGLE_QUOTE).Replace(" ", "");
                        var condKey = normalizedFilter + SanityConstants.ARROW;

                        // Unwrap incValue if it's aliased or a deref switch
                        if (incValue is JObject incValueObj)
                        {
                            // Handle alias
                            foreach (var p in incValueObj.Properties())
                                if (KeyMatchesPart(p.Name, baseName))
                                {
                                    incValue = p.Value;
                                    break;
                                }

                            // Handle deref switch unwrap
                            if (incValue is JObject incValueObjUnwrapped)
                                foreach (var p in incValueObjUnwrapped.Properties())
                                {
                                    var untokenizedP = GroqJsonHelper.Untokenize(p.Name);
                                    if (untokenizedP == condKey || (untokenizedP.StartsWith(condKey) && (untokenizedP.EndsWith(SanityConstants.DEREFERENCING_OPERATOR) || untokenizedP == SanityConstants.DEREFERENCING_SWITCH)))
                                    {
                                        incValue = p.Value;
                                        break;
                                    }
                                }
                        }

                        // Find target conditional expansion (handle @-> suffix)
                        string? targetCondKey = null;
                        foreach (var p in existingObj.Properties())
                        {
                            var untokenizedP = GroqJsonHelper.Untokenize(p.Name);
                            if (untokenizedP == condKey || (untokenizedP.StartsWith(condKey) && (untokenizedP.EndsWith(SanityConstants.DEREFERENCING_OPERATOR) || untokenizedP == SanityConstants.DEREFERENCING_SWITCH)))
                            {
                                targetCondKey = p.Name;
                                break;
                            }
                        }

                        if (targetCondKey != null && existingObj[targetCondKey] is JObject existingCondObj && incValue is JObject incValueObj2)
                            existingCondObj.Merge(incValueObj2, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Union });
                        else
                            existingObj[condKey] = incValue;
                    }
                    else
                    {
                        // Not filtered, merge directly
                        if (incValue is JObject incValueObj)
                            existingObj.Merge(incValueObj, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Union });
                        else
                            existingObj[incKey] = incValue;
                    }
                }

                // 3. Ensure key is base name (with [] if it was filtered)
                if (existingKey != null)
                {
                    var untokenizedExisting = GroqJsonHelper.Untokenize(existingKey);
                    var firstOpen = untokenizedExisting.IndexOf(SanityConstants.CHAR_OPEN_BRACKET);
                    if (firstOpen >= 0)
                    {
                        var firstClose = untokenizedExisting.IndexOf(SanityConstants.CHAR_CLOSE_BRACKET, firstOpen);
                        if (firstClose > firstOpen + 1)
                        {
                            // Filtered key (e.g. topic[_type=="foo"]) -> convert to base array key
                            var bName = untokenizedExisting.Substring(0, firstOpen);
                            var newKey = bName + SanityConstants.ARRAY_INDICATOR + SanityConstants.ARRAY_FILTER;

                            var val = parent[existingKey];
                            parent.Remove(existingKey);
                            parent[newKey] = val;
                        }
                    }
                }
            }
        }

        return GroqJsonHelper.JsonToGroq(jObjectProjection.ToString(Formatting.None));
    }
}