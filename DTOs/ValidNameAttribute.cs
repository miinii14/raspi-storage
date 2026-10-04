using System.ComponentModel.DataAnnotations;

namespace RaspiStorage.DTO;

public class ValidNameAttribute : ValidationAttribute
{
    public const int MaxLength = 255;

    public static string? Validate(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "Name must not be empty";
        if (name.Length > MaxLength)
            return $"Name must be at most {MaxLength} characters";
        if (name != name.Trim())
            return "Name must not start or end with whitespace";
        if (name is "." or "..")
            return "Name must not be '.' or '..'";
        if (name.IndexOfAny(['/', '\\']) >= 0)
            return "Name must not contain '/' or '\\'";
        if (name.Any(char.IsControl))
            return "Name must not contain control characters";
        return null;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var error = Validate(value as string);
        return error is null ? ValidationResult.Success : new ValidationResult(error);
    }
}
