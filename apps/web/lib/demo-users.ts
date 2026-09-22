/**
 * Demo identities.
 *
 * There is no login: the API trusts whatever id it's sent, and the staff ids below must match
 * Consultations:Staff in apps/api/.../appsettings.Development.json. Everything here is a stand-in
 * for real authentication, which has to exist before any of this shape is used for real.
 */

export interface DemoUser {
  id: string;
  name: string;
  role: "patient" | "triage" | "doctor";
}

export const TRIAGE_AGENTS: DemoUser[] = [
  { id: "triage-001", name: "Amaka Obi", role: "triage" },
  { id: "triage-002", name: "Tunde Bello", role: "triage" },
];

export const DOCTORS: DemoUser[] = [
  { id: "doctor-001", name: "Adebayo Uche", role: "doctor" },
  { id: "doctor-002", name: "Ruth Alamina", role: "doctor" },
];

const PATIENT_STORAGE_KEY = "amt-demo-patient";

/**
 * One patient identity per browser profile, so two windows behave like two different people.
 *
 * It lives in a tiny store rather than component state because the id can only be read in the
 * browser: resolving it during render would break hydration, and setting it from an effect
 * triggers a cascading render (which the React lint rules rightly reject).
 */
let patient: DemoUser | null = null;
const listeners = new Set<() => void>();

export function ensurePatient(): void {
  if (patient) {
    return;
  }

  const created: DemoUser = {
    id: `patient-${Math.random().toString(36).slice(2, 8)}`,
    name: "Daniel Okafor",
    role: "patient",
  };

  try {
    const stored = window.localStorage.getItem(PATIENT_STORAGE_KEY);
    patient = stored ? (JSON.parse(stored) as DemoUser) : created;

    if (!stored) {
      window.localStorage.setItem(PATIENT_STORAGE_KEY, JSON.stringify(created));
    }
  } catch {
    // Storage blocked (private window); a fresh id is still fine for one session.
    patient = created;
  }

  listeners.forEach((listener) => listener());
}

export function subscribeToPatient(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function getPatient(): DemoUser | null {
  return patient;
}

/** Nothing is known about the patient while rendering on the server. */
export function getServerPatient(): DemoUser | null {
  return null;
}

export function findDemoUser(userId: string): DemoUser | undefined {
  return [...TRIAGE_AGENTS, ...DOCTORS].find((user) => user.id === userId);
}

export function displayName(userId: string): string {
  return findDemoUser(userId)?.name ?? userId;
}

export function initials(name: string): string {
  return name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? "")
    .join("");
}
