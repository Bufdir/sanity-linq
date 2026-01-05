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
using Sanity.Linq.Internal;

// ReSharper disable MemberCanBePrivate.Global

namespace Sanity.Linq.QueryProvider;

internal class SanityExpressionParser(Expression expression, Type docType, int maxNestingLevel, Type? resultType = null) : ExpressionVisitor
{
    private readonly SanityQueryBuilder _queryBuilder = new()
    {
        DocType = docType,
        ResultType = resultType != null ? TypeSystem.GetElementType(resultType) : docType,
        ExpectsArray = resultType != null && resultType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(resultType)
    };

    private readonly HashSet<Expression> _visited = [];
    private SanityMethodCallTranslator? _nestedTranslator;
    private SanityMethodCallTranslator? _topLevelTranslator;

    public Expression Expression { get; } = expression;
    public int MaxNestingLevel { get; set; } = maxNestingLevel;

    /// <summary>
    ///     Builds and returns a query string by parsing the provided expression tree.
    /// </summary>
    /// <param name="includeProjections">
    ///     A boolean value indicating whether to include projections in the generated query.
    ///     If true, projections are included; otherwise, they are not.
    /// </param>
    /// <returns>
    ///     A string representing the constructed query after parsing the expression tree and applying optional projections.
    /// </returns>
    public string BuildQuery(bool includeProjections = true)
    {
        // Parse Query
        var expression = Evaluator.PartialEval(Expression);
        if (expression is MethodCallExpression or LambdaExpression)
            // Traverse expression to build query
            Visit(expression);

        // Build query
        var query = _queryBuilder.Build(includeProjections, MaxNestingLevel);
        return query;
    }

    /// <summary>
    ///     Visits the specified expression and processes it based on its type to construct or modify
    ///     the query representation.
    /// </summary>
    /// <param name="expression">
    ///     The expression to be visited. This can be of various types such as BinaryExpression,
    ///     UnaryExpression, MethodCallExpression, or LambdaExpression. If null, the method
    ///     returns without processing.
    /// </param>
    /// <returns>
    ///     The original expression after it has been visited and processed. If the expression is
    ///     already visited, the method returns the input without re-processing.
    /// </returns>
    public override Expression? Visit(Expression? expression)
    {
        if (expression == null || !_visited.Add(expression)) return expression;

        return expression switch
        {
            BinaryExpression b => HandleVisitBinary(b),
            UnaryExpression u => HandleVisitUnary(u),
            MethodCallExpression m => HandleVisitMethodCall(m),
            LambdaExpression l => HandleVisitLambda(l),
            _ => expression
        };
    }

    private BinaryExpression HandleVisitBinary(BinaryExpression b)
    {
        var wasSilent = _queryBuilder.IsSilent;
        _queryBuilder.IsSilent = true;
        try
        {
            _queryBuilder.AddConstraint(TransformBinaryExpression(b));
        }
        finally
        {
            _queryBuilder.IsSilent = wasSilent;
        }

        return b;
    }

    private LambdaExpression HandleVisitLambda(LambdaExpression l)
    {
        var wasSilent = _queryBuilder.IsSilent;
        _queryBuilder.IsSilent = true;
        try
        {
            switch (l.Body)
            {
                case BinaryExpression b:
                    _queryBuilder.AddConstraint(TransformBinaryExpression(b));
                    break;
                case UnaryExpression u:
                    _queryBuilder.AddConstraint(TransformUnaryExpression(u));
                    break;
                case MethodCallExpression m:
                    _queryBuilder.AddConstraint(TransformMethodCallExpression(m));
                    break;
            }
        }
        finally
        {
            _queryBuilder.IsSilent = wasSilent;
        }

        return l;
    }

    private MethodCallExpression HandleVisitMethodCall(MethodCallExpression m)
    {
        TransformMethodCallExpression(m, true);
        if (m.Arguments.Count > 0 && m.Arguments[0] is not ConstantExpression) Visit(m.Arguments[0]);
        return m;
    }

    private UnaryExpression HandleVisitUnary(UnaryExpression u)
    {
        var wasSilent = _queryBuilder.IsSilent;
        _queryBuilder.IsSilent = true;
        try
        {
            _queryBuilder.AddConstraint(TransformUnaryExpression(u));
        }
        finally
        {
            _queryBuilder.IsSilent = wasSilent;
        }

        return u;
    }

    private string TransformBinaryExpression(BinaryExpression b)
    {
        return SanityExpressionParserHelper.TransformBinaryExpression(b, TransformOperand);
    }

    private string TransformMethodCallExpression(MethodCallExpression e, bool isTopLevel = false)
    {
        var translator = isTopLevel
            ? _topLevelTranslator ??= new SanityMethodCallTranslator(_queryBuilder, TransformOperand, Visit, true)
            : _nestedTranslator ??= new SanityMethodCallTranslator(_queryBuilder, TransformOperand, Visit);
        return translator.Translate(e);
    }

    private string TransformOperand(Expression e)
    {
        var wasSilent = _queryBuilder.IsSilent;
        _queryBuilder.IsSilent = true;
        try
        {
            return SanityExpressionTransformer.TransformOperand(e, mc => TransformMethodCallExpression(mc),
                TransformBinaryExpression, TransformUnaryExpression, _queryBuilder.UseCoalesceFallback);
        }
        finally
        {
            _queryBuilder.IsSilent = wasSilent;
        }
    }

    private string TransformUnaryExpression(UnaryExpression u)
    {
        return SanityExpressionTransformer.TransformUnaryExpression(u, TransformOperand);
    }
}