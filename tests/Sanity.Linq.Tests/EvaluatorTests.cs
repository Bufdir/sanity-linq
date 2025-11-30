using System;
using System.Linq.Expressions;
using System.Reflection;
using Xunit;

namespace Sanity.Linq.Tests;

public class EvaluatorTests
{
    [Fact]
    public void PartialEval_OnConstantBinary_AddsAndReturnsConstant()
    {
        // Arrange: 1 + 2
        var expr = Expression.Add(Expression.Constant(1), Expression.Constant(2));

        // Act
        var simplified = InvokePartialEval(expr);

        // Assert
        var constExpr = Assert.IsType<ConstantExpression>(simplified);
        Assert.Equal(typeof(int), constExpr.Type);
        Assert.Equal(3, constExpr.Value);
    }

    [Fact]
    public void PartialEval_OnLambdaWithParameter_EvaluatesOnlyConstantSubtree()
    {
        // Arrange: x => (1 + 2) + x
        Expression<Func<int, int>> lambda = x => (1 + 2) + x;

        // Act
        var simplified = InvokePartialEval(lambda);

        // Assert: x => 3 + x
        var simplifiedLambda = Assert.IsAssignableFrom<LambdaExpression>(simplified);
        var body = Assert.IsAssignableFrom<BinaryExpression>(simplifiedLambda.Body);

        // Left should be constant 3
        var leftConst = Assert.IsType<ConstantExpression>(body.Left);
        Assert.Equal(3, leftConst.Value);

        // Right should be a parameter (or a derived runtime type)
        Assert.IsAssignableFrom<ParameterExpression>(body.Right);

        // Optional: compile and execute to validate runtime behavior
        var compiled = (Func<int, int>)simplifiedLambda.Compile();
        Assert.Equal(8, compiled(5));
    }

    private static Expression InvokePartialEval(Expression expression)
    {
        // Get the internal Evaluator type from the Sanity.Linq assembly
        var asm = typeof(SanityOptions).Assembly;
        var evaluatorType = asm.GetType("Sanity.Linq.Internal.Evaluator", throwOnError: true)!
            ?? throw new InvalidOperationException("Evaluator type not found");

        var method = evaluatorType.GetMethod(
            "PartialEval",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(Expression)],
            modifiers: null
        ) ?? throw new InvalidOperationException("PartialEval(Expression) method not found");

        var result = (Expression)method.Invoke(null, [expression])!;
        return result;
    }
}