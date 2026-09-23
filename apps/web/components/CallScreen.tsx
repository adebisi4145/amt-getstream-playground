"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { MdKeyboardArrowLeft } from "react-icons/md";
import {
  CallingState,
  ParticipantsAudio,
  ParticipantView,
  useCall,
  useCallStateHooks,
  type StreamVideoParticipant,
} from "@stream-io/video-react-sdk";
import { Avatar } from "./Avatar";
import { CallControls } from "./CallControls";
import { api, ApiError } from "@/lib/api";
import type { Modality } from "@/lib/api";

interface CallScreenProps {
  /**
   * The modality this screen was opened with. It's only the starting point: staff can switch a
   * running consultation, so the live value comes from the call's own custom data.
   */
  modality: Modality;
  localName: string;
  /** Shown centre-screen until someone else joins. */
  waitingLabel: string;
  callType: string;
  callId: string;
  onLeave: () => void;
  /** Role-specific actions (invite a doctor, complete) rendered above the control bar. */
  actions?: ReactNode;
}

export function CallScreen({
  modality,
  localName,
  waitingLabel,
  callType,
  callId,
  onLeave,
  actions,
}: CallScreenProps) {
  const call = useCall();
  const {
    useRemoteParticipants,
    useLocalParticipant,
    useMicrophoneState,
    useSpeakerState,
    useCameraState,
    useCallCustomData,
    useCallSession,
    useCallCallingState,
  } = useCallStateHooks();

  // "Remote" means another *person*, not another session. A patient rejoining while an older
  // session of theirs lingers would otherwise appear as their own clinician.
  const allRemote = useRemoteParticipants();
  const remoteParticipants = allRemote.filter(
    (participant) => participant.userId !== call?.currentUserId,
  );

  const callingState = useCallCallingState();
  const localParticipant = useLocalParticipant();
  const { microphone, isMute } = useMicrophoneState();
  const { speaker } = useSpeakerState();
  const { camera, isMute: cameraOff } = useCameraState();
  const session = useCallSession();

  // Staff can turn video on part-way through a consultation. The modality lives in the call's
  // custom data, so Stream delivers the change to every screen in the call — including the
  // patient's — without this page polling the API.
  const custom = useCallCustomData();
  const liveModality: Modality =
    custom?.modality === "audio" || custom?.modality === "video" ? custom.modality : modality;

  // Audio means cameras off for everyone, so switching back to audio closes any camera that's on.
  // Turning video *on* deliberately doesn't open anyone's camera: whoever asks for video enables
  // their own, and the other side chooses for themselves.
  useEffect(() => {
    if (liveModality === "audio" && !cameraOff) {
      void camera.disable();
    }
  }, [camera, cameraOff, liveModality]);

  const [recording, setRecording] = useState(false);
  const [speakerMuted, setSpeakerMuted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [leaving, setLeaving] = useState(false);
  const [departed, setDeparted] = useState<string | null>(null);

  // Staff must be told when the patient drops, rather than facing a frozen screen and a
  // running timer. Subscribing to the call's own events keeps this out of the render path.
  useEffect(() => {
    if (!call) {
      return;
    }

    const onLeft = call.on("call.session_participant_left", (event) => {
      if (event.type !== "call.session_participant_left") {
        return;
      }

      const who = event.participant.user;
      if (who.id !== call.currentUserId) {
        setDeparted(who.name || who.id);
      }
    });

    const onJoined = call.on("call.session_participant_joined", () => setDeparted(null));

    return () => {
      onLeft();
      onJoined();
    };
  }, [call]);

  // One hang-up per screen: a second click used to call leave() again and throw.
  const handleLeave = () => {
    if (leaving) {
      return;
    }

    setLeaving(true);
    onLeave();
  };

  // The other side can end the call for everyone (triage completing a consultation), so this
  // screen has to notice the call going away rather than sit on a dead call.
  const endedRef = useRef(false);
  useEffect(() => {
    if (callingState === CallingState.LEFT && !endedRef.current) {
      endedRef.current = true;
      onLeave();
    }
  }, [callingState, onLeave]);

  const remote = remoteParticipants[0];

  // Nobody left to talk to: freeze the clock rather than let it tick against an empty call.
  const alone = !remote && departed !== null;
  const elapsed = useElapsed(session?.started_at, !alone);

  // Stream refuses to record a call with no session, so the button waits for company.
  const canRecord = remoteParticipants.length > 0;

  const toggleRecording = async () => {
    setError(null);
    try {
      if (recording) {
        await api.stopRecording(callType, callId);
        setRecording(false);
      } else {
        await api.startRecording(callType, callId);
        setRecording(true);
      }
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : "Recording failed.");
    }
  };

  const toggleSpeaker = () => {
    const next = !speakerMuted;
    speaker.setVolume(next ? 0 : 1);
    setSpeakerMuted(next);
  };

  return (
    <div className="amt-call-gradient relative flex min-h-[640px] flex-1 flex-col overflow-hidden rounded-md p-6">
      {/* Binds an audio element per remote participant. Without this there is picture but no sound,
          including on audio-only calls where no video element is rendered at all. */}
      <ParticipantsAudio participants={remoteParticipants} />

      <button
        type="button"
        onClick={handleLeave}
        disabled={leaving}
        aria-label="Leave call"
        className="flex size-10 items-center justify-center rounded-full bg-white text-amt-black"
      >
        <MdKeyboardArrowLeft className="text-xl" />
      </button>

      {/* Picture-in-picture tile for the local user, top right in the design. */}
      <div className="amt-pip-gradient absolute right-6 top-12 flex w-[218px] flex-col items-center justify-center gap-5 rounded-md p-10">
        {liveModality === "video" && localParticipant && !cameraOff ? (
          <ParticipantTile participant={localParticipant} />
        ) : (
          <Avatar name={localName} size={70} className="bg-amt-blue-400" />
        )}
        <p className="text-lg font-semibold tracking-[-0.18px] text-white">{localName}</p>
      </div>

      <div className="flex flex-1 flex-col items-center justify-center gap-6 text-center">
        {remote ? (
          <>
            {/* Rendered whenever the call is video, NOT only when a video track already exists:
                Stream subscribes to a participant's video because a ParticipantView asks for it.
                Waiting for remote.videoStream first meant the track was never requested, so the
                other side's camera never appeared. */}
            {liveModality === "video" ? (
              <div className="w-full max-w-md overflow-hidden rounded-md">
                <ParticipantTile participant={remote} />
              </div>
            ) : (
              <Avatar name={remote.name || remote.userId} />
            )}
            <p className="font-display text-2xl font-semibold text-white">
              {remote.name || remote.userId}
            </p>
            <span className="rounded-[32px] bg-amt-blue px-3 py-0.5 text-sm font-semibold text-white">
              {elapsed}
            </span>
          </>
        ) : alone ? (
          <>
            <Avatar name={departed ?? ""} className="bg-white/30" />
            <p className="font-display text-2xl font-semibold text-white">{departed} left the call</p>
            <p className="max-w-sm text-sm text-white/80">
              They may rejoin. Complete the consultation when you’re done.
            </p>
          </>
        ) : (
          <>
            <div className="size-[100px] animate-pulse rounded-full bg-white/30" />
            <p className="font-display text-2xl font-semibold text-white">{waitingLabel}</p>
            <span className="rounded-[32px] bg-amt-blue px-3 py-0.5 text-sm font-semibold text-white">
              {elapsed}
            </span>
          </>
        )}
      </div>

      {error && (
        <p className="mb-4 text-center text-sm font-medium text-white" role="alert">
          {error}
        </p>
      )}

      {actions && <div className="mb-6 flex flex-wrap justify-center gap-3">{actions}</div>}

      <CallControls
        micEnabled={!isMute}
        onToggleMic={() => void microphone.toggle()}
        onEndCall={handleLeave}
        recording={recording}
        onToggleRecording={() => void toggleRecording()}
        canRecord={canRecord}
        speakerMuted={speakerMuted}
        onToggleSpeaker={toggleSpeaker}
        cameraEnabled={!cameraOff}
        onToggleCamera={() => void camera.toggle()}
        canUseCamera={liveModality === "video"}
      />

      {!call && <p className="sr-only">Call not ready</p>}
    </div>
  );
}

function ParticipantTile({ participant }: { participant: StreamVideoParticipant }) {
  return <ParticipantView participant={participant} />;
}

/**
 * mm:ss since the call session started, as the design's little blue pill shows.
 * The clock is read inside the interval rather than during render, which keeps the render pure.
 */
function useElapsed(startedAt: string | undefined, running: boolean): string {
  const [seconds, setSeconds] = useState(0);

  useEffect(() => {
    if (!running) {
      return;
    }

    const startedMs = startedAt ? Date.parse(startedAt) : Date.now();

    const timer = window.setInterval(
      () => setSeconds(Math.max(0, Math.floor((Date.now() - startedMs) / 1000))),
      1000,
    );

    return () => window.clearInterval(timer);
  }, [startedAt, running]);

  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}
