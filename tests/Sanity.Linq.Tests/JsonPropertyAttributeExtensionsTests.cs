using System.Reflection;
using Newtonsoft.Json;
using Xunit;
namespace Sanity.Linq.Tests;

public class JsonPropertyAttributeExtensionsTests
{
    private interface ITestInterface
    {
        [JsonProperty("interface_prop")]
        string InterfaceProp { get; set; }
        
        string CamelCaseProp { get; set; }
    }

    private class TestClass : ITestInterface
    {
        [JsonProperty("direct_prop")]
        public string DirectProp { get; set; } = string.Empty;

        public string CamelCaseProp { get; set; } = string.Empty;

        public string InterfaceProp { get; set; } = string.Empty;

        [JsonProperty("direct_field")]
        public string DirectField = string.Empty;

        public string CamelCaseField = string.Empty;
    }

    [Fact]
    public void GetJsonProperty_ShouldReturnExplicitPropertyName_WhenAttributeIsPresent()
    {
        // Arrange
        var member = typeof(TestClass).GetProperty(nameof(TestClass.DirectProp))!;

        // Act
        var result = member.GetJsonProperty();

        // Assert
        Assert.Equal("direct_prop", result);
    }

    [Fact]
    public void GetJsonProperty_ShouldReturnCamelCaseName_WhenAttributeIsMissing()
    {
        // Arrange
        var member = typeof(TestClass).GetProperty(nameof(TestClass.CamelCaseProp))!;

        // Act
        var result = member.GetJsonProperty();

        // Assert
        Assert.Equal("camelCaseProp", result);
    }

    [Fact]
    public void GetJsonProperty_ShouldReturnInterfacePropertyName_WhenAttributeIsOnInterface()
    {
        // Arrange
        var member = typeof(TestClass).GetProperty(nameof(TestClass.InterfaceProp))!;

        // Act
        var result = member.GetJsonProperty();

        // Assert
        Assert.Equal("interface_prop", result);
    }

    [Fact]
    public void GetJsonProperty_ShouldReturnExplicitFieldName_WhenAttributeIsPresentOnField()
    {
        // Arrange
        var member = typeof(TestClass).GetField(nameof(TestClass.DirectField))!;

        // Act
        var result = member.GetJsonProperty();

        // Assert
        Assert.Equal("direct_field", result);
    }

    [Fact]
    public void GetJsonProperty_ShouldReturnCamelCaseFieldName_WhenAttributeIsMissingOnField()
    {
        // Arrange
        var member = typeof(TestClass).GetField(nameof(TestClass.CamelCaseField))!;

        // Act
        var result = member.GetJsonProperty();

        // Assert
        Assert.Equal("camelCaseField", result);
    }
}
