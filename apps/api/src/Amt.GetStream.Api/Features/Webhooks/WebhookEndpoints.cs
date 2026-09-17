using Amt.GetStream.Api.Services.Stream;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Amt.GetStream.Api.Features.Webhooks;

public static class WebhookEndpoints
{
    private const string SignatureHeader = "X-Signature";
    private const string WebhookIdHeader = "X-Webhook-Id";

    /// <summary>
    /// Receives Stream's webhook events. Stream is a server, not a browser, so this route is
    /// deliberately outside the CORS policy.
    /// </summary>
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/stream", ReceiveAsync)
            .WithName("ReceiveStreamWebhook")
            .WithTags("Webhooks")
            .WithSummary("Receive a Stream webhook event")
            .WithDescription(
                "Verifies the X-Signature header against the raw body. A valid signature returns 204, " +
                "including for event types we don't handle yet, so Stream stops retrying. An invalid or " +
                "missing signature returns 401. Responses never carry a body.")
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> ReceiveAsync(
        HttpRequest request,
        IStreamWebhookVerifier verifier,
        ILogger<StreamWebhookLog> logger,
        CancellationToken cancellationToken)
    {
        // The signature covers the exact bytes Stream sent, so read the raw body, never a bound model.
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        var body = buffer.ToArray();

        var webhookId = request.Headers[WebhookIdHeader].ToString();
        var eventType = verifier.VerifyAndGetEventType(body, request.Headers[SignatureHeader]);

        if (eventType is null)
        {
            // No detail in the response: a caller must not learn why verification failed.
            logger.LogWarning("Rejected Stream webhook {WebhookId}: signature verification failed", webhookId);
            return TypedResults.Unauthorized();
        }

        logger.LogInformation("Stream webhook {WebhookId} received: {EventType}", webhookId, eventType);

        return TypedResults.NoContent();
    }
}

/// <summary>Log category for received Stream webhooks.</summary>
public sealed class StreamWebhookLog;
