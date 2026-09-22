using Amt.GetStream.Api.Services.Stream;

namespace Amt.GetStream.Api.Tests.Consultations;

internal sealed class FakeStreamConsultationService : IStreamConsultationService
{
    public const string PatientId = "patient-001";

    private readonly Dictionary<string, Consultation> _consultations = [];

    public List<(string CallId, string StaffId)> Assigned { get; } = [];

    public List<(string CallId, string DoctorId)> DoctorsAdded { get; } = [];

    public List<(string CallId, string DoctorId)> Rung { get; } = [];

    public List<(string CallId, string Status, string? EndReason)> Closed { get; } = [];

    /// <summary>Stands in for Stream's view of who is still in the session.</summary>
    public bool PatientPresent { get; init; }

    public Task<bool> IsParticipantPresentAsync(
        string callType, string callId, string userId, CancellationToken cancellationToken) =>
        Task.FromResult(PatientPresent);

    /// <summary>How many people the fake reports as being in the call.</summary>
    public int SessionParticipants { get; init; }

    public Task<int> SessionParticipantCountAsync(
        string callType, string callId, CancellationToken cancellationToken) =>
        Task.FromResult(SessionParticipants);

    /// <summary>When set, RingAsync throws it, standing in for Stream refusing the ring.</summary>
    public Exception? RingFailure { get; init; }

    public Consultation Seed(
        string callId,
        string status,
        string? assignedTo = null,
        string patientId = PatientId,
        IEnumerable<string>? members = null,
        string modality = ConsultationModality.Video,
        DateTimeOffset? requestedAt = null)
    {
        var consultation = new Consultation(
            "development",
            callId,
            $"development:{callId}",
            patientId,
            "Daniel Okafor",
            modality,
            "sore throat",
            status,
            EndReason: null,
            assignedTo,
            requestedAt ?? new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
            assignedTo is null ? null : new DateTimeOffset(2030, 1, 2, 3, 5, 5, TimeSpan.Zero),
            EndedAt: null,
            [.. members ?? (assignedTo is null ? [patientId] : [patientId, assignedTo])]);

        _consultations[callId] = consultation;
        return consultation;
    }

    public List<(string CallId, string PatientId, string? PatientName, string Modality, string? Reason)> Created { get; } =
        [];

    public Task<Consultation> CreateAsync(
        string callType,
        string callId,
        string patientId,
        string? patientName,
        string modality,
        string? reason,
        CancellationToken cancellationToken)
    {
        Created.Add((callId, patientId, patientName, modality, reason));

        var consultation = new Consultation(
            callType, callId, $"{callType}:{callId}", patientId, patientName, modality, reason,
            ConsultationStatus.Waiting, EndReason: null, AssignedTo: null,
            new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero), AcceptedAt: null, EndedAt: null, [patientId]);

        _consultations[callId] = consultation;
        return Task.FromResult(consultation);
    }

    public Task<Consultation?> GetAsync(string callType, string callId, CancellationToken cancellationToken) =>
        Task.FromResult(_consultations.GetValueOrDefault(callId));

    public List<(string Status, string? PatientId)> Queries { get; } = [];

    public Task<IReadOnlyList<Consultation>> QueryByStatusAsync(
        string callType, string status, string? patientId, int limit, CancellationToken cancellationToken)
    {
        Queries.Add((status, patientId));

        IReadOnlyList<Consultation> found =
        [
            .. _consultations.Values
                .Where(consultation => consultation.Status == status)
                .Where(consultation => patientId is null || consultation.PatientId == patientId)
                .Take(limit),
        ];

        return Task.FromResult(found);
    }

    public Task<Consultation> AssignAsync(string callType, string callId, string staffId, CancellationToken cancellationToken)
    {
        Assigned.Add((callId, staffId));
        var updated = _consultations[callId] with
        {
            Status = ConsultationStatus.Accepted,
            AssignedTo = staffId,
            MemberIds = [.. _consultations[callId].MemberIds, staffId],
        };

        _consultations[callId] = updated;
        return Task.FromResult(updated);
    }

    public Task<Consultation> AddDoctorAsync(string callType, string callId, string doctorId, CancellationToken cancellationToken)
    {
        DoctorsAdded.Add((callId, doctorId));
        var updated = _consultations[callId] with { MemberIds = [.. _consultations[callId].MemberIds, doctorId] };

        _consultations[callId] = updated;
        return Task.FromResult(updated);
    }

    public Task RingAsync(string callType, string callId, string doctorId, CancellationToken cancellationToken)
    {
        if (RingFailure is not null)
        {
            throw RingFailure;
        }

        Rung.Add((callId, doctorId));
        return Task.CompletedTask;
    }

    public Task<Consultation> CloseAsync(
        string callType,
        string callId,
        string status,
        string? endReason,
        CancellationToken cancellationToken)
    {
        Closed.Add((callId, status, endReason));
        var updated = _consultations[callId] with
        {
            Status = status,
            EndReason = endReason,
            EndedAt = new DateTimeOffset(2030, 1, 2, 4, 0, 0, TimeSpan.Zero),
        };

        _consultations[callId] = updated;
        return Task.FromResult(updated);
    }
}
