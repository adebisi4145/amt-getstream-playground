using System.Text.Json;
using GetStream;
using GetStream.Models;

namespace Amt.GetStream.Api.Services.Stream;

internal sealed class StreamConsultationService(VideoClient video, TimeProvider timeProvider) : IStreamConsultationService
{
    /// <summary>Marks our calls so the board can tell consultations apart from plain calls.</summary>
    private const string ConsultationKind = "consultation";

    private const string KindField = "kind";
    private const string StatusField = "status";
    private const string PatientField = "patientId";
    private const string PatientNameField = "patientName";
    private const string ModalityField = "modality";
    private const string ReasonField = "reason";
    private const string EndReasonField = "endReason";
    private const string AssignedToField = "assignedTo";
    private const string RequestedAtField = "requestedAt";
    private const string AcceptedAtField = "acceptedAt";

    public async Task<Consultation> CreateAsync(
        string callType,
        string callId,
        string patientId,
        string? patientName,
        string modality,
        string? reason,
        CancellationToken cancellationToken)
    {
        var custom = new Dictionary<string, object>
        {
            [KindField] = ConsultationKind,
            [StatusField] = ConsultationStatus.Waiting,
            [PatientField] = patientId,
            [ModalityField] = modality,
            [RequestedAtField] = timeProvider.GetUtcNow().ToString("O"),
        };

        if (patientName is not null)
        {
            custom[PatientNameField] = patientName;
        }

        if (reason is not null)
        {
            custom[ReasonField] = reason;
        }

        var response = await CallStreamAsync(
            nameof(CreateAsync),
            () => video.GetOrCreateCallAsync(
                callType,
                callId,
                new GetOrCreateCallRequest
                {
                    Data = new CallRequest
                    {
                        CreatedByID = patientId,
                        Members = [new MemberRequest { UserID = patientId }],
                        Custom = custom,
                    },
                },
                cancellationToken));

        return ToConsultation(response.Data!.Call, response.Data.Members);
    }

    public async Task<Consultation?> GetAsync(string callType, string callId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await video.GetCallAsync(callType, callId, cancellationToken: cancellationToken);
            var call = response.Data!.Call;

            // Only calls we created as consultations are visible through this feature.
            return ReadString(call.Custom, KindField) == ConsultationKind
                ? ToConsultation(call, response.Data.Members)
                : null;
        }
        catch (GetStreamApiException exception) when (exception.StatusCode == StatusCodes.Status404NotFound)
        {
            return null;
        }
        catch (GetStreamException exception)
        {
            throw Wrap(nameof(GetAsync), exception);
        }
    }

    public async Task<IReadOnlyList<Consultation>> QueryByStatusAsync(
        string callType,
        string status,
        string? patientId,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = new Dictionary<string, object>
        {
            ["type"] = callType,
            [$"custom.{KindField}"] = ConsultationKind,
            [$"custom.{StatusField}"] = status,
        };

        // Filtered in Stream rather than in the API, so one patient's browser never receives
        // another patient's consultations.
        if (patientId is not null)
        {
            filter[$"custom.{PatientField}"] = patientId;
        }

        var response = await CallStreamAsync(
            nameof(QueryByStatusAsync),
            () => video.QueryCallsAsync(
                new QueryCallsRequest
                {
                    FilterConditions = filter,
                    // Newest first: the waiting board wants the longest wait visible, and history
                    // is only useful in reverse order.
                    Sort = [new SortParamRequest { Field = "created_at", Direction = -1 }],
                    Limit = limit,
                },
                cancellationToken));

        return [.. response.Data!.Calls.Select(call => ToConsultation(call.Call, call.Members))];
    }

    public async Task<Consultation> AssignAsync(
        string callType,
        string callId,
        string staffId,
        CancellationToken cancellationToken)
    {
        await CallStreamAsync(
            nameof(AssignAsync),
            () => video.UpdateCallMembersAsync(
                callType,
                callId,
                new UpdateCallMembersRequest { UpdateMembers = [new MemberRequest { UserID = staffId }] },
                cancellationToken));

        // Custom writes merge, so only the changed fields are sent.
        await UpdateCustomAsync(
            callType,
            callId,
            new Dictionary<string, object>
            {
                [StatusField] = ConsultationStatus.Accepted,
                [AssignedToField] = staffId,
                [AcceptedAtField] = timeProvider.GetUtcNow().ToString("O"),
            },
            cancellationToken);

        // The update-call response doesn't carry members, so read the consultation back
        // to return a complete picture including the agent just added.
        var consultation = await GetAsync(callType, callId, cancellationToken);

        return consultation ?? throw new StreamRequestFailedException(
            nameof(AssignAsync),
            StatusCodes.Status404NotFound,
            new InvalidOperationException($"Consultation {callType}:{callId} disappeared after assigning."));
    }

    public async Task<Consultation> AddDoctorAsync(
        string callType,
        string callId,
        string doctorId,
        CancellationToken cancellationToken)
    {
        var response = await CallStreamAsync(
            nameof(AddDoctorAsync),
            () => video.UpdateCallMembersAsync(
                callType,
                callId,
                new UpdateCallMembersRequest { UpdateMembers = [new MemberRequest { UserID = doctorId }] },
                cancellationToken));

        var consultation = await GetAsync(callType, callId, cancellationToken);

        return consultation ?? throw new StreamRequestFailedException(
            nameof(AddDoctorAsync),
            StatusCodes.Status404NotFound,
            new InvalidOperationException($"Consultation {callType}:{callId} disappeared after adding a doctor."));
    }

    public Task RingAsync(string callType, string callId, string doctorId, CancellationToken cancellationToken) =>
        CallStreamAsync(
            nameof(RingAsync),
            () => video.RingCallAsync(
                callType,
                callId,
                new RingCallRequest { MembersIds = [doctorId] },
                cancellationToken));

    public async Task<bool> IsParticipantPresentAsync(
        string callType,
        string callId,
        string userId,
        CancellationToken cancellationToken)
    {
        var participants = await SessionParticipantsAsync(
            nameof(IsParticipantPresentAsync), callType, callId, cancellationToken);

        return participants.Any(participant => participant.User?.ID == userId);
    }

    public async Task<int> SessionParticipantCountAsync(
        string callType,
        string callId,
        CancellationToken cancellationToken)
    {
        var participants = await SessionParticipantsAsync(
            nameof(SessionParticipantCountAsync), callType, callId, cancellationToken);

        return participants.Count;
    }

    private async Task<IReadOnlyList<CallParticipantResponse>> SessionParticipantsAsync(
        string operation,
        string callType,
        string callId,
        CancellationToken cancellationToken)
    {
        var response = await CallStreamAsync(
            operation,
            () => video.GetCallAsync(callType, callId, cancellationToken: cancellationToken));

        return response.Data?.Call.Session?.Participants ?? [];
    }

    public async Task<Consultation> SetModalityAsync(
        string callType,
        string callId,
        string modality,
        CancellationToken cancellationToken)
    {
        await UpdateCustomAsync(
            callType,
            callId,
            new Dictionary<string, object> { [ModalityField] = modality },
            cancellationToken);

        var consultation = await GetAsync(callType, callId, cancellationToken);

        return consultation ?? throw new StreamRequestFailedException(
            nameof(SetModalityAsync),
            StatusCodes.Status404NotFound,
            new InvalidOperationException($"Consultation {callType}:{callId} disappeared after changing modality."));
    }

    public async Task<Consultation> CloseAsync(
        string callType,
        string callId,
        string status,
        string? endReason,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, object> { [StatusField] = status };

        if (endReason is not null)
        {
            fields[EndReasonField] = endReason;
        }

        await UpdateCustomAsync(callType, callId, fields, cancellationToken);

        await CallStreamAsync(
            nameof(CloseAsync),
            () => video.EndCallAsync(callType, callId, cancellationToken: cancellationToken));

        var consultation = await GetAsync(callType, callId, cancellationToken);

        return consultation ?? throw new StreamRequestFailedException(
            nameof(CloseAsync),
            StatusCodes.Status404NotFound,
            new InvalidOperationException($"Consultation {callType}:{callId} disappeared after closing."));
    }

    private async Task UpdateCustomAsync(
        string callType,
        string callId,
        Dictionary<string, object> fields,
        CancellationToken cancellationToken)
    {
        await CallStreamAsync(
            nameof(UpdateCustomAsync),
            () => video.UpdateCallAsync(
                callType,
                callId,
                new UpdateCallRequest { Custom = fields },
                cancellationToken));
    }

    private static Consultation ToConsultation(CallResponse call, List<MemberResponse>? members) =>
        new(
            call.Type,
            call.ID,
            call.Cid,
            ReadString(call.Custom, PatientField) ?? string.Empty,
            ReadString(call.Custom, PatientNameField),
            ReadString(call.Custom, ModalityField) ?? ConsultationModality.Video,
            ReadString(call.Custom, ReasonField),
            ReadString(call.Custom, StatusField) ?? ConsultationStatus.Waiting,
            ReadString(call.Custom, EndReasonField),
            ReadString(call.Custom, AssignedToField),
            ReadDate(call.Custom, RequestedAtField) ?? call.CreatedAt,
            ReadDate(call.Custom, AcceptedAtField),
            call.EndedAt,
            members?.Select(member => member.UserID).ToArray() ?? []);

    private static string? ReadString(object? custom, string field) =>
        custom is JsonElement { ValueKind: JsonValueKind.Object } element
        && element.TryGetProperty(field, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadDate(object? custom, string field) =>
        DateTimeOffset.TryParse(ReadString(custom, field), out var parsed) ? parsed : null;

    private static async Task<T> CallStreamAsync<T>(string operation, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (GetStreamException exception)
        {
            throw Wrap(operation, exception);
        }
    }

    private static StreamRequestFailedException Wrap(string operation, GetStreamException exception) =>
        new(operation, (exception as GetStreamApiException)?.StatusCode, exception);
}
