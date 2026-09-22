"use client";

import type { ReactNode } from "react";
import {
  MdCallEnd,
  MdFiberManualRecord,
  MdMic,
  MdMicOff,
  MdMoreHoriz,
  MdVolumeOff,
  MdVolumeUp,
} from "react-icons/md";

interface ControlButtonProps {
  label: string;
  icon: ReactNode;
  onClick?: () => void;
  disabled?: boolean;
  title?: string;
  /** End call is the only red button in the design (error-500). */
  danger?: boolean;
  active?: boolean;
}

function ControlButton({ label, icon, onClick, disabled, title, danger, active }: ControlButtonProps) {
  const background = danger ? "bg-amt-error text-white" : active ? "bg-amt-blue text-white" : "bg-white text-amt-black";

  return (
    <div className="flex flex-col items-center gap-2">
      <button
        type="button"
        onClick={onClick}
        disabled={disabled}
        title={title}
        aria-label={label}
        className={`flex size-[60px] items-center justify-center rounded-[32px] transition-opacity ${background} ${
          disabled ? "cursor-not-allowed opacity-40" : "hover:opacity-90"
        }`}
      >
        <span className="text-[28px] leading-none">{icon}</span>
      </button>
      <span className="text-sm font-medium text-white">{label}</span>
    </div>
  );
}

export interface CallControlsProps {
  micEnabled: boolean;
  onToggleMic: () => void;
  onEndCall: () => void;
  recording: boolean;
  onToggleRecording: () => void;
  /** Stream refuses to record a call nobody has joined, so this stays off until someone is there. */
  canRecord: boolean;
  speakerMuted: boolean;
  onToggleSpeaker: () => void;
}

export function CallControls({
  micEnabled,
  onToggleMic,
  onEndCall,
  recording,
  onToggleRecording,
  canRecord,
  speakerMuted,
  onToggleSpeaker,
}: CallControlsProps) {
  return (
    <div className="flex flex-wrap items-start justify-center gap-7">
      <ControlButton
        label="mute"
        icon={micEnabled ? <MdMic /> : <MdMicOff />}
        onClick={onToggleMic}
        active={!micEnabled}
      />
      <ControlButton
        label="record"
        icon={<MdFiberManualRecord />}
        onClick={onToggleRecording}
        disabled={!canRecord}
        active={recording}
        title={canRecord ? undefined : "Recording needs someone else on the call"}
      />
      {/* Mutes incoming audio by setting output volume; works in every browser,
          unlike choosing a specific output device. */}
      <ControlButton
        label="speaker"
        icon={speakerMuted ? <MdVolumeOff /> : <MdVolumeUp />}
        onClick={onToggleSpeaker}
        active={speakerMuted}
      />
      <ControlButton
        label="options"
        icon={<MdMoreHoriz />}
        disabled
        title="No behaviour defined in the design yet"
      />
      <ControlButton label="end call" icon={<MdCallEnd />} onClick={onEndCall} danger />
    </div>
  );
}
