using ObjeX.Core.Validation;

namespace ObjeX.Tests.Unit;

public class ObjectKeyValidatorTests
{
    [Theory]
    [InlineData("file.txt")]
    [InlineData("path/to/file.txt")]
    [InlineData("file with spaces.txt")]
    [InlineData("a")]
    public void Valid_Keys_Return_Null(string key)
    {
        Assert.Null(ObjectKeyValidator.GetValidationError(key));
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..")]
    [InlineData("....")]
    [InlineData("..\\..\\..")]
    [InlineData("..\\..\\windows\\system32")]
    public void Dot_And_Backslash_Keys_Are_Valid(string key)
    {
        Assert.Null(ObjectKeyValidator.GetValidationError(key));
    }

    [Fact]
    public void Empty_Returns_Error()
    {
        Assert.NotNull(ObjectKeyValidator.GetValidationError(""));
    }

    [Fact]
    public void Null_Returns_Error()
    {
        Assert.NotNull(ObjectKeyValidator.GetValidationError(null!));
    }

    [Fact]
    public void Over_1024_Chars_Returns_Error()
    {
        var key = new string('a', 1025);
        Assert.NotNull(ObjectKeyValidator.GetValidationError(key));
    }

    [Fact]
    public void Exactly_1024_Chars_Is_Valid()
    {
        var key = new string('a', 1024);
        Assert.Null(ObjectKeyValidator.GetValidationError(key));
    }

    [Fact]
    public void Leading_Slash_Returns_Error()
    {
        Assert.NotNull(ObjectKeyValidator.GetValidationError("/leading-slash"));
    }

    [Theory]
    [InlineData("has\0" + "null")]
    [InlineData("has\x01" + "ctrl")]
    [InlineData("has\u007f" + "delete")]
    public void Control_Characters_Return_Error(string key)
    {
        Assert.NotNull(ObjectKeyValidator.GetValidationError(key));
    }
}
