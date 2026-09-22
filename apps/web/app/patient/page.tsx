"use client";

import { useCallback, useEffect, useState, useSyncExternalStore } from "react";
import Link from "next/link";
import { StreamCall, StreamTheme, StreamVideo, type Call } from "@stream-io/video-react-sdk";
import "@stream-io/video-react-sdk/dist/css/styles.css";
import { CallScreen } from "@/components/CallScreen";
import { ConsultationModal } from "@/components/ConsultationModal";
import { PillButton } from "@/components/PillButton";
import { api, ApiError, type Consultation, type Modality } from "@/lib/api";
import { ensurePatient, getPatient, getServerPatient, subscribeToPatient } from "@/lib/demo-users";
import { leaveCallQuietly } from "@/lib/leave-call";
import { useStreamClient } from "@/lib/useStreamClient";

/** Matches the API's stale-consultation sweeper, so the button never outlives the consultation. */
const REJOIN_WINDOW_MS = 10 * 60 * 1000;

export default function PatientPage() {
  const patient = useSyncExternalStore(subscribeToPatient, getPatient, getServerPatient);
  const { client, error: clientError } = useStreamClient(patient);

  const [modalOpen, setModalOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [consultation, setConsultation] = useState<Consultation | null>(null);
  const [call, setCall] = useState<Call | null>(null);
  const [openConsultation, setOpenConsultation] = useState<Consultation | null>(null);

  // The patient identity lives in localStorage so a second browser profile is a second person.
  useEffect(ensurePatient, []);

  /**
   * A patient who drops out — hangs up, loses signal, closes the tab — must be able to get back
   * into the consultation they were in. Without this the only button on the page starts a second
   * one while staff are still sitting in the first.
   */
  useEffect(() => {
    if (!patient || call) {
      return;
    }

    let active = true;

    const check = async () => {
      try {
        const [accepted, waiting] = await Promise.all([
          api.board("accepted", patient.id),
          api.board("waiting", patient.id),
        ]);

        if (active) {
          // Only offer to rejoin something recent: a consultation from yesterday is not
          // somewhere a patient should be invited back into.
          const recent = [...accepted, ...waiting].find(
            (item) => Date.now() - new Date(item.requestedAt).getTime() < REJOIN_WINDOW_MS,
          );

          setOpenConsultation(recent ?? null);
        }
      } catch {
        // Offline or the API is down; the home screen still works.
      }
    };

    void check();
    const timer = window.setInterval(() => void check(), 5000);

    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, [patient, call]);

  /** Joins an existing consultation, in the modality it was started with. */
  const joinCall = async (target: Consultation) => {
    if (!client) {
      return;
    }

    const joined = client.call(target.callType, target.callId);

    // Turn the camera off *before* joining: the SDK opens it by default, and on an audio
    // consultation the patient should never see their camera light come on at all.
    if (target.modality === "audio") {
      await joined.camera.disable(true);
    }

    // The API already created the call; the browser only joins it.
    await joined.join();
    await joined.microphone.enable();

    if (target.modality === "video") {
      await joined.camera.enable();
    }

    setConsultation(target);
    setCall(joined);
    setOpenConsultation(null);
  };

  const rejoin = async () => {
    if (!openConsultation) {
      return;
    }

    setError(null);

    try {
      await joinCall(openConsultation);
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : "Could not rejoin the consultation.");
    }
  };

  const start = async (modality: Modality) => {
    if (!client || !patient) {
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const started = await api.startConsultation(patient.id, patient.name, modality);
      await joinCall(started);
      setModalOpen(false);
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : "Could not start the consultation.");
    } finally {
      setBusy(false);
    }
  };

  const leave = useCallback(async () => {
    if (!call || !consultation || !patient) {
      return;
    }

    await leaveCallQuietly(call);

    // Leaving while still waiting is how a patient gives up: it takes them off the board.
    try {
      const latest = await api.getConsultation(consultation.callId);
      if (latest.status === "waiting") {
        await api.cancel(consultation.callId, patient.id);
      }
    } catch {
      // Best effort: the consultation may already be completed or cancelled.
    }

    setCall(null);
    setConsultation(null);
  }, [call, consultation, patient]);

  if (clientError) {
    return <Notice title="Can’t reach the service">{clientError}</Notice>;
  }

  // The home screen needs no Stream connection, so it renders immediately; only the button that
  // starts a call waits for the client to be ready.
  const home = (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-8 p-8">
      <header className="flex items-center justify-between">
        <Link href="/" className="font-display text-lg font-medium text-amt-blue">
          AfricaMedTech
        </Link>
        {/* Name only: the user id is internal plumbing, never shown in a real product. */}
        <span className="text-sm text-amt-black-400">{patient?.name ?? " "}</span>
      </header>

      {/* Figma 13717:43838: blue-50 band, radius 20, with three blue-100 ellipses clipped by it.
          The shapes are quarter-discs (the bottom-right quadrant of a 200px circle), so they're
          drawn in CSS rather than shipped as two SVG files for what is a border-radius. */}
      {/* The way back in after dropping out. Starting a second consultation while staff are
          still waiting in the first is the wrong thing to offer here. */}
      {openConsultation && (
        <section className="flex flex-wrap items-center justify-between gap-4 rounded-xl border border-amt-blue bg-amt-blue-50 p-5">
          <div className="flex flex-col">
            <span className="font-medium text-amt-black">
              {openConsultation.status === "waiting"
                ? "You’re still in the queue"
                : "Your consultation is still open"}
            </span>
            <span className="text-sm text-amt-black-400">
              {openConsultation.status === "waiting"
                ? "A doctor will be with you shortly. Rejoin to wait in the call."
                : "The clinician is still there. Rejoin to carry on."}
            </span>
          </div>

          <PillButton onClick={() => void rejoin()} disabled={!client}>
            Rejoin consultation
          </PillButton>
        </section>
      )}

      {error && !modalOpen && (
        <p className="text-sm font-medium text-amt-error" role="alert">
          {error}
        </p>
      )}

      <section className="relative overflow-hidden rounded-[20px] bg-amt-blue-50 px-15 py-10">
        {/* The call to action lives in the band: in Figma it sits in a toolbar row we don't have,
            and on its own below the band it reads as left over rather than intended. */}
        <div className="relative flex max-w-[400px] flex-col items-start gap-6">
          <h1 className="font-display text-[28px] font-semibold leading-[1.2] text-amt-blue">
            Consult top doctors and book appointments with ease
          </h1>

          <PillButton onClick={() => setModalOpen(true)} disabled={!client || openConsultation !== null}>
            {client ? "Speak to a doctor now" : "Preparing…"}
          </PillButton>
        </div>

        <span aria-hidden className="absolute -top-[18px] right-[188px] size-[100px] rounded-br-full bg-amt-blue-100" />
        <span aria-hidden className="absolute -top-[18px] right-20 size-[100px] rounded-br-full bg-amt-blue-100" />
        <span aria-hidden className="absolute -top-[18px] -right-[25px] size-[100px] rounded-br-full bg-amt-blue-100" />
      </section>
    </main>
  );

  if (!client || !patient) {
    return home;
  }

  return (
    <StreamVideo client={client}>
      <StreamTheme className="flex min-h-full flex-1 flex-col">
        {call && consultation ? (
          <StreamCall call={call}>
            <main className="flex flex-1 flex-col p-4">
              <CallScreen
                modality={consultation.modality}
                localName={patient.name}
                waitingLabel="Connecting you to a doctor…"
                callType={consultation.callType}
                callId={consultation.callId}
                onLeave={() => void leave()}
              />
            </main>
          </StreamCall>
        ) : (
          home
        )}

        {modalOpen && (
          <ConsultationModal
            onConfirm={(modality) => void start(modality)}
            onClose={() => setModalOpen(false)}
            busy={busy}
            error={error}
          />
        )}
      </StreamTheme>
    </StreamVideo>
  );
}

function Notice({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <main className="mx-auto flex w-full max-w-lg flex-col gap-2 p-8">
      <h1 className="font-display text-xl font-medium text-amt-black">{title}</h1>
      <p className="text-sm text-amt-black-400">{children}</p>
      <Link href="/" className="text-sm font-medium text-amt-blue">
        Back
      </Link>
    </main>
  );
}
