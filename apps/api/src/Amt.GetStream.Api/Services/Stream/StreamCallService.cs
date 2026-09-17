using GetStream;
using GetStream.Models;

namespace Amt.GetStream.Api.Services.Stream;

internal sealed class StreamCallService(VideoClient video) : IStreamCallService
{
    public async Task<CallSummary> GetOrCreateAsync(
        string type,
        string id,
        string createdById,
        IReadOnlyList<CallMember> members,
        CancellationToken cancellationToken)
    {
        var response = await CallStreamAsync(
            nameof(GetOrCreateAsync),
            () => video.GetOrCreateCallAsync(
                type,
                id,
                new GetOrCreateCallRequest
                {
                    Data = new CallRequest
                    {
                        CreatedByID = createdById,
                        Members = members.Count == 0 ? null! : [.. members.Select(ToMemberRequest)],
                    },
                },
                cancellationToken));

        return ToSummary(response.Data!.Call, response.Data.Members);
    }

    public async Task<CallSummary?> GetAsync(string type, string id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await video.GetCallAsync(type, id, cancellationToken: cancellationToken);
            return ToSummary(response.Data!.Call, response.Data.Members);
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

    public async Task<CallPage> QueryAsync(string? type, int? limit, string? next, CancellationToken cancellationToken)
    {
        var filter = new Dictionary<string, object>();
        if (type is not null)
        {
            filter["type"] = type;
        }

        var response = await CallStreamAsync(
            nameof(QueryAsync),
            () => video.QueryCallsAsync(
                new QueryCallsRequest
                {
                    FilterConditions = filter,
                    Limit = limit,
                    Next = next,
                },
                cancellationToken));

        var calls = response.Data!.Calls
            .Select(call => ToSummary(call.Call, call.Members))
            .ToArray();

        return new CallPage(calls, response.Data.Next);
    }

    public async Task<CallSummary> UpdateMembersAsync(
        string type,
        string id,
        IReadOnlyList<CallMember> add,
        IReadOnlyList<string> remove,
        CancellationToken cancellationToken)
    {
        await CallStreamAsync(
            nameof(UpdateMembersAsync),
            () => video.UpdateCallMembersAsync(
                type,
                id,
                new UpdateCallMembersRequest
                {
                    UpdateMembers = add.Count == 0 ? null! : [.. add.Select(ToMemberRequest)],
                    RemoveMembers = remove.Count == 0 ? null! : [.. remove],
                },
                cancellationToken));

        // The update response carries members only, so read the call back for a consistent shape.
        var call = await GetAsync(type, id, cancellationToken);
        return call ?? throw new StreamRequestFailedException(
            nameof(UpdateMembersAsync),
            StatusCodes.Status404NotFound,
            new InvalidOperationException($"Call {type}:{id} disappeared after updating members."));
    }

    public Task EndAsync(string type, string id, CancellationToken cancellationToken) =>
        CallStreamAsync(nameof(EndAsync), () => video.EndCallAsync(type, id, cancellationToken: cancellationToken));

    private static MemberRequest ToMemberRequest(CallMember member) =>
        new() { UserID = member.UserId, Role = member.Role };

    private static CallSummary ToSummary(CallResponse call, List<MemberResponse>? members) =>
        new(
            call.Type,
            call.ID,
            call.Cid,
            call.CreatedBy?.ID,
            call.CreatedAt,
            call.EndedAt,
            call.Recording,
            members?.Select(member => new CallMember(member.UserID, member.Role)).ToArray() ?? []);

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
