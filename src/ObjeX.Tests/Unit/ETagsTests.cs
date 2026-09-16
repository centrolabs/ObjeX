using ObjeX.Core.Utilities;

namespace ObjeX.Tests.Unit;

public class ETagsTests
{
    [Theory]
    [InlineData("d41d8cd98f00b204e9800998ecf8427e-2")]
    [InlineData("d41d8cd98f00b204e9800998ecf8427e-10000")]
    public void IsMultipart_TrueForPartCountSuffix(string etag) =>
        Assert.True(ETags.IsMultipart(etag));

    [Theory]
    [InlineData("d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("")]
    [InlineData(null)]
    public void IsMultipart_FalseForPlainMd5(string? etag) =>
        Assert.False(ETags.IsMultipart(etag));
}
