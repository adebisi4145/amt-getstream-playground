import type { FastifyInstance } from "fastify";
import type { ZodTypeProvider } from "fastify-type-provider-zod";
import { z } from "zod";
import { problemDetailsSchema } from "../../http/problemDetails.ts";
import {
  InvalidWebhookError,
  type StreamWebhookService,
  type WebhookEventLog,
} from "../../services/stream/streamWebhookService.ts";

export const SIGNATURE_HEADER = "x-signature";

export const recentEventsResponseSchema = z.object({
  events: z.array(
    z.object({
      type: z.string(),
      receivedAt: z.string(),
      callCid: z.string().optional(),
      userId: z.string().optional(),
    }),
  ),
});

export interface WebhookRouteOptions {
  webhooks: StreamWebhookService;
  log: WebhookEventLog;
  /** Only in development: exposes the recent events over HTTP. */
  exposeRecentEvents: boolean;
}

/**
 * POST /webhooks/stream receives Stream's call events (ring, accepted, rejected, missed, ended...).
 * It authenticates the request by HMAC signature, not by the dev flag, so it's safe to leave on.
 * Point the webhook URL in the Stream dashboard at this route.
 */
export async function webhookRoutes(app: FastifyInstance, { webhooks, log, exposeRecentEvents }: WebhookRouteOptions) {
  // Signatures are over the exact bytes Stream sent, so this route must not use the parsed JSON body.
  app.addContentTypeParser("application/json", { parseAs: "buffer" }, (_request, body, done) => done(null, body));

  app.post(
    "/webhooks/stream",
    {
      schema: {
        operationId: "ReceiveStreamWebhook",
        tags: ["Webhooks"],
        summary: "Receive a Stream webhook event",
        description:
          "Verifies Stream's x-signature header against the raw body and records the event. " +
          "Unsigned or badly signed requests get 401.",
        response: { 204: z.null(), 401: problemDetailsSchema },
      },
    },
    async (request, reply) => {
      const signature = request.headers[SIGNATURE_HEADER];
      const rawBody = request.body;

      if (typeof signature !== "string" || !Buffer.isBuffer(rawBody)) {
        return reply.code(401).type("application/problem+json").send({ title: "Unauthorized", status: 401 });
      }

      let event;
      try {
        event = webhooks.verifyAndParse(rawBody, signature);
      } catch (error) {
        if (error instanceof InvalidWebhookError) {
          request.log.warn({ err: error }, "Rejected a Stream webhook");
          return reply.code(401).type("application/problem+json").send({ title: "Unauthorized", status: 401 });
        }
        throw error;
      }

      log.record(event);
      request.log.info({ event: event.type, callCid: (event as { call_cid?: string }).call_cid }, "Stream webhook event");

      return reply.code(204).send();
    },
  );

  if (exposeRecentEvents) {
    app.withTypeProvider<ZodTypeProvider>().get(
      "/webhooks/recent",
      {
        schema: {
          operationId: "ListRecentWebhookEvents",
          tags: ["Webhooks"],
          summary: "List the webhook events received since startup",
          description: "Development only. Events are kept in memory and lost on restart.",
          response: { 200: recentEventsResponseSchema },
        },
      },
      async () => ({ events: log.recent() }),
    );
  }
}
