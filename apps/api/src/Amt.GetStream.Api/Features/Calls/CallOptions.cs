using System.ComponentModel.DataAnnotations;

namespace Amt.GetStream.Api.Features.Calls;

/// <summary>
/// Settings for the call endpoints, bound from the "Calls" configuration section.
/// </summary>
public sealed class CallOptions
{
    public const string SectionName = "Calls";

    /// <summary>
    /// Call types this API accepts. These are Stream's built-ins; custom types created in the
    /// Stream dashboard can be added here without a code change. This is our allowlist, not Stream's.
    /// </summary>
    [MinLength(1)]
    public string[] AllowedTypes { get; set; } = ["default", "audio_room", "livestream", "development"];
}
