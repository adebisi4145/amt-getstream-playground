using Amt.GetStream.Api.Services.Stream;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Amt.GetStream.Api.Features.Calls;

public static class RecordingEndpoints
{
    public static IEndpointRouteBuilder MapRecordingEndpoints(this IEndpointRouteBuilder api)
    {
        var recordings = api.MapGroup("/calls/{type}/{id}/recordings").WithTags("Recordings");

        recordings.MapPost("/start", StartAsync)
            .WithName("StartRecording")
            .WithSummary("Start recording the call")
            .WithDescription("Recording is server-controlled and always the composite (single mixed file) type.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        recordings.MapPost("/stop", StopAsync)
            .WithName("StopRecording")
            .WithSummary("Stop recording the call")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        recordings.MapGet("/", ListAsync)
            .WithName("ListRecordings")
            .WithSummary("List the call's recordings")
            .WithDescription("A recording appears here once Stream has finished processing it, which can lag after stopping.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return api;
    }

    private static async Task<Results<NoContent, ValidationProblem>> StartAsync(
        string type,
        string id,
        IStreamRecordingService recordings,
        IOptions<CallOptions> options,
        CancellationToken cancellationToken)
    {
        if (CallEndpoints.InvalidRoute(type, id, options) is { } problem)
        {
            return problem;
        }

        await recordings.StartAsync(type, id, cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> StopAsync(
        string type,
        string id,
        IStreamRecordingService recordings,
        IOptions<CallOptions> options,
        CancellationToken cancellationToken)
    {
        if (CallEndpoints.InvalidRoute(type, id, options) is { } problem)
        {
            return problem;
        }

        await recordings.StopAsync(type, id, cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<CallRecordingResponse>>, ValidationProblem>> ListAsync(
        string type,
        string id,
        IStreamRecordingService recordings,
        IOptions<CallOptions> options,
        CancellationToken cancellationToken)
    {
        if (CallEndpoints.InvalidRoute(type, id, options) is { } problem)
        {
            return problem;
        }

        var found = await recordings.ListAsync(type, id, cancellationToken);

        IReadOnlyList<CallRecordingResponse> response =
        [
            .. found.Select(recording => new CallRecordingResponse(
                recording.Filename,
                recording.Url,
                recording.RecordingType,
                recording.SessionId,
                recording.StartTime,
                recording.EndTime)),
        ];

        return TypedResults.Ok(response);
    }
}
