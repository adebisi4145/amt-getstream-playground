using System.ComponentModel.DataAnnotations;

namespace Amt.GetStream.Api.Features.Tokens;

/// <summary>
/// Requires an absolute http or https URL. The built-in <see cref="UrlAttribute"/> also accepts ftp:// links.
/// Null is valid; combine with [Required] when the value is mandatory.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class HttpUrlAttribute() : ValidationAttribute("The {0} field must be an absolute http or https URL.")
{
    public override bool IsValid(object? value) =>
        value is null
        || value is string text
        && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
