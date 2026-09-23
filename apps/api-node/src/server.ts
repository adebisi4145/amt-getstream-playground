import { existsSync } from "node:fs";
import { buildApp } from "./app.ts";
import { ConfigError, loadConfig } from "./config.ts";

if (existsSync(".env")) {
  process.loadEnvFile(".env");
}

let config;
try {
  config = loadConfig();
} catch (error) {
  if (error instanceof ConfigError) {
    console.error(error.message);
    process.exit(1);
  }
  throw error;
}

const app = await buildApp(config, undefined, { logger: { level: "info" } });

await app.listen({ port: config.port, host: "localhost" });

for (const signal of ["SIGINT", "SIGTERM"] as const) {
  process.once(signal, () => {
    app.log.info({ signal }, "Shutting down");
    void app.close().then(() => process.exit(0));
  });
}
