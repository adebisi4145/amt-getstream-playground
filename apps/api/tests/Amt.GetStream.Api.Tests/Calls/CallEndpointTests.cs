using System.Net;
using System.Net.Http.Json;
using System.Text;
using Amt.GetStream.Api.Features.Calls;
using Amt.GetStream.Api.Services.Stream;
using Microsoft.Extensions.DependencyInjection;

namespace Amt.GetStream.Api.Tests.Calls;

public sealed class CallEndpointTests
{
    [Fact]
    public async Task Create_passes_type_members_and_creator_to_stream()
    {
        var calls = new FakeStreamCallService();
        using var client = CreateClient(calls);

        var response = await client.PostAsJsonAsync(
            "/api/calls",
            new
            {
                type = "default",
                id = "team-standup",
                createdById = "alice",
                members = new[] { new { userId = "bob", role = "host" } },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = Assert.Single(calls.Created);
        Assert.Equal("default", created.Type);
        Assert.Equal("team-standup", created.Id);
        Assert.Equal("alice", created.CreatedById);
        Assert.Equal([new CallMember("bob", "host")], created.Members);

        var body = await response.Content.ReadFromJsonAsync<CallResponseBody>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("default:team-standup", body.Cid);
        Assert.Equal("bob", Assert.Single(body.Members).UserId);
    }

    [Fact]
    public async Task Create_generates_an_id_when_none_is_given()
    {
        var calls = new FakeStreamCallService();
        using var client = CreateClient(calls);

        var response = await client.PostAsJsonAsync(
            "/api/calls",
            new { type = "development", createdById = "alice" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = Assert.Single(calls.Created);
        Assert.Matches("^[0-9a-f]{32}$", created.Id);
    }

    public static TheoryData<string> InvalidCreateRequests => new()
    {
        """{ "type": "chat", "createdById": "alice" }""",
        """{ "createdById": "alice" }""",
        """{ "type": "default" }""",
        """{ "type": "default", "createdById": "bad id!" }""",
        """{ "type": "default", "id": "bad id!", "createdById": "alice" }""",
        """{ "type": "default", "createdById": "alice", "members": [ { "userId": "bad id!" } ] }""",
    };

    [Theory]
    [MemberData(nameof(InvalidCreateRequests))]
    public async Task Invalid_create_returns_400_without_calling_stream(string json)
    {
        var calls = new FakeStreamCallService();
        using var client = CreateClient(calls);

        var response = await client.PostAsync("/api/calls", Json(json), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(calls.Created);
    }

    [Fact]
    public async Task Unknown_call_type_in_the_route_returns_400()
    {
        var calls = new FakeStreamCallService();
        using var client = CreateClient(calls);

        var response = await client.GetAsync("/api/calls/chat/team-standup", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(calls.Fetched);
    }

    [Fact]
    public async Task Call_types_can_be_added_through_configuration()
    {
        var calls = new FakeStreamCallService();
        using var client = CreateClient(calls, new Dictionary<string, string?>
        {
            // A custom call type created in the Stream dashboard needs config only, not a code change.
            ["Calls:AllowedTypes:4"] = "townhall",
        });

        var allowed = await client.PostAsJsonAsync(
            "/api/calls",
            new { type = "townhall", createdById = "alice" },
            TestContext.Current.CancellationToken);
        var refused = await client.PostAsJsonAsync(
            "/api/calls",
            new { type = "chat", createdById = "alice" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Get_returns_404_when_stream_does_not_have_the_call()
    {
        using var client = CreateClient(new FakeStreamCallService { CallMissing = true });

        var response = await client.GetAsync("/api/calls/default/missing", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Query_passes_filters_through_and_returns_the_paging_cursor()
    {
        var calls = new FakeStreamCallService();
        using var client = CreateClient(calls);

        var response = await client.GetAsync("/api/calls?type=default&limit=10", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([("default", 10, (string?)null)], calls.Queried);

        var body = await response.Content.ReadFromJsonAsync<CallListResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("cursor-2", body.Next);
        Assert.Single(body.Calls);
    }

    [Theory]
    [InlineData("/api/calls?limit=0")]
    [InlineData("/api/calls?limit=101")]
    public async Task Query_rejects_an_out_of_range_limit(string url)
    {
        var calls = new FakeStreamCallService();
        using var client = CreateClient(calls);

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(calls.Queried);
    }

    [Fact]
    public async Task Members_add_and_remove_are_passed_through()
    {
        var calls = new FakeStreamCallService();
        using var client = CreateClient(calls);

        var response = await client.PostAsJsonAsync(
            "/api/calls/default/team-standup/members",
            new { add = new[] { new { userId = "carol", role = (string?)null } }, remove = new[] { "bob" } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var update = Assert.Single(calls.MemberUpdates);
        Assert.Equal([new CallMember("carol", null)], update.Add);
        Assert.Equal(["bob"], update.Remove);
    }

    [Fact]
    public async Task Members_request_with_nothing_to_do_returns_400()
    {
        var calls = new FakeStreamCallService();
        using var client = CreateClient(calls);

        var response = await client.PostAsJsonAsync(
            "/api/calls/default/team-standup/members",
            new { add = Array.Empty<object>(), remove = Array.Empty<string>() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(calls.MemberUpdates);
    }

    [Fact]
    public async Task End_returns_204_and_ends_the_call()
    {
        var calls = new FakeStreamCallService();
        using var client = CreateClient(calls);

        var response = await client.PostAsync("/api/calls/default/team-standup/end", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([("default", "team-standup")], calls.Ended);
    }

    [Fact]
    public async Task Stream_failure_returns_502_without_stream_details()
    {
        const string streamDetail = "stream-internal-detail-that-must-not-leak";
        using var client = CreateClient(new FakeStreamCallService
        {
            Failure = new StreamRequestFailedException("GetOrCreateAsync", 500, new Exception(streamDetail)),
        });

        var response = await client.PostAsJsonAsync(
            "/api/calls",
            new { type = "default", createdById = "alice" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(streamDetail, body);
    }

    [Theory]
    [InlineData(400, HttpStatusCode.Conflict)]
    [InlineData(404, HttpStatusCode.Conflict)]
    [InlineData(429, HttpStatusCode.TooManyRequests)]
    [InlineData(500, HttpStatusCode.BadGateway)]
    [InlineData(null, HttpStatusCode.BadGateway)]
    public async Task Stream_status_decides_whether_the_caller_or_stream_is_at_fault(
        int? streamStatus, HttpStatusCode expected)
    {
        // Stream rejecting a request (for example "there is no active session") is the caller's
        // problem to fix, so it must not look like Stream being down.
        using var client = CreateClient(new FakeStreamCallService
        {
            Failure = new StreamRequestFailedException("GetOrCreateAsync", streamStatus, new Exception("boom")),
        });

        var response = await client.PostAsJsonAsync(
            "/api/calls",
            new { type = "default", createdById = "alice" },
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("boom", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Call_errors_keep_cors_headers_for_the_web_origin()
    {
        using var client = CreateClient(new FakeStreamCallService
        {
            Failure = new StreamRequestFailedException("GetOrCreateAsync", 500, new Exception("boom")),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/calls")
        {
            Content = Json("""{ "type": "default", "createdById": "alice" }"""),
        };
        request.Headers.Add("Origin", "http://localhost:3000");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("http://localhost:3000", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    private static HttpClient CreateClient(
        FakeStreamCallService calls,
        IDictionary<string, string?>? settings = null) =>
        ApiFactory.Create(
            settings: settings,
            configureServices: services => services.AddSingleton<IStreamCallService>(calls))
            .CreateClient();

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
