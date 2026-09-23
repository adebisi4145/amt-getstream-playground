"use client";

import { useState } from "react";
import { useCall, useCallStateHooks } from "@stream-io/video-react-sdk";
import { PillButton } from "./PillButton";
import { ApiError, type Consultation, type Modality } from "@/lib/api";
import { switchModality } from "@/lib/switch-modality";

interface ModalityToggleProps {
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
export function ModalityToggle({ staffId, fallback, onSwitched, onError }: ModalityToggleProps) {
  const call = useCall();
  const { useCallCustomData } = useCallStateHooks();
  const custom = useCallCustomData();
  const [busy, setBusy] = useState(false);

  const modality: Modality =
    custom?.modality === "audio" || custom?.modality === "video" ? custom.modality : fallback;
  const next: Modality = modality === "audio" ? "video" : "audio";

  const switchTo = async () => {
    if (!call) {
      return;
    }

    setBusy(true);

    try {
      onSwitched?.(await switchModality(call, staffId, next));
    } catch (failure) {
      onError?.(
        failure instanceof ApiError ? failure.message : "Could not switch the consultation.",
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <PillButton variant="white" disabled={busy || !call} onClick={() => void switchTo()}>
      {busy ? "Switching…" : next === "video" ? "Start video" : "Back to audio"}
    </PillButton>
  );
}
