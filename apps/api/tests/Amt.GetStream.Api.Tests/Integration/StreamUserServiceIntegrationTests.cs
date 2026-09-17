using System.Text.Json;
using Amt.GetStream.Api.Services.Stream;
using GetStream;
using GetStream.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Amt.GetStream.Api.Tests.Integration;

/// <summary>
/// Runs the user-update acceptance criteria against the real Stream app.
/// Opt-in: skipped unless a Stream API secret is available from the API project's user secrets
/// or the Stream__ApiSecret environment variable. Each test uses a unique it-&lt;guid&gt; user and hard-deletes it.
/// Run before any commit that changes Services/Stream or upgrades getstream-net:
///   dotnet test apps/api/Amt.GetStream.Playground.slnx --filter-trait "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public sealed class StreamUserServiceIntegrationTests : IAsyncLifetime
{
    private readonly string _userId = $"it-{Guid.NewGuid():N}";
    private StreamIntegrationHost? _host;

    private IStreamUserService Users => _host!.Services.GetRequiredService<IStreamUserService>();

    private StreamClient Stream => _host!.Services.GetRequiredService<StreamClient>();

    public ValueTask InitializeAsync()
    {
        _host = StreamIntegrationHost.TryCreate();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task New_user_is_created_with_default_role()
    {
        SkipWithoutSecret();

        await Users.EnsureUserAsync(_userId, name: null, image: null, TestContext.Current.CancellationToken);

        var user = await GetUserAsync();
        Assert.NotNull(user);
        Assert.Equal("user", user.Role);
    }

    [Fact]
    public async Task Existing_user_is_unchanged_when_only_user_id_is_sent()
    {
        SkipWithoutSecret();
        await CreateUserAsync(name: "Alice", image: "https://example.com/alice.png");

        await Users.EnsureUserAsync(_userId, name: null, image: null, TestContext.Current.CancellationToken);

        var user = await GetUserAsync();
        Assert.NotNull(user);
        Assert.Equal("Alice", user.Name);
        Assert.Equal("https://example.com/alice.png", user.Image);
        Assert.Equal("admin", user.Role);
        Assert.Equal("blue", GetCustomTeam(user));
    }

    [Fact]
    public async Task Only_sent_fields_change_on_existing_user()
    {
        SkipWithoutSecret();
        await CreateUserAsync(name: "Alice", image: "https://example.com/alice.png");

        await Users.EnsureUserAsync(_userId, name: "Alice B", image: null, TestContext.Current.CancellationToken);

        var user = await GetUserAsync();
        Assert.NotNull(user);
        Assert.Equal("Alice B", user.Name);
        Assert.Equal("https://example.com/alice.png", user.Image);
        Assert.Equal("admin", user.Role);
        Assert.Equal("blue", GetCustomTeam(user));
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            var response = await Stream.DeleteUsersAsync(new DeleteUsersRequest
            {
                UserIds = [_userId],
                User = "hard",
            });

            // Deletion runs as a Stream background task.
            await Stream.WaitForTaskAsync(response.Data!.TaskID, timeout: TimeSpan.FromSeconds(60));
        }
        finally
        {
            await _host.DisposeAsync();
        }
    }

    private void SkipWithoutSecret() =>
        Assert.SkipWhen(_host is null, StreamIntegrationHost.SkipReason);

    /// <summary>Arrange directly with the SDK: role and custom data as if set in the dashboard.</summary>
    private async Task CreateUserAsync(string name, string image)
    {
        await Stream.UpdateUsersAsync(
            new UpdateUsersRequest
            {
                Users = new Dictionary<string, UserRequest>
                {
                    [_userId] = new UserRequest
                    {
                        ID = _userId,
                        Name = name,
                        Image = image,
                        Role = "admin",
                        Custom = new Dictionary<string, object> { ["team"] = "blue" },
                    },
                },
            },
            TestContext.Current.CancellationToken);
    }

    private async Task<FullUserResponse?> GetUserAsync()
    {
        var response = await Stream.QueryUsersAsync(
            new QueryUsersPayload { FilterConditions = new Dictionary<string, object> { ["id"] = _userId } },
            TestContext.Current.CancellationToken);

        return response.Data?.Users.SingleOrDefault();
    }

    private static string? GetCustomTeam(FullUserResponse user) =>
        user.Custom is JsonElement { ValueKind: JsonValueKind.Object } custom && custom.TryGetProperty("team", out var team)
            ? team.GetString()
            : null;
}
