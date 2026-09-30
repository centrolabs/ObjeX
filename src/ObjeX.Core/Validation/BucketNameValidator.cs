using System.Text.RegularExpressions;

namespace ObjeX.Core.Validation;

public static partial class BucketNameValidator
{
    /// <summary>
    /// S3 bucket naming rules: 3-63 lowercase letters, digits, hyphens and periods, starting and ending with a letter or digit.
    /// </summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$")]
    private static partial Regex BucketNameRegex();

    [GeneratedRegex(@"^\d+\.\d+\.\d+\.\d+$")]
    private static partial Regex IpAddressRegex();

    public static string? GetValidationError(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Bucket name cannot be empty";
        
        if (name.Length < 3)
            return "Bucket name must be at least 3 characters";
        
        if (name.Length > 63)
            return "Bucket name must not exceed 63 characters";
        
        if (!(char.IsLower(name[0]) || char.IsAsciiDigit(name[0])))
            return "Bucket name must start with a lowercase letter or number";

        if (!(char.IsLower(name[^1]) || char.IsAsciiDigit(name[^1])))
            return "Bucket name must end with a lowercase letter or number";
        
        if (name.Contains(".."))
            return "Bucket name cannot contain consecutive periods";

        if (name.Contains(".-") || name.Contains("-."))
            return "Bucket name cannot have a period next to a hyphen";

        if (IpAddressRegex().IsMatch(name))
            return "Bucket name must not be formatted as an IP address";

        if (!BucketNameRegex().IsMatch(name))
            return "Bucket name can only contain lowercase letters, numbers, hyphens and periods";
        
        return null;
    }
}