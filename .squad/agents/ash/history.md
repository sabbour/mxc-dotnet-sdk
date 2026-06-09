# Project Context

- **Owner:** Ahmed Sabbour
- **Project:** Port the MXC SDK (Microsoft eXecution Containers) from TypeScript/Node.js to .NET 10.
- **Source:** https://github.com/microsoft/mxc/tree/main/sdk (`@microsoft/mxc-sdk` v0.6.1)
- **Stack:** .NET 10, C#, xUnit. Source: TypeScript on Node >=18 (deps: node-pty, semver).
- **Architecture:** Thin policy/config + process-spawn layer over a separate native `wxc-exec` (mxc) binary. No native sandbox code in the SDK itself. Transforms SandboxPolicy -> ContainerConfig, spawns the native binary (pipe via child_process / PTY via node-pty), probes host via registry/dism/`wxc-exec --probe`.
- **Source modules:** index, types, platform, sandbox, policy, errors, logger, helper, diagnostic, state-aware, state-aware-types, state-aware-helper.
- **Created:** 2026-06-08

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->


## 2026-06-08T22:26:04-07:00 — Parity scaffold and test assessment recorded

Scribe recorded Ash's parity scaffold under `tests\Sabbour.Mxc.Sdk.Tests\Parity\` with namespace `Sabbour.Mxc.Sdk.Tests.Parity`, including `ParityTestHelpers.cs` and `ParityScaffoldSmokeTests.cs`. Also recorded Ash's follow-up `test-redundancy-assessment.md` and `parity-red-flags.md` recommendation outputs. Existing 371 tests stayed green during scaffold validation.
