using ObjeX.Infrastructure.Hashing;

namespace ObjeX.Tests.Unit;

public class Sha256HashServiceTests
{
    private readonly Sha256HashService _sut = new();

    [Fact]
    public void Returns_64_Char_Lowercase_Hex()
    {
        var result = _sut.ComputeHash("test");
        Assert.Equal(64, result.Length);
        Assert.Equal(result, result.ToLowerInvariant());
        Assert.All(result, c => Assert.True(char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c)));
    }

    [Fact]
    public void Deterministic_Same_Input_Same_Output()
    {
        var a = _sut.ComputeHash("photos/2024/trip.jpg");
        var b = _sut.ComputeHash("photos/2024/trip.jpg");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_Inputs_Different_Outputs()
    {
        var a = _sut.ComputeHash("bucket-a/file");
        var b = _sut.ComputeHash("bucket-b/file");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Empty_String_Returns_Valid_Hash()
    {
        var result = _sut.ComputeHash("");
        Assert.Equal(64, result.Length);
    }
}
