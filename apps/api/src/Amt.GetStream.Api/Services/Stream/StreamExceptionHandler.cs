using Microsoft.AspNetCore.Diagnostics;

namespace Amt.GetStream.Api.Services.Stream;

/// <summary>
/// Maps <see cref="StreamRequestFailedException"/> to a 502 ProblemDetails response with a generic message.
/// Stream's own error details are logged, not returned.
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

        httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Stream request failed",
                Detail = "The request to Stream could not be completed. Try again later.",
            },
        });
    }
}
