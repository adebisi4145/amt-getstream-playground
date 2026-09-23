import { afterEach, describe, expect, it } from "vitest";
import { StreamRequestFailedError } from "../src/services/stream/streamRequestFailedError.ts";
import { createTestApp, FakeStreamUserService } from "./testApp.ts";

type App = Awaited<ReturnType<typeof createTestApp>>;
let app: App | undefined;
afterEach(async () => {
  await app?.close();
  app = undefined;
});

describe("GET /api/users", () => {
  it("lists users with the default limit", async () => {
    const users = new FakeStreamUserService();
    app = await createTestApp({ users });

    const response = await app.inject({ method: "GET", url: "/api/users" });

    expect(response.statusCode).toBe(200);
    expect(users.listUserLimits).toEqual([50]);
    expect(response.json()).toEqual({ users: [{ userId: "alice", name: "Alice", role: "user", online: true }] });
  });

  it("passes the requested limit through", async () => {
    const users = new FakeStreamUserService();
    app = await createTestApp({ users });

    const response = await app.inject({ method: "GET", url: "/api/users?limit=7" });

    expect(response.statusCode).toBe(200);
    expect(users.listUserLimits).toEqual([7]);
  });

  it.each(["0", "101", "abc"])("returns 400 for limit=%s", async (limit) => {
    const users = new FakeStreamUserService();
    app = await createTestApp({ users });

    const response = await app.inject({ method: "GET", url: `/api/users?limit=${limit}` });

    expect(response.statusCode).toBe(400);
    expect(users.listUserLimits).toEqual([]);
  });

  it("returns 502 when Stream fails", async () => {
    const users = new FakeStreamUserService();
    users.listUsers = async () => {
      throw new StreamRequestFailedError("listUsers", 500, new Error("boom"));
    };
    app = await createTestApp({ users });

    const response = await app.inject({ method: "GET", url: "/api/users" });

    expect(response.statusCode).toBe(502);
  });

  it("is not mapped when untrusted requests are not allowed", async () => {
    app = await createTestApp({}, { ALLOW_UNTRUSTED_REQUESTS: "false" });

    const response = await app.inject({ method: "GET", url: "/api/users" });

    expect(response.statusCode).toBe(404);
  });
});
