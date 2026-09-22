/**
 * Typed client for the playground API (apps/api).
 *
 * The API answers with ProblemDetails (RFC 9457), so failures are turned into ApiError with the
 * server's own message. 403 and 409 are normal here — "you're not triage", "someone already took
 * that consultation" — and the UI shows them, so they must not be swallowed as generic failures.
 */

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5056";

export type Modality = "audio" | "video";

export type ConsultationStatus = "waiting" | "accepted" | "completed" | "cancelled";

export interface TokenResponse {
  apiKey: string;
  userId: string;
  token: string;
  expiresAt: string;
}

export interface Consultation {
  callType: string;
  callId: string;
  cid: string;
  patientId: string;
  /** Display name. Staff screens show this; the id is internal. */
  patientName: string | null;
  modality: Modality;
  reason: string | null;
  status: ConsultationStatus;
  /** How it ended: "finished", "patient_left" or "patient_cancelled". Null while still open. */
  endReason: string | null;
  assignedTo: string | null;
  requestedAt: string;
  acceptedAt: string | null;
  endedAt: string | null;
  memberIds: string[];
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = "ApiError";
  }

  /** The consultation was in the wrong state — e.g. already accepted by someone else. */
  get isConflict() {
    return this.status === 409;
  }

  /** A permission rule refused this — e.g. a patient trying to accept. */
  get isForbidden() {
    return this.status === 403;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;

  try {
    response = await fetch(`${API_URL}${path}`, {
      ...init,
      headers: {
        ...(init?.body ? { "Content-Type": "application/json" } : {}),
        ...init?.headers,
      },
    });
  } catch {
    // Almost always the API isn't running, or CORS blocked it. Say so rather than "fetch failed".
    throw new ApiError(0, `Could not reach the API at ${API_URL}. Is it running (npm run dev:api)?`);
  }

  if (!response.ok) {
    throw new ApiError(response.status, await describeFailure(response));
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

async function describeFailure(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as ProblemDetails;

    // Validation problems carry per-field messages, which are far more useful than the title.
    const fieldErrors = Object.values(problem.errors ?? {}).flat();
    if (fieldErrors.length > 0) {
      return fieldErrors.join(" ");
    }

    return problem.detail ?? problem.title ?? `Request failed (${response.status}).`;
  } catch {
    return `Request failed (${response.status}).`;
  }
}

export const api = {
  createToken: (userId: string, name?: string) =>
    request<TokenResponse>("/api/tokens", {
      method: "POST",
      body: JSON.stringify({ userId, name }),
    }),

  startConsultation: (patientId: string, patientName: string, modality: Modality, reason?: string) =>
    request<Consultation>("/api/consultations", {
      method: "POST",
      body: JSON.stringify({ patientId, patientName, modality, reason }),
    }),

  board: (status: ConsultationStatus = "waiting", patientId?: string) =>
    request<Consultation[]>(
      `/api/consultations?status=${status}${patientId ? `&patientId=${encodeURIComponent(patientId)}` : ""}`,
    ),

  getConsultation: (callId: string) => request<Consultation>(`/api/consultations/${callId}`),

  accept: (callId: string, staffId: string) =>
    request<Consultation>(`/api/consultations/${callId}/accept`, {
      method: "POST",
      body: JSON.stringify({ staffId }),
    }),

  invite: (callId: string, doctorId: string) =>
    request<Consultation>(`/api/consultations/${callId}/invite`, {
      method: "POST",
      body: JSON.stringify({ doctorId }),
    }),

  ring: (callId: string, doctorId: string) =>
    request<void>(`/api/consultations/${callId}/ring`, {
      method: "POST",
      body: JSON.stringify({ doctorId }),
    }),

  complete: (callId: string, staffId: string) =>
    request<Consultation>(`/api/consultations/${callId}/complete`, {
      method: "POST",
      body: JSON.stringify({ staffId }),
    }),

  cancel: (callId: string, patientId: string) =>
    request<Consultation>(`/api/consultations/${callId}/cancel`, {
      method: "POST",
      body: JSON.stringify({ patientId }),
    }),

  startRecording: (callType: string, callId: string) =>
    request<void>(`/api/calls/${callType}/${callId}/recordings/start`, { method: "POST" }),

  stopRecording: (callType: string, callId: string) =>
    request<void>(`/api/calls/${callType}/${callId}/recordings/stop`, { method: "POST" }),
};
