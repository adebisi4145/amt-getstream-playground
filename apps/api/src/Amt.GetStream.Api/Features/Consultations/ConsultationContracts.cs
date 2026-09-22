using System.ComponentModel.DataAnnotations;
using Amt.GetStream.Api.Services.Stream;

namespace Amt.GetStream.Api.Features.Consultations;

public sealed class StartConsultationRequest
{
    [Required]
    [StreamId]
    public string? PatientId { get; init; }

    /// <summary>The patient's display name. Staff screens show this instead of the id.</summary>
    [StringLength(100)]
    public string? PatientName { get; init; }

    /// <summary>"audio" or "video" — how the patient chose to speak to a doctor. Triage sees it on the board.</summary>
    [Required]
    [RegularExpression($"^({ConsultationModality.Audio}|{ConsultationModality.Video})$",
        ErrorMessage = "The Modality field must be either 'audio' or 'video'.")]
    public string? Modality { get; init; }

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
    string? PatientName,
    string Modality,
    string? Reason,
    string Status,
    string? EndReason,
    string? AssignedTo,
    DateTimeOffset RequestedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<string> MemberIds);
