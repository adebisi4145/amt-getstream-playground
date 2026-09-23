import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    include: ["test/**/*.test.ts"],
    // Integration tests call the real Stream app and wait for background deletes.
    testTimeout: 90_000,
    hookTimeout: 90_000,
  },
});
