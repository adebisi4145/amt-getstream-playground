using GetStream;
using GetStream.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Amt.GetStream.Api.Services.Stream;

internal sealed class StreamUserService(StreamClient client, IOptions<StreamOptions> options) : IStreamUserService
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    public async Task EnsureUserAsync(string userId, string? name, string? image, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, object>();
        if (name is not null)
        {
            fields["name"] = name;
        }

        if (image is not null)
        {
            fields["image"] = image;
        }

        try
        {
            // A full upsert replaces the user's existing data (role, custom data), and Stream doesn't
            // document whether a partial update creates missing users. So: look up first, then either
            // create the user or partially update only the fields that were sent.
            if (await UserExistsAsync(userId, cancellationToken))
            {
                if (fields.Count == 0)
                {
                    return;
                }

                await client.UpdateUsersPartialAsync(
                    new UpdateUsersPartialRequest
                    {
                        Users = [new UpdateUserPartialRequest { ID = userId, Set = fields }],
                    },
                    cancellationToken);
                return;
            }

            await client.UpdateUsersAsync(
                new UpdateUsersRequest
                {
                    Users = new Dictionary<string, UserRequest>
                    {
                        [userId] = new UserRequest { ID = userId, Name = name, Image = image },
                    },
                },
                cancellationToken);
        }
        catch (GetStreamException exception)
        {
            throw new StreamRequestFailedException(
                nameof(EnsureUserAsync),
                (exception as GetStreamApiException)?.StatusCode,
                exception);
        }
    }

    public UserToken CreateToken(string userId)
    {
        var token = client.CreateUserToken(userId, options.Value.TokenLifetime);
        var expiresAt = DateTime.SpecifyKind(TokenHandler.ReadJsonWebToken(token).ValidTo, DateTimeKind.Utc);

        return new UserToken(token, new DateTimeOffset(expiresAt));
    }

    private async Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken)
    {
        var response = await client.QueryUsersAsync(
            new QueryUsersPayload
            {
                FilterConditions = new Dictionary<string, object> { ["id"] = userId },
                Limit = 1,
            },
            cancellationToken);

        return response.Data?.Users is { Count: > 0 };
    }
}
