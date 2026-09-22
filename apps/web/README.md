# @amt/web

Next.js 16 (App Router) demo of the AMT consultation flow, talking to the playground API in `apps/api`.

## Running the demo

Two terminals, both from the repo root:

```bash
npm run dev:api   # API on http://localhost:5056 (needs the Stream secret in user secrets)
npm run dev       # this app on http://localhost:3000
```

`NEXT_PUBLIC_API_URL` points at the API and defaults to `http://localhost:5056`. Copy `.env.example` to `.env.local` to change it. It holds no secrets: the Stream **API secret never reaches the browser**, only the public key and a user token issued by the API.

## Routes

| Route | Who | What it does |
| --- | --- | --- |
| `/` | — | Role picker and the demo identities |
| `/patient` | Patient | "Speak to a doctor now" → pick **Audio call** or **Video call** → connects immediately and waits for triage |
| `/triage` | Triage | Board of waiting consultations (polled every 3s), accept, join, invite a doctor, ring again, complete |
| `/doctor` | Doctor | Stays connected so triage can ring them in; accept or decline the incoming call |

**Open each role in its own browser profile or window.** The patient identity is stored per profile, so two normal tabs would be the same patient.

## How it fits together

- **`lib/api.ts`** — typed client for the API. It reads ProblemDetails, so a `403` ("you're not triage") or `409` ("someone already took it") arrives as a readable message instead of a generic failure.
- **`lib/useStreamClient.ts`** — connects a Stream client for a demo user, with a `tokenProvider` that calls `POST /api/tokens`. The doctor page keeps this connected, which is what lets an incoming call ring.
- **`components/CallScreen.tsx`** — the call UI from Figma: gradient, avatar or video, elapsed timer, PiP tile, control bar.

## Known gaps

- **No login.** Ids come from the browser and the API trusts them. Real authentication has to come first in the product.
- **Options** (`…`) is disabled: the Figma flow defines no behaviour for it.
- **Record** is disabled until someone else joins, because Stream refuses to record a call with no session.
- **Payment** from the design is skipped; this is a demo.

## Design

Rebuilt from Figma `HbR89xTbi1tRVOrDYueq5d` (section `11347:20133`). Tokens live in `app/globals.css` as `--color-amt-*`; Work Sans and Geist are loaded with `next/font`; icons come from `react-icons/md`, as the design does.
