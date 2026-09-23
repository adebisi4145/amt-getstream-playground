import { afterEach, describe, expect, it } from "vitest";
import { StreamRequestFailedError } from "../src/services/stream/streamRequestFailedError.ts";
import { createTestApp, FakeStreamCallService } from "./testApp.ts";

type App = Awaited<ReturnType<typeof createTestApp>>;
let app: App | undefined;
afterEach(async () => {
  await app?.close();
  app = undefined;
});

function postCall(target: App, body: unknown) {
  return target.inject({ method: "POST", url: "/api/calls", payload: body as object });
}

describe("POST /api/calls", () => {
  it("starts a ringing video call by default", async () => {
    const calls = new FakeStreamCallService();
    app = await createTestApp({ calls });

    const response = await postCall(app, { createdById: "alice", memberIds: ["bob"] });

    expect(response.statusCode).toBe(201);
    expect(response.json()).toEqual(FakeStreamCallService.startedCall);
    expect(calls.startedCalls).toEqual([{ createdById: "alice", memberIds: ["bob"], kind: "video", ring: true }]);
  });

  it("passes kind and ring through", async () => {
    const calls = new FakeStreamCallService();
    app = await createTestApp({ calls });

    const response = await postCall(app, { createdById: "alice", memberIds: ["bob", "carol"], kind: "audio", ring: false });

    expect(response.statusCode).toBe(201);
    expect(calls.startedCalls).toEqual([{ createdById: "alice", memberIds: ["bob", "carol"], kind: "audio", ring: false }]);
  });

  it.each([
    [{}],
    [{ createdById: "alice" }],
    [{ createdById: "alice", memberIds: [] }],
    [{ createdById: "alice", memberIds: ["alice"] }],
    [{ createdById: "bad id!", memberIds: ["bob"] }],
    [{ createdById: "alice", memberIds: ["bad id!"] }],
    [{ createdById: "alice", memberIds: ["bob"], kind: "screen" }],
    [{ createdById: "alice", memberIds: Array.from({ length: 11 }, (_, i) => `user${i}`) }],
  ])("returns 400 and does not call Stream for %j", async (body) => {
    const calls = new FakeStreamCallService();
    app = await createTestApp({ calls });

    const response = await postCall(app, body);

    expect(response.statusCode).toBe(400);
    expect(response.headers["content-type"]).toMatch(/^application\/problem\+json/);
    expect(calls.startedCalls).toEqual([]);
  });

  it("returns 502 without Stream details when Stream fails", async () => {
    const calls = new FakeStreamCallService();
    calls.startCallFailure = new StreamRequestFailedError("startCall", 400, new Error("stream-detail"));
    app = await createTestApp({ calls });

    const response = await postCall(app, { createdById: "alice", memberIds: ["bob"] });

    expect(response.statusCode).toBe(502);
    expect(response.body).not.toContain("stream-detail");
  });

  it("is not mapped when untrusted requests are not allowed", async () => {
    const calls = new FakeStreamCallService();
    app = await createTestApp({ calls }, { ALLOW_UNTRUSTED_REQUESTS: "false" });

    const response = await postCall(app, { createdById: "alice", memberIds: ["bob"] });

    expect(response.statusCode).toBe(404);
    expect(calls.startedCalls).toEqual([]);
  });
});

describe("GET /api/calls/:callId", () => {
  it("returns the call state", async () => {
    const calls = new FakeStreamCallService();
    app = await createTestApp({ calls });

    const response = await app.inject({ method: "GET", url: "/api/calls/call-1" });

    expect(response.statusCode).toBe(200);
    expect(calls.readCalls).toEqual(["call-1"]);
    expect(response.json()).toEqual(FakeStreamCallService.callDetails);
  });

  it("returns 404 when Stream doesn't know the call", async () => {
    const calls = new FakeStreamCallService();
    calls.getCallFailure = new StreamRequestFailedError("getCall", 404, new Error("Can't find call"));
    app = await createTestApp({ calls });

    const response = await app.inject({ method: "GET", url: "/api/calls/missing" });

    expect(response.statusCode).toBe(404);
    expect(response.headers["content-type"]).toMatch(/^application\/problem\+json/);
    expect(response.body).not.toContain("Can't find call");
  });

  it("returns 400 for a call id Stream would reject", async () => {
    const calls = new FakeStreamCallService();
    app = await createTestApp({ calls });

    const response = await app.inject({ method: "GET", url: "/api/calls/bad%20id" });

    expect(response.statusCode).toBe(400);
    expect(calls.readCalls).toEqual([]);
  });
});

describe("POST /api/calls/:callId/end", () => {
  it("ends the call", async () => {
    const calls = new FakeStreamCallService();
    app = await createTestApp({ calls });

    const response = await app.inject({ method: "POST", url: "/api/calls/call-1/end" });

    expect(response.statusCode).toBe(204);
    expect(response.body).toBe("");
    expect(calls.endedCalls).toEqual(["call-1"]);
  });

  it("returns 404 when Stream doesn't know the call", async () => {
    const calls = new FakeStreamCallService();
    calls.endCallFailure = new StreamRequestFailedError("endCall", 404, new Error("nope"));
    app = await createTestApp({ calls });

    const response = await app.inject({ method: "POST", url: "/api/calls/missing/end" });

    expect(response.statusCode).toBe(404);
  });

  it("is not mapped when untrusted requests are not allowed", async () => {
    const calls = new FakeStreamCallService();
    app = await createTestApp({ calls }, { ALLOW_UNTRUSTED_REQUESTS: "false" });

    const response = await app.inject({ method: "POST", url: "/api/calls/call-1/end" });

    expect(response.statusCode).toBe(404);
    expect(calls.endedCalls).toEqual([]);
  });
});
