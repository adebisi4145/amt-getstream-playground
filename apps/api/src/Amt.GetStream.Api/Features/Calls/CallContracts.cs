using System.ComponentModel.DataAnnotations;

namespace Amt.GetStream.Api.Features.Calls;

/// <summary>
/// Creates a call, or returns it when the id already exists.
/// `createdById` is taken from the request only because there's no authentication yet; it moves to the
/// authenticated identity once there is. See docs/api.md.
/// </summary>
public sealed class CreateCallRequest
{
    [Required]
    public string? Type { get; init; }

    /// <summary>Optional. A random id is generated when this is left out.</summary>
    [StreamId]
    public string? Id { get; init; }

    [Required]
    [StreamId]
    public string? CreatedById { get; init; }

    public CallMemberRequest[] Members { get; init; } = [];
}

public sealed class CallMemberRequest
{
    [Required]
    [StreamId]
    public string? UserId { get; init; }

    /// <summary>Optional Stream call role, for example "host". Left to Stream's default when omitted.</summary>
    [StringLength(64)]
    public string? Role { get; init; }
}

public sealed class UpdateCallMembersRequestBody
{
    public CallMemberRequest[] Add { get; init; } = [];

    public string[] Remove { get; init; } = [];
}

public sealed record CallMemberResponse(string UserId, string? Role);

public sealed record CallResponseBody(
    string Type,
    string Id,
    string Cid,
    string? CreatedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EndedAt,
    bool Recording,
    IReadOnlyList<CallMemberResponse> Members);

public sealed record CallListResponse(IReadOnlyList<CallResponseBody> Calls, string? Next);

public sealed record CallRecordingResponse(
    string Filename,
    string Url,
    string RecordingType,
    string SessionId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime);
