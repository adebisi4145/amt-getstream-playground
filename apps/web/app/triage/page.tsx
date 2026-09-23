"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { StreamCall, StreamTheme, StreamVideo, type Call } from "@stream-io/video-react-sdk";
import "@stream-io/video-react-sdk/dist/css/styles.css";
import { CallScreen } from "@/components/CallScreen";
import { ModalityToggle } from "@/components/ModalityToggle";
import { PillButton } from "@/components/PillButton";
import { api, ApiError, type Consultation, type ConsultationStatus } from "@/lib/api";
import { DOCTORS, TRIAGE_AGENTS, displayName, type DemoUser } from "@/lib/demo-users";
import { leaveCallQuietly } from "@/lib/leave-call";
import { useAlert } from "@/lib/useAlert";
import { useStreamClient } from "@/lib/useStreamClient";
import { AlertToggle } from "@/components/AlertToggle";

const POLL_MS = 3000;

type Tab = "waiting" | "inProgress" | "history";

/** Each tab maps to the consultation statuses it shows. */
const TABS: Record<Tab, ConsultationStatus[]> = {
  waiting: ["waiting"],
  inProgress: ["accepted"],
  history: ["completed", "cancelled"],
};

const TAB_LABELS: Record<Tab, string> = {
  waiting: "Waiting",
  inProgress: "In progress",
  history: "History",
};

export default function TriagePage() {
  const [agent, setAgent] = useState<DemoUser>(TRIAGE_AGENTS[0]);
  const { client, error: clientError } = useStreamClient(agent);

  const [tab, setTab] = useState<Tab>("waiting");
  const [rows, setRows] = useState<Consultation[]>([]);
  const [consultation, setConsultation] = useState<Consultation | null>(null);
  const [call, setCall] = useState<Call | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  // The API has no push channel — the webhook receiver only logs — so the board polls.
  useEffect(() => {
    if (call) {
      return;
    }

    let active = true;

    const poll = async () => {
      try {
        // History is completed and cancelled together: both are finished, and how they ended
        // is shown per row.
        const statuses = TABS[tab];
        const results = await Promise.all(statuses.map((status) => api.board(status)));

        if (active) {
          setRows(results.flat());
        }
      } catch (failure) {
        if (active) {
          setMessage(failure instanceof ApiError ? failure.message : "Could not load the board.");
        }
      }
    };

    void poll();
    const timer = window.setInterval(() => void poll(), POLL_MS);

    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, [call, tab]);

  /**
   * Takes a waiting consultation, or goes back into one this agent already has.
   *
   * Both are the same request: /accept returns the consultation unchanged for the agent it's
   * already assigned to, so leaving and coming back needs no separate endpoint — and a rejoin
   * still can't step on another agent's consultation, which comes back as 409.
   */
  const joinConsultation = async (target: Consultation) => {
    if (!client) {
      return;
    }

    setMessage(null);

    try {
      const accepted = await api.accept(target.callId, agent.id);
      const joined = client.call(accepted.callType, accepted.callId);

      // The patient chose the modality for the whole consultation, not just for themselves:
      // answering an audio call on video would put them on the spot. On a rejoin this is the
      // modality as it stands now, which staff may have switched while this agent was away.
      if (accepted.modality === "audio") {
        await joined.camera.disable(true);
      }

      await joined.join();
      await joined.microphone.enable();

      if (accepted.modality === "video") {
        await joined.camera.enable();
      }

      setConsultation(accepted);
      setCall(joined);
    } catch (failure) {
      // 409 here is ordinary: someone else took it a moment ago.
      setMessage(
        failure instanceof ApiError ? failure.message : "Could not open that consultation.",
      );
    }
  };

  const invite = async (doctorId: string) => {
    if (!consultation) {
      return;
    }

    setMessage(null);

    try {
      await api.invite(consultation.callId, doctorId);
      setMessage(`Ringing ${displayName(doctorId)}…`);
    } catch (failure) {
      setMessage(failure instanceof ApiError ? failure.message : "Could not invite that doctor.");
    }
  };

  const ringAgain = async (doctorId: string) => {
    if (!consultation) {
      return;
    }

    try {
      await api.ring(consultation.callId, doctorId);
      setMessage(`Ringing ${displayName(doctorId)} again…`);
    } catch (failure) {
      setMessage(failure instanceof ApiError ? failure.message : "Could not ring that doctor.");
    }
  };

  /**
   * Triage drops out; the call carries on without them.
   * This is the handover: triage introduces the doctor, then leaves them with the patient.
   * The consultation stays `accepted`, and whoever is left completes it.
   */
  const leave = useCallback(async () => {
    if (!call) {
      return;
    }

    await leaveCallQuietly(call);
    setCall(null);
    setConsultation(null);
  }, [call]);

  /** Ends the consultation for everyone. The deliberate "this is finished" action. */
  const complete = useCallback(async () => {
    if (!call || !consultation) {
      return;
    }

    try {
      await api.complete(consultation.callId, agent.id);
    } catch (failure) {
      setMessage(failure instanceof ApiError ? failure.message : "Could not complete the consultation.");
    }

    await leaveCallQuietly(call);
    setCall(null);
    setConsultation(null);
  }, [agent.id, call, consultation]);

  // Ring while someone is waiting and this agent is free to take them.
  const waitingCount = tab === "waiting" ? rows.length : 0;
  useAlert(
    !call && waitingCount > 0,
    waitingCount === 1 ? "1 patient waiting" : `${waitingCount} patients waiting`,
    "A patient is waiting for triage.",
  );

  if (clientError) {
    return <Notice title="Can’t reach the service">{clientError}</Notice>;
  }

  const board = (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-6 p-8">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <Link href="/" className="font-display text-lg font-medium text-amt-blue">
          Triage board
        </Link>
        <AlertToggle />
        <label className="flex items-center gap-2 text-sm text-amt-black-400">
          Signed in as
          <select
            value={agent.id}
            onChange={(event) =>
              setAgent(TRIAGE_AGENTS.find((a) => a.id === event.target.value) ?? TRIAGE_AGENTS[0])
            }
            className="rounded-md border border-amt-grey px-2 py-1 text-amt-black"
          >
            {TRIAGE_AGENTS.map((option) => (
              <option key={option.id} value={option.id}>
                {option.name}
              </option>
            ))}
          </select>
        </label>
      </header>

      {message && (
        <p className="rounded-md bg-amt-blue-50 p-3 text-sm text-amt-black" role="status">
          {message}
        </p>
      )}

      <nav className="flex gap-1 border-b border-amt-grey">
        {(Object.keys(TABS) as Tab[]).map((option) => (
          <button
            key={option}
            type="button"
            onClick={() => setTab(option)}
            className={`-mb-px border-b-2 px-4 py-2 text-sm font-medium transition-colors ${
              tab === option
                ? "border-amt-blue text-amt-blue"
                : "border-transparent text-amt-black-400 hover:text-amt-black"
            }`}
          >
            {TAB_LABELS[option]}
            {tab === option ? ` (${rows.length})` : ""}
          </button>
        ))}
      </nav>

      <section className="flex flex-col gap-3">
        {rows.length === 0 ? (
          <p className="rounded-xl border border-dashed border-amt-grey p-8 text-center text-sm text-amt-black-400">
            {EMPTY_MESSAGES[tab]}
          </p>
        ) : (
          <ul className="flex flex-col gap-2">
            {rows.map((item) => (
              <li
                key={item.callId}
                className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-amt-grey p-4"
              >
                <div className="flex flex-col">
                  <span className="font-medium text-amt-black">{item.patientName ?? "Patient"}</span>
                  <span className="text-sm text-amt-black-400">{describe(item)}</span>
                </div>

                {tab === "waiting" && (
                  // Accepting joins the Stream call, so it waits for the client.
                  <PillButton onClick={() => void joinConsultation(item)} disabled={!client}>
                    {client ? "Accept" : "Preparing…"}
                  </PillButton>
                )}

                {tab === "inProgress" &&
                  // Triage hands over to a doctor and drops out, or loses the tab. The consultation
                  // is still theirs and still running, so there has to be a way back in. Someone
                  // else's consultation stays a label: taking it over is a different decision.
                  (item.assignedTo === agent.id ? (
                    <PillButton onClick={() => void joinConsultation(item)} disabled={!client}>
                      {client ? "Rejoin" : "Preparing…"}
                    </PillButton>
                  ) : (
                    <span className="text-sm text-amt-black-300">
                      with {item.assignedTo ? displayName(item.assignedTo) : "a colleague"}
                    </span>
                  ))}

                {tab === "history" && <EndTag item={item} />}
              </li>
            ))}
          </ul>
        )}
      </section>
    </main>
  );

  // The board is plain API polling, so it shows straight away; only Accept needs Stream.
  if (!client) {
    return board;
  }

  return (
    <StreamVideo client={client}>
      <StreamTheme className="flex min-h-full flex-1 flex-col">
        {call && consultation ? (
          <StreamCall call={call}>
            <main className="flex flex-1 flex-col gap-3 p-4">
              <CallScreen
                modality={consultation.modality}
                localName={agent.name}
                waitingLabel={`Waiting for ${consultation.patientName ?? "the patient"}…`}
                callType={consultation.callType}
                callId={consultation.callId}
                onLeave={() => void leave()}
                actions={
                  <>
                    <ModalityToggle
                      callId={consultation.callId}
                      staffId={agent.id}
                      fallback={consultation.modality}
                      onSwitched={setConsultation}
                      onError={setMessage}
                    />
                    {DOCTORS.map((doctor) => (
                      <PillButton
                        key={doctor.id}
                        variant="white"
                        onClick={() =>
                          void (consultation.memberIds.includes(doctor.id)
                            ? ringAgain(doctor.id)
                            : invite(doctor.id))
                        }
                      >
                        {consultation.memberIds.includes(doctor.id)
                          ? `Ring ${doctor.name} again`
                          : `Invite ${doctor.name}`}
                      </PillButton>
                    ))}
                    {/* End call only drops triage out; finishing the consultation is deliberate. */}
                    <PillButton onClick={() => void complete()}>Complete consultation</PillButton>
                  </>
                }
              />
            </main>
          </StreamCall>
        ) : (
          board
        )}
      </StreamTheme>
    </StreamVideo>
  );
}

const EMPTY_MESSAGES: Record<Tab, string> = {
  waiting: "Nobody is waiting. Start a consultation from the patient screen.",
  inProgress: "No consultations are in progress.",
  history: "No consultations have finished yet.",
};

/** The second line of a row: what matters differs by status. */
function describe(item: Consultation): string {
  const reason = item.reason ? ` · ${item.reason}` : "";

  if (item.status === "waiting") {
    return `${item.modality} · waiting ${elapsed(item.requestedAt)}${reason}`;
  }

  if (item.status === "accepted") {
    return `${item.modality} · started ${elapsed(item.acceptedAt ?? item.requestedAt)} ago${reason}`;
  }

  return `${item.modality} · ${new Date(item.requestedAt).toLocaleString()}${reason}`;
}

/**
 * How it ended, not just that it ended: a consultation closed with the patient already gone is
 * clinically different from one that ran to the end, and it needs following up.
 */
function EndTag({ item }: { item: Consultation }) {
  const label =
    item.endReason === "patient_left"
      ? "patient left"
      : item.endReason === "patient_cancelled"
        ? "gave up waiting"
        : item.status === "completed"
          ? "finished"
          : item.status;

  // Anything that didn't run to the end is worth noticing, so it isn't styled like a success.
  const needsAttention = item.endReason === "patient_left";

  const style = needsAttention
    ? "bg-amt-error/10 text-amt-error"
    : item.status === "completed"
      ? "bg-amt-blue-50 text-amt-blue"
      : "bg-amt-grey-100 text-amt-black-400";

  return <span className={`rounded-[32px] px-3 py-1 text-xs font-semibold ${style}`}>{label}</span>;
}

function elapsed(from: string): string {
  const seconds = Math.max(0, Math.round((Date.now() - new Date(from).getTime()) / 1000));

  return seconds < 60 ? `${seconds}s` : `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
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
