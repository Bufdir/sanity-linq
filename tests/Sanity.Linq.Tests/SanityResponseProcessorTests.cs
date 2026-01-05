using Newtonsoft.Json.Linq;
using Sanity.Linq.QueryProvider;
using Xunit;

namespace Sanity.Linq.Tests;

public class SanityResponseProcessorTests
{
    [Fact]
    public void ExtractResult_ShouldExtractResult_FromRoot()
    {
        // Arrange
        var json = @"{
  ""query"" : ""*[_type == 'userquestion']"",
  ""result"" : [ { ""_id"": ""1"" } ]
}";

        // Act
        var result = SanityResponseProcessor.ExtractResult(json);

        // Assert
        Assert.NotNull(result);
        var jArray = JArray.Parse(result);
        Assert.Single(jArray);
        Assert.Equal("1", jArray[0]["_id"]?.ToString());
    }

    [Fact]
    public void ExtractResult_ShouldExtractResult_FromNestedResponse()
    {
        // Arrange
        var json = @"{
  ""response"": {
    ""content"": {
      ""result"": [ { ""_id"": ""2"" } ]
    }
  }
}";

        // Act
        var result = SanityResponseProcessor.ExtractResult(json);

        // Assert
        Assert.NotNull(result);
        var jArray = JArray.Parse(result);
        Assert.Single(jArray);
        Assert.Equal("2", jArray[0]["_id"]?.ToString());
    }

    [Fact]
    public void ExtractResult_ShouldReturnNull_IfResultNotFound()
    {
        // Arrange
        var json = @"{ ""foo"": ""bar"" }";

        // Act
        var result = SanityResponseProcessor.ExtractResult(json);

        // Assert
        Assert.Null(result);
    }


    [Fact]
    public void ExtractSanityResult_ExtensionMethod_ShouldWork()
    {
        // Arrange
        var json = @"{ ""result"": [1, 2, 3] }";

        // Act
        var result = json.ExtractSanityResult();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("1", result);
        Assert.Contains("2", result);
        Assert.Contains("3", result);
    }
}
