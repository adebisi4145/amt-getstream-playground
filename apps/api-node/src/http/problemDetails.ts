import type { FastifyError, FastifyInstance, FastifyReply } from "fastify";
import { hasZodFastifySchemaValidationErrors } from "fastify-type-provider-zod";
import { z } from "zod";
import { StreamRequestFailedError } from "../services/stream/streamRequestFailedError.ts";

/** RFC 9457 problem details, shaped like ASP.NET Core's so both APIs return the same errors. */
export const problemDetailsSchema = z.object({
  type: z.string().optional(),
  title: z.string(),
  status: z.number(),
  detail: z.string().optional(),
});

export const validationProblemSchema = problemDetailsSchema.extend({
  errors: z.record(z.string(), z.array(z.string())),
});

function sendProblem(reply: FastifyReply, problem: z.infer<typeof validationProblemSchema> | z.infer<typeof problemDetailsSchema>) {
  return reply.code(problem.status).type("application/problem+json").send(problem);
}

/**
 * Maps request validation errors to 400 ValidationProblem and {@link StreamRequestFailedError} to 502
 * with a generic message. Stream's own error details are logged, not returned.
 */
export function registerProblemDetails(app: FastifyInstance) {
  app.setErrorHandler((error: FastifyError, request, reply) => {
    if (hasZodFastifySchemaValidationErrors(error)) {
      const errors: Record<string, string[]> = {};
      for (const issue of error.validation) {
        const field = issue.instancePath.replace(/^\//, "").replaceAll("/", ".") || "$";
        (errors[field] ??= []).push(issue.message ?? "Invalid value.");
      }
      return sendProblem(reply, {
        type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        title: "One or more validation errors occurred.",
        status: 400,
        errors,
      });
    }

    if (error instanceof StreamRequestFailedError && error.streamStatusCode === 404) {
      // Stream says the call or user doesn't exist, which is the client's problem, not a bad gateway.
      request.log.info({ err: error, operation: error.operation }, "Stream resource not found");
      return sendProblem(reply, {
        type: "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        title: "Not found",
        status: 404,
      });
    }

    if (error instanceof StreamRequestFailedError) {
      request.log.error(
        { err: error, operation: error.operation, streamStatusCode: error.streamStatusCode },
        "Stream request %s failed with Stream status %s",
        error.operation,
        error.streamStatusCode,
      );
      return sendProblem(reply, {
        type: "https://tools.ietf.org/html/rfc9110#section-15.6.3",
        title: "Stream request failed",
        status: 502,
        detail: "The request to Stream could not be completed. Try again later.",
      });
    }

    // Malformed JSON and similar client errors raised by Fastify itself. A 400 keeps the ValidationProblem
    // shape the routes declare (like ASP.NET Core's "$" entry); anything else (415, 413...) is plain.
    if (error.statusCode === 400) {
      return sendProblem(reply, {
        type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        title: "One or more validation errors occurred.",
        status: 400,
        errors: { $: [error.message] },
      });
    }

    if (error.statusCode !== undefined && error.statusCode > 400 && error.statusCode < 500) {
      return sendProblem(reply, { title: error.message, status: error.statusCode });
    }

    request.log.error({ err: error }, "Unhandled error");
    return sendProblem(reply, {
      type: "https://tools.ietf.org/html/rfc9110#section-15.6.1",
      title: "An error occurred while processing your request.",
      status: 500,
    });
  });
}
