import type { StreamClient, WHEvent } from "@stream-io/node-sdk";
import { InvalidWebhookError } from "@stream-io/node-sdk";

export { InvalidWebhookError };

export interface StreamWebhookService {
  /**
   * Verifies Stream's `x-signature` against the raw request body and returns the event.
   * Throws {@link InvalidWebhookError} when the signature or envelope doesn't check out.
   */
  verifyAndParse(rawBody: Buffer, signature: string): WHEvent;
}

export function createStreamWebhookService(client: StreamClient): StreamWebhookService {
  return { verifyAndParse: (rawBody, signature) => client.verifyAndParseWebhook(rawBody, signature) };
}

export interface RecordedEvent {
  type: string;
  receivedAt: string;
  callCid: string | undefined;
  userId: string | undefined;
}

export interface WebhookEventLog {
  record(event: WHEvent): void;
  recent(): RecordedEvent[];
}

/** Keeps the last few events in memory so the playground can see what Stream sent without tailing logs. */
export function createWebhookEventLog(limit = 50): WebhookEventLog {
  const events: RecordedEvent[] = [];

  return {
    record(event) {
      const { call_cid, user } = event as { call_cid?: unknown; user?: { id?: unknown } };
      events.unshift({
        type: event.type,
        receivedAt: new Date().toISOString(),
        callCid: typeof call_cid === "string" ? call_cid : undefined,
        userId: typeof user?.id === "string" ? user.id : undefined,
      });
      events.length = Math.min(events.length, limit);
    },
    recent: () => [...events],
  };
}
