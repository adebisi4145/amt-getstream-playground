"use client";

import { useState } from "react";
import { useCallStateHooks } from "@stream-io/video-react-sdk";
import { PillButton } from "./PillButton";
import { api, ApiError, type Consultation, type Modality } from "@/lib/api";

interface ModalityToggleProps {
  callId: string;
  /** The staff member asking. The API refuses anyone who isn't on this consultation. */
  staffId: string;
  /** Used until the call's custom data arrives. */
  fallback: Modality;
  onSwitched?: (consultation: Consultation) => void;
  onError?: (message: string) => void;
}

/**
 * Lets staff turn video on part-way through an audio consultation — the patient describes a rash,
 * and triage needs to look at it — and go back to audio afterwards.
 *
 * Must be rendered inside <StreamCall>: it reads the modality from the call itself, so it stays
 * right when the other clinician switches it.
 */
export function ModalityToggle({ callId, staffId, fallback, onSwitched, onError }: ModalityToggleProps) {
  const { useCallCustomData, useCameraState } = useCallStateHooks();
  const custom = useCallCustomData();
  const { camera } = useCameraState();
  const [busy, setBusy] = useState(false);

  const modality: Modality =
    custom?.modality === "audio" || custom?.modality === "video" ? custom.modality : fallback;
  const next: Modality = modality === "audio" ? "video" : "audio";

  const switchTo = async () => {
    setBusy(true);

    try {
      const updated = await api.setModality(callId, staffId, next);

      // Whoever asks for video shows their own face; the patient still decides about theirs.
      // Going back to audio needs nothing here: every screen closes its camera on the change.
      if (next === "video") {
        await camera.enable();
      }

      onSwitched?.(updated);
    } catch (failure) {
      onError?.(
        failure instanceof ApiError ? failure.message : "Could not switch the consultation.",
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <PillButton variant="white" disabled={busy} onClick={() => void switchTo()}>
      {busy ? "Switching…" : next === "video" ? "Start video" : "Back to audio"}
    </PillButton>
  );
}
