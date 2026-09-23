import type { FastifyInstance } from "fastify";
import type { ZodTypeProvider } from "fastify-type-provider-zod";
import { z } from "zod";
import { problemDetailsSchema, validationProblemSchema } from "../../http/problemDetails.ts";
import type { StreamCallService } from "../../services/stream/streamCallService.ts";
import { userIdSchema } from "../validation.ts";

export const MAX_CALL_MEMBERS = 10;

export const startCallRequestSchema = z
  .object({
    createdById: userIdSchema,
    memberIds: z
      .array(userIdSchema)
      .min(1, "The memberIds field must list at least one user to call.")
      .max(MAX_CALL_MEMBERS, `The memberIds field may list at most ${MAX_CALL_MEMBERS} users.`),
    kind: z.enum(["video", "audio"]).default("video"),
    ring: z.boolean().default(true),
  })
  .refine((request) => request.memberIds.some((id) => id !== request.createdById), {
    path: ["memberIds"],
    message: "The memberIds field must include someone other than createdById.",
  });

export const startCallResponseSchema = z.object({
  callType: z.string(),
  callId: z.string(),
  cid: z.string(),
});

/** Stream's call ids: letters, digits, `_` and `-`. Ours are UUIDs. */
export const callIdParamsSchema = z.object({
  callId: z
    .string()
    .min(1)
    .max(64)
    .regex(/^[A-Za-z0-9_-]+$/, "The callId field may only contain letters, digits, _ and -."),
});

export const callDetailsSchema = startCallResponseSchema.extend({
  kind: z.enum(["video", "audio"]),
  createdById: z.string(),
  createdAt: z.string(),
  endedAt: z.string().optional(),
  live: z.boolean(),
  members: z.array(z.object({ userId: z.string(), name: z.string().optional(), image: z.string().optional() })),
  acceptedBy: z.array(z.string()),
  rejectedBy: z.array(z.string()),
  missedBy: z.array(z.string()),
});

export interface CallRouteOptions {
  calls: StreamCallService;
}

/**
 * POST /calls. Trusts createdById as sent, so the caller must only register it when untrusted requests are allowed.
 */
export async function callRoutes(app: FastifyInstance, { calls }: CallRouteOptions) {
  app.withTypeProvider<ZodTypeProvider>().post(
    "/calls",
    {
      schema: {
        operationId: "StartCall",
        tags: ["Calls"],
        summary: "Start a video or audio call",
        description:
          "Creates a call of Stream's `default` type with the creator and members, creating any missing users " +
          "(id only). With `ring` (the default), members get an incoming-call event. Audio calls start with the " +
          "camera off. Clients join with `client.call(callType, callId)`. Development only: createdById is trusted as sent.",
        body: startCallRequestSchema,
        response: { 201: startCallResponseSchema, 400: validationProblemSchema, 502: problemDetailsSchema },
      },
    },
    async (request, reply) => {
      const call = await calls.startCall(request.body);
      return reply.code(201).send(call);
    },
  );

  app.withTypeProvider<ZodTypeProvider>().get(
    "/calls/:callId",
    {
      schema: {
        operationId: "GetCall",
        tags: ["Calls"],
        summary: "Read a call's state",
        description: "Who the members are, whether anyone is in the call, and who accepted, rejected or missed it.",
        params: callIdParamsSchema,
        response: {
          200: callDetailsSchema,
          400: validationProblemSchema,
          404: problemDetailsSchema,
          502: problemDetailsSchema,
        },
      },
    },
    async (request) => calls.getCall(request.params.callId),
  );

  app.withTypeProvider<ZodTypeProvider>().post(
    "/calls/:callId/end",
    {
      schema: {
        operationId: "EndCall",
        tags: ["Calls"],
        summary: "End a call for everyone",
        description: "Drops every participant and stops a ringing call. Clients can also end a call themselves.",
        params: callIdParamsSchema,
        response: {
          204: z.null(),
          400: validationProblemSchema,
          404: problemDetailsSchema,
          502: problemDetailsSchema,
        },
      },
    },
    async (request, reply) => {
      await calls.endCall(request.params.callId);
      return reply.code(204).send(null);
    },
  );
}
