# API: Stream tokens

How the web app gets a Stream Video token from `apps/api`, and why it works the way it does. Request and response shapes are in the OpenAPI document; browse them at http://localhost:5056/scalar while the API runs in Development.

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
- **Errors are ProblemDetails.** Invalid input returns `400` with field errors. A failure talking to Stream returns `502` with a generic message.
- **`userId` format.** Letters, digits, `@`, `_` and `-`, up to 255 characters. This is our own conservative rule, because Stream doesn't publish its user ID limits.

## SDK behavior notes (getstream-net 16.0.1)

These were checked against the SDK source at the 16.0.1 release commit (`9ce65bd`) and Stream's platform user docs. They explain why `StreamUserService.EnsureUserAsync` works the way it does. Re-check them when upgrading the SDK.

- **User updates.**
  - A full upsert (`UpdateUsersAsync`, `POST /api/v2/users`) replaces the user's existing data, so it would wipe role and custom data.
  - A partial update (`UpdateUsersPartialAsync`, `PATCH /api/v2/users`) sets only the given fields, but Stream doesn't document whether it creates missing users.
  - So the service looks the user up first (`QueryUsersAsync`). It creates missing users with a full upsert of only the sent fields, and partially updates existing users only when `name` or `image` was sent.
- **Known race condition.** Two simultaneous first requests for the same new user can both see "missing" and both upsert, so one `name` or `image` may overwrite the other. That's accepted for this playground.
- **Errors.** The SDK throws `GetStreamApiException` (with `StatusCode`) for API errors and `GetStreamTransportException` for network failures, both derived from `GetStreamException`. The service converts them to `StreamRequestFailedException`, which becomes a `502`.
- **Tokens.** `CreateUserToken(userId, lifetime)` sets `user_id`, `iat` and `nbf` (5 seconds in the past) and `exp` from the system clock. `expiresAt` is read back from the token's `exp` claim, so it always matches.
- **HTTP.** Each `StreamClient` builds its own connection pool, so the API registers a single instance.
- **Deleting users** is a background task. `DeleteUsersAsync` returns a task id to wait on with `WaitForTaskAsync`. The integration tests use `User = "hard"`.
