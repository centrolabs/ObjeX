using ObjeX.Api.S3;

namespace ObjeX.Tests.Unit;

public class SigV4SignerTests
{
    private const string Sig = "5d672d79c15b13162d9279b0855cfba6789a8edb4c82c400e06b5924a6f2b5d7";

    [Fact]
    public void SignaturesEqual_SameHex_True() => Assert.True(SigV4Signer.SignaturesEqual(Sig, Sig));

    [Fact]
    public void SignaturesEqual_CaseDiffers_True() => Assert.True(SigV4Signer.SignaturesEqual(Sig, Sig.ToUpperInvariant()));

    [Fact]
    public void SignaturesEqual_LastCharDiffers_False() => Assert.False(SigV4Signer.SignaturesEqual(Sig, Sig[..^1] + "8"));

    [Fact]
    public void SignaturesEqual_LengthDiffers_False() => Assert.False(SigV4Signer.SignaturesEqual(Sig, Sig[..^1]));

    [Fact]
    public void SignaturesEqual_Empty_False() => Assert.False(SigV4Signer.SignaturesEqual(Sig, ""));

    [Theory]
    [InlineData("AZaz09-_.~", "AZaz09-_.~")]
    [InlineData("a b/c+d", "a%20b%2Fc%2Bd")]
    [InlineData("ü", "%C3%BC")]
    [InlineData("€", "%E2%82%AC")]
    [InlineData("emoji-😀.txt", "emoji-%F0%9F%98%80.txt")]
    public void UriEncodeStrict_EncodesUtf8PerCodePoint(string value, string expected) =>
        Assert.Equal(expected, SigV4Signer.UriEncodeStrict(value));
}
