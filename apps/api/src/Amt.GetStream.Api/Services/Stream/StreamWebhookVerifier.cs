using GetStream;

namespace Amt.GetStream.Api.Services.Stream;

internal sealed class StreamWebhookVerifier(StreamClient client) : IStreamWebhookVerifier
{
    public const string UnknownEventType = "unknown";

    public string? VerifyAndGetEventType(byte[] body, string? signature)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return null;
        }

        try
        {
            // Verifies the HMAC over the uncompressed body, handling gzip, and parses the event.
            var @event = client.VerifyAndParseWebhook(body, signature);

            return Webhook.GetEventType(body)
                ?? @event?.GetType().Name
                ?? UnknownEventType;
        }
        catch (Webhook.StreamInvalidWebhookException)
        {
            // Covers a signature mismatch and any malformed payload. The caller turns this into a 401
            // with no body, so we never tell a caller why verification failed.
            return null;
        }
    }
}
