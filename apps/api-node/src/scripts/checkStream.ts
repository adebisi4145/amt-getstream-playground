/**
 * Checks that the Stream app itself is set up for calling: the credentials work and the `default` call type
 * lets ordinary users start, join and be rung for calls. Read-only; it changes nothing in Stream.
 *   npm run check:stream
 */
import { existsSync } from "node:fs";
import { StreamClient } from "@stream-io/node-sdk";
import { ConfigError, loadConfig } from "../config.ts";
import { CALL_TYPE } from "../services/stream/streamCallService.ts";

/** What a user needs to place and take part in a video or audio call. */
const REQUIRED_CAPABILITIES = ["create-call", "read-call", "join-call", "send-audio", "send-video", "end-call"];

const results: Array<{ ok: boolean; text: string }> = [];
const check = (ok: boolean, text: string) => results.push({ ok, text });

if (existsSync(".env")) process.loadEnvFile(".env");

let config;
try {
  config = loadConfig();
} catch (error) {
  console.error(error instanceof ConfigError ? error.message : error);
  process.exit(1);
}

const client = new StreamClient(config.stream.apiKey, config.stream.apiSecret, { timeout: 15_000 });

const callType = await client.video.getCallType({ name: CALL_TYPE }).catch((error: unknown) => {
  const status = (error as { metadata?: { responseCode?: number } }).metadata?.responseCode;
  check(false, status === 401
    ? `Stream rejected the credentials. Check STREAM_API_SECRET for API key ${config.stream.apiKey}.`
    : `Could not read call type '${CALL_TYPE}': ${(error as Error).message}`);
  report();
});

check(true, `Credentials work (API key ${config.stream.apiKey})`);
const { ring, video, audio } = callType.settings;

check(ring.incoming_call_timeout_ms > 0, `Ringing: incoming timeout ${ring.incoming_call_timeout_ms}ms, auto-cancel ${ring.auto_cancel_timeout_ms}ms`);
check(video.enabled, `Video ${video.enabled ? "enabled" : "disabled"} (camera on by default: ${video.camera_default_on})`);
check(true, `Audio default device: ${audio.default_device}, mic on by default: ${audio.mic_default_on}`);

const userGrants = callType.grants.user ?? [];
const missing = REQUIRED_CAPABILITIES.filter((capability) => !userGrants.includes(capability));
check(missing.length === 0, missing.length === 0
  ? `The 'user' role can start and join calls`
  : `The 'user' role is missing: ${missing.join(", ")}`);

// GET /api/users lists every user, which needs an unfiltered query.
try {
  await client.queryUsers({ payload: { filter_conditions: {}, limit: 1 } });
  check(true, "Listing users works (GET /api/users)");
} catch (error) {
  check(false, `Listing users failed: ${(error as Error).message}`);
}

report();

function report(): never {
  console.log(`\nStream call type '${CALL_TYPE}':\n`);
  for (const { ok, text } of results) console.log(`  ${ok ? "✓" : "✗"} ${text}`);

  const failed = results.filter((result) => !result.ok);
  if (failed.length > 0) {
    console.log(`\n${failed.length} check(s) failed. Credentials live in apps/api-node/.env; call type settings are under Video & Audio > Call types in the Stream dashboard.`);
    process.exit(1);
  }

  console.log("\nAll checks passed. The Stream app is set up for voice and video calls.");
  process.exit(0);
}
