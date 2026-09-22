import Link from "next/link";
import { DOCTORS, TRIAGE_AGENTS } from "@/lib/demo-users";

const ROLES = [
  {
    href: "/patient",
    title: "Patient",
    description: "Tap “Speak to a doctor now”, pick audio or video, and wait for triage to pick up.",
  },
  {
    href: "/triage",
    title: "Triage",
    description: "See who is waiting, take a consultation, then bring a doctor into the call.",
  },
  {
    href: "/doctor",
    title: "Doctor",
    description: "Stay available and answer when triage rings you into a consultation.",
  },
];

export default function Home() {
  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-8 p-8">
      <header className="flex flex-col gap-2">
        <h1 className="font-display text-3xl font-semibold text-amt-black">AMT consultation demo</h1>
        <p className="text-amt-black-400">
          A playground for Stream Video. Open each role in its own browser profile or window to see the
          whole flow: patient → triage → doctor.
        </p>
      </header>

      <div className="grid gap-4 sm:grid-cols-3">
        {ROLES.map((role) => (
          <Link
            key={role.href}
            href={role.href}
            className="flex flex-col gap-2 rounded-xl border border-amt-grey p-5 transition-colors hover:border-amt-blue hover:bg-amt-blue-50"
          >
            <span className="font-display text-xl font-medium text-amt-black">{role.title}</span>
            <span className="text-sm text-amt-black-400">{role.description}</span>
          </Link>
        ))}
      </div>

      <section className="rounded-xl bg-amt-grey-100 p-5 text-sm text-amt-black-400">
        <h2 className="mb-2 font-semibold text-amt-black">Demo identities</h2>
        <p className="mb-3">
          There is no login. The API trusts the id it is sent, so these are stand-ins for real
          authentication. Staff ids must match the API’s configuration.
        </p>
        <ul className="grid gap-1 sm:grid-cols-2">
          {[...TRIAGE_AGENTS, ...DOCTORS].map((user) => (
            <li key={user.id}>
              <span className="font-medium text-amt-black">{user.name}</span> — {user.id} ({user.role})
            </li>
          ))}
          <li>Patients get a generated id per browser profile.</li>
        </ul>
      </section>
    </main>
  );
}
