using Amt.GetStream.Api.Services.Stream;

namespace Amt.GetStream.Api.Tests.Tokens;

internal sealed class FakeStreamUserService : IStreamUserService
{
    public static readonly UserToken IssuedToken = new("fake-token", new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));

    public List<(string UserId, string? Name, string? Image)> EnsuredUsers { get; } = [];

    public List<string> TokensCreatedFor { get; } = [];

    public Exception? EnsureUserFailure { get; init; }

    public Task EnsureUserAsync(string userId, string? name, string? image, CancellationToken cancellationToken)
    {
        if (EnsureUserFailure is not null)
        {
            throw EnsureUserFailure;
        }

        EnsuredUsers.Add((userId, name, image));
        return Task.CompletedTask;
    }

    public UserToken CreateToken(string userId)
    {
        TokensCreatedFor.Add(userId);
        return IssuedToken;
    }
}
