using System.Net;
using System.Net.Http.Json;
using Amt.GetStream.Api.Features.Tokens;
using Amt.GetStream.Api.Services.Stream;

namespace Amt.GetStream.Api.Tests.Tokens;

public sealed class TokenEndpointTests
{
    private const string WebOrigin = "http://localhost:3000";

    [Theory]
    [InlineData("alice", null, null)]
    [InlineData("alice", "Alice", null)]
    [InlineData("alice", "Alice", "https://example.com/alice.png")]
    [InlineData("bob@example_1-2", null, "http://example.com/bob.png")]
    public async Task Valid_request_ensures_user_with_exactly_the_fields_sent_and_returns_token(
        string userId, string? name, string? image)
    {
        var users = new FakeStreamUserService();
        await using var factory = ApiFactory.Create(users);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/tokens", new { userId, name, image }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([(userId, name, image)], users.EnsuredUsers);

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(ApiFactory.TestApiKey, body.ApiKey);
        Assert.Equal(userId, body.UserId);
        Assert.Equal(FakeStreamUserService.IssuedToken.Token, body.Token);
        Assert.Equal(FakeStreamUserService.IssuedToken.ExpiresAt, body.ExpiresAt);
    }

    public static TheoryData<string> InvalidRequests => new()
    {
        """{}""",
        """{ "userId": "" }""",
        """{ "userId": "bad id!" }""",
        """{ "userId": "alice/../bob" }""",
        $$"""{ "userId": "{{new string('a', 256)}}" }""",
        """{ "userId": "alice", "image": "/relative/alice.png" }""",
        """{ "userId": "alice", "image": "ftp://example.com/alice.png" }""",
        """{ "userId": "alice", "image": "" }""",
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_request_returns_400_and_does_not_call_stream(string json)
    {
        var users = new FakeStreamUserService();
        await using var factory = ApiFactory.Create(users);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/tokens", JsonContent(json), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(users.EnsuredUsers);
        Assert.Empty(users.TokensCreatedFor);
    }

    [Fact]
    public async Task Stream_failure_returns_502_without_token_or_stream_details()
    {
        const string streamDetail = "stream-internal-detail-that-must-not-leak";
        var users = new FakeStreamUserService
        {
            EnsureUserFailure = new StreamRequestFailedException("EnsureUserAsync", 500, new Exception(streamDetail)),
        };
        await using var factory = ApiFactory.Create(users);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/tokens", new { userId = "alice" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(FakeStreamUserService.IssuedToken.Token, body);
        Assert.DoesNotContain(streamDetail, body);
    }

    [Fact]
    public async Task Endpoint_is_not_mapped_when_untrusted_requests_are_not_allowed()
    {
        var users = new FakeStreamUserService();
        await using var factory = ApiFactory.Create(users, new Dictionary<string, string?>
        {
            [TokenEndpoints.AllowUntrustedRequestsKey] = "false",
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/tokens", new { userId = "alice" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(users.EnsuredUsers);
    }

    [Fact]
    public async Task Validation_error_keeps_cors_headers_for_the_web_origin()
    {
        await using var factory = ApiFactory.Create(new FakeStreamUserService());
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tokens") { Content = JsonContent("""{ "userId": "bad id!" }""") };
        request.Headers.Add("Origin", WebOrigin);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(WebOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Stream_failure_keeps_cors_headers_for_the_web_origin()
    {
        var users = new FakeStreamUserService
        {
            EnsureUserFailure = new StreamRequestFailedException("EnsureUserAsync", 500, new Exception("boom")),
        };
        await using var factory = ApiFactory.Create(users);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tokens") { Content = JsonContent("""{ "userId": "alice" }""") };
        request.Headers.Add("Origin", WebOrigin);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(WebOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    private static StringContent JsonContent(string json) =>
        new(json, System.Text.Encoding.UTF8, "application/json");
}
