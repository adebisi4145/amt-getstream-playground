using Amt.GetStream.Api.Services.Stream;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Amt.GetStream.Api.Features.Calls;

public static class CallEndpoints
{
    public static IEndpointRouteBuilder MapCallEndpoints(this IEndpointRouteBuilder api)
    {
        var calls = api.MapGroup("/calls").WithTags("Calls");

        calls.MapPost("/", CreateAsync)
            .WithName("CreateCall")
            .WithSummary("Create a call, or return the existing one with that id")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        calls.MapGet("/", QueryAsync)
            .WithName("QueryCalls")
            .WithSummary("List calls, newest first, with Stream's paging cursor")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        calls.MapGet("/{type}/{id}", GetAsync)
            .WithName("GetCall")
            .WithSummary("Get a single call")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        calls.MapPost("/{type}/{id}/members", UpdateMembersAsync)
            .WithName("UpdateCallMembers")
            .WithSummary("Add or remove call members")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        calls.MapPost("/{type}/{id}/end", EndAsync)
            .WithName("EndCall")
            .WithSummary("End the call for everyone")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return api;
    }

    private static async Task<Results<Ok<CallResponseBody>, ValidationProblem>> CreateAsync(
        CreateCallRequest request,
        IStreamCallService calls,
        IOptions<CallOptions> options,
        CancellationToken cancellationToken)
    {
        if (InvalidType(request.Type, options) is { } problem)
        {
            return problem;
        }

        if (InvalidMembers(request.Members) is { } memberProblem)
        {
            return memberProblem;
        }

        var call = await calls.GetOrCreateAsync(
            request.Type!,
            request.Id ?? Guid.NewGuid().ToString("N"),
            request.CreatedById!,
            [.. request.Members.Select(member => new CallMember(member.UserId!, member.Role))],
            cancellationToken);

        return TypedResults.Ok(ToResponse(call));
    }

    private static async Task<Results<Ok<CallListResponse>, ValidationProblem>> QueryAsync(
        IStreamCallService calls,
        IOptions<CallOptions> options,
        CancellationToken cancellationToken,
        string? type = null,
        int? limit = null,
        string? next = null)
    {
        if (type is not null && InvalidType(type, options) is { } problem)
        {
            return problem;
        }

        if (limit is < 1 or > 100)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = ["The limit field must be between 1 and 100."],
            });
        }

        var page = await calls.QueryAsync(type, limit, next, cancellationToken);

        return TypedResults.Ok(new CallListResponse([.. page.Calls.Select(ToResponse)], page.Next));
    }

    private static async Task<Results<Ok<CallResponseBody>, NotFound, ValidationProblem>> GetAsync(
        string type,
        string id,
        IStreamCallService calls,
        IOptions<CallOptions> options,
        CancellationToken cancellationToken)
    {
        if (InvalidRoute(type, id, options) is { } problem)
        {
            return problem;
        }

        var call = await calls.GetAsync(type, id, cancellationToken);

        return call is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(call));
    }

    private static async Task<Results<Ok<CallResponseBody>, ValidationProblem>> UpdateMembersAsync(
        string type,
        string id,
        UpdateCallMembersRequestBody request,
        IStreamCallService calls,
        IOptions<CallOptions> options,
        CancellationToken cancellationToken)
    {
        if (InvalidRoute(type, id, options) is { } problem)
        {
            return problem;
        }

        if (InvalidMembers(request.Add) is { } memberProblem)
        {
            return memberProblem;
        }

        if (request.Remove.Any(userId => !StreamId.IsValid(userId)))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["remove"] = [StreamId.Message],
            });
        }

        if (request.Add.Length == 0 && request.Remove.Length == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["add"] = ["Provide at least one member to add or remove."],
            });
        }

        var call = await calls.UpdateMembersAsync(
            type,
            id,
            [.. request.Add.Select(member => new CallMember(member.UserId!, member.Role))],
            request.Remove,
            cancellationToken);

        return TypedResults.Ok(ToResponse(call));
    }

    private static async Task<Results<NoContent, ValidationProblem>> EndAsync(
        string type,
        string id,
        IStreamCallService calls,
        IOptions<CallOptions> options,
        CancellationToken cancellationToken)
    {
        if (InvalidRoute(type, id, options) is { } problem)
        {
            return problem;
        }

        await calls.EndAsync(type, id, cancellationToken);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Route values aren't covered by request-body validation, so type and id are checked here.
    /// The allowed types come from configuration, so this can't be a DataAnnotations attribute.
    /// </summary>
    internal static ValidationProblem? InvalidRoute(string type, string id, IOptions<CallOptions> options)
    {
        if (InvalidType(type, options) is { } problem)
        {
            return problem;
        }

        return StreamId.IsValid(id)
            ? null
            : TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["id"] = [StreamId.Message] });
    }

    /// <summary>
    /// Minimal API validation doesn't recurse into arrays of nested objects, so member ids are checked here.
    /// Without this, an invalid user id would reach Stream.
    /// </summary>
    private static ValidationProblem? InvalidMembers(IReadOnlyList<CallMemberRequest> members) =>
        members.All(member => StreamId.IsValid(member.UserId))
            ? null
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["members"] = [StreamId.Message],
            });

    private static ValidationProblem? InvalidType(string? type, IOptions<CallOptions> options) =>
        type is not null && options.Value.AllowedTypes.Contains(type)
            ? null
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["type"] = [$"The type field must be one of: {string.Join(", ", options.Value.AllowedTypes)}."],
            });

    private static CallResponseBody ToResponse(CallSummary call) =>
        new(
            call.Type,
            call.Id,
            call.Cid,
            call.CreatedById,
            call.CreatedAt,
            call.EndedAt,
            call.Recording,
            [.. call.Members.Select(member => new CallMemberResponse(member.UserId, member.Role))]);
}
