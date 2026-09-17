using Amt.GetStream.Api.Services.Stream;
using GetStream;
using GetStream.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Amt.GetStream.Api.Tests.Integration;

/// <summary>
/// Runs a full call lifecycle against the real Stream app: create with a member, read it back,
/// add and remove members, end it. Opt-in, like the user tests: skipped unless a Stream API secret
/// is configured. Uses a unique it-&lt;guid&gt; call id and deletes everything it made.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StreamCallServiceIntegrationTests : IAsyncLifetime
{
    private const string CallType = "development";

    private readonly string _callId = $"it-{Guid.NewGuid():N}";
    private readonly string _ownerId = $"it-{Guid.NewGuid():N}";
    private readonly string _memberId = $"it-{Guid.NewGuid():N}";
    private StreamIntegrationHost? _host;

    public ValueTask InitializeAsync()
    {
        _host = StreamIntegrationHost.TryCreate();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Call_lifecycle_create_read_update_members_and_end()
    {
        Assert.SkipWhen(_host is null, StreamIntegrationHost.SkipReason);
        var calls = _host!.Services.GetRequiredService<IStreamCallService>();
        var users = _host.Services.GetRequiredService<IStreamUserService>();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Stream needs the creator and members to exist before they can join a call.
        await users.EnsureUserAsync(_ownerId, "Owner", image: null, cancellationToken);
        await users.EnsureUserAsync(_memberId, "Member", image: null, cancellationToken);

        var created = await calls.GetOrCreateAsync(
            CallType, _callId, _ownerId, [new CallMember(_memberId, null)], cancellationToken);

        Assert.Equal(_callId, created.Id);
        Assert.Equal(CallType, created.Type);
        Assert.Equal($"{CallType}:{_callId}", created.Cid);
        Assert.Equal(_ownerId, created.CreatedById);
        Assert.Contains(created.Members, member => member.UserId == _memberId);
        Assert.Null(created.EndedAt);

        var fetched = await calls.GetAsync(CallType, _callId, cancellationToken);
        Assert.NotNull(fetched);
        Assert.Equal(created.Cid, fetched.Cid);

        var afterRemove = await calls.UpdateMembersAsync(
            CallType, _callId, add: [], remove: [_memberId], cancellationToken);
        Assert.DoesNotContain(afterRemove.Members, member => member.UserId == _memberId);

        var afterAdd = await calls.UpdateMembersAsync(
            CallType, _callId, add: [new CallMember(_memberId, null)], remove: [], cancellationToken);
        Assert.Contains(afterAdd.Members, member => member.UserId == _memberId);

        await calls.EndAsync(CallType, _callId, cancellationToken);

        var ended = await calls.GetAsync(CallType, _callId, cancellationToken);
        Assert.NotNull(ended);
        Assert.NotNull(ended.EndedAt);
    }

    [Fact]
    public async Task Missing_call_is_reported_as_missing_rather_than_failing()
    {
        Assert.SkipWhen(_host is null, StreamIntegrationHost.SkipReason);
        var calls = _host!.Services.GetRequiredService<IStreamCallService>();

        var call = await calls.GetAsync(CallType, $"it-{Guid.NewGuid():N}", TestContext.Current.CancellationToken);

        Assert.Null(call);
    }

    /// <summary>Cleanup runs even when a test fails partway, and tolerates anything already gone.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_host is null)
        {
            return;
        }

        var stream = _host.Services.GetRequiredService<StreamClient>();
        var video = _host.Services.GetRequiredService<VideoClient>();

        try
        {
            try
            {
                await video.DeleteCallAsync(CallType, _callId, new DeleteCallRequest { Hard = true });
            }
            catch (GetStreamException)
            {
                // The call may never have been created.
            }

            try
            {
                var deletion = await stream.DeleteUsersAsync(new DeleteUsersRequest
                {
                    UserIds = [_ownerId, _memberId],
                    User = "hard",
                });
                await stream.WaitForTaskAsync(deletion.Data!.TaskID, timeout: TimeSpan.FromSeconds(60));
            }
            catch (GetStreamException)
            {
                // Users may never have been created.
            }
        }
        finally
        {
            await _host.DisposeAsync();
        }
    }
}
