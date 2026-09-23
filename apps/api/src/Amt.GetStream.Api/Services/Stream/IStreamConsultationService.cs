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
        string? patientName,
        string modality,
        string? reason,
        CancellationToken cancellationToken);

    Task<Consultation?> GetAsync(string callType, string callId, CancellationToken cancellationToken);

    /// <summary>
    /// Consultations with the given status, newest first. Pass <paramref name="patientId"/> to get
    /// only that patient's — a patient who dropped needs to find their own consultation again.
    /// </summary>
    Task<IReadOnlyList<Consultation>> QueryByStatusAsync(
        string callType,
        string status,
        string? patientId,
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

    /// <summary>
    /// Changes the consultation's modality. Custom writes merge, so status, patient and the rest are untouched.
    /// Updating the call also reaches every joined client as a call.updated event, which is how both sides
    /// switch between audio and video mid-consultation.
    /// </summary>
    Task<Consultation> SetModalityAsync(
        string callType,
        string callId,
        string modality,
        CancellationToken cancellationToken);

    /// <summary>Writes the final status and reason, then ends the Stream call.</summary>
    Task<Consultation> CloseAsync(
        string callType,
        string callId,
        string status,
        string? endReason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether the user is still in the call's live session, according to Stream.
    /// Used to tell "the consultation finished" apart from "the patient disappeared".
    /// </summary>
    Task<bool> IsParticipantPresentAsync(
        string callType,
        string callId,
        string userId,
        CancellationToken cancellationToken);

    /// <summary>How many people are in the call's live session. Zero means it's an empty room.</summary>
    Task<int> SessionParticipantCountAsync(string callType, string callId, CancellationToken cancellationToken);
}

public static class ConsultationStatus
{
    public const string Waiting = "waiting";
    public const string Accepted = "accepted";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// How the patient asked to speak to a doctor. Triage sees this on the board before joining,
/// so it can't be a client-only setting.
/// </summary>
public static class ConsultationModality
{
    public const string Audio = "audio";
    public const string Video = "video";

    public static bool IsValid(string? modality) => modality is Audio or Video;
}

/// <summary>
/// How a consultation ended. The status alone can't tell a finished consultation apart from one
/// where the patient dropped and staff closed it — clinically those are opposites.
/// </summary>
public static class ConsultationEndReason
{
    /// <summary>Everyone was still there; staff closed it deliberately.</summary>
    public const string Finished = "finished";

    /// <summary>The patient was no longer in the call when staff closed it.</summary>
    public const string PatientLeft = "patient_left";

    /// <summary>The patient gave up before anyone picked up.</summary>
    public const string PatientCancelled = "patient_cancelled";

    /// <summary>Nobody ever closed it and the call sat empty; closed automatically.</summary>
    public const string Abandoned = "abandoned";
}

public sealed record Consultation(
    string CallType,
    string CallId,
    string Cid,
    string PatientId,
    /// <summary>Display name, so staff screens never have to show a raw id.</summary>
    string? PatientName,
    string Modality,
    string? Reason,
    string Status,
    /// <summary>Set when the consultation ends. See <see cref="ConsultationEndReason"/>.</summary>
    string? EndReason,
    string? AssignedTo,
    DateTimeOffset RequestedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<string> MemberIds);
