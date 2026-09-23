import cors from "@fastify/cors";
import swagger from "@fastify/swagger";
import scalar from "@scalar/fastify-api-reference";
import { StreamClient } from "@stream-io/node-sdk";
import Fastify, { type FastifyServerOptions } from "fastify";
import { jsonSchemaTransform, serializerCompiler, validatorCompiler } from "fastify-type-provider-zod";
import type { AppConfig } from "./config.ts";
import { callRoutes } from "./features/calls/callRoutes.ts";
import { tokenRoutes } from "./features/tokens/tokenRoutes.ts";
import { registerProblemDetails } from "./http/problemDetails.ts";
import { createStreamCallService, type StreamCallService } from "./services/stream/streamCallService.ts";
import { createStreamUserService, type StreamUserService } from "./services/stream/streamUserService.ts";
import {
  createStreamWebhookService,
  createWebhookEventLog,
  type StreamWebhookService,
  type WebhookEventLog,
} from "./services/stream/streamWebhookService.ts";
import { userRoutes } from "./features/users/userRoutes.ts";
import { webhookRoutes } from "./features/webhooks/webhookRoutes.ts";

export interface AppServices {
  users: StreamUserService;
  calls: StreamCallService;
  webhooks: StreamWebhookService;
  webhookLog: WebhookEventLog;
}

/** One StreamClient for the whole app; the SDK shares its HTTP connections. */
export function createStreamServices(config: AppConfig): AppServices {
  const client = new StreamClient(config.stream.apiKey, config.stream.apiSecret);
  const users = createStreamUserService(client, config.stream.tokenLifetimeSeconds);
  return {
    users,
    calls: createStreamCallService(client, users),
    webhooks: createStreamWebhookService(client),
    webhookLog: createWebhookEventLog(),
  };
}

/** Builds the app without listening. Tests pass fake services. */
export async function buildApp(
  config: AppConfig,
  services: AppServices = createStreamServices(config),
  options: FastifyServerOptions = {},
) {
  const app = Fastify(options);
  const isDevelopment = config.environment === "development";

  app.setValidatorCompiler(validatorCompiler);
  app.setSerializerCompiler(serializerCompiler);
  registerProblemDetails(app);

  if (isDevelopment) {
    await app.register(swagger, {
      openapi: { info: { title: "Amt.GetStream.Api (Node)", version: "v1" } },
      transform: jsonSchemaTransform,
    });
  }

  await app.register(
    async (api) => {
      // Registered inside /api only, and before the routes, so error responses keep their CORS headers.
      await api.register(cors, { origin: config.cors.allowedOrigins });

      // Authenticated by Stream's signature, so it doesn't depend on the dev flag.
      await api.register(webhookRoutes, {
        webhooks: services.webhooks,
        log: services.webhookLog,
        exposeRecentEvents: isDevelopment,
      });

      if (config.allowUntrustedRequests) {
        await api.register(tokenRoutes, { apiKey: config.stream.apiKey, users: services.users });
        await api.register(callRoutes, { calls: services.calls });
        await api.register(userRoutes, { users: services.users });
      }
    },
    { prefix: "/api" },
  );

  app.get("/health", { schema: { hide: true } }, () => ({ status: "ok" }));

  if (isDevelopment) {
    app.get("/openapi/v1.json", { schema: { hide: true } }, () => app.swagger());
    await app.register(scalar, { routePrefix: "/scalar", configuration: { url: "/openapi/v1.json" } });
  }

  return app;
}
