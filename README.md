# amt-getstream-playground

A monorepo for experimenting with [GetStream](https://getstream.io/). It has a Next.js web client and an ASP.NET Core API.

> **Status:** early. The API issues Stream Video user tokens (see [docs/api.md](docs/api.md)). The web app is still the starter template.

## Repository layout

```
.
├── apps/                   # Runnable applications
│   ├── web/                # Next.js 16 (App Router) client (npm workspace)
│   └── api/                # ASP.NET Core (.NET 10) minimal API
│       ├── Amt.GetStream.Playground.slnx
│       ├── src/Amt.GetStream.Api/
│       │   ├── Features/          # Endpoints, one folder per feature
│       │   └── Services/Stream/   # Everything that talks to the Stream SDK
│       └── tests/Amt.GetStream.Api.Tests/
├── packages/               # Shared JS/TS packages (npm workspaces)
├── docs/                   # Project documentation
├── global.json             # Pins the .NET SDK version and test runner
└── package.json            # Root npm workspace config and scripts
```

| Path | Stack | Tooling |
| --- | --- | --- |
| `apps/web` | Next.js 16 (App Router), React 19, TypeScript 5, Tailwind CSS 4 | npm workspaces, ESLint |
| `apps/api` | ASP.NET Core minimal APIs (.NET 10), `getstream-net` 16.0.1, Scalar API reference | `dotnet` CLI, `.slnx` solution, xUnit v3 on Microsoft.Testing.Platform |

### Conventions

- **`apps/`** holds things you run or deploy. **`packages/`** holds code that apps share.
- The npm workspace covers `apps/web` and `packages/*`. The API is a separate .NET solution, but you can run it through the root npm scripts too.
- All JS dependencies install into the root `node_modules`, and there's a single `package-lock.json` at the root. Run `npm install` from the root, not from inside an app.
- New .NET projects go under `apps/api/src/` (tests under `apps/api/tests/`) and get added to `Amt.GetStream.Playground.slnx` (`dotnet sln apps/api/Amt.GetStream.Playground.slnx add <path-to-csproj>`).
- In the API, endpoints live in `Features/<Feature>/` and depend only on service interfaces. Code that uses the Stream SDK stays in `Services/Stream/`.
- See [packages/README.md](packages/README.md) for adding a shared package.

## Prerequisites

- [Node.js](https://nodejs.org/) 24+ (with npm)
- [.NET SDK](https://dotnet.microsoft.com/download) 10.0.100+ (pinned in `global.json`, which accepts newer 10.0.x releases)
- Access to the `amt-getstream-playground` app in the Stream dashboard, for its API secret

## Getting started

```bash
git clone https://github.com/adebisi4145/amt-getstream-playground.git
cd amt-getstream-playground

npm install
dotnet restore apps/api/Amt.GetStream.Playground.slnx
```

Set the Stream API secret before running the API (see [Configuration](#configuration)). Then start each app in its own terminal:

```bash
npm run dev       # web → http://localhost:3000
npm run dev:api   # api → http://localhost:5056
```

In the `Development` environment, the API serves its OpenAPI document at `/openapi/v1.json` and an API reference UI at http://localhost:5056/scalar. [Amt.GetStream.Api.http](apps/api/src/Amt.GetStream.Api/Amt.GetStream.Api.http) has sample requests you can send from VS Code (REST Client) or Visual Studio.

## Configuration

The API reads Stream settings from the `Stream` configuration section.

| Setting | Where it lives |
| --- | --- |
| `Stream:ApiKey` | `appsettings.json`. It's public by design. |
| `Stream:ApiSecret` | **User secrets** locally, or the `Stream__ApiSecret` environment variable elsewhere. Never commit it, and never paste it into chat or issues. |
| `Stream:TokenLifetime` | Optional, defaults to `01:00:00`. |
| `Cors:AllowedOrigins` | `http://localhost:3000` in Development, empty otherwise. |
| `Tokens:AllowUntrustedRequests` | `true` in Development only. It turns on `POST /api/tokens`, which trusts the `userId` it's sent. |

Set the secret once per machine:

```bash
dotnet user-secrets set "Stream:ApiSecret" "<secret from the Stream dashboard>" --project apps/api/src/Amt.GetStream.Api
```

The API won't start without it, and fails with an options-validation error naming `Stream:ApiSecret`.

## Tests

```bash
npm run test:api               # all API tests; Stream integration tests run only if a secret is configured
npm run test:api:integration   # only the tests that call the real Stream app
```

- **Offline tests** use a fake Stream service and never touch the network.
- **Integration tests** (`Category=Integration`) run the user-update rules against the real Stream app, using the secret from user secrets or `Stream__ApiSecret`. Without a secret they're skipped. Run them before any commit that changes `Services/Stream/` or upgrades `getstream-net`.
- **Leftover test users:** each integration test creates a user named `it-<guid>` and hard-deletes it afterwards. If a test run is killed, clean up by searching for `it-` under Users in the Stream dashboard and deleting what's left.
- **Test runner:** tests run on Microsoft.Testing.Platform, enabled in `global.json`, so use `dotnet test --solution …`, not `dotnet test <path>`.

## Scripts

Run these from the repo root:

| Command | What it does |
| --- | --- |
| `npm run dev` | Start the Next.js dev server for `apps/web` |
| `npm run build` | Create a production build of `apps/web` (output in `apps/web/.next`) |
| `npm run lint` | Run ESLint on `apps/web` |
| `npm run dev:api` | Run the API with hot reload (`dotnet watch`) |
| `npm run build:api` | Build the API solution |
| `npm run test:api` | Run the API tests |
| `npm run test:api:integration` | Run only the Stream integration tests |

To run the API on HTTPS (https://localhost:7177), use `dotnet run --project apps/api/src/Amt.GetStream.Api --launch-profile https`.
