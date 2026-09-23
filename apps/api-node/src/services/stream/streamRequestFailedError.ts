/**
 * Raised by the Stream service wrappers when a call to Stream fails, so SDK error types never leave services/stream.
 * Stream's status code and message are kept for logging only and must not be returned to clients.
 */
export class StreamRequestFailedError extends Error {
  readonly operation: string;
  readonly streamStatusCode: number | undefined;

  constructor(operation: string, streamStatusCode: number | undefined, cause: unknown) {
    super(`Stream request '${operation}' failed.`, { cause });
    this.name = "StreamRequestFailedError";
    this.operation = operation;
    this.streamStatusCode = streamStatusCode;
  }
}

/**
 * Runs Stream SDK calls and converts their failures to {@link StreamRequestFailedError}.
 * The SDK throws its `StreamError` for API errors, timeouts and network failures alike, but doesn't export the class,
 * so every rejection is treated as a Stream failure. Keep non-SDK logic out of `action`.
 */
export async function callStream<T>(operation: string, action: () => Promise<T>): Promise<T> {
  try {
    return await action();
  } catch (error) {
    if (error instanceof StreamRequestFailedError) throw error;

    const metadata = (error as { metadata?: { responseCode?: unknown } } | undefined)?.metadata;
    const statusCode = typeof metadata?.responseCode === "number" ? metadata.responseCode : undefined;
    throw new StreamRequestFailedError(operation, statusCode, error);
  }
}
