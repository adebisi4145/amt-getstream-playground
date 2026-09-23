import type { FastifyInstance } from "fastify";
import type { ZodTypeProvider } from "fastify-type-provider-zod";
import { z } from "zod";
import { problemDetailsSchema, validationProblemSchema } from "../../http/problemDetails.ts";
import type { StreamUserService } from "../../services/stream/streamUserService.ts";
import { httpUrlSchema, optional, userIdSchema } from "../validation.ts";

export const tokenRequestSchema = z.object({
  userId: userIdSchema,
  name: optional(z.string()),
  image: optional(httpUrlSchema),
});

export const tokenResponseSchema = z.object({
  apiKey: z.string(),
  userId: z.string(),
  token: z.string(),
  expiresAt: z.string().describe("ISO 8601 UTC timestamp read from the token's exp claim."),
});

export interface TokenRouteOptions {
  apiKey: string;
  users: StreamUserService;
}

/**
 * POST /tokens. The endpoint trusts the userId sent by the client, so the caller must only register it
 * when untrusted requests are allowed (development).
 */
export async function tokenRoutes(app: FastifyInstance, { apiKey, users }: TokenRouteOptions) {
  app.withTypeProvider<ZodTypeProvider>().post(
    "/tokens",
    {
      schema: {
        operationId: "CreateToken",
        tags: ["Tokens"],
        summary: "Issue a Stream user token",
        description:
          "Ensures the user exists in Stream (setting only the fields sent) and returns the Stream API key " +
          "and a user token. The Stream API secret is never returned. Development only: the userId is trusted as sent.",
        body: tokenRequestSchema,
        response: { 200: tokenResponseSchema, 400: validationProblemSchema, 502: problemDetailsSchema },
      },
    },
    async (request) => {
      const { userId, name, image } = request.body;

      await users.ensureUser(userId, name, image);
      const { token, expiresAt } = users.createToken(userId);

      return { apiKey, userId, token, expiresAt: expiresAt.toISOString() };
    },
  );
}
