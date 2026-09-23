import { createHmac } from "node:crypto";
import { StreamClient } from "@stream-io/node-sdk";
import { afterEach, describe, expect, it } from "vitest";
import { createStreamWebhookService, createWebhookEventLog } from "../src/services/stream/streamWebhookService.ts";
import { createTestApp, FakeStreamWebhookService, TEST_API_KEY, TEST_API_SECRET } from "./testApp.ts";

type App = Awaited<ReturnType<typeof createTestApp>>;
let app: App | undefined;
afterEach(async () => {
  await app?.close();
  app = undefined;
});

const eventBody = JSON.stringify({ type: "call.ring", call_cid: "default:call-1", user: { id: "alice" } });

function postWebhook(target: App, body: string, headers: Record<string, string> = {}) {
  return target.inject({
    method: "POST",
    url: "/api/webhooks/stream",
    headers: { "content-type": "application/json", ...headers },
    payload: body,
  });
}

describe("POST /api/webhooks/stream", () => {
  it("accepts a correctly signed event and records it", async () => {
    const webhooks = new FakeStreamWebhookService();
    const webhookLog = createWebhookEventLog();
    app = await createTestApp({ webhooks, webhookLog });

    const response = await postWebhook(app, eventBody, { "x-signature": FakeStreamWebhookService.validSignature });

    expect(response.statusCode).toBe(204);
    // The exact bytes Stream signed, not a re-serialized object.
    expect(webhooks.verified).toEqual([{ rawBody: eventBody, signature: FakeStreamWebhookService.validSignature }]);
    expect(webhookLog.recent()).toEqual([
      { type: "call.ring", receivedAt: expect.any(String), callCid: "default:fake-call", userId: "alice" },
    ]);
  });

  it.each([
    ["a wrong signature", { "x-signature": "wrong" }],
    ["no signature", {}],
  ])("returns 401 and records nothing for %s", async (_case, headers) => {
    const webhookLog = createWebhookEventLog();
    app = await createTestApp({ webhooks: new FakeStreamWebhookService(), webhookLog });

    const response = await postWebhook(app, eventBody, headers);

    expect(response.statusCode).toBe(401);
    expect(response.headers["content-type"]).toMatch(/^application\/problem\+json/);
    expect(webhookLog.recent()).toEqual([]);
  });

  it("is on even when untrusted requests are not allowed", async () => {
    app = await createTestApp({}, { ALLOW_UNTRUSTED_REQUESTS: "false" });

    const response = await postWebhook(app, eventBody, { "x-signature": FakeStreamWebhookService.validSignature });

    expect(response.statusCode).toBe(204);
  });

  it("verifies a real Stream signature end to end", async () => {
    const client = new StreamClient(TEST_API_KEY, TEST_API_SECRET);
    const webhookLog = createWebhookEventLog();
    app = await createTestApp({ webhooks: createStreamWebhookService(client), webhookLog });
    const signature = createHmac("sha256", TEST_API_SECRET).update(eventBody).digest("hex");

    const accepted = await postWebhook(app, eventBody, { "x-signature": signature });
    const rejected = await postWebhook(app, eventBody, { "x-signature": signature.replace(/^./, "0") });

    expect(accepted.statusCode).toBe(204);
    expect(rejected.statusCode).toBe(401);
    expect(webhookLog.recent().map((event) => event.type)).toEqual(["call.ring"]);
  });
});

describe("GET /api/webhooks/recent", () => {
  it("lists recorded events in development, newest first", async () => {
    const webhookLog = createWebhookEventLog();
    app = await createTestApp({ webhooks: new FakeStreamWebhookService(), webhookLog });
    const webhooks = new FakeStreamWebhookService();
    webhooks.event = { type: "call.ended", call_cid: "default:call-2" } as never;
    webhookLog.record(webhooks.event);
    await postWebhook(app, eventBody, { "x-signature": FakeStreamWebhookService.validSignature });

    const response = await app.inject({ method: "GET", url: "/api/webhooks/recent" });

    expect(response.statusCode).toBe(200);
    expect(response.json().events.map((event: { type: string }) => event.type)).toEqual(["call.ring", "call.ended"]);
  });

  it("is not exposed outside development", async () => {
    app = await createTestApp({}, { NODE_ENV: "production" });

    const response = await app.inject({ method: "GET", url: "/api/webhooks/recent" });

    expect(response.statusCode).toBe(404);
  });
});

describe("GET /health", () => {
  it("answers without touching Stream", async () => {
    app = await createTestApp({}, { NODE_ENV: "production" });

    const response = await app.inject({ method: "GET", url: "/health" });

    expect(response.statusCode).toBe(200);
    expect(response.json()).toEqual({ status: "ok" });
  });
});
