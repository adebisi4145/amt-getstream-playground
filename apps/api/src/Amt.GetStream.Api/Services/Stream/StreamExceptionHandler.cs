using Microsoft.AspNetCore.Diagnostics;

namespace Amt.GetStream.Api.Services.Stream;

/// <summary>
/// Turns <see cref="StreamRequestFailedException"/> into a ProblemDetails response.
/// Stream's own error text is logged, never returned.
///
/// Stream rejecting a request (4xx) is not the same as Stream being broken (5xx, network):
/// - 4xx becomes 409 Conflict, meaning "the call or user isn't in a state that allows this",
///   for example starting a recording on a call nobody has joined.
/// - 429 is passed through, so a caller can back off.
/// - Everything else becomes 502, meaning "Stream is unreachable or failing".
/// </summary>
internal sealed class StreamExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<StreamExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not StreamRequestFailedException streamException)
        {
            return false;
        }

        logger.LogError(
            streamException,
            "Stream request {Operation} failed with Stream status {StreamStatusCode}",
            streamException.Operation,
            streamException.StreamStatusCode);

        var (status, title, detail) = Describe(streamException.StreamStatusCode);

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Detail = detail,
            },
        });
    }

    private static (int Status, string Title, string Detail) Describe(int? streamStatusCode) => streamStatusCode switch
    {
        StatusCodes.Status429TooManyRequests => (
            StatusCodes.Status429TooManyRequests,
            "Stream rate limit reached",
            "Stream is rate limiting this app. Retry later."),

        >= 400 and < 500 => (
            StatusCodes.Status409Conflict,
            "Stream rejected the request",
            "Stream rejected this request for the current state of the call or user. "
            + "For example, recording can only start once someone has joined the call."),

        _ => (
            StatusCodes.Status502BadGateway,
            "Stream request failed",
            "The request to Stream could not be completed. Try again later."),
    };
}
