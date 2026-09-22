using Amt.GetStream.Api.Services.Stream;
using Microsoft.Extensions.Options;

namespace Amt.GetStream.Api.Features.Consultations;

/// <summary>
/// Closes consultations that nobody finished.
///
/// A consultation only leaves waiting/accepted when a human presses Complete or Cancel. Staff close
/// tabs, patients lose signal, and those consultations would otherwise stay open forever — clogging
/// the board and, worse, inviting a patient to rejoin a call from days ago.
///
/// Anything with nobody in the call and no activity for <see cref="ConsultationOptions.StaleAfter"/>
/// is closed as <see cref="ConsultationEndReason.Abandoned"/> — deliberately distinct from
/// "finished", because nobody actually concluded it.
/// </summary>
internal sealed class StaleConsultationSweeper(
    IServiceScopeFactory scopeFactory,
    IOptions<ConsultationOptions> options,
    TimeProvider timeProvider,
    ILogger<StaleConsultationSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed sweep must never take the API down; the next one tries again.
                logger.LogError(exception, "Sweeping stale consultations failed");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }

    /// <summary>One pass. Internal so tests drive it directly instead of racing the timer.</summary>
    internal async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var consultations = scope.ServiceProvider.GetRequiredService<IStreamConsultationService>();

        var callType = options.Value.CallType;
        var cutoff = timeProvider.GetUtcNow() - options.Value.StaleAfter;

        foreach (var status in new[] { ConsultationStatus.Waiting, ConsultationStatus.Accepted })
        {
            var open = await consultations.QueryByStatusAsync(callType, status, patientId: null, 100, cancellationToken);

            foreach (var consultation in open.Where(item => item.RequestedAt < cutoff))
            {
                // Long consultations are normal: only close it if nobody is actually in the call.
                var participants = await consultations.SessionParticipantCountAsync(
                    callType, consultation.CallId, cancellationToken);

                if (participants > 0)
                {
                    continue;
                }

                await consultations.CloseAsync(
                    callType,
                    consultation.CallId,
                    status == ConsultationStatus.Waiting ? ConsultationStatus.Cancelled : ConsultationStatus.Completed,
                    ConsultationEndReason.Abandoned,
                    cancellationToken);

                logger.LogInformation(
                    "Closed stale consultation {CallId} (was {Status}, requested {RequestedAt})",
                    consultation.CallId,
                    status,
                    consultation.RequestedAt);
            }
        }
    }
}
