using System.ComponentModel.DataAnnotations;

namespace Amt.GetStream.Api.Features.Consultations;

public sealed class StartConsultationRequest
{
    [Required]
    [StreamId]
    public string? PatientId { get; init; }

    /// <summary>Why the patient is calling. Shown on the dispatch board.</summary>
    [StringLength(500)]
    public string? Reason { get; init; }
}

public sealed class StaffActionRequest
{
    [Required]
    [StreamId]
    public string? StaffId { get; init; }
}

public sealed class DoctorActionRequest
{
    [Required]
    [StreamId]
    public string? DoctorId { get; init; }
}

public sealed class PatientActionRequest
{
    [Required]
    [StreamId]
    public string? PatientId { get; init; }
}

public sealed record ConsultationResponse(
    string CallType,
    string CallId,
    string Cid,
    string PatientId,
    string? Reason,
    string Status,
    string? AssignedTo,
    DateTimeOffset RequestedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<string> MemberIds);
