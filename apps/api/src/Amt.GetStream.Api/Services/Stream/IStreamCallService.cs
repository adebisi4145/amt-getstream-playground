namespace Amt.GetStream.Api.Services.Stream;

public interface IStreamCallService
{
    /// <summary>Creates the call if it doesn't exist, or returns the existing one.</summary>
    Task<CallSummary> GetOrCreateAsync(
        string type,
        string id,
        string createdById,
        IReadOnlyList<CallMember> members,
        CancellationToken cancellationToken);

    /// <summary>Returns the call, or null when Stream doesn't have it.</summary>
    Task<CallSummary?> GetAsync(string type, string id, CancellationToken cancellationToken);

    Task<CallPage> QueryAsync(string? type, int? limit, string? next, CancellationToken cancellationToken);

    Task<CallSummary> UpdateMembersAsync(
        string type,
        string id,
        IReadOnlyList<CallMember> add,
        IReadOnlyList<string> remove,
        CancellationToken cancellationToken);

    Task EndAsync(string type, string id, CancellationToken cancellationToken);
}

public sealed record CallMember(string UserId, string? Role);

public sealed record CallSummary(
    string Type,
    string Id,
    string Cid,
    string? CreatedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EndedAt,
    bool Recording,
    IReadOnlyList<CallMember> Members);

public sealed record CallPage(IReadOnlyList<CallSummary> Calls, string? Next);
