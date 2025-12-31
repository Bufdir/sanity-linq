using System;
using System.Linq.Expressions;
using System.Reflection;
using Newtonsoft.Json;
using Sanity.Linq.CommonTypes;
using Sanity.Linq.QueryProvider;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityExpressionTransformerTests
{
    private string MethodCallHandler(MethodCallExpression mc) => "mc";
    private string BinaryExpressionHandler(BinaryExpression b) => "b";
    private string UnaryExpressionHandler(UnaryExpression u) => "u";

    [Fact]
    public void TransformOperand_ConstantNull_ReturnsNullString()
    {
        var expr = Expression.Constant(null);
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("null", result);
    }

    [Fact]
    public void TransformOperand_ConstantString_ReturnsQuotedString()
    {
        var expr = Expression.Constant("hello");
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("\"hello\"", result);
    }

    [Fact]
    public void TransformOperand_ConstantString_EscapesSpecialChars()
    {
        var expr = Expression.Constant("hello \"world\" \\");
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("\"hello \\\"world\\\" \\\\\"", result);
    }

    [Fact]
    public void TransformOperand_ConstantInt_ReturnsString()
    {
        var expr = Expression.Constant(123);
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("123", result);
    }

    [Fact]
    public void TransformOperand_ConstantLong_ReturnsString()
    {
        var expr = Expression.Constant(123L);
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("123", result);
    }

    [Fact]
    public void TransformOperand_ConstantBoolTrue_ReturnsLowerTrue()
    {
        var expr = Expression.Constant(true);
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("true", result);
    }

    [Fact]
    public void TransformOperand_ConstantDateTime_ReturnsQuotedDate()
    {
        var dt = new DateTime(2023, 10, 5, 12, 0, 0);
        var expr = Expression.Constant(dt);
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal($"\"{dt:O}\"", result);
    }

    [Fact]
    public void TransformOperand_ConstantDateTimeDateOnly_ReturnsQuotedShortDate()
    {
        var dt = new DateTime(2023, 10, 5);
        var expr = Expression.Constant(dt);
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("\"2023-10-05\"", result);
    }

    [Fact]
    public void TransformOperand_ConstantGuid_ReturnsQuotedGuid()
    {
        var guid = Guid.NewGuid();
        var expr = Expression.Constant(guid);
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal($"\"{guid}\"", result);
    }

    [Fact]
    public void TransformOperand_Parameter_ReturnsAt()
    {
        var expr = Expression.Parameter(typeof(object), "p");
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("@", result);
    }

    [Fact]
    public void TransformOperand_NewArray_ReturnsJoinedArray()
    {
        var expr = Expression.NewArrayInit(typeof(int), Expression.Constant(1), Expression.Constant(2));
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("[1,2]", result);
    }

    private class TestDoc
    {
        public string? Title { get; set; }
        public TestDoc? Nested { get; set; }
        [JsonProperty("custom_name")]
        public string? CustomName { get; set; }
        public SanityReference<TestDoc>? Ref { get; set; }
    }

    public interface IInterfaceWithJsonProperty
    {
        [JsonProperty("_from_interface")]
        string? InterfaceProp { get; set; }
    }

    private class DocWithInterface : IInterfaceWithJsonProperty
    {
        public string? InterfaceProp { get; set; }
    }

    [Fact]
    public void TransformOperand_JsonPropertyOnInterface_ReturnsInterfaceJsonPropertyName()
    {
        var param = Expression.Parameter(typeof(DocWithInterface), "d");
        var expr = Expression.Property(param, nameof(DocWithInterface.InterfaceProp));
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("_from_interface", result);
    }

    [Fact]
    public void TransformOperand_MemberExpression_ReturnsCamelCase()
    {
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var expr = Expression.Property(param, nameof(TestDoc.Title));
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("title", result);
    }

    [Fact]
    public void TransformOperand_NestedMemberExpression_ReturnsDottedPath()
    {
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var nested = Expression.Property(param, nameof(TestDoc.Nested));
        var expr = Expression.Property(nested, nameof(TestDoc.Title));
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("nested.title", result);
    }

    [Fact]
    public void TransformOperand_CustomJsonProperty_ReturnsJsonPropertyName()
    {
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var expr = Expression.Property(param, nameof(TestDoc.CustomName));
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("custom_name", result);
    }

    [Fact]
    public void TransformOperand_SanityReferenceValue_ReturnsDereference()
    {
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var refProp = Expression.Property(param, nameof(TestDoc.Ref));
        var valProp = Expression.Property(refProp, "Value");
        var result = SanityExpressionTransformer.TransformOperand(valProp, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("ref->", result);
    }

    [Fact]
    public void TransformOperand_NewExpression_ReturnsProjection()
    {
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var titleProp = Expression.Property(param, nameof(TestDoc.Title));
        
        // Simulating anonymous type: new { Title = d.Title }
        var anonType = new { Title = "" }.GetType();
        var constructor = anonType.GetConstructors()[0];
        var members = new MemberInfo[] { anonType.GetProperty("Title")! };
        var expr = Expression.New(constructor, new Expression[] { titleProp }, members);
        
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("title", result);
    }
    
    [Fact]
    public void TransformOperand_NewExpression_WithAlias_ReturnsAliasedProjection()
    {
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var titleProp = Expression.Property(param, nameof(TestDoc.Title));
        
        // Simulating anonymous type: new { MyTitle = d.Title }
        var anonType = new { MyTitle = "" }.GetType();
        var constructor = anonType.GetConstructors()[0];
        var members = new MemberInfo[] { anonType.GetProperty("MyTitle")! };
        var expr = Expression.New(constructor, new Expression[] { titleProp }, members);
        
        var result = SanityExpressionTransformer.TransformOperand(expr, MethodCallHandler, BinaryExpressionHandler, UnaryExpressionHandler);
        Assert.Equal("\"myTitle\": title", result);
    }

    [Fact]
    public void TransformUnaryExpression_Not_ReturnsExclamation()
    {
        var operand = Expression.Constant(true);
        var expr = Expression.Not(operand);
        var result = SanityExpressionTransformer.TransformUnaryExpression(expr, e => "true");
        Assert.Equal("!(true)", result);
    }

    [Fact]
    public void TransformUnaryExpression_Convert_ReturnsOperand()
    {
        var operand = Expression.Constant(1);
        var expr = Expression.Convert(operand, typeof(object));
        var result = SanityExpressionTransformer.TransformUnaryExpression(expr, e => "1");
        Assert.Equal("1", result);
    }

    [Fact]
    public void EscapeString_EscapesBackslashAndQuote()
    {
        var input = "a\\b\"c";
        var result = SanityExpressionTransformer.EscapeString(input);
        Assert.Equal("a\\\\b\\\"c", result);
    }
}
