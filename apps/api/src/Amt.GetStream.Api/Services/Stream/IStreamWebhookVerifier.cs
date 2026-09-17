namespace Amt.GetStream.Api.Services.Stream;

public interface IStreamWebhookVerifier
{
    /// <summary>
    /// Verifies the X-Signature header against the raw request body.
    /// Returns null when the signature is missing or doesn't match; otherwise the event type,
    /// which is "unknown" when the payload carries no type.
    /// </summary>
    string? VerifyAndGetEventType(byte[] body, string? signature);
}
