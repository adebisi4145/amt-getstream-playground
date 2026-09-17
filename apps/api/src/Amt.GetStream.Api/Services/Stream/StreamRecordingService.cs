using GetStream;
using GetStream.Models;

namespace Amt.GetStream.Api.Services.Stream;

internal sealed class StreamRecordingService(VideoClient video) : IStreamRecordingService
{
    /// <summary>
    /// The only recording type we use: one mixed audio/video file of the call.
    /// Clients can't choose it. Stream sends the value as a path segment and publishes no list of valid values.
    /// </summary>
    private const string CompositeRecordingType = "composite";

    public Task StartAsync(string type, string id, CancellationToken cancellationToken) =>
        CallStreamAsync(
            nameof(StartAsync),
            () => video.StartRecordingAsync(type, id, CompositeRecordingType, new StartRecordingRequest(), cancellationToken));

    public Task StopAsync(string type, string id, CancellationToken cancellationToken) =>
        CallStreamAsync(
            nameof(StopAsync),
            () => video.StopRecordingAsync(type, id, CompositeRecordingType, new StopRecordingRequest(), cancellationToken));

    public async Task<IReadOnlyList<CallRecordingSummary>> ListAsync(
        string type,
        string id,
        CancellationToken cancellationToken)
    {
        var response = await CallStreamAsync(
            nameof(ListAsync),
            () => video.ListRecordingsAsync(type, id, cancellationToken: cancellationToken));

        return response.Data?.Recordings
            .Select(recording => new CallRecordingSummary(
                recording.Filename,
                recording.Url,
                recording.RecordingType,
                recording.SessionID,
                recording.StartTime,
                recording.EndTime))
            .ToArray() ?? [];
    }

    private static async Task<T> CallStreamAsync<T>(string operation, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (GetStreamException exception)
        {
            throw new StreamRequestFailedException(
                operation,
                (exception as GetStreamApiException)?.StatusCode,
                exception);
        }
    }
}
