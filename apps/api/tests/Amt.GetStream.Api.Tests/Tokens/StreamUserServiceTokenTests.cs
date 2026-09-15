using Amt.GetStream.Api.Services.Stream;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Amt.GetStream.Api.Tests.Tokens;

/// <summary>
/// Checks our wiring around token creation (configured lifetime, user id, reported expiry),
/// not how Stream builds its JWTs. No network calls are made.
/// </summary>
public sealed class StreamUserServiceTokenTests
{
    [Fact]
    public async Task CreateToken_uses_requested_user_and_configured_lifetime_and_reports_real_expiry()
    {
        var lifetime = TimeSpan.FromMinutes(15);
        await using var factory = ApiFactory.Create(settings: new Dictionary<string, string?>
        {
            ["Stream:TokenLifetime"] = lifetime.ToString(),
        });
        var users = factory.Services.GetRequiredService<IStreamUserService>();

        var result = users.CreateToken("alice");

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(result.Token);
        Assert.Equal("alice", jwt.GetClaim("user_id").Value);
        Assert.Equal(jwt.ValidTo, result.ExpiresAt.UtcDateTime);
        Assert.InRange(jwt.ValidTo - jwt.IssuedAt, lifetime, lifetime + TimeSpan.FromSeconds(10));
    }
}
