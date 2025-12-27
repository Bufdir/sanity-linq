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
    private readonly HashSet<Expression> _visited = [];
    public Type DocType { get; } = docType;
    public Expression Expression { get; } = expression;
    public int MaxNestingLevel { get; set; } = maxNestingLevel;
    public Type ResultType { get; } = resultType != null ? TypeSystem.GetElementType(resultType) : docType;
    public bool ExpectsArray { get; } = resultType != null && resultType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(resultType);
    private SanityQueryBuilder QueryBuilder { get; set; } = new();

    public string BuildQuery(bool includeProjections = true)
    {
        //Initialize query builder
        QueryBuilder = new SanityQueryBuilder
        {
            // Add constraint for root type
            DocType = DocType,
            ResultType = ResultType,
            ExpectsArray = ExpectsArray
        };

        // Parse Query
        var expression = Evaluator.PartialEval(Expression);
        if (expression is MethodCallExpression or LambdaExpression)
            // Traverse expression to build query
            Visit(expression);

        // Build query
        var query = QueryBuilder.Build(includeProjections, MaxNestingLevel);
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
        var simplified = (LambdaExpression)Evaluator.PartialEval(l);
        var wasSilent = QueryBuilder.IsSilent;
        QueryBuilder.IsSilent = true;
        try
        {
            switch (simplified.Body)
            {
                case BinaryExpression b:
                    HandleVisitBinary(b);
                    break;
                case UnaryExpression u:
                    HandleVisitUnary(u);
                    break;
                case MethodCallExpression m:
                    QueryBuilder.Constraints.Add(TransformMethodCallExpression(m));
                    break;
            }
        }
        finally
        {
            QueryBuilder.IsSilent = wasSilent;
        }

        return l;
    }

    private BinaryExpression HandleVisitBinary(BinaryExpression b)
    {
        var wasSilent = QueryBuilder.IsSilent;
        QueryBuilder.IsSilent = true;
        try
        {
            QueryBuilder.Constraints.Add(TransformBinaryExpression(b));
        }
        finally
        {
            QueryBuilder.IsSilent = wasSilent;
        }

        return b;
    }

    private UnaryExpression HandleVisitUnary(UnaryExpression u)
    {
        var wasSilent = QueryBuilder.IsSilent;
        QueryBuilder.IsSilent = true;
        try
        {
            QueryBuilder.Constraints.Add(TransformUnaryExpression(u));
        }
        finally
        {
            QueryBuilder.IsSilent = wasSilent;
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

        if (left == "null" && op is "==" or "!=")
            // Swap left and right so null is always on the right for comparison logic
            (left, right) = (right, left);

        return right switch
        {
            "null" when op == "==" => $"(!(defined({left})) || {left} {op} {right})",
            "null" when op == "!=" => $"(defined({left}) && {left} {op} {right})",
            _ => $"{left} {op} {right}"
        };
    }

    private static string GetBinaryOperator(ExpressionType nodeType)
    {
        return nodeType switch
        {
            ExpressionType.Equal => "==",
            ExpressionType.AndAlso => "&&",
            ExpressionType.OrElse => "||",
            ExpressionType.LessThan => "<",
            ExpressionType.GreaterThan => ">",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.NotEqual => "!=",
            _ => throw new NotImplementedException($"Operator '{nodeType}' is not supported.")
        };
    }

    private string TransformMethodCallExpression(MethodCallExpression e, bool isTopLevel = false)
    {
        var translator = new SanityMethodCallTranslator(QueryBuilder, TransformOperand, Visit, isTopLevel);
        return translator.Translate(e);
    }

    private string TransformOperand(Expression e)
    {
        var wasSilent = QueryBuilder.IsSilent;
        QueryBuilder.IsSilent = true;
        try
        {
            return SanityExpressionTransformer.TransformOperand(e, mc => TransformMethodCallExpression(mc),
                TransformBinaryExpression, TransformUnaryExpression);
        }
        finally
        {
            QueryBuilder.IsSilent = wasSilent;
        }
    }

    private string TransformUnaryExpression(UnaryExpression u)
    {
        return SanityExpressionTransformer.TransformUnaryExpression(u, TransformOperand);
    }
}