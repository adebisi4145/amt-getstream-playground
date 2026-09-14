# amt-getstream-playground

A monorepo for experimenting with [GetStream](https://getstream.io/). It has a React web client and an ASP.NET Core API.

> **Status:** early scaffold. Both apps are still close to their starter templates.

## Repository layout

```
.
├── apps/                   # Runnable applications
│   ├── web/                # React 19 + TypeScript + Vite client (npm workspace)
│   └── api/                # ASP.NET Core (.NET 10) minimal API
│       ├── Amt.GetStream.Playground.slnx
│       └── src/Amt.GetStream.Api/
├── packages/               # Shared JS/TS packages (npm workspaces)
├── docs/                   # Project documentation
├── global.json             # Pins the .NET SDK version
└── package.json            # Root npm workspace config and scripts
```

| Path | Stack | Tooling |
| --- | --- | --- |
| `apps/web` | React 19, TypeScript 6, Vite 8 | npm workspaces, ESLint |
| `apps/api` | ASP.NET Core, .NET 10 | `dotnet` CLI, `.slnx` solution |

### Conventions

- **`apps/`** holds things you run or deploy. **`packages/`** holds code that apps share.
- The npm workspace covers `apps/web` and `packages/*`. The API is a separate .NET solution, but you can run it through the root npm scripts too.
- All JS dependencies install into the root `node_modules`, and there's a single `package-lock.json` at the root. Run `npm install` from the root, not from inside an app.
- New .NET projects go under `apps/api/src/` and get added to `Amt.GetStream.Playground.slnx` (`dotnet sln apps/api/Amt.GetStream.Playground.slnx add <path-to-csproj>`).
- See [packages/README.md](packages/README.md) for adding a shared package.

## Prerequisites

- [Node.js](https://nodejs.org/) 24+ (with npm)
- [.NET SDK](https://dotnet.microsoft.com/download) 10.0.100+ (pinned in `global.json`, which accepts newer 10.0.x releases)

## Getting started

```bash
git clone <repo-url>
cd amt-getstream-playground

npm install
dotnet restore apps/api/Amt.GetStream.Playground.slnx
```

Then start each app in its own terminal:

```bash
npm run dev       # web → http://localhost:5173
npm run dev:api   # api → http://localhost:5056
```

In the `Development` environment, the API serves its OpenAPI document at `/openapi/v1.json`. [Amt.GetStream.Api.http](apps/api/src/Amt.GetStream.Api/Amt.GetStream.Api.http) has sample requests you can send from VS Code (REST Client) or Visual Studio.

## Scripts

Run these from the repo root:

| Command | What it does |
| --- | --- |
| `npm run dev` | Start the Vite dev server for `apps/web` |
| `npm run build` | Type-check and build `apps/web` to `apps/web/dist` |
| `npm run lint` | Run ESLint on `apps/web` |
| `npm run dev:api` | Run the API with hot reload (`dotnet watch`) |
| `npm run build:api` | Build the API solution |

To run the API on HTTPS (https://localhost:7177), use `dotnet run --project apps/api/src/Amt.GetStream.Api --launch-profile https`.
