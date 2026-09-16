using ObjeX.Core.Utilities;

namespace ObjeX.Tests.Unit;

public class InlineMediaTypesTests
{
    [Theory]
    [InlineData("image/png")]
    [InlineData("image/bmp")]
    [InlineData("image/x-icon")]
    [InlineData("IMAGE/JPEG")]
    [InlineData("text/plain; charset=utf-8")]
    [InlineData("application/pdf")]
    [InlineData("video/mp4")]
    [InlineData("audio/mpeg")]
    public void IsInlineSafe_AllowsRenderableTypes(string contentType)
        => Assert.True(InlineMediaTypes.IsInlineSafe(contentType));

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("image/tiff")]
    [InlineData("text/html")]
    [InlineData("application/json")]
    [InlineData("application/octet-stream")]
    public void IsInlineSafe_RejectsEverythingElse(string contentType)
        => Assert.False(InlineMediaTypes.IsInlineSafe(contentType));

    [Fact]
    public void MediaType_StripsParametersAndLowercases()
        => Assert.Equal("text/plain", InlineMediaTypes.MediaType("Text/Plain; charset=utf-8"));
}
