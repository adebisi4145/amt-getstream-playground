import type { FastifyInstance } from "fastify";
import type { ZodTypeProvider } from "fastify-type-provider-zod";
import { z } from "zod";
import { problemDetailsSchema, validationProblemSchema } from "../../http/problemDetails.ts";
import type { StreamUserService } from "../../services/stream/streamUserService.ts";

export const MAX_LISTED_USERS = 100;

export const listUsersQuerySchema = z.object({
  limit: z.coerce.number().int().min(1).max(MAX_LISTED_USERS).default(50),
});

export const listUsersResponseSchema = z.object({
  users: z.array(
    z.object({
      userId: z.string(),
      name: z.string().optional(),
      image: z.string().optional(),
      role: z.string(),
      online: z.boolean(),
    }),
  ),
});

export interface UserRouteOptions {
  users: StreamUserService;
}

/** GET /users. Returns everyone in the Stream app, so the caller must only register it in development. */
export async function userRoutes(app: FastifyInstance, { users }: UserRouteOptions) {
  app.withTypeProvider<ZodTypeProvider>().get(
    "/users",
    {
      schema: {
        operationId: "ListUsers",
        tags: ["Users"],
        summary: "List users you can call",
        description:
          "Lists the Stream app's users, newest first, so a client can show who there is to call. " +
          "Development only: it returns every user, with no filtering by who is asking.",
        querystring: listUsersQuerySchema,
        response: { 200: listUsersResponseSchema, 400: validationProblemSchema, 502: problemDetailsSchema },
      },
    },
    async (request) => ({ users: await users.listUsers(request.query.limit) }),
  );
}
