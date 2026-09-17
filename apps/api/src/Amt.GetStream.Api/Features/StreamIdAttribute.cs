using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Amt.GetStream.Api.Features;

/// <summary>
/// Our rule for Stream user ids and call ids: letters, digits, @, _ and -, up to 255 characters.
/// It's deliberately conservative; Stream doesn't publish its id limits.
/// </summary>
public static partial class StreamId
{
    public const string Message =
        "The field may only contain letters, digits, @, _ and -, up to 255 characters.";

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9@_-]{1,255}$")]
    private static partial Regex Pattern();
}

/// <summary>
/// Applies <see cref="StreamId"/> to a request property. Null is valid; combine with [Required] when mandatory.
/// Note that minimal API validation doesn't recurse into arrays of nested objects, so ids inside collections
/// must also be checked in the endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class StreamIdAttribute() : ValidationAttribute(StreamId.Message)
{
    public override bool IsValid(object? value) =>
        value is null || (value is string text && StreamId.IsValid(text));
}
