using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Amt.GetStream.Api.Tests.Webhooks;

/// <summary>
/// Uses the real signature verification with a test secret. It's deterministic and offline,
/// and it's our security boundary, so it's worth testing for real rather than faking.
/// </summary>
public sealed class WebhookEndpointTests
{
    private const string CallEndedEvent = """{"type":"call.ended","call_cid":"default:team-standup"}""";

    [Fact]
    public async Task Correctly_signed_event_is_accepted_with_an_empty_response()
    {
        using var client = ApiFactory.Create().CreateClient();

        var response = await PostAsync(client, CallEndedEvent, Sign(CallEndedEvent));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unknown_event_type_is_still_accepted_so_stream_stops_retrying()
    {
        using var client = ApiFactory.Create().CreateClient();
        const string body = """{"type":"call.some_future_event","call_cid":"default:team-standup"}""";

        var response = await PostAsync(client, body, Sign(body));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("deadbeef")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Bad_or_missing_signature_is_rejected_with_401_and_no_body(string? signature)
    {
        using var client = ApiFactory.Create().CreateClient();

        var response = await PostAsync(client, CallEndedEvent, signature);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Signature_of_a_different_body_is_rejected()
    {
        using var client = ApiFactory.Create().CreateClient();

        var response = await PostAsync(client, CallEndedEvent, Sign("""{"type":"call.created"}"""));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_route_is_not_part_of_the_web_cors_policy()
    {
        using var client = ApiFactory.Create().CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stream")
        {
            Content = new StringContent(CallEndedEvent, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("X-Signature", Sign(CallEndedEvent));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string body, string? signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stream")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (signature is not null)
        {
            request.Headers.Add("X-Signature", signature);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Sign(string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(ApiFactory.TestApiSecret),
            Encoding.UTF8.GetBytes(body)));
}
