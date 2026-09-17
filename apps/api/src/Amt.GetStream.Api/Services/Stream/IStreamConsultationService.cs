namespace Amt.GetStream.Api.Services.Stream;

/// <summary>
/// A consultation is a Stream call carrying queue state in its custom data.
/// Verified against Stream (2026-09-17): calls can be filtered by custom fields, and writing custom
/// data merges rather than replaces, so a status change doesn't disturb the rest.
/// </summary>
public interface IStreamConsultationService
{
    Task<Consultation> CreateAsync(
        string callType,
        string callId,
        string patientId,
        string? reason,
        CancellationToken cancellationToken);

    Task<Consultation?> GetAsync(string callType, string callId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Consultation>> QueryByStatusAsync(
        string callType,
        string status,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Adds the agent to the call and records the assignment.</summary>
    Task<Consultation> AssignAsync(
        string callType,
        string callId,
        string staffId,
        CancellationToken cancellationToken);

    /// <summary>Adds the doctor as a member. Ringing is a separate step, so a failed ring doesn't undo it.</summary>
    Task<Consultation> AddDoctorAsync(
        string callType,
        string callId,
        string doctorId,
        CancellationToken cancellationToken);

    /// <summary>Rings a doctor who is already a member. Success means Stream accepted it, not that a browser rang.</summary>
    Task RingAsync(string callType, string callId, string doctorId, CancellationToken cancellationToken);

    /// <summary>Writes the final status and ends the Stream call.</summary>
    Task<Consultation> CloseAsync(
        string callType,
        string callId,
        string status,
        CancellationToken cancellationToken);
}

public static class ConsultationStatus
{
    public const string Waiting = "waiting";
    public const string Accepted = "accepted";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

public sealed record Consultation(
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
