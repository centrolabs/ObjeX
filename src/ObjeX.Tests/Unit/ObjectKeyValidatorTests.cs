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

    [Fact]
    public void Traversal_Key_Passes_Validation_Because_Sanitized_Non_Empty()
    {
        // "../../../etc/passwd" → Replace("..", "") → "///etc/passwd" → Trim('/') → "etc/passwd" (non-empty)
        Assert.Null(ObjectKeyValidator.GetValidationError("../../../etc/passwd"));
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

    [Fact]
    public void All_Dots_Sanitizes_To_Empty_Returns_Error()
    {
        // "...." → Replace("..", "") → "" → empty after Trim
        Assert.NotNull(ObjectKeyValidator.GetValidationError("...."));
    }

    [Fact]
    public void Backslash_Traversal_Sanitizes_To_Empty_Returns_Error()
    {
        // "..\\..\\.." → Replace("..", "") → "\\\\" → Replace("\\", "/") → "//" → Trim('/') → ""
        Assert.NotNull(ObjectKeyValidator.GetValidationError("..\\..\\.."));
    }

    [Fact]
    public void Mixed_Traversal_With_Content_Passes()
    {
        // "..\\..\\windows\\system32" → Replace("..", "") → "\\\\windows\\system32" → Replace("\\", "/") → "//windows/system32" → Trim('/') → "windows/system32"
        Assert.Null(ObjectKeyValidator.GetValidationError("..\\..\\windows\\system32"));
    }
}
