using Amt.GetStream.Api.Services.Stream;

namespace Amt.GetStream.Api.Tests.Calls;

internal sealed class FakeStreamCallService : IStreamCallService
{
    public List<(string Type, string Id, string CreatedById, IReadOnlyList<CallMember> Members)> Created { get; } = [];

    public List<(string Type, string Id)> Fetched { get; } = [];

    public List<(string? Type, int? Limit, string? Next)> Queried { get; } = [];

    public List<(string Type, string Id, IReadOnlyList<CallMember> Add, IReadOnlyList<string> Remove)> MemberUpdates { get; } = [];

    public List<(string Type, string Id)> Ended { get; } = [];

    /// <summary>When set, every method throws it, standing in for a Stream failure.</summary>
    public Exception? Failure { get; init; }

    /// <summary>When true, GetAsync reports the call as missing.</summary>
    public bool CallMissing { get; init; }

    public Task<CallSummary> GetOrCreateAsync(
        string type,
        string id,
        string createdById,
        IReadOnlyList<CallMember> members,
        CancellationToken cancellationToken)
    {
        Throw();
        Created.Add((type, id, createdById, members));
        return Task.FromResult(Summary(type, id, createdById, members));
    }

    public Task<CallSummary?> GetAsync(string type, string id, CancellationToken cancellationToken)
    {
        Throw();
        Fetched.Add((type, id));
        return Task.FromResult<CallSummary?>(CallMissing ? null : Summary(type, id, "alice", []));
    }

    public Task<CallPage> QueryAsync(string? type, int? limit, string? next, CancellationToken cancellationToken)
    {
        Throw();
        Queried.Add((type, limit, next));
        return Task.FromResult(new CallPage([Summary(type ?? "default", "call-1", "alice", [])], "cursor-2"));
    }

    public Task<CallSummary> UpdateMembersAsync(
        string type,
        string id,
        IReadOnlyList<CallMember> add,
        IReadOnlyList<string> remove,
        CancellationToken cancellationToken)
    {
        Throw();
        MemberUpdates.Add((type, id, add, remove));
        return Task.FromResult(Summary(type, id, "alice", add));
    }

    public Task EndAsync(string type, string id, CancellationToken cancellationToken)
    {
        Throw();
        Ended.Add((type, id));
        return Task.CompletedTask;
    }

    private void Throw()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }

    private static CallSummary Summary(string type, string id, string createdById, IReadOnlyList<CallMember> members) =>
        new(
            type,
            id,
            $"{type}:{id}",
            createdById,
            new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
            EndedAt: null,
            Recording: false,
            members);
}

internal sealed class FakeStreamRecordingService : IStreamRecordingService
{
    public static readonly CallRecordingSummary Recording = new(
        "call.mp4",
        "https://example.com/call.mp4",
        "composite",
        "session-1",
        new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
        new DateTimeOffset(2030, 1, 2, 3, 9, 5, TimeSpan.Zero));

    public List<(string Type, string Id)> Started { get; } = [];

    public List<(string Type, string Id)> Stopped { get; } = [];

    public List<(string Type, string Id)> Listed { get; } = [];

    public Task StartAsync(string type, string id, CancellationToken cancellationToken)
    {
        Started.Add((type, id));
        return Task.CompletedTask;
    }

    public Task StopAsync(string type, string id, CancellationToken cancellationToken)
    {
        Stopped.Add((type, id));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CallRecordingSummary>> ListAsync(string type, string id, CancellationToken cancellationToken)
    {
        Listed.Add((type, id));
        return Task.FromResult<IReadOnlyList<CallRecordingSummary>>([Recording]);
    }
}
