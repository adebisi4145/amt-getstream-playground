using Amt.GetStream.Api.Features.Consultations;
using Amt.GetStream.Api.Services.Stream;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Amt.GetStream.Api.Tests.Consultations;

/// <summary>
/// The sweeper closes what nobody finished. Its judgement calls — how old is too old, and whether
/// anyone is still in the call — are what these tests pin down.
/// </summary>
public sealed class StaleConsultationSweeperTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ConsultationStatus.Waiting, ConsultationStatus.Cancelled)]
    [InlineData(ConsultationStatus.Accepted, ConsultationStatus.Completed)]
    public async Task Closes_an_empty_consultation_that_nobody_finished(string open, string expectedStatus)
    {
        var consultations = new FakeStreamConsultationService { SessionParticipants = 0 };
        consultations.Seed("stale", open, requestedAt: Now.AddMinutes(-30));

        await SweepAsync(consultations);

        Assert.Equal([("stale", expectedStatus, ConsultationEndReason.Abandoned)], consultations.Closed);
    }

    [Fact]
    public async Task Leaves_a_long_consultation_alone_while_people_are_still_in_the_call()
    {
        // Consultations can legitimately run for a long time; length is not abandonment.
        var consultations = new FakeStreamConsultationService { SessionParticipants = 2 };
        consultations.Seed("busy", ConsultationStatus.Accepted, requestedAt: Now.AddMinutes(-30));

        await SweepAsync(consultations);

        Assert.Empty(consultations.Closed);
    }

    [Fact]
    public async Task Leaves_a_recent_consultation_alone_even_when_the_call_is_empty()
    {
        // A patient who is reconnecting has an empty call for a few moments.
        var consultations = new FakeStreamConsultationService { SessionParticipants = 0 };
        consultations.Seed("fresh", ConsultationStatus.Waiting, requestedAt: Now.AddMinutes(-2));

        await SweepAsync(consultations);

        Assert.Empty(consultations.Closed);
    }

    private static async Task SweepAsync(FakeStreamConsultationService consultations)
    {
        var services = new ServiceCollection()
            .AddSingleton<IStreamConsultationService>(consultations)
            .BuildServiceProvider();

        var time = new FakeTimeProvider(Now);

        var sweeper = new StaleConsultationSweeper(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ConsultationOptions { CallType = "development", StaleAfter = TimeSpan.FromMinutes(10) }),
            time,
            NullLogger<StaleConsultationSweeper>.Instance);

        // Driven directly: starting the hosted service would race its own timer.
        await sweeper.SweepOnceAsync(TestContext.Current.CancellationToken);
    }
}
