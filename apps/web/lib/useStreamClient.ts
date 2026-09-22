"use client";

import { useEffect, useState } from "react";
import { StreamVideoClient, type User } from "@stream-io/video-react-sdk";
import { api, ApiError } from "./api";
import type { DemoUser } from "./demo-users";

interface StreamClientState {
  client: StreamVideoClient | null;
  error: string | null;
}

/**
 * Connects a Stream client for a demo user and keeps it connected for as long as the page is open.
 *
 * Staying connected is what makes ringing work: a doctor whose client isn't connected never
 * receives the incoming call. The token comes from our API, which is the only thing holding the
 * Stream secret — the browser only ever sees the public API key and a user token.
 */
export function useStreamClient(user: DemoUser | null): StreamClientState {
  const [state, setState] = useState<StreamClientState>({ client: null, error: null });

  useEffect(() => {
    if (!user) {
      return;
    }

    let client: StreamVideoClient | undefined;
    let cancelled = false;

    const connect = async () => {
      try {
        const { apiKey, token } = await api.createToken(user.id, user.name);
        if (cancelled) {
          return;
        }

        const streamUser: User = { id: user.id, name: user.name };

        client = new StreamVideoClient({
          apiKey,
          user: streamUser,
          token,
          // Called again when the token nears expiry; the endpoint is safe to re-call.
          tokenProvider: async () => (await api.createToken(user.id, user.name)).token,
        });

        setState({ client, error: null });
      } catch (error) {
        if (!cancelled) {
          setState({
            client: null,
            error: error instanceof ApiError ? error.message : "Could not connect to Stream.",
          });
        }
      }
    };

    void connect();

    return () => {
      cancelled = true;
      void client?.disconnectUser();
      setState({ client: null, error: null });
    };
  }, [user]);

  return state;
}
