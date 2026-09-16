using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class CustomMetadataTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{not json")]
    public void Parse_ReturnsEmpty(string? json)
        => Assert.Empty(CustomMetadata.Parse(json));

    [Fact]
    public void Parse_StripsPrefixAndSortsByKey()
    {
        var entries = CustomMetadata.Parse("""{"x-amz-meta-zeta":"last","x-amz-meta-alpha":"first","plain":"kept"}""");

        Assert.Equal(
            [
                new KeyValuePair<string, string>("alpha", "first"),
                new KeyValuePair<string, string>("plain", "kept"),
                new KeyValuePair<string, string>("zeta", "last"),
            ],
            entries);
    }

    [Fact]
    public void Parse_KeepsNonStringValuesAsRawJson()
        => Assert.Equal("42", Assert.Single(CustomMetadata.Parse("""{"x-amz-meta-count":42}""")).Value);
}
