using System.ComponentModel.DataAnnotations;
using Amt.GetStream.Api.Services.Stream;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Amt.GetStream.Api.Features.Tokens;

public static class TokenEndpoints
{
    public const string AllowUntrustedRequestsKey = "Tokens:AllowUntrustedRequests";

    /// <summary>
    /// Maps POST /tokens onto <paramref name="api"/>, but only when <see cref="AllowUntrustedRequestsKey"/> is true.
    /// The endpoint trusts the userId sent by the client, so it must stay off outside development.
    /// Pass <c>app.Configuration</c> (read after Build) so test configuration overrides apply.
    /// </summary>
    public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder api, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>(AllowUntrustedRequestsKey))
        {
            return api;
        }

        api.MapPost("/tokens", CreateTokenAsync)
            .WithName("CreateToken")
            .WithTags("Tokens")
            .WithSummary("Issue a Stream user token")
            .WithDescription(
                "Ensures the user exists in Stream (setting only the fields sent) and returns the Stream API key " +
                "and a user token. The Stream API secret is never returned. Development only: the userId is trusted as sent.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return api;
    }

    private static async Task<Ok<TokenResponse>> CreateTokenAsync(
        TokenRequest request,
        IStreamUserService users,
        IOptions<StreamOptions> streamOptions,
        CancellationToken cancellationToken)
    {
        // [Required] has already been enforced by validation, so UserId is not null here.
        var userId = request.UserId!;

        await users.EnsureUserAsync(userId, request.Name, request.Image, cancellationToken);
        var token = users.CreateToken(userId);

        return TypedResults.Ok(new TokenResponse(streamOptions.Value.ApiKey, userId, token.Token, token.ExpiresAt));
    }
}

public sealed class TokenRequest
{
    /// <summary>
    /// Letters, digits, @, _ and -, up to 255 characters. This is our own conservative rule;
    /// Stream doesn't publish its user ID limits.
    /// </summary>
    [Required]
    [StringLength(255)]
    [RegularExpression("^[A-Za-z0-9@_-]+$", ErrorMessage = "The UserId field may only contain letters, digits, @, _ and -.")]
    public string? UserId { get; init; }

    public string? Name { get; init; }

    [HttpUrl]
    public string? Image { get; init; }
}

public sealed record TokenResponse(string ApiKey, string UserId, string Token, DateTimeOffset ExpiresAt);
