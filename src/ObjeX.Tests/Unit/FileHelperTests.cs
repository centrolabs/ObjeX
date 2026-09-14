using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class FileHelperTests
{
    [Theory]
    [InlineData("test/report#final.pdf", "/api/objects/b/test/report%23final.pdf")]
    [InlineData("a?b.txt", "/api/objects/b/a%3Fb.txt")]
    [InlineData("100%.txt", "/api/objects/b/100%25.txt")]
    [InlineData("with space.txt", "/api/objects/b/with%20space.txt")]
    [InlineData("q&a.txt", "/api/objects/b/q%26a.txt")]
    [InlineData("photos/2024/trip.jpg", "/api/objects/b/photos/2024/trip.jpg")]
    public void ObjectUrl_EncodesEachSegmentAndKeepsSlashes(string key, string expected)
        => Assert.Equal(expected, FileHelper.ObjectUrl("b", key));

    [Fact]
    public void ObjectUrl_Download_AppendsQuery()
        => Assert.Equal("/api/objects/b/report%23final.pdf?download=true", FileHelper.ObjectUrl("b", "report#final.pdf", download: true));

    [Fact]
    public void FolderZipUrl_EncodesPrefix()
        => Assert.Equal("/api/objects/b/download?prefix=q%26a%2F", FileHelper.FolderZipUrl("b", "q&a/"));
}
