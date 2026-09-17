using Amt.GetStream.Api.Services.Stream;
using GetStream;
using GetStream.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Amt.GetStream.Api.Tests.Integration;

/// <summary>
/// Proves the consultation queue works against the real Stream app, including the part that can't be
/// faked: that a waiting consultation is found by querying Stream on its custom data.
/// Opt-in, like the other integration tests, with it-&lt;guid&gt; data cleaned up in DisposeAsync.
/// </summary>
[Trait("Category", "Integration")]
[Collection(StreamIntegrationCollection.Name)]
public sealed class StreamConsultationServiceIntegrationTests : IAsyncLifetime
{
    private const string CallType = "development";

    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly List<string> _callIds = [];
    private readonly List<string> _userIds = [];
    private StreamIntegrationHost? _host;

    private IStreamConsultationService Consultations =>
        _host!.Services.GetRequiredService<IStreamConsultationService>();

    private IStreamUserService Users => _host!.Services.GetRequiredService<IStreamUserService>();

    private VideoClient Video => _host!.Services.GetRequiredService<VideoClient>();

    public ValueTask InitializeAsync()
    {
        _host = StreamIntegrationHost.TryCreate();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Waiting_consultation_is_found_by_querying_stream_and_a_plain_call_is_not()
    {
        Assert.SkipWhen(_host is null, StreamIntegrationHost.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var patient = await CreateUserAsync("patient");

        var consultation = await CreateConsultationAsync(patient, "sore throat");

        // A plain call must not leak onto the dispatch board; without this, a filter matching
        // everything would still look like it worked.
        var plainCallId = TrackCall($"it-plain-{_suffix}");
        await Video.GetOrCreateCallAsync(
            CallType,
            plainCallId,
            new GetOrCreateCallRequest { Data = new CallRequest { CreatedByID = patient } },
            cancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        var board = await Consultations.QueryByStatusAsync(CallType, ConsultationStatus.Waiting, 100, cancellationToken);

        Assert.Contains(board, found => found.CallId == consultation.CallId);
        Assert.DoesNotContain(board, found => found.CallId == plainCallId);

        var mine = board.Single(found => found.CallId == consultation.CallId);
        Assert.Equal(patient, mine.PatientId);
        Assert.Equal("sore throat", mine.Reason);
    }

    [Fact]
    public async Task Full_flow_accept_invite_ring_and_complete()
    {
        Assert.SkipWhen(_host is null, StreamIntegrationHost.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var patient = await CreateUserAsync("patient");
        var triage = await CreateUserAsync("triage");
        var doctor = await CreateUserAsync("doctor");

        var consultation = await CreateConsultationAsync(patient, "headache");

        var accepted = await Consultations.AssignAsync(CallType, consultation.CallId, triage, cancellationToken);
        Assert.Equal(ConsultationStatus.Accepted, accepted.Status);
        Assert.Equal(triage, accepted.AssignedTo);
        Assert.Contains(triage, accepted.MemberIds);
        // The merge behaviour matters: a status write must not wipe the patient's details.
        Assert.Equal(patient, accepted.PatientId);
        Assert.Equal("headache", accepted.Reason);

        var withDoctor = await Consultations.AddDoctorAsync(CallType, consultation.CallId, doctor, cancellationToken);
        Assert.Contains(doctor, withDoctor.MemberIds);

        // Stream accepting the ring is all this can prove; a browser ringing needs the web app.
        await Consultations.RingAsync(CallType, consultation.CallId, doctor, cancellationToken);

        var completed = await Consultations.CloseAsync(
            CallType, consultation.CallId, ConsultationStatus.Completed, cancellationToken);
        Assert.Equal(ConsultationStatus.Completed, completed.Status);
        Assert.NotNull(completed.EndedAt);
    }

    [Fact]
    public async Task Cancelled_stays_distinguishable_from_completed_even_though_both_calls_ended()
    {
        Assert.SkipWhen(_host is null, StreamIntegrationHost.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var patient = await CreateUserAsync("patient");

        var consultation = await CreateConsultationAsync(patient, "gave up waiting");

        var cancelled = await Consultations.CloseAsync(
            CallType, consultation.CallId, ConsultationStatus.Cancelled, cancellationToken);

        Assert.Equal(ConsultationStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.EndedAt);

        var reread = await Consultations.GetAsync(CallType, consultation.CallId, cancellationToken);
        Assert.Equal(ConsultationStatus.Cancelled, reread!.Status);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            foreach (var callId in _callIds)
            {
                try
                {
                    await Video.DeleteCallAsync(CallType, callId, new DeleteCallRequest { Hard = true });
                }
                catch (GetStreamException)
                {
                    // Already gone, or never created.
                }
            }

            if (_userIds.Count > 0)
            {
                try
                {
                    var stream = _host.Services.GetRequiredService<StreamClient>();
                    var deletion = await stream.DeleteUsersAsync(new DeleteUsersRequest
                    {
                        UserIds = _userIds,
                        User = "hard",
                    });
                    await stream.WaitForTaskAsync(deletion.Data!.TaskID, timeout: TimeSpan.FromSeconds(60));
                }
                catch (GetStreamException)
                {
                    // Nothing to delete.
                }
            }
        }
        finally
        {
            await _host.DisposeAsync();
        }
    }

    private async Task<string> CreateUserAsync(string role)
    {
        var userId = $"it-{role}-{Guid.NewGuid():N}";
        _userIds.Add(userId);
        await Users.EnsureUserAsync(userId, role, image: null, TestContext.Current.CancellationToken);
        return userId;
    }

    private async Task<Consultation> CreateConsultationAsync(string patientId, string reason)
    {
        var callId = TrackCall($"it-consult-{Guid.NewGuid():N}");
        return await Consultations.CreateAsync(
            CallType, callId, patientId, reason, TestContext.Current.CancellationToken);
    }

    private string TrackCall(string callId)
    {
        _callIds.Add(callId);
        return callId;
    }
}
