# API: Stream Video

How the web app works with `apps/api`, and why it works this way. Request and response shapes are in the OpenAPI document; browse them at http://localhost:5056/scalar while the API runs in Development.

**The split:** the API owns everything that needs the Stream secret — tokens, users, calls, recordings and webhooks. The web app uses `@stream-io/video-react-sdk` to join calls and show the UI. Live audio and video never pass through the API.

## Token flow

1. The browser calls `POST /api/tokens` with a `userId`, plus optional `name` and `image`.
2. The API makes sure the user exists in Stream and sets only the fields that were sent.
3. The API returns the Stream **API key**, the `userId`, a user **token** and its `expiresAt`.
4. The web app connects to Stream with those values. When the token nears expiry, the Stream SDK calls the `tokenProvider` again, which calls the same endpoint.

```ts
import { StreamVideoClient } from "@stream-io/video-react-sdk";

const API_URL = "http://localhost:5056";

async function fetchToken(userId: string) {
  const response = await fetch(`${API_URL}/api/tokens`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ userId }),
  });
  if (!response.ok) throw new Error(`Token request failed: ${response.status}`);
  return (await response.json()) as { apiKey: string; userId: string; token: string; expiresAt: string };
}

const { apiKey, token } = await fetchToken("alice");

const client = new StreamVideoClient({
  apiKey,
  user: { id: "alice", name: "Alice" },
  token,
  tokenProvider: async () => (await fetchToken("alice")).token,
});
```

## Rules the web app can rely on

- **The secret never reaches the client.** The Stream API secret stays in the API. Only the API key, which is public by design, and a user token are returned. The secret must never be added to `apps/web` or any `NEXT_PUBLIC_*` variable.
- **Refreshes are safe.** Calling the endpoint with only `userId` never changes an existing user. Sending `name` or `image` changes only those fields. Role and custom data set in the Stream dashboard are kept.
- **The client can't set a role.** The request has no `role` field, and new users get Stream's default `user` role. Letting the browser choose roles would let anyone make themselves an admin.
- **Development only.** The endpoint trusts whatever `userId` it's sent, so anyone who can reach it can get a token for any user. It's mapped only when `Tokens:AllowUntrustedRequests` is `true`, which is set in `appsettings.Development.json` only. Real authentication is needed before anything is deployed.
- **Errors are ProblemDetails.** See [Error responses](#error-responses).
- **`userId` format.** Letters, digits, `@`, `_` and `-`, up to 255 characters. This is our own conservative rule, because Stream doesn't publish its user ID limits. The same rule applies to call ids.

## Calls

The **server creates calls; the browser joins calls that already exist.** The API decides the call id, who the members are, and the settings.

1. The web app asks the API to create a call: `POST /api/calls` with `type`, `createdById`, optional `id` and optional `members`. A random id is generated when none is sent.
2. The API returns the call, including its `cid` (`type:id`).
3. The browser joins it with the Stream SDK:

```ts
const call = client.call("development", callId);
await call.join(); // not join({ create: true }): the API creates calls
```

4. Membership changes go through `POST /api/calls/{type}/{id}/members` with `add` and `remove`, and `POST /api/calls/{type}/{id}/end` ends it for everyone.

**Things to know**
- **This is a convention, not a lock.** The Stream client SDK can still create calls on its own if the call type's permissions allow it. Enforcing the split means removing `create-call` from the `user` role for that call type in the Stream dashboard. We haven't done that.
- **`createdById` comes from the request body,** which is acceptable only because there's no authentication yet, exactly like `userId` on the token endpoint. It moves to the authenticated identity when real auth arrives.
- **Allowed call types** come from configuration (`Calls:AllowedTypes`), defaulting to Stream's built-ins: `default`, `audio_room`, `livestream` and `development`. It's our allowlist, not Stream's, so a custom call type made in the dashboard just needs a config entry. `development` allows everything and is the easiest for testing.
- **Custom call settings aren't accepted from clients.** That's our scope decision for the playground; Stream itself does support settings overrides on create.

## Recordings

Recording is **server-controlled**: the browser asks the API, and the API talks to Stream.

- `POST /api/calls/{type}/{id}/recordings/start` and `/stop`
- `GET /api/calls/{type}/{id}/recordings`

**Things to know**
- **The recording type is always `composite`**, one mixed audio/video file. Clients can't choose it.
- **Recording needs an active session.** Starting a recording on a call nobody has joined returns `409`, with Stream's reason being "there is no active session". Join the call first.
- **Recordings appear after processing.** The list can be empty right after stopping, until Stream finishes the file.
- **Not covered by an automated test.** The recording lifecycle is asynchronous and depends on dashboard settings, so it's verified by hand rather than with a flaky test.

## Webhooks

`POST /api/webhooks/stream` receives Stream's events. Only Stream calls it, so it's outside the CORS policy and the web app has nothing to do here.

- **Verification:** the `X-Signature` header is checked against the raw body. A valid signature returns `204`, including for event types we don't handle yet, so Stream stops retrying. An invalid or missing signature returns `401`.
- **No response body, ever.** Responses never say why verification failed, and never contain the secret or the expected signature. Details go to the logs.
- **Handling:** events are currently logged only. Acting on them comes later.
- **Local delivery:** Stream can't reach `localhost`, so expose the API with a tunnel and paste the public URL into the Stream dashboard under the app's webhook settings:

```bash
devtunnel host -p 5056 --allow-anonymous   # or: ngrok http 5056
# webhook URL: https://<public-host>/api/webhooks/stream
```

## Consultations (patient → triage → doctor)

The workflow this playground exists for: a patient taps **Call now**, a triage agent picks it up from a board, and triage can pull a doctor into the live call.

**A consultation is a Stream call** whose custom data carries the queue state: `kind`, `status`, `patientId`, `modality`, `reason`, `assignedTo`, `requestedAt`, `acceptedAt`. There's no database.

**`modality` is `audio` or `video`**, chosen by the patient when they start the call and required on `POST /api/consultations`. It's stored server-side rather than kept in the browser so triage can see, from the board alone, whether the patient expects video before deciding how to join. Staff on the call can change it while the consultation runs — see [Switching between audio and video](#switching-between-audio-and-video).

| Step | Endpoint | What happens |
| --- | --- | --- |
| Patient calls | `POST /api/consultations` | Creates the call with `status: waiting` and the patient as its only member. The patient's browser joins and waits. |
| Dispatch board | `GET /api/consultations?status=waiting` | Triage sees who's waiting, with the reason and when they asked. |
| Triage picks up | `POST /api/consultations/{callId}/accept` | Adds the agent to the call, `status: accepted`. Their browser joins. |
| Bring in a doctor | `POST /api/consultations/{callId}/invite` | Adds the doctor as a member, then rings them. Triage stays with the patient. |
| Ring again | `POST /api/consultations/{callId}/ring` | Rings an already-invited doctor again, for when the first ring wasn't answered. |
| Switch modality | `POST /api/consultations/{callId}/modality` | Staff turn video on, or go back to audio, part-way through. |
| Finish | `POST /api/consultations/{callId}/complete` | `status: completed`, call ended. |
| Patient gives up | `POST /api/consultations/{callId}/cancel` | `status: cancelled`, call ended. |

**States.** `waiting → accepted → completed`, or `waiting → cancelled`. Anything else returns `409` and changes nothing: accepting a consultation someone already took, inviting a doctor before triage accepted, completing one that's still waiting, cancelling one that's already been accepted.

**`status` is authoritative, not `ended_at`.** Both a completed and a cancelled consultation have an ended Stream call, so only `status` distinguishes "the consultation happened" from "the patient gave up".

**Who may do what** (ids come from the request body; see the caveat below):

| Rule | Why |
| --- | --- |
| Only **triage** can accept | Doctors join by invite, not from the board. Patients can't accept at all. |
| Only a configured **doctor** can be invited | Invitations are for clinicians. |
| `/ring` only rings a doctor **already invited to that consultation** | Otherwise it becomes a way to ring any doctor about a call they aren't part of. |
| Only **staff on that consultation** can complete it | Triage often leaves after handing over, so the doctor must be able to close it. |
| Only **staff on that consultation** can change its modality | Asking to see something is a clinical decision. The patient still chooses whether to turn their own camera on. |
| Only the **owning patient** can cancel | One patient must not be able to cancel another's consultation. |

Anything else returns `403`.

**Two limits worth knowing**
- **Identity is faked.** Staff are a list in configuration (`Consultations:Staff`), and the API believes whatever id the request sends. The rules are real; the identity is not. Real authentication has to come before this shape is used for anything.
- **Accept has a race.** Checking the status and writing it are two calls to Stream, and Stream has no compare-and-set, so two agents accepting in the same instant can both succeed, with the second overwriting `assignedTo`. The `409` covers the everyday case ("someone already took it"), not the millisecond one. In the product, a database row or a lock closes it.

### Switching between audio and video

A consultation that began as audio often needs video part-way through: the patient describes a rash, and the clinician needs to look at it. `POST /api/consultations/{callId}/modality` with `{ "staffId": "triage-001", "modality": "video" }` switches it, and returns the updated consultation.

- **Staff on the call only**, and only while the consultation is `accepted`. Before that the modality is the patient's choice; after it ends the record shouldn't move. Asking for the modality it already has succeeds and changes nothing.
- **Both screens follow, without polling.** The modality lives in the call's custom data, so changing it makes Stream send `call.updated` to everyone in the call, and each client re-reads `custom.modality`. The web app reads it with `useCallCustomData()`.
- **Nobody's camera is turned on for them.** The clinician who asks for video enables their own camera; the patient's stays off until they choose. Switching back to audio does close every camera, because an audio consultation means cameras off.
- **The record moves with it.** `modality` is the consultation's current modality, not the one it started in, so the board and history show what it became. If you need "started as audio, escalated at 14:03", that's another custom field.

### Rejoining

There's no separate rejoin endpoint, for either side.

- **Triage** rejoins with `POST /api/consultations/{callId}/accept`. Accepting a consultation already assigned to you returns it unchanged, so the same call covers "take this" and "let me back in". Another agent's consultation still answers `409`, so a rejoin can't take one over by accident.
- **The patient** finds their consultation again with `GET /api/consultations?status=accepted&patientId=...`, filtered in Stream so one patient never receives another's.

Both rely on leaving a call not ending it: only `/complete`, `/cancel` and the stale sweeper end the Stream call.

**Recordings are deliberately not part of this.** A recorded consultation is patient data with consent and retention rules attached, and that deserves its own decision.

## Error responses

Every error is ProblemDetails (RFC 9457). Stream's own error text is never returned; it goes to our logs.

| Status | Meaning | What the web app should do |
| --- | --- | --- |
| `400` | Our validation rejected the request | Fix the request. Field errors are in `errors`. |
| `403` | The caller isn't allowed to do this | A permission rule, not a malformed request. See the consultation rules above. |
| `404` | The call or consultation doesn't exist | Create it, or check the id. |
| `409` | Stream rejected it for the current state of the call or user | Fix the situation, for example join the call before recording. |
| `429` | Stream is rate limiting this app | Back off and retry. |
| `502` | Stream is unreachable or failing | Retry later; nothing the caller can fix. |

The split matters: `409` means the caller can do something about it, while `502` means Stream is broken.

## SDK behavior notes (getstream-net 16.0.1)

These were checked against the SDK source at the 16.0.1 release commit (`9ce65bd`) and Stream's platform user docs. They explain why `StreamUserService.EnsureUserAsync` works the way it does. Re-check them when upgrading the SDK.

- **User updates.**
  - A full upsert (`UpdateUsersAsync`, `POST /api/v2/users`) replaces the user's existing data, so it would wipe role and custom data.
  - A partial update (`UpdateUsersPartialAsync`, `PATCH /api/v2/users`) sets only the given fields, but Stream doesn't document whether it creates missing users.
  - So the service looks the user up first (`QueryUsersAsync`). It creates missing users with a full upsert of only the sent fields, and partially updates existing users only when `name` or `image` was sent.
- **Known race condition.** Two simultaneous first requests for the same new user can both see "missing" and both upsert, so one `name` or `image` may overwrite the other. That's accepted for this playground.
- **Errors.** The SDK throws `GetStreamApiException` (with `StatusCode`) for API errors and `GetStreamTransportException` for network failures, both derived from `GetStreamException`. The services convert them to `StreamRequestFailedException`, and `StreamExceptionHandler` turns Stream's status into ours: 4xx becomes `409`, 429 passes through, everything else becomes `502`.
- **Calls.** `GetOrCreateCallAsync(type, id, request)` creates or returns a call; `UpdateCallMembersAsync` takes `UpdateMembers` and `RemoveMembers`; `EndCallAsync` ends it; `QueryCallsAsync` pages with `Next`. The update-members response carries members only, so the service reads the call back afterwards to return one consistent shape.
- **Recording.** `StartRecordingAsync(type, id, recordingType, request)` takes the recording type as a path segment. The SDK doesn't enumerate valid values and Stream doesn't publish them; `composite` was accepted (the call was refused for having no active session, not for the type).
- **Webhooks.** `StreamClient.VerifyAndParseWebhook(body, signature)` gunzips, verifies the HMAC and parses, throwing `Webhook.StreamInvalidWebhookException` for every failure mode. Event type constants live on `WebhookEventType`.
- **Validation gap worth remembering.** .NET's minimal API validation does not recurse into arrays of nested objects, so ids inside `members` are validated explicitly in the endpoints. Without that, an invalid id reached Stream.
- **Custom data (checked live on 2026-09-17).** Calls can be filtered by custom fields (`{"custom.status":"waiting"}`), which is what makes the consultation board possible without a database. Writing `custom` **merges** rather than replacing, so a status change leaves the patient's reason intact — the opposite of what we assumed before testing it.
- **Responses don't all carry members.** The update-call response has no member list, so the consultation service re-reads the call after assigning or adding a doctor. An integration test caught this.
- **Tokens.** `CreateUserToken(userId, lifetime)` sets `user_id`, `iat` and `nbf` (5 seconds in the past) and `exp` from the system clock. `expiresAt` is read back from the token's `exp` claim, so it always matches.
- **HTTP.** Each `StreamClient` builds its own connection pool, so the API registers a single instance.
- **Deleting users** is a background task. `DeleteUsersAsync` returns a task id to wait on with `WaitForTaskAsync`. The integration tests use `User = "hard"`.
