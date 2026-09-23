import { z } from "zod";

/**
 * Settings read from environment variables (and `.env` when present).
 * The API secret must come from `.env` or the environment, never a committed file.
 */
export interface AppConfig {
  environment: string;
  port: number;
  stream: {
    apiKey: string;
    apiSecret: string;
    /** Token lifetime in seconds. */
    tokenLifetimeSeconds: number;
  };
  cors: { allowedOrigins: string[] };
  /**
   * Turns on the endpoints that trust the user ids they're sent (`POST /api/tokens`, `POST /api/calls`).
   * Development only.
   */
  allowUntrustedRequests: boolean;
}

// The API key is public by design (same value as apps/api/appsettings.json).
const DEFAULT_STREAM_API_KEY = "s3c4w9uuvs5k";
const DEVELOPMENT_ORIGINS = ["http://localhost:3000"];

const commaList = z
  .string()
  .transform((value) => value.split(",").map((item) => item.trim()).filter(Boolean));

const booleanFlag = z.enum(["true", "false"]).transform((value) => value === "true");

const envSchema = z.object({
  NODE_ENV: z.string().default("production"),
  PORT: z.coerce.number().int().min(1).max(65535).default(5057),
  STREAM_API_KEY: z.string().min(1).default(DEFAULT_STREAM_API_KEY),
  STREAM_API_SECRET: z.string({ error: "STREAM_API_SECRET is required" }).min(1, "STREAM_API_SECRET is required"),
  STREAM_TOKEN_LIFETIME_SECONDS: z.coerce.number().int().min(60).max(86_400).default(3600),
  CORS_ALLOWED_ORIGINS: commaList.optional(),
  ALLOW_UNTRUSTED_REQUESTS: booleanFlag.optional(),
});

export class ConfigError extends Error {}

/** Validates the environment up front, so a missing secret fails at startup, not on the first request. */
export function loadConfig(env: Record<string, string | undefined> = process.env): AppConfig {
  const result = envSchema.safeParse(env);
  if (!result.success) {
    const problems = result.error.issues.map((issue) => `${issue.path.join(".")}: ${issue.message}`);
    throw new ConfigError(`Invalid configuration:\n  ${problems.join("\n  ")}`);
  }

  const values = result.data;
  const isDevelopment = values.NODE_ENV === "development";

  return {
    environment: values.NODE_ENV,
    port: values.PORT,
    stream: {
      apiKey: values.STREAM_API_KEY,
      apiSecret: values.STREAM_API_SECRET,
      tokenLifetimeSeconds: values.STREAM_TOKEN_LIFETIME_SECONDS,
    },
    cors: { allowedOrigins: values.CORS_ALLOWED_ORIGINS ?? (isDevelopment ? DEVELOPMENT_ORIGINS : []) },
    allowUntrustedRequests: values.ALLOW_UNTRUSTED_REQUESTS ?? isDevelopment,
  };
}
