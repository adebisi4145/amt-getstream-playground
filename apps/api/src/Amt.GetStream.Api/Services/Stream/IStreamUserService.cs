namespace Amt.GetStream.Api.Services.Stream;

public interface IStreamUserService
{
    /// <summary>
    /// Makes sure the user exists in Stream and sets only the fields provided.
    /// Never sends a role and never unsets existing fields.
    /// </summary>
    Task EnsureUserAsync(string userId, string? name, string? image, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a Stream user token. <see cref="UserToken.ExpiresAt"/> is read from the token itself.
    /// </summary>
    UserToken CreateToken(string userId);
}

public sealed record UserToken(string Token, DateTimeOffset ExpiresAt);
