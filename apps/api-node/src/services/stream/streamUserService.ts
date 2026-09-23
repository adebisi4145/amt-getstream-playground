import type { StreamClient } from "@stream-io/node-sdk";
import { callStream } from "./streamRequestFailedError.ts";

export interface UserToken {
  token: string;
  expiresAt: Date;
}

export interface ListedUser {
  userId: string;
  name: string | undefined;
  image: string | undefined;
  role: string;
  online: boolean;
}

export interface StreamUserService {
  /**
   * Makes sure the user exists in Stream and sets only the fields provided.
   * Never sends a role and never unsets existing fields.
   */
  ensureUser(userId: string, name: string | undefined, image: string | undefined): Promise<void>;

  /**
   * Makes sure every user exists, creating missing ones with only their id. Existing users are never changed.
   */
  ensureUsersExist(userIds: readonly string[]): Promise<void>;

  /** Lists users, newest first, so a client can show who there is to call. */
  listUsers(limit: number): Promise<ListedUser[]>;

  /** Creates a Stream user token. `expiresAt` is read from the token itself. */
  createToken(userId: string): UserToken;
}

export function createStreamUserService(client: StreamClient, tokenLifetimeSeconds: number): StreamUserService {
  async function findExistingIds(userIds: readonly string[]): Promise<Set<string>> {
    const response = await client.queryUsers({
      payload: { filter_conditions: { id: { $in: userIds } }, limit: userIds.length },
    });
    return new Set(response.users.map((user) => user.id));
  }

  return {
    ensureUser: (userId, name, image) =>
      callStream("ensureUser", async () => {
        const fields: Record<string, string> = {};
        if (name !== undefined) fields.name = name;
        if (image !== undefined) fields.image = image;

        // A full upsert replaces the user's existing data (role, custom data), and Stream doesn't
        // document whether a partial update creates missing users. So: look up first, then either
        // create the user or partially update only the fields that were sent.
        if ((await findExistingIds([userId])).has(userId)) {
          if (Object.keys(fields).length === 0) return;
          await client.updateUsersPartial({ users: [{ id: userId, set: fields }] });
          return;
        }

        await client.updateUsers({ users: { [userId]: { id: userId, name, image } } });
      }),

    ensureUsersExist: (userIds) =>
      callStream("ensureUsersExist", async () => {
        const existing = await findExistingIds(userIds);
        const missing = userIds.filter((id) => !existing.has(id));
        if (missing.length === 0) return;

        await client.updateUsers({ users: Object.fromEntries(missing.map((id) => [id, { id }])) });
      }),

    listUsers: (limit) =>
      callStream("listUsers", async () => {
        const response = await client.queryUsers({
          payload: { filter_conditions: {}, sort: [{ field: "created_at", direction: -1 }], limit },
        });

        return response.users.map((user) => ({
          userId: user.id,
          name: user.name,
          image: user.image,
          role: user.role,
          online: user.online,
        }));
      }),

    createToken: (userId) => {
      const token = client.generateUserToken({ user_id: userId, validity_in_seconds: tokenLifetimeSeconds });
      return { token, expiresAt: readExpiry(token) };
    },
  };
}

/** Reads the `exp` claim so the reported expiry always matches the token. The token is ours, so no verification. */
export function readExpiry(token: string): Date {
  const payload = JSON.parse(Buffer.from(token.split(".")[1] ?? "", "base64url").toString("utf8")) as { exp?: unknown };
  if (typeof payload.exp !== "number") {
    throw new Error("Stream token has no exp claim.");
  }
  return new Date(payload.exp * 1000);
}
