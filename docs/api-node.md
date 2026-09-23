# API (Node): Stream tokens and calls

`apps/api-node` is a Node.js port of the ASP.NET Core API in `apps/api`. It issues the same Stream Video tokens, follows the same rules and returns the same error shapes, so the web app can use either one. It also has the calling extras the C# API doesn't have yet: starting ringing calls, reading and ending them, listing users to call, and a webhook receiver for call events.

Request and response shapes are in the OpenAPI document. Browse them at http://localhost:5057/scalar while the API runs in development.

| | C# API (`apps/api`) | Node API (`apps/api-node`) |
| --- | --- | --- |
| URL | http://localhost:5056 | http://localhost:5057 |
| Run | `npm run dev:api` | `npm run dev:api-node` |
| Stack | ASP.NET Core, `getstream-net` 16.0.1 | Fastify 5, Zod 4, `@stream-io/node-sdk` 0.8.6 |
| `POST /api/tokens` | ✓ | ✓ (same request, response and rules) |
| Call, user and webhook routes | — | ✓ |

## Setup

```bash
npm install                                   # from the repo root
cp apps/api-node/.env.example apps/api-node/.env
# set STREAM_API_SECRET in apps/api-node/.env (it's git-ignored)
npm run dev:api-node
npm run check:stream   # confirms the Stream app itself is set up for calling
```

| Variable | Default | Notes |
| --- | --- | --- |
| `STREAM_API_SECRET` | none, **required** | From the Stream dashboard. Put it in `.env` or the environment, never in a committed file. The API won't start without it. |
| `STREAM_API_KEY` | `s3c4w9uuvs5k` | Public by design. |
| `STREAM_TOKEN_LIFETIME_SECONDS` | `3600` | From 60 to 86400. |
| `PORT` | `5057` | |
| `CORS_ALLOWED_ORIGINS` | `http://localhost:3000` in development, none otherwise | Comma-separated. |
| `ALLOW_UNTRUSTED_REQUESTS` | `true` in development, `false` otherwise | Turns on both endpoints. Both trust the user ids they're sent. |

`npm run dev:api-node` sets `NODE_ENV=development`. That turns on the development defaults and the `/scalar` and `/openapi/v1.json` routes.

## What calls need, and what's here

Stream Video runs the call itself. A backend has to do exactly two things, and both are done:

1. **Issue user tokens** (`POST /api/tokens`), so a client can connect as a user.
2. **Make sure the users exist** before they're added to a call, which both `POST /api/tokens` and `POST /api/calls` do.

Everything else in this API is convenience or observability, not a requirement: clients can create, join, accept, decline and end calls on their own.

Two things sit outside the code and are worth checking once, with `npm run check:stream`:

- The `default` call type must have **ringing timeouts** and let the **`user` role** create, read, join and end calls, and send audio and video. That's Stream's default; the script tells you if this app differs.
- **Webhooks are optional.** Stream only sends them once you set a webhook URL in the dashboard.

## Endpoints

- **`POST /api/tokens`** takes `{ userId, name?, image? }` and returns `{ apiKey, userId, token, expiresAt }`. It follows the rules in [api.md](api.md): the secret never leaves the server, a refresh with only `userId` changes nothing, only the fields sent are updated, the client can't set a role, and `userId` uses the same format.
- **`POST /api/calls`** takes `{ createdById, memberIds, kind?: "video" | "audio", ring?: boolean }` and returns `201 { callType, callId, cid }`.
  - It creates a call of type `default` whose members are the creator plus `memberIds` (at most 10).
  - Stream rejects members who don't exist, so any missing users are created first, with only their id. Existing users aren't changed.
  - `ring` defaults to `true`. It sends an incoming-call event to members who are connected.
  - `kind: "audio"` sets `video: false`, which is what ring and push notifications show, and stores `custom.kind`. Keeping the camera off is the client's job, the way `apps/web`'s `CallScreen` does it, and the same way the .NET API treats `modality`.
- **`GET /api/calls/{callId}`** returns the call's members, `kind`, whether anyone is in it (`live`), `endedAt`, and who accepted, rejected or missed it. It's the server-side view of what happened to a ring.
- **`POST /api/calls/{callId}/end`** ends the call for everyone and stops it ringing.
- **`GET /api/users?limit=50`** lists the app's users, newest first, so a client can show who there is to call. Development only: it returns everyone, with no filtering by who is asking.
- **`POST /api/webhooks/stream`** receives Stream's call events. It's authenticated by Stream's `x-signature` HMAC header rather than the development flag, so it stays on in every environment, and a bad or missing signature gets `401`. Received events are logged and the last 50 are kept in memory; in development `GET /api/webhooks/recent` lists them.
- **`GET /health`** answers `{ "status": "ok" }` without touching Stream.
- **Unknown calls and users** return `404`, because Stream said the resource doesn't exist. Other Stream failures are `502`.
- **Errors** are ProblemDetails (`application/problem+json`). Invalid input returns `400` with field errors under `errors`. A failure talking to Stream returns `502` with a generic message, and Stream's details are only logged.

## Making voice and video calls from the web app

Stream Video does the calling itself: signaling, media servers, TURN and ringing. The backend only has to issue tokens, and optionally start calls. Everything else happens in the client with `@stream-io/video-react-sdk`.

```ts
import { StreamVideoClient } from "@stream-io/video-react-sdk";

const API_URL = "http://localhost:5057";

async function fetchToken(userId: string) {
  const response = await fetch(`${API_URL}/api/tokens`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ userId }),
  });
  if (!response.ok) throw new Error(`Token request failed: ${response.status}`);
  return (await response.json()) as { apiKey: string; token: string };
}

const { apiKey } = await fetchToken("alice");
const client = StreamVideoClient.getOrCreateInstance({
  apiKey,
  user: { id: "alice" },
  tokenProvider: async () => (await fetchToken("alice")).token,
});
```

**Start a call.** Do one of the following:

- Ring from the client, without calling the backend:

  ```ts
  const call = client.call("default", crypto.randomUUID());
  await call.getOrCreate({
    ring: true,
    video: false, // audio call; use true for video
    data: { members: [{ user_id: "alice" }, { user_id: "bob" }] },
  });
  ```

  This only works if every member already exists in Stream, which means they've fetched a token at least once.

- Ring from the backend, which creates missing members:

  ```ts
  const { callType, callId } = await (
    await fetch(`${API_URL}/api/calls`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ createdById: "alice", memberIds: ["bob"], kind: "audio" }),
    })
  ).json();
  const call = client.call(callType, callId);
  ```

  The caller also receives the ring event. On the caller's side, `call.isCreatedByMe` is `true`.

**Receive a call.** Wrap the app in `<StreamVideo client={client}>`. `useCalls()` returns calls whose `state.callingState` is `CallingState.RINGING`.
- For an incoming call (`!call.isCreatedByMe`), call `call.join()` to accept or `call.leave({ reject: true, reason: "decline" })` to decline.
- For an outgoing call, `call.leave({ reject: true, reason: "cancel" })` cancels it.
- The `RingingCall` component gives you a ready-made UI for both.

**Audio compared with video.** Both use the `default` call type, with the same settings. For an audio call, read `custom.kind` (`GET /api/calls/{callId}` returns it as `kind`), call `call.camera.disable()` before `call.join()`, and render participants without video tiles. Because the call's own settings are untouched, either side can still turn the camera on to escalate to video. For a video call, use `SpeakerLayout` or `PaginatedGrid` inside `<StreamCall call={call}>`.

**Things that get in the way.**
- Browsers only allow camera and microphone access on `https` or `localhost`. To test on a phone, use a tunnel.
- A ring reaches only clients that are connected. A web app has no push notifications.
- Test with two different user ids, in two browsers or in a normal and a private window.

## Watching calls with webhooks

Webhooks are how the backend learns what happened: `call.ring`, `call.accepted`, `call.rejected`, `call.missed`, `call.session_started`, `call.ended`. Nothing here needs them, but they're the hook for anything a call should trigger later, like missed-call records or notifications.

Stream has to be able to reach the route, so in local development put a tunnel in front of it:

```bash
npm run dev:api-node
npx untun@latest tunnel http://localhost:5057   # or ngrok, cloudflared...
```

Set the resulting `https://.../api/webhooks/stream` as the webhook URL in the Stream dashboard (Video & Audio > Webhooks), place a call, then:

```bash
curl http://localhost:5057/api/webhooks/recent
```

The signature is checked against the **raw** request bytes, so that route parses the body as a buffer. Don't add middleware that re-serializes it.

## Tests

```bash
npm run test:api-node               # offline tests; integration tests are skipped without a secret
npm run test:api-node:integration   # only the tests that call the real Stream app
npm run typecheck:api-node
```

- **Offline tests** use fake services and Fastify's `inject`. They cover the same cases as the C# endpoint tests, plus CORS preflight, the calls endpoint, token lifetime and expiry, and the call payload sent to the SDK.
- **Integration tests** read `STREAM_API_SECRET` from `apps/api-node/.env` or the environment. They check the user-update rules, create a real video call and a real audio call, read and end a call, list users, and verify a real webhook signature. Each test uses `it-<uuid>` users and hard-deletes them and their calls. If a run is killed, delete leftover `it-` users in the Stream dashboard.
- **`npm run check:stream`** is not a test. It reads this Stream app's `default` call type and reports whether ringing, video and the `user` role's permissions are set up for calling. Run it when calls fail in a way the code can't explain.

## SDK behavior notes (`@stream-io/node-sdk` 0.8.6)

These were checked against the published package. Re-check them when you upgrade the SDK.

- **Users.** The SDK has the same full upsert (`updateUsers`, which replaces user data) and partial update (`updateUsersPartial`) as the .NET SDK. So `ensureUser` looks the user up first, the same way the C# `EnsureUserAsync` does, and has the same accepted race condition.
- **Errors.** Every failed request (API error, timeout or network failure) throws the SDK's `StreamError`. `metadata.responseCode` holds the HTTP status and `code` holds Stream's error code. The class isn't exported, so `callStream` treats any rejection from an SDK call as a Stream failure and converts it to `StreamRequestFailedError`, which becomes a `502`.
- **Timeout.** Requests time out after 3 seconds by default (`new StreamClient(key, secret, { timeout })`).
- **Tokens.** `generateUserToken({ user_id, validity_in_seconds })` sets `iat` 1 second in the past and `exp = iat + validity`. It doesn't set `nbf`. `expiresAt` is read back from the token's `exp` claim.
- **Calls.** `client.video.call(type, id).getOrCreate({ ring, video, data })` creates the call. `data.members` must reference existing users. The same handle has `get()`, `end()` and `delete({ hard })`.
- **`settings_override` is all or nothing.** Checked against real Stream on 2026-09-23: sending only `video.camera_default_on` is rejected with `400` ("target_resolution.width must be 240 or greater"), because the whole `video` block is validated. Sending the block in full then silently sets `enabled: false` for any boolean left out, which disables video for the call. So this API doesn't override call settings at all; the kind lives in `custom`.
- **Webhooks.** `client.verifyAndParseWebhook(rawBody, signature)` checks the HMAC and returns a typed event, handling gzipped bodies. It throws `InvalidWebhookError`, which is exported, unlike `StreamError`.
