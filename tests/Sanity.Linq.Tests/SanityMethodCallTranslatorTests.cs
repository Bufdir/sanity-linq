using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.QueryProvider;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityMethodCallTranslatorTests
{
    private readonly SanityQueryBuilder _queryBuilder = new();
    private readonly SanityMethodCallTranslator _translator;

    public SanityMethodCallTranslatorTests()
    {
        _translator = new SanityMethodCallTranslator(_queryBuilder, TransformOperand, Visit, true);
    }

    private string TransformOperand(Expression e)
    {
        return e switch
        {
            ParameterExpression => "@",
            MemberExpression m => m.Member.Name.ToLower(),
            ConstantExpression c when c.Type == typeof(string) => $"\"{c.Value}\"",
            ConstantExpression c when typeof(IQueryable).IsAssignableFrom(c.Type) => "source",
            ConstantExpression c => c.Value?.ToString() ?? "null",
            BinaryExpression { NodeType: ExpressionType.Equal } b => $"{TransformOperand(b.Left)} == {TransformOperand(b.Right)}",
            MethodCallExpression mc => _translator.Translate(mc),
            _ => "expr"
        };
    }

    private static Expression? Visit(Expression? e)
    {
        return e;
    }

    [Fact]
    public void Translate_StartsWith_Constant_ReturnsMatch()
    {
        var param = Expression.Parameter(typeof(string), "s");
        var method = typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!;
        var expr = Expression.Call(param, method, Expression.Constant("abc"));

        var result = _translator.Translate(expr);
        Assert.Equal("@ match \"abc*\"", result);
    }

    [Fact]
    public void Translate_Where_AddsConstraint()
    {
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var predicate = Expression.Lambda<Func<TestDoc, bool>>(
            Expression.Equal(Expression.Property(param, "Title"), Expression.Constant("Hello")),
            param);

        var source = Expression.Constant(new List<TestDoc>().AsQueryable());
        var method = typeof(Queryable).GetMethods().First(m => m.Name == "Where" && m.GetParameters().Length == 2).MakeGenericMethod(typeof(TestDoc));
        var expr = Expression.Call(null, method, source, Expression.Quote(predicate));

        _translator.Translate(expr);

        Assert.Contains("title == \"Hello\"", _queryBuilder.Constraints);
    }

    [Fact]
    public void Translate_OrderBy_AddsOrdering()
    {
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var keySelector = Expression.Lambda<Func<TestDoc, string>>(
            Expression.Property(param, "Title"),
            param);

        var source = Expression.Constant(new List<TestDoc>().AsQueryable());
        var method = typeof(Queryable).GetMethods().First(m => m.Name == "OrderBy" && m.GetParameters().Length == 2).MakeGenericMethod(typeof(TestDoc), typeof(string));
        var expr = Expression.Call(null, method, source, Expression.Quote(keySelector));

        _translator.Translate(expr);

        Assert.Contains("title asc", _queryBuilder.Orderings);
    }

    [Fact]
    public void Translate_OrderByDescending_AddsOrderingDesc()
    {
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var keySelector = Expression.Lambda<Func<TestDoc, string>>(
            Expression.Property(param, "Title"),
            param);

        var source = Expression.Constant(new List<TestDoc>().AsQueryable());
        var method = typeof(Queryable).GetMethods().First(m => m.Name == "OrderByDescending" && m.GetParameters().Length == 2).MakeGenericMethod(typeof(TestDoc), typeof(string));
        var expr = Expression.Call(null, method, source, Expression.Quote(keySelector));

        _translator.Translate(expr);

        Assert.Contains("title desc", _queryBuilder.Orderings);
    }

    [Fact]
    public void Translate_Take_SetsTake()
    {
        var source = Expression.Constant(new List<TestDoc>().AsQueryable());
        var method = typeof(Queryable).GetMethods().First(m => m.Name == "Take" && m.GetParameters().Length == 2).MakeGenericMethod(typeof(TestDoc));
        var expr = Expression.Call(null, method, source, Expression.Constant(10));

        _translator.Translate(expr);

        Assert.Equal(10, _queryBuilder.Take);
    }

    [Fact]
    public void Translate_Skip_SetsSkip()
    {
        var source = Expression.Constant(new List<TestDoc>().AsQueryable());
        var method = typeof(Queryable).GetMethods().First(m => m.Name == "Skip" && m.GetParameters().Length == 2).MakeGenericMethod(typeof(TestDoc));
        var expr = Expression.Call(null, method, source, Expression.Constant(5));

        _translator.Translate(expr);

        Assert.Equal(5, _queryBuilder.Skip);
    }

    [Fact]
    public void Translate_Count_SetsAggregate()
    {
        var source = Expression.Constant(new List<TestDoc>().AsQueryable());
        var method = typeof(Queryable).GetMethods().First(m => m.Name == "Count" && m.GetParameters().Length == 1).MakeGenericMethod(typeof(TestDoc));
        var expr = Expression.Call(null, method, source);

        _translator.Translate(expr);

        Assert.Equal("count", _queryBuilder.AggregateFunction);
    }

    [Fact]
    public void Translate_Any_SetsAggregate()
    {
        var source = Expression.Constant(new List<TestDoc>().AsQueryable());
        var method = typeof(Queryable).GetMethods().First(m => m.Name == "Any" && m.GetParameters().Length == 1).MakeGenericMethod(typeof(TestDoc));
        var expr = Expression.Call(null, method, source);

        _translator.Translate(expr);

        Assert.Equal("count", _queryBuilder.AggregateFunction);
        Assert.Equal(" > 0", _queryBuilder.AggregatePostFix);
    }

    [Fact]
    public void Translate_Contains_Enumerable_ReturnsInExpression()
    {
        var list = new List<string> { "a", "b" };
        var listExpr = Expression.Constant(list);
        var param = Expression.Parameter(typeof(string), "s");
        var method = typeof(Enumerable).GetMethods().First(m => m.Name == "Contains" && m.GetParameters().Length == 2).MakeGenericMethod(typeof(string));
        var expr = Expression.Call(null, method, listExpr, param);

        var result = _translator.Translate(expr);
        Assert.Equal("@ in [\"a\",\"b\"]", result);
    }

    [Fact]
    public void Translate_IsNullOrEmpty_ReturnsCombinedExpression()
    {
        var method = typeof(string).GetMethod("IsNullOrEmpty", new[] { typeof(string) })!;
        var expr = Expression.Call(null, method, Expression.Parameter(typeof(string), "s"));

        var result = _translator.Translate(expr);
        Assert.Equal("(@ == null || @ == \"\" || !(defined(@)))", result);
    }

    [Fact]
    public void Translate_Max_SetsAggregateAndPostFix()
    {
        var source = Expression.Constant(new List<TestDoc>().AsQueryable());
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var selector = Expression.Lambda<Func<TestDoc, string>>(Expression.Property(param, "Title"), param);
        var method = typeof(Queryable).GetMethods().First(m => m.Name == "Max" && m.GetGenericArguments().Length == 2).MakeGenericMethod(typeof(TestDoc), typeof(string));
        var expr = Expression.Call(null, method, source, selector);

        _translator.Translate(expr);

        Assert.Equal("title", _queryBuilder.Projection);
        Assert.Contains("| order(@ desc)[0]", _queryBuilder.AggregatePostFix);
    }

    [Fact]
    public void Translate_Min_SetsAggregateAndPostFix()
    {
        var source = Expression.Constant(new List<TestDoc>().AsQueryable());
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var selector = Expression.Lambda<Func<TestDoc, string>>(Expression.Property(param, "Title"), param);
        var method = typeof(Queryable).GetMethods().First(m => m.Name == "Min" && m.GetGenericArguments().Length == 2).MakeGenericMethod(typeof(TestDoc), typeof(string));
        var expr = Expression.Call(null, method, source, selector);

        _translator.Translate(expr);

        Assert.Equal("title", _queryBuilder.Projection);
        Assert.Contains("| order(@ asc)[0]", _queryBuilder.AggregatePostFix);
    }

    [Fact]
    public void Translate_Any_WithPredicate_AddsConstraint()
    {
        var source = Expression.Constant(new List<TestDoc>().AsQueryable());
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var predicate = Expression.Lambda<Func<TestDoc, bool>>(Expression.Equal(Expression.Property(param, "Title"), Expression.Constant("A")), param);
        var method = typeof(Queryable).GetMethods().First(m => m.Name == "Any" && m.GetParameters().Length == 2).MakeGenericMethod(typeof(TestDoc));
        var expr = Expression.Call(null, method, source, predicate);

        _translator.Translate(expr);

        Assert.Equal("count", _queryBuilder.AggregateFunction);
        Assert.Equal(" > 0", _queryBuilder.AggregatePostFix);
        Assert.Contains("title == \"A\"", _queryBuilder.Constraints);
    }

    [Fact]
    public void Translate_Any_OnLocalCollection_ReturnsInExpression()
    {
        var localList = new List<string> { "id1", "id2" };
        var listExpr = Expression.Constant(localList);
        var param = Expression.Parameter(typeof(string), "id");
        var predicate = Expression.Lambda<Func<string, bool>>(Expression.Equal(param, Expression.Constant("id1")), param);
        
        // This is tricky to simulate perfectly as a method call without a full expression tree,
        // but we can try to call TryHandleAnyConstantCollection via HandleAny.
        
        var method = typeof(Enumerable).GetMethods().First(m => m.Name == "Any" && m.GetParameters().Length == 2).MakeGenericMethod(typeof(string));
        var expr = Expression.Call(null, method, listExpr, predicate);

        var result = _translator.Translate(expr);
        Assert.Equal("\"id1\" in [\"id1\",\"id2\"]", result);
    }

    [Fact]
    public void Translate_OfType_AddsFilter()
    {
        var source = Expression.Constant(new List<TestDoc>().AsQueryable());
        var method = typeof(Queryable).GetMethod("OfType")!.MakeGenericMethod(typeof(DerivedDoc));
        var expr = Expression.Call(null, method, source);

        var result = _translator.Translate(expr);
        Assert.Equal("source[_type == \"derivedDoc\"]", result);
    }

    [Fact]
    public void Translate_IsDraft_ReturnsDraftPathFilter()
    {
        var method = typeof(SanityDocumentExtensions).GetMethod("IsDraft")!;
        var expr = Expression.Call(null, method, Expression.Parameter(typeof(TestDoc), "d"));

        var result = _translator.Translate(expr);
        Assert.Contains("path(\"drafts.**\")", result);
    }

    [Fact]
    public void Translate_IsDefined_ReturnsDefinedExpression()
    {
        var method = typeof(SanityDocumentExtensions).GetMethod("IsDefined")!;
        var expr = Expression.Call(null, method, Expression.Property(Expression.Parameter(typeof(TestDoc), "d"), "Title"));

        var result = _translator.Translate(expr);
        Assert.Equal("defined(title)", result);
    }

    [Fact]
    public void Translate_Select_ComplexObject_SetsFlattenProjection()
    {
        var source = Expression.Constant(new List<TestDoc>().AsQueryable());
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var selector = Expression.Lambda<Func<TestDoc, TestDoc>>(Expression.Property(param, "Nested"), param);
        var method = typeof(Queryable).GetMethods().First(m => m.Name == "Select" && m.GetGenericArguments().Length == 2).MakeGenericMethod(typeof(TestDoc), typeof(TestDoc));
        var expr = Expression.Call(null, method, source, selector);

        _translator.Translate(expr);

        Assert.Equal("nested", _queryBuilder.Projection);
        Assert.True(_queryBuilder.FlattenProjection);
    }

    private class DerivedDoc : TestDoc
    {
    }

    private class TestDoc : SanityDocument
    {
        public string Title { get; set; } = null!;
        public TestDoc Nested { get; set; } = null!;
    }
}