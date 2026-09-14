using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class TextPreviewTests
{
    [Theory]
    [InlineData("text/plain", true)]
    [InlineData("text/csv; charset=utf-8", true)]
    [InlineData("application/json", true)]
    [InlineData("Application/XML; charset=utf-8", true)]
    [InlineData("application/x-yaml", true)]
    [InlineData("application/javascript", true)]
    [InlineData("application/ld+json", true)]
    [InlineData("application/octet-stream", false)]
    [InlineData("image/png", false)]
    [InlineData("application/pdf", false)]
    public void IsTextLike(string contentType, bool expected)
        => Assert.Equal(expected, TextPreview.IsTextLike(contentType));

    [Fact]
    public void PrettyPrintJson_IndentsValidJson()
    {
        var pretty = TextPreview.PrettyPrintJson("{\"a\":1,\"b\":[1,2],\"c\":\"ü\"}");

        Assert.Contains("\n", pretty);
        Assert.Contains("\"a\": 1", pretty);
        Assert.Contains("\"c\": \"ü\"", pretty);
    }

    [Fact]
    public void PrettyPrintJson_LeavesInvalidJsonUnchanged()
        => Assert.Equal("{not json", TextPreview.PrettyPrintJson("{not json"));
}
