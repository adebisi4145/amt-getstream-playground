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
    private const string ReasonField = "reason";
    private const string AssignedToField = "assignedTo";
    private const string RequestedAtField = "requestedAt";
    private const string AcceptedAtField = "acceptedAt";

    public async Task<Consultation> CreateAsync(
        string callType,
        string callId,
        string patientId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var custom = new Dictionary<string, object>
        {
            [KindField] = ConsultationKind,
            [StatusField] = ConsultationStatus.Waiting,
            [PatientField] = patientId,
            [RequestedAtField] = timeProvider.GetUtcNow().ToString("O"),
        };

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
        int limit,
        CancellationToken cancellationToken)
    {
        var response = await CallStreamAsync(
            nameof(QueryByStatusAsync),
            () => video.QueryCallsAsync(
                new QueryCallsRequest
                {
                    FilterConditions = new Dictionary<string, object>
                    {
                        ["type"] = callType,
                        [$"custom.{KindField}"] = ConsultationKind,
                        [$"custom.{StatusField}"] = status,
                    },
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

    public async Task<Consultation> CloseAsync(
        string callType,
        string callId,
        string status,
        CancellationToken cancellationToken)
    {
        await UpdateCustomAsync(
            callType,
            callId,
            new Dictionary<string, object> { [StatusField] = status },
            cancellationToken);

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
            ReadString(call.Custom, ReasonField),
            ReadString(call.Custom, StatusField) ?? ConsultationStatus.Waiting,
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
