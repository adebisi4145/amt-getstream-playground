using System.ComponentModel.DataAnnotations;

namespace Amt.GetStream.Api.Services.Stream;

/// <summary>
/// Stream integration settings, bound from the "Stream" configuration section.
/// The API secret must come from user secrets or the Stream__ApiSecret environment variable, never a committed file.
/// </summary>
public sealed class StreamOptions
{
    public const string SectionName = "Stream";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string ApiSecret { get; set; } = string.Empty;

    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);
}
