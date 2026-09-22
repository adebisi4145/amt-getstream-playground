"use client";

import { useState, type ReactNode } from "react";
import { MdClose, MdOutlinePhone, MdOutlineVideocam } from "react-icons/md";
import { PillButton } from "./PillButton";
import type { Modality } from "@/lib/api";

interface ConsultationModalProps {
  onConfirm: (modality: Modality) => void;
  onClose: () => void;
  busy: boolean;
  error: string | null;
}

/**
 * Figma modal 13717:44576 (card 416 wide, padding 24/20, gap 20, actions right-aligned).
 * The payment copy is dropped for the demo, but the Confirm step is kept: picking a modality
 * starts a real consultation and opens the camera, so a misclick must be recoverable.
 */
export function ConsultationModal({ onConfirm, onClose, busy, error }: ConsultationModalProps) {
  const [selected, setSelected] = useState<Modality | null>(null);

  return (
    <div
      className="fixed inset-0 z-50 flex items-end justify-center bg-[rgba(33,34,33,0.5)] p-0 sm:items-center sm:p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="consultation-modal-title"
    >
      <div className="flex w-full max-w-[416px] flex-col items-end gap-5 rounded-t-[20px] bg-white px-5 py-6 sm:rounded-xl">
        <button
          type="button"
          onClick={onClose}
          aria-label="Close"
          className="text-2xl text-amt-black-300 transition-colors hover:text-amt-black"
        >
          <MdClose />
        </button>

        <div className="flex w-full flex-col gap-6">
          <div className="flex flex-col gap-3">
            <h2 id="consultation-modal-title" className="font-display text-xl font-medium text-amt-black">
              Speak to a doctor now
            </h2>
            <p className="text-sm tracking-[-0.14px] text-amt-black-400">
              Choose how you’d like to speak to a doctor, then confirm to connect.
            </p>
          </div>

          <div className="flex gap-3">
            <OptionCard
              label="Audio call"
              icon={<MdOutlinePhone className="text-xl" />}
              selected={selected === "audio"}
              onSelect={() => setSelected("audio")}
              disabled={busy}
            />
            <OptionCard
              label="Video call"
              icon={<MdOutlineVideocam className="text-xl" />}
              selected={selected === "video"}
              onSelect={() => setSelected("video")}
              disabled={busy}
            />
          </div>
        </div>

        {error && (
          <p className="w-full text-sm font-medium text-amt-error" role="alert">
            {error}
          </p>
        )}

        <PillButton
          className="w-full sm:w-auto"
          disabled={!selected || busy}
          onClick={() => selected && onConfirm(selected)}
        >
          {busy ? "Connecting…" : "Confirm"}
        </PillButton>
      </div>
    </div>
  );
}

interface OptionCardProps {
  label: string;
  icon: ReactNode;
  selected: boolean;
  onSelect: () => void;
  disabled: boolean;
}

function OptionCard({ label, icon, selected, onSelect, disabled }: OptionCardProps) {
  // Figma: default 0.5px #D7D7D7 border; selected 1px #2052AA with a blue-50 fill.
  const style = selected
    ? "border border-amt-blue bg-amt-blue-50"
    : "border-[0.5px] border-amt-grey hover:border-amt-blue";

  return (
    <button
      type="button"
      onClick={onSelect}
      disabled={disabled}
      aria-pressed={selected}
      className={`flex flex-1 items-center gap-2 rounded-md p-4 text-left text-[15px] tracking-[-0.15px] text-amt-black transition-colors disabled:cursor-not-allowed disabled:opacity-60 ${style}`}
    >
      {icon}
      {label}
    </button>
  );
}
