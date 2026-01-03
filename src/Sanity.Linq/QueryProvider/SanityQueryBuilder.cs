using Sanity.Linq.CommonTypes;

// ReSharper disable MemberCanBePrivate.Global

namespace Sanity.Linq.QueryProvider;

internal sealed class SanityQueryBuilder
{
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

    public static string GetJoinProjection(string sourceName, string targetName, Type propertyType, int nestingLevel, int maxNestingLevel, bool isExplicit = false)
    {
        return SanityQueryBuilderHelper.GetJoinProjection(sourceName, targetName, propertyType, nestingLevel, maxNestingLevel, isExplicit);
    }

    public static List<string> GetPropertyProjectionList(Type type, int nestingLevel, int maxNestingLevel)
    {
        return SanityQueryBuilderHelper.GetPropertyProjectionList(type, nestingLevel, maxNestingLevel);
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


    private void AddDocTypeConstraintIfAny()
    {
        if (DocType == null || DocType == typeof(object) || DocType == typeof(SanityDocument)) return;

        var rootTypeName = DocTypeCache.Instance.GetOrAdd(DocType, type =>
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

        sb.Append(SanityConstants.CHAR_SPACE).Append(SanityConstants.CHAR_PIPE).Append(SanityConstants.CHAR_SPACE).Append(SanityConstants.ORDER).Append(SanityConstants.CHAR_OPEN_PAREN);
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

        var expanded = SanityQueryBuilderHelper.ExpandIncludesInProjection(normalized, Includes);

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
            sb.Append(SanityConstants.CHAR_DOT).Append(expanded);
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


    private string ResolveProjection(int maxNestingLevel)
    {
        if (!string.IsNullOrEmpty(Projection)) return Projection;

        // Joins require an explicit projection
        var propertyList = SanityQueryBuilderHelper.GetPropertyProjectionList(ResultType ?? DocType ?? typeof(object), 0, maxNestingLevel);
        if (propertyList.Count > 0) return SanityQueryBuilderHelper.JoinComma(propertyList);

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
}