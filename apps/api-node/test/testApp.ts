import { buildApp, type AppServices } from "../src/app.ts";
import { loadConfig } from "../src/config.ts";
import type {
  CallDetails,
  StartCallInput,
  StartedCall,
  StreamCallService,
} from "../src/services/stream/streamCallService.ts";
import type { ListedUser, StreamUserService, UserToken } from "../src/services/stream/streamUserService.ts";
import {
  createWebhookEventLog,
  InvalidWebhookError,
  type StreamWebhookService,
} from "../src/services/stream/streamWebhookService.ts";
import type { WHEvent } from "@stream-io/node-sdk";

export const TEST_API_KEY = "test-api-key";
export const TEST_API_SECRET = "test-api-secret-that-is-long-enough-for-hmac-sha256";
export const WEB_ORIGIN = "http://localhost:3000";

/** Development config with test Stream credentials. `env` entries override the defaults. */
export function testConfig(env: Record<string, string | undefined> = {}) {
  return loadConfig({
    NODE_ENV: "development",
    STREAM_API_KEY: TEST_API_KEY,
    STREAM_API_SECRET: TEST_API_SECRET,
    ...env,
  });
}

export class FakeStreamUserService implements StreamUserService {
  static readonly issuedToken: UserToken = { token: "fake-token", expiresAt: new Date("2030-01-02T03:04:05Z") };

  readonly ensuredUsers: Array<[string, string | undefined, string | undefined]> = [];
  readonly tokensCreatedFor: string[] = [];
  ensureUserFailure: Error | undefined;

  async ensureUser(userId: string, name: string | undefined, image: string | undefined) {
    if (this.ensureUserFailure) throw this.ensureUserFailure;
    this.ensuredUsers.push([userId, name, image]);
  }

  readonly usersEnsuredToExist: string[][] = [];

  async ensureUsersExist(userIds: readonly string[]) {
    this.usersEnsuredToExist.push([...userIds]);
  }

  listedUsers: ListedUser[] = [{ userId: "alice", name: "Alice", image: undefined, role: "user", online: true }];
  readonly listUserLimits: number[] = [];

  async listUsers(limit: number) {
    this.listUserLimits.push(limit);
    return this.listedUsers;
  }

  createToken(userId: string) {
    this.tokensCreatedFor.push(userId);
    return FakeStreamUserService.issuedToken;
  }
}

export class FakeStreamCallService implements StreamCallService {
  static readonly startedCall: StartedCall = { callType: "default", callId: "fake-call", cid: "default:fake-call" };
  static readonly callDetails: CallDetails = {
    ...FakeStreamCallService.startedCall,
    kind: "audio",
    createdById: "alice",
    createdAt: "2030-01-02T03:04:05.000Z",
    endedAt: undefined,
    live: true,
    members: [{ userId: "alice", name: "Alice", image: undefined }],
    acceptedBy: ["bob"],
    rejectedBy: [],
    missedBy: [],
  };

  readonly startedCalls: StartCallInput[] = [];
  readonly readCalls: string[] = [];
  readonly endedCalls: string[] = [];
  startCallFailure: Error | undefined;
  getCallFailure: Error | undefined;
  endCallFailure: Error | undefined;

  async startCall(input: StartCallInput) {
    if (this.startCallFailure) throw this.startCallFailure;
    this.startedCalls.push(input);
    return FakeStreamCallService.startedCall;
  }

  async getCall(callId: string) {
    if (this.getCallFailure) throw this.getCallFailure;
    this.readCalls.push(callId);
    return FakeStreamCallService.callDetails;
  }

  async endCall(callId: string) {
    if (this.endCallFailure) throw this.endCallFailure;
    this.endedCalls.push(callId);
  }
}

export class FakeStreamWebhookService implements StreamWebhookService {
  /** Accepts exactly this signature, so tests don't need the HMAC. */
  static readonly validSignature = "valid-signature";

  event: WHEvent = { type: "call.ring", call_cid: "default:fake-call", user: { id: "alice" } } as unknown as WHEvent;
  readonly verified: Array<{ rawBody: string; signature: string }> = [];

  verifyAndParse(rawBody: Buffer, signature: string) {
    this.verified.push({ rawBody: rawBody.toString("utf8"), signature });
    if (signature !== FakeStreamWebhookService.validSignature) {
      throw new InvalidWebhookError("signature mismatch");
    }
    return this.event;
  }
}

export async function createTestApp(
  services: Partial<AppServices> = {},
  env: Record<string, string | undefined> = {},
) {
  return buildApp(testConfig(env), {
    users: services.users ?? new FakeStreamUserService(),
    calls: services.calls ?? new FakeStreamCallService(),
    webhooks: services.webhooks ?? new FakeStreamWebhookService(),
    webhookLog: services.webhookLog ?? createWebhookEventLog(),
  });
}
