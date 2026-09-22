import { CallingState, type Call } from "@stream-io/video-react-sdk";

/**
 * Leaves a call without throwing if it's already gone.
 *
 * Stream throws "Cannot leave call that has already been left" when leave() runs twice — which
 * happens easily: an impatient second click, or hanging up from one screen while the other side
 * ends the call. The caller only cares that it's left afterwards.
 */
export async function leaveCallQuietly(call: Call): Promise<void> {
  const state = call.state.callingState;

  if (state === CallingState.LEFT || state === CallingState.IDLE) {
    return;
  }

  try {
    await call.leave();
  } catch {
    // Already left, or the call ended underneath us.
  }
}
