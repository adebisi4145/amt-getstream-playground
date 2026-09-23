import { StreamClient } from "@stream-io/node-sdk";
import { describe, expect, it } from "vitest";
import { ConfigError, loadConfig } from "../src/config.ts";
import { createStreamCallService } from "../src/services/stream/streamCallService.ts";
import { callStream, StreamRequestFailedError } from "../src/services/stream/streamRequestFailedError.ts";
import { createStreamUserService } from "../src/services/stream/streamUserService.ts";
import { FakeStreamUserService, TEST_API_KEY, TEST_API_SECRET } from "./testApp.ts";

function decodePayload(token: string) {
  return JSON.parse(Buffer.from(token.split(".")[1]!, "base64url").toString("utf8")) as Record<string, unknown>;
}

/**
 * Checks our wiring around the SDK (configured lifetime, user id, reported expiry, call payload),
 * not how Stream builds its JWTs. No network calls are made.
 */
describe("Stream services", () => {
  it("createToken uses the requested user and configured lifetime, and reports the real expiry", () => {
    const lifetime = 15 * 60;
    const users = createStreamUserService(new StreamClient(TEST_API_KEY, TEST_API_SECRET), lifetime);

    const result = users.createToken("alice");

    const payload = decodePayload(result.token);
    expect(payload.user_id).toBe("alice");
    expect(result.expiresAt.getTime()).toBe((payload.exp as number) * 1000);
    expect((payload.exp as number) - (payload.iat as number)).toBe(lifetime);
  });

  it("startCall creates a call with the creator as a member and an audio call with the camera off", async () => {
    const client = new StreamClient(TEST_API_KEY, TEST_API_SECRET);
    const requests: Array<{ type: string; id: string; body: unknown }> = [];
    client.video.getOrCreateCall = async (request) => {
      const { type, id, ...body } = request;
      requests.push({ type, id, body });
      return {} as never;
    };
    const users = new FakeStreamUserService();

    const call = await createStreamCallService(client, users).startCall({
      createdById: "alice",
      memberIds: ["bob", "alice", "bob"],
      kind: "audio",
      ring: true,
    });

    expect(users.usersEnsuredToExist).toEqual([["alice", "bob"]]);
    expect(call).toEqual({ callType: "default", callId: requests[0]!.id, cid: `default:${requests[0]!.id}` });
    expect(requests[0]!.body).toEqual({
      ring: true,
      video: false,
      data: {
        created_by_id: "alice",
        members: [{ user_id: "alice" }, { user_id: "bob" }],
        video: false,
        custom: { kind: "audio" },
        settings_override: { video: { camera_default_on: false } },
      },
    });
  });

  it("callStream converts SDK failures and keeps Stream's HTTP status", async () => {
    const sdkError = Object.assign(new Error("Stream error code 5: invalid token"), { metadata: { responseCode: 401 } });

    const failure = await callStream("op", () => Promise.reject(sdkError)).catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(StreamRequestFailedError);
    expect(failure).toMatchObject({ operation: "op", streamStatusCode: 401, cause: sdkError });
  });

  it("config fails fast without the API secret and bounds the token lifetime", () => {
    expect(() => loadConfig({})).toThrow(ConfigError);
    expect(() => loadConfig({})).toThrow(/STREAM_API_SECRET/);
    expect(() => loadConfig({ STREAM_API_SECRET: "x", STREAM_TOKEN_LIFETIME_SECONDS: "59" })).toThrow(ConfigError);
    expect(loadConfig({ STREAM_API_SECRET: "x" })).toMatchObject({
      allowUntrustedRequests: false,
      cors: { allowedOrigins: [] },
      stream: { tokenLifetimeSeconds: 3600 },
    });
  });
});
