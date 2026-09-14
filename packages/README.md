# packages

Shared JavaScript/TypeScript packages used by the apps in `apps/`. Each subfolder is an npm workspace (`packages/*`).

## Adding a package

1. Create `packages/<name>/package.json` with a scoped name, e.g. `"name": "@amt/<name>"`.
2. Run `npm install` at the repo root to link it.
3. Add `"@amt/<name>": "*"` to the consuming app's `dependencies`.
