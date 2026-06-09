---
name: "upstream-sdk-sync"
description: >
  Detect and port changes from the upstream microsoft/mxc TypeScript SDK (the
  sdk/ subtree) into this C# port. Use this skill whenever the user wants to
  check for upstream SDK drift, sync the port with upstream, see what changed in
  the mxc SDK since last time, pull or port the latest microsoft/mxc SDK commits,
  verify the port is up to date, or asks "are we behind upstream" / "what did
  upstream change" / "sync from main". Trigger it even when the user only says
  "sync upstream" or "check mxc" without naming the SDK explicitly — in this repo
  that means the SDK port.
domain: "upstream-sync"
confidence: "medium"
source: "manual"
---

## What this does

This repo, `Sabbour.Mxc.Sdk`, is a hand-written .NET 10 port of the TypeScript
`@microsoft/mxc-sdk` that lives in the `sdk/` subtree of `microsoft/mxc`. Upstream
keeps moving; this skill finds the upstream commits that touched `sdk/` since the
port was last aligned, maps each change to its C# counterpart, and produces a
**sync plan** you can act on.

The port is a behavioral translation, not a mechanical transpile. So the default
output is a review-gated plan — what changed upstream, where it lands in C#, and a
recommended action per file — not a silent code rewrite. Applying the changes is a
separate, explicit step routed through the team.

## State: the sync checkpoint

Sync progress is tracked in `.upstream-sync.json` at the repo root:

```json
{
  "upstreamRepo": "microsoft/mxc",
  "upstreamBranch": "main",
  "upstreamPath": "sdk",
  "lastSyncedCommit": "1736b48398c3fe4d1315b2311c0951cc893eb3ae",
  "lastSyncedDate": "2026-06-08T21:32:26Z",
  "log": [
    { "syncedAt": "...", "fromCommit": "...", "toCommit": "...", "note": "..." }
  ]
}
```

`lastSyncedCommit` is the upstream SHA the C# port currently corresponds to. The
diff is always computed from there to the upstream branch tip. If the file is
missing, the skill helps you establish a baseline before doing anything else —
never guess silently, because a wrong baseline produces either a flood of phantom
changes or a false "all clear".

## Workflow

### 1. Find the drift

Run the helper from the repo root (it reads the checkpoint for you):

```powershell
pwsh .copilot/skills/upstream-sdk-sync/scripts/Get-UpstreamSdkChanges.ps1
```

It emits one JSON object. Three shapes:

- `mode: "needs-baseline"` — no checkpoint yet. It returns the current upstream
  tip and recent SDK-touching commits. Go to step 1a.
- `mode: "diff"` — the net SDK file changes and commit log since the baseline. Go
  to step 2. If `sdkFileCount` is 0, the port is current — say so and stop.
- `mode: "error"` — surface the message (usually `gh` missing or unauthenticated).

`truncated: true` means the comparison hit the API's commit cap (large drift);
fall back to `gh api "repos/{repo}/commits?path=sdk&sha=main&per_page=100"` to
page the full history, and note the truncation in the plan.

#### 1a. Establish a baseline (only when needs-baseline)

The baseline is the upstream commit the port already reflects. Pick deliberately:

- If the port was just brought in line with upstream, the baseline is the upstream
  tip at that moment — running the skill should then report zero drift.
- If you genuinely don't know, the safe move is to diff against the public-release
  commit and review everything since, accepting that some of it may already be in
  the port.

Write the chosen SHA into `.upstream-sync.json`, then re-run step 1. Confirm the
baseline with the user when one isn't already recorded — this is the one decision
the whole sync hinges on.

### 2. Classify and map each change

The helper tags every changed file with a `category`. Handle them by category:

| category | What it is | Default action |
|---|---|---|
| `src` | An `sdk/src/*.ts` module changed | Map to its C# counterpart via `references/module-mapping.md`; this is real logic to port. |
| `unit-test` | An `sdk/tests/unit/*.test.ts` changed | Map to the matching `*ParityTests.cs`; port new/changed cases faithfully. |
| `test-helper` | `test-helpers.ts` changed | Reconcile against `ParityTestHelpers.cs`. |
| `integration-test` | `sdk/tests/integration/**` | Flag for awareness only — needs the native binary; out of scope unless asked. |
| `doc` | `sdk/*.md` (CHANGELOG, README) | Read for intent and version signals; rarely a 1:1 C# edit. |
| `build/config` | lockfiles, tsconfig, npmrc | Note version bumps (especially `SUPPORTED_VERSION` / package `version`); usually no C# edit. |
| `other` | Anything else under `sdk/` | Inspect and judge. |

Read `references/module-mapping.md` for the full upstream-module → C#-file table
and the list of C# files that have no upstream origin (so you never "sync away" a
.NET-only seam just because it's absent upstream).

To see what actually changed in a file, use the patch from the compare API or
fetch both revisions:

```powershell
# raw file at a specific ref
gh api "repos/microsoft/mxc/contents/sdk/src/platform.ts?ref=<sha>" --jq '.content' |
  ForEach-Object { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_)) }
```

### 3. Respect the port's intentional adaptations

Before recommending any C# edit, read `.squad/decisions.md`. The port deliberately
diverges from upstream in documented ways (async APIs, generics over TS conditional
types, `SandboxId<TBackend>`, NuGet.Versioning, source-gen JSON, security seams). A
sync must preserve those adaptations — match upstream **behavior**, not its syntax.
If an upstream change collides with a recorded decision, flag the conflict in the
plan and let the user resolve it; do not quietly overwrite either side.

### 4. Produce the sync plan (default output)

Always emit the plan in this structure:

```
# Upstream SDK sync plan
Baseline <short-sha> (<date>) → upstream tip <short-sha> (<date>)
<N> commits, <M> SDK files changed.

## Commits
- <short-sha> <date> — <subject>
  ...

## Changes by file
| Upstream file | category | C# target(s) | Recommended action |
|---|---|---|---|
| sdk/src/platform.ts | src | Platform/PlatformProber.cs, ... | Port: <one-line of what changed and why> |
| sdk/tests/unit/platform.test.ts | unit-test | Parity/PlatformParityTests.cs | Port N new cases |
| sdk/tests/integration/... | integration-test | — | Flagged: needs native binary, out of scope |

## Conflicts with recorded decisions
<list, or "none">

## Native / out-of-scope context
<Rust/backend changes that rode along, for awareness>

## Recommendation
<port now / review first / nothing to do>
```

Stop here unless the user asked to apply. The plan is the deliverable.

### 5. Apply (only when explicitly asked)

When the user says to port/apply the changes, route through the team rather than
editing inline — this is the same shape as the original port:

- The lead maps and sequences the work from the plan.
- Per-domain agents port `src` changes into the mapped C# files and the matching
  `unit-test` changes into the Parity suite, faithfully — adapting only mechanics
  (async, generics, source-gen), never weakening an assertion.
- When a faithfully ported test exposes a genuine behavioral discrepancy, leave it
  **failing as a visible red flag**; record it in the plan rather than adapting the
  assertion to pass.
- Rebuild with `dotnet build -c Release --no-incremental -t:Rebuild` (a plain
  incremental build hides warnings on a no-op), then `dotnet test`.

### 6. Advance the checkpoint

Only after changes are applied and verified, update `.upstream-sync.json`: set
`lastSyncedCommit`/`lastSyncedDate` to the synced tip and append a `log` entry
(`fromCommit`, `toCommit`, note). In plan-only mode, **do not** advance the
checkpoint — nothing was ported yet, and moving it would make the next run miss
the very changes you just reported.

## Notes

- `gh` must be installed and authenticated (`gh auth status`). The GitHub MCP
  server, if configured, is an equivalent fallback for the same API calls.
- The skill never clones the upstream repo; it reads via the GitHub API. That keeps
  it fast and avoids dragging the large native tree onto disk.
- Scope is the managed SDK only (`sdk/`). Native backend and binary changes are
  reported as context but are not portable into this repo.
