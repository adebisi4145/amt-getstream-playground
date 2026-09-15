# @amt/web

Next.js 16 (App Router) client for the playground, styled with Tailwind CSS 4. Pages live in `app/`, and static files live in `public/`.

Run these from the repo root:

| Command | What it does |
| --- | --- |
| `npm run dev` | Start the dev server at http://localhost:3000 |
| `npm run build` | Create a production build in `apps/web/.next` |
| `npm run lint` | Run ESLint |

To run a command that has no root script, use `npm run <script> --workspace=apps/web`. For example, `npm run start --workspace=apps/web` serves the production build.

Next.js 16 has breaking changes from earlier versions. Its bundled docs are in `node_modules/next/dist/docs/` at the repo root, and [AGENTS.md](AGENTS.md) points AI coding agents there.
