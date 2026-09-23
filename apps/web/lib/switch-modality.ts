import type { Call } from "@stream-io/video-react-sdk";
import { api, type Consultation, type Modality } from "./api";

/**
 * Switches a running consultation between audio and video, on behalf of staff on the call.
 *
 * Whoever asks for video turns their own camera on. The other side's camera is left alone — the
 * patient decides whether to show their face — and going back to audio needs nothing here, because
 * every screen closes its own camera when the consultation becomes audio.
 */
export async function switchModality(
  call: Call,
  staffId: string,
  next: Modality,
): Promise<Consultation> {
  const updated = await api.setModality(call.id, staffId, next);

  if (next === "video") {
    await call.camera.enable();
  }

  return updated;
}
