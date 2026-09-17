namespace Amt.GetStream.Api.Services.Stream;

public interface IStreamRecordingService
{
    Task StartAsync(string type, string id, CancellationToken cancellationToken);

    Task StopAsync(string type, string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CallRecordingSummary>> ListAsync(string type, string id, CancellationToken cancellationToken);
}

public sealed record CallRecordingSummary(
    string Filename,
    string Url,
    string RecordingType,
    string SessionId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime);
