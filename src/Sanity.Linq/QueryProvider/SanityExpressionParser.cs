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

    private MethodCallExpression HandleVisitMethodCall(MethodCallExpression m)
    {
        TransformMethodCallExpression(m, true);
        if (m.Arguments.Count > 0 && m.Arguments[0] is not ConstantExpression) Visit(m.Arguments[0]);
        return m;
    }

    private string TransformBinaryExpression(BinaryExpression b)
    {
        var op = GetBinaryOperator(b.NodeType);
        var left = TransformOperand(b.Left);
        var right = TransformOperand(b.Right);

        if (left == SanityConstants.NULL && op is SanityConstants.EQUALS or SanityConstants.NOT_EQUALS)
            // Swap left and right so null is always on the right for comparison logic
            (left, right) = (right, left);

        return right switch
        {
            SanityConstants.NULL when op == SanityConstants.EQUALS => $"{SanityConstants.OPEN_PAREN}{SanityConstants.NOT}{SanityConstants.OPEN_PAREN}{SanityConstants.DEFINED}{SanityConstants.OPEN_PAREN}{left}{SanityConstants.CLOSE_PAREN}{SanityConstants.CLOSE_PAREN}{SanityConstants.SPACE}{SanityConstants.OR}{SanityConstants.SPACE}{left}{SanityConstants.SPACE}{op}{SanityConstants.SPACE}{right}{SanityConstants.CLOSE_PAREN}",
            SanityConstants.NULL when op == SanityConstants.NOT_EQUALS => $"{SanityConstants.OPEN_PAREN}{SanityConstants.DEFINED}{SanityConstants.OPEN_PAREN}{left}{SanityConstants.CLOSE_PAREN}{SanityConstants.SPACE}{SanityConstants.AND}{SanityConstants.SPACE}{left}{SanityConstants.SPACE}{op}{SanityConstants.SPACE}{right}{SanityConstants.CLOSE_PAREN}",
            _ => $"{left}{SanityConstants.SPACE}{op}{SanityConstants.SPACE}{right}"
        };
    }

    private static string GetBinaryOperator(ExpressionType nodeType)
    {
        return nodeType switch
        {
            ExpressionType.Equal => SanityConstants.EQUALS,
            ExpressionType.AndAlso => SanityConstants.AND,
            ExpressionType.OrElse => SanityConstants.OR,
            ExpressionType.LessThan => SanityConstants.LESS_THAN,
            ExpressionType.GreaterThan => SanityConstants.GREATER_THAN,
            ExpressionType.LessThanOrEqual => SanityConstants.LESS_THAN_OR_EQUAL,
            ExpressionType.GreaterThanOrEqual => SanityConstants.GREATER_THAN_OR_EQUAL,
            ExpressionType.NotEqual => SanityConstants.NOT_EQUALS,
            _ => throw new NotImplementedException($"Operator '{nodeType}' is not supported.")
        };
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