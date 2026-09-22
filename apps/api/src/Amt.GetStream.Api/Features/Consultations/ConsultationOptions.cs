using System.ComponentModel.DataAnnotations;

namespace Amt.GetStream.Api.Features.Consultations;

/// <summary>
/// Settings for the consultation queue, bound from the "Consultations" configuration section.
/// </summary>
public sealed class ConsultationOptions
{
    public const string SectionName = "Consultations";

    /// <summary>Stream call type used for consultations. Must also be in Calls:AllowedTypes.</summary>
    [Required]
    public string CallType { get; set; } = "default";

    /// <summary>
    /// Demo staff. Identity is faked in this playground: the API trusts the id in the request,
    /// so this only proves the rules work, not who anyone is. Real authentication replaces it.
    /// </summary>
    public StaffMember[] Staff { get; set; } = [];

    /// <summary>
    /// How long an untouched consultation stays open before the sweeper closes it as abandoned.
    /// Long enough to survive a patient reconnecting, short enough that yesterday's never reappears.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "24:00:00")]
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(10);
}

public sealed class StaffMember
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>"triage" or "doctor".</summary>
    [Required]
    [RegularExpression($"^({StaffRoles.Triage}|{StaffRoles.Doctor})$")]
    public string Role { get; set; } = StaffRoles.Triage;
}

public static class StaffRoles
{
    public const string Triage = "triage";
    public const string Doctor = "doctor";
}

/// <summary>Answers "is this id staff, and what kind" from configuration.</summary>
public sealed class StaffDirectory(ConsultationOptions options)
{
    public bool IsTriage(string userId) => HasRole(userId, StaffRoles.Triage);

    public bool IsDoctor(string userId) => HasRole(userId, StaffRoles.Doctor);

    private bool HasRole(string userId, string role) =>
        options.Staff.Any(member =>
            string.Equals(member.UserId, userId, StringComparison.Ordinal)
            && string.Equals(member.Role, role, StringComparison.OrdinalIgnoreCase));
}
