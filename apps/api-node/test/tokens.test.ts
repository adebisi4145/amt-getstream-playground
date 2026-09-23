import { afterEach, describe, expect, it } from "vitest";
import { StreamRequestFailedError } from "../src/services/stream/streamRequestFailedError.ts";
import { createTestApp, FakeStreamUserService, TEST_API_KEY, WEB_ORIGIN } from "./testApp.ts";

type App = Awaited<ReturnType<typeof createTestApp>>;
let app: App | undefined;
afterEach(async () => {
  await app?.close();
  app = undefined;
});

function postToken(target: App, body: string, headers: Record<string, string> = {}) {
  return target.inject({
    method: "POST",
    url: "/api/tokens",
    headers: { "content-type": "application/json", ...headers },
    payload: body,
  });
}

describe("POST /api/tokens", () => {
  it.each([
    ["alice", undefined, undefined],
    ["alice", "Alice", undefined],
    ["alice", "Alice", "https://example.com/alice.png"],
    ["bob@example_1-2", undefined, "http://example.com/bob.png"],
  ])("ensures user %s with exactly the fields sent and returns a token", async (userId, name, image) => {
    const users = new FakeStreamUserService();
    app = await createTestApp({ users });

    const response = await postToken(app, JSON.stringify({ userId, name, image }));

    expect(response.statusCode).toBe(200);
    expect(users.ensuredUsers).toEqual([[userId, name, image]]);
    expect(response.json()).toEqual({
      apiKey: TEST_API_KEY,
      userId,
      token: FakeStreamUserService.issuedToken.token,
      expiresAt: FakeStreamUserService.issuedToken.expiresAt.toISOString(),
    });
  });

  it("treats null fields as not sent and ignores a role", async () => {
    const users = new FakeStreamUserService();
    app = await createTestApp({ users });

    const response = await postToken(app, JSON.stringify({ userId: "alice", name: null, image: null, role: "admin" }));

    expect(response.statusCode).toBe(200);
    expect(users.ensuredUsers).toEqual([["alice", undefined, undefined]]);
  });

  it.each([
    "{}",
    '{ "userId": "" }',
    '{ "userId": "bad id!" }',
    '{ "userId": "alice/../bob" }',
    `{ "userId": "${"a".repeat(256)}" }`,
    '{ "userId": "alice", "image": "/relative/alice.png" }',
    '{ "userId": "alice", "image": "ftp://example.com/alice.png" }',
    '{ "userId": "alice", "image": "" }',
    '{ "userId": ',
  ])("returns 400 and does not call Stream for %s", async (body) => {
    const users = new FakeStreamUserService();
    app = await createTestApp({ users });

    const response = await postToken(app, body);

    expect(response.statusCode).toBe(400);
    expect(response.headers["content-type"]).toMatch(/^application\/problem\+json/);
    expect(users.ensuredUsers).toEqual([]);
    expect(users.tokensCreatedFor).toEqual([]);
  });

  it("returns 502 without the token or Stream details when Stream fails", async () => {
    const streamDetail = "stream-internal-detail-that-must-not-leak";
    const users = new FakeStreamUserService();
    users.ensureUserFailure = new StreamRequestFailedError("ensureUser", 500, new Error(streamDetail));
    app = await createTestApp({ users });

    const response = await postToken(app, '{ "userId": "alice" }');

    expect(response.statusCode).toBe(502);
    expect(response.headers["content-type"]).toMatch(/^application\/problem\+json/);
    expect(response.body).not.toContain(FakeStreamUserService.issuedToken.token);
    expect(response.body).not.toContain(streamDetail);
  });

  it("is not mapped when untrusted requests are not allowed", async () => {
    const users = new FakeStreamUserService();
    app = await createTestApp({ users }, { ALLOW_UNTRUSTED_REQUESTS: "false" });

    const response = await postToken(app, '{ "userId": "alice" }');

    expect(response.statusCode).toBe(404);
    expect(users.ensuredUsers).toEqual([]);
  });

  it("is not mapped outside development by default", async () => {
    app = await createTestApp({}, { NODE_ENV: "production" });

    const response = await postToken(app, '{ "userId": "alice" }');

    expect(response.statusCode).toBe(404);
  });

  it("keeps CORS headers for the web origin on validation errors", async () => {
    app = await createTestApp();

    const response = await postToken(app, '{ "userId": "bad id!" }', { origin: WEB_ORIGIN });

    expect(response.statusCode).toBe(400);
    expect(response.headers["access-control-allow-origin"]).toBe(WEB_ORIGIN);
  });

  it("keeps CORS headers for the web origin on Stream failures", async () => {
    const users = new FakeStreamUserService();
    users.ensureUserFailure = new StreamRequestFailedError("ensureUser", 500, new Error("boom"));
    app = await createTestApp({ users });

    const response = await postToken(app, '{ "userId": "alice" }', { origin: WEB_ORIGIN });

    expect(response.statusCode).toBe(502);
    expect(response.headers["access-control-allow-origin"]).toBe(WEB_ORIGIN);
  });

  it("does not allow other origins", async () => {
    app = await createTestApp();

    const response = await postToken(app, '{ "userId": "alice" }', { origin: "https://evil.example" });

    expect(response.headers["access-control-allow-origin"]).toBeUndefined();
  });

  it("answers the CORS preflight for the web origin", async () => {
    app = await createTestApp();

    const response = await app.inject({
      method: "OPTIONS",
      url: "/api/tokens",
      headers: { origin: WEB_ORIGIN, "access-control-request-method": "POST", "access-control-request-headers": "content-type" },
    });

    expect(response.statusCode).toBe(204);
    expect(response.headers["access-control-allow-origin"]).toBe(WEB_ORIGIN);
  });
});
