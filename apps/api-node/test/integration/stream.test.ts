import { createHmac, randomUUID } from "node:crypto";
import { existsSync } from "node:fs";
import { StreamClient } from "@stream-io/node-sdk";
import { afterEach, beforeAll, describe, expect, it } from "vitest";
import { loadConfig } from "../../src/config.ts";
import { createStreamCallService } from "../../src/services/stream/streamCallService.ts";
import { StreamRequestFailedError } from "../../src/services/stream/streamRequestFailedError.ts";
import { createStreamUserService } from "../../src/services/stream/streamUserService.ts";
import { createStreamWebhookService } from "../../src/services/stream/streamWebhookService.ts";

/**
 * Runs the user-update rules and call creation against the real Stream app.
 * Opt-in: skipped unless STREAM_API_SECRET is set (in apps/api-node/.env or the environment).
 * Each test uses unique it-<uuid> users and hard-deletes them (and their calls) afterwards.
 * Run before any commit that changes src/services/stream or upgrades @stream-io/node-sdk:
 *   npm run test:api-node:integration
 */
if (existsSync(".env")) process.loadEnvFile(".env");
const hasSecret = Boolean(process.env.STREAM_API_SECRET);

describe.skipIf(!hasSecret)("Stream integration", () => {
  let client: StreamClient;
  let createdUserIds: string[] = [];

  beforeAll(() => {
    const config = loadConfig({ ...process.env, NODE_ENV: "development" });
    client = new StreamClient(config.stream.apiKey, config.stream.apiSecret, { timeout: 15_000 });
  });

  afterEach(async () => {
    if (createdUserIds.length === 0) return;
    const response = await client.deleteUsers({ user_ids: createdUserIds, user: "hard", calls: "hard" });
    createdUserIds = [];

    // Deletion runs as a Stream background task.
    const deadline = Date.now() + 60_000;
    while (Date.now() < deadline) {
      const task = await client.getTask({ id: response.task_id });
      if (task.status === "completed" || task.status === "failed") return;
      await new Promise((resolve) => setTimeout(resolve, 1000));
    }
  });

  function newUserId() {
    const id = `it-${randomUUID().replaceAll("-", "")}`;
    createdUserIds.push(id);
    return id;
  }

  const users = () => createStreamUserService(client, 3600);

  async function getUser(id: string) {
    const response = await client.queryUsers({ payload: { filter_conditions: { id } } });
    return response.users[0];
  }

  /** Arrange directly with the SDK: role and custom data as if set in the dashboard. */
  async function createUser(id: string) {
    await client.updateUsers({
      users: { [id]: { id, name: "Alice", image: "https://example.com/alice.png", role: "admin", custom: { team: "blue" } } },
    });
  }

  it("creates a new user with the default role", async () => {
    const id = newUserId();

    await users().ensureUser(id, undefined, undefined);

    expect(await getUser(id)).toMatchObject({ id, role: "user" });
  });

  it("leaves an existing user unchanged when only the userId is sent", async () => {
    const id = newUserId();
    await createUser(id);

    await users().ensureUser(id, undefined, undefined);

    expect(await getUser(id)).toMatchObject({
      name: "Alice",
      image: "https://example.com/alice.png",
      role: "admin",
      custom: { team: "blue" },
    });
  });

  it("changes only the sent fields on an existing user", async () => {
    const id = newUserId();
    await createUser(id);

    await users().ensureUser(id, "Alice B", undefined);

    expect(await getUser(id)).toMatchObject({
      name: "Alice B",
      image: "https://example.com/alice.png",
      role: "admin",
      custom: { team: "blue" },
    });
  });

  it.each(["video", "audio"] as const)("starts a %s call with missing members created and the right camera default", async (kind) => {
    const caller = newUserId();
    const callee = newUserId();
    await users().ensureUser(caller, "Caller", undefined);

    const started = await createStreamCallService(client, users()).startCall({
      createdById: caller,
      memberIds: [callee],
      kind,
      // Nobody is connected to receive a ring; creating the call is what's under test.
      ring: false,
    });

    expect(await getUser(callee)).toMatchObject({ id: callee, role: "user" });

    const call = client.video.call(started.callType, started.callId);
    const { call: details, members } = await call.get();
    expect(details.created_by.id).toBe(caller);
    expect(details.custom).toEqual({ kind });
    expect(members.map((member) => member.user_id).sort()).toEqual([caller, callee].sort());
    if (kind === "audio") {
      expect(details.settings.video.camera_default_on).toBe(false);
    }
    expect(details.settings.ring.incoming_call_timeout_ms).toBeGreaterThan(0);

    await call.delete({ hard: true });
  });

  it("lists users, reads a call's state and ends it", async () => {
    const caller = newUserId();
    const callee = newUserId();
    const service = createStreamCallService(client, users());
    const started = await service.startCall({ createdById: caller, memberIds: [callee], kind: "video", ring: false });

    // GET /api/users needs an unfiltered query to work.
    const listed = await users().listUsers(100);
    expect(listed.some((user) => user.userId === caller)).toBe(true);

    const details = await service.getCall(started.callId);
    expect(details).toMatchObject({ callId: started.callId, kind: "video", createdById: caller, live: false });
    expect(details.members.map((member) => member.userId).sort()).toEqual([caller, callee].sort());

    await service.endCall(started.callId);
    expect((await service.getCall(started.callId)).endedAt).toBeDefined();

    await client.video.call(started.callType, started.callId).delete({ hard: true });
  });

  it("reports a missing call as a not-found Stream failure", async () => {
    const service = createStreamCallService(client, users());

    const failure = await service.getCall(randomUUID()).catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(StreamRequestFailedError);
    expect(failure).toMatchObject({ streamStatusCode: 404 });
  });

  it("verifies a webhook signed with the app secret", () => {
    const body = JSON.stringify({ type: "call.ring", call_cid: "default:example" });
    const signature = createHmac("sha256", process.env.STREAM_API_SECRET!).update(body).digest("hex");

    const event = createStreamWebhookService(client).verifyAndParse(Buffer.from(body), signature);

    expect(event.type).toBe("call.ring");
    expect(() => createStreamWebhookService(client).verifyAndParse(Buffer.from(body), "0".repeat(64))).toThrow();
  });
});
