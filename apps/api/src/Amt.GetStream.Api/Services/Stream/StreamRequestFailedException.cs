namespace Amt.GetStream.Api.Services.Stream;

/// <summary>
/// Raised by the Stream service wrappers when a call to Stream fails, so SDK exception types never leave Services/Stream.
/// Stream's status code and message are kept for logging only and must not be returned to clients.
/// </summary>
public sealed class StreamRequestFailedException(string operation, int? streamStatusCode, Exception innerException)
    : Exception($"Stream request '{operation}' failed.", innerException)
{
    public string Operation { get; } = operation;

    public int? StreamStatusCode { get; } = streamStatusCode;
}
