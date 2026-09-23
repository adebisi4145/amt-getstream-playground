"use client";

import { useState, type ReactNode } from "react";
import Link from "next/link";
import {
  CallingState,
  StreamCall,
  StreamTheme,
  StreamVideo,
  useCalls,
  type Call,
} from "@stream-io/video-react-sdk";
import "@stream-io/video-react-sdk/dist/css/styles.css";
import { CallScreen } from "@/components/CallScreen";
import { ModalityToggle } from "@/components/ModalityToggle";
import { PillButton } from "@/components/PillButton";
import { api, ApiError, type Modality } from "@/lib/api";
import { DOCTORS, type DemoUser } from "@/lib/demo-users";
import { leaveCallQuietly } from "@/lib/leave-call";
import { switchModality } from "@/lib/switch-modality";
import { useAlert } from "@/lib/useAlert";
import { useStreamClient } from "@/lib/useStreamClient";
import { AlertToggle } from "@/components/AlertToggle";

export default function DoctorPage() {
  const [doctor, setDoctor] = useState<DemoUser>(DOCTORS[0]);
  const { client, error } = useStreamClient(doctor);

  if (error) {
    return (
      <Desk doctor={doctor} onSwitch={setDoctor}>
        <Status title="Can’t reach the service">{error}</Status>
      </Desk>
    );
  }

  // Unlike the other screens this one really does need the connection — it's what receives the
  // ring — so it shows the desk immediately and says it isn't available yet.
  if (!client) {
    return (
      <Desk doctor={doctor} onSwitch={setDoctor}>
        <Status title="Going available…">
          Connecting to Stream. Calls can only ring once this is done.
        </Status>
      </Desk>
    );
  }

  return (
    <StreamVideo client={client}>
      <StreamTheme className="flex min-h-full flex-1 flex-col">
        <ConnectedDesk doctor={doctor} onSwitch={setDoctor} />
      </StreamTheme>
    </StreamVideo>
  );
}

/** Watches for an incoming call. Must live inside StreamVideo, hence the split. */
function ConnectedDesk({ doctor, onSwitch }: { doctor: DemoUser; onSwitch: (user: DemoUser) => void }) {
  const calls = useCalls();
  const [joined, setJoined] = useState<Call | null>(null);
  const [modality, setModality] = useState<Modality>("video");
  const [message, setMessage] = useState<string | null>(null);

  const incoming = calls.find(
    (call) => !call.isCreatedByMe && call.state.callingState === CallingState.RINGING,
  );

  // A doctor between consultations isn't watching this tab, so an incoming call has to be heard.
  useAlert(Boolean(incoming) && !joined, "Incoming consultation", "Triage is bringing you into a call.");

  // The consultation's modality travels in the call's custom data, so the doctor joins the way
  // the patient asked — joining an audio consultation on video would be wrong.
  const accept = async (call: Call) => {
    const chosen: Modality = call.state.custom?.modality === "audio" ? "audio" : "video";

    if (chosen === "audio") {
      await call.camera.disable(true);
    }

    await call.join();
    await call.microphone.enable();

    if (chosen === "video") {
      await call.camera.enable();
    }

    setModality(chosen);
    setJoined(call);
  };

  /** The camera button on an audio consultation: ask for video, then show your own face. */
  const requestVideo = async (call: Call) => {
    setMessage(null);

    try {
      const updated = await switchModality(call, doctor.id, "video");
      setModality(updated.modality);
    } catch (failure) {
      setMessage(
        failure instanceof ApiError ? failure.message : "Could not switch the consultation.",
      );
    }
  };

  /**
   * Triage usually hands over and leaves, so the doctor is last in the room and ends the
   * consultation. Leaving (the red button) only drops the doctor out.
   */
  const complete = async (call: Call) => {
    try {
      await api.complete(call.id, doctor.id);
    } catch (failure) {
      setMessage(failure instanceof ApiError ? failure.message : "Could not complete the consultation.");
    }

    await leaveCallQuietly(call);
    setJoined(null);
  };

  if (joined) {
    return (
      <StreamCall call={joined}>
        <main className="flex flex-1 flex-col p-4">
          <CallScreen
            modality={modality}
            localName={doctor.name}
            waitingLabel="Joining the consultation…"
            callType={joined.type}
            callId={joined.id}
            onLeave={() => {
              void leaveCallQuietly(joined);
              setJoined(null);
            }}
            // The doctor is staff on this consultation too, and is usually the one who needs to see.
            onRequestVideo={() => void requestVideo(joined)}
            actions={
              <>
                <ModalityToggle
                  staffId={doctor.id}
                  fallback={modality}
                  onSwitched={(consultation) => setModality(consultation.modality)}
                  onError={setMessage}
                />
                <PillButton variant="white" onClick={() => void complete(joined)}>
                  Complete consultation
                </PillButton>
              </>
            }
          />
        </main>
      </StreamCall>
    );
  }

  return (
    <Desk doctor={doctor} onSwitch={onSwitch}>
      {message && (
        <p className="rounded-md bg-amt-blue-50 p-3 text-sm text-amt-black" role="status">
          {message}
        </p>
      )}

      {incoming ? (
        <section className="flex flex-col items-center gap-4 rounded-xl border border-amt-blue bg-amt-blue-50 p-8 text-center">
          <h1 className="font-display text-2xl font-semibold text-amt-black">Incoming consultation</h1>
          <p className="text-sm text-amt-black-400">
            Triage is bringing you into a consultation.
          </p>
          <div className="flex gap-3">
            <PillButton onClick={() => void accept(incoming)}>Accept</PillButton>
            <PillButton
              variant="white"
              className="text-amt-error"
              onClick={() => void incoming.leave({ reject: true, reason: "decline" })}
            >
              Decline
            </PillButton>
          </div>
        </section>
      ) : (
        <Status title="Available">
          Keep this tab open. When triage invites you to a consultation, it rings here.
        </Status>
      )}
    </Desk>
  );
}

function Desk({
  doctor,
  onSwitch,
  children,
}: {
  doctor: DemoUser;
  onSwitch: (user: DemoUser) => void;
  children: ReactNode;
}) {
  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-6 p-8">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <Link href="/" className="font-display text-lg font-medium text-amt-blue">
          Doctor
        </Link>
        <AlertToggle />
        <label className="flex items-center gap-2 text-sm text-amt-black-400">
          Signed in as
          <select
            value={doctor.id}
            onChange={(event) => onSwitch(DOCTORS.find((d) => d.id === event.target.value) ?? DOCTORS[0])}
            className="rounded-md border border-amt-grey px-2 py-1 text-amt-black"
          >
            {DOCTORS.map((option) => (
              <option key={option.id} value={option.id}>
                {option.name}
              </option>
            ))}
          </select>
        </label>
      </header>

      {children}
    </main>
  );
}

function Status({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="rounded-xl border border-dashed border-amt-grey p-10 text-center">
      <h1 className="font-display text-2xl font-semibold text-amt-black">{title}</h1>
      <p className="mt-2 text-sm text-amt-black-400">{children}</p>
    </section>
  );
}
