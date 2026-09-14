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
}
