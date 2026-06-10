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

- 2026-06-08: Locked architecture for the .NET 10 MXC SDK port: SDK remains a thin policy/config + spawn layer over native `wxc-exec`; PTY uses Porta.Pty behind `IPtyConnection`; full tri-platform parity (Windows/Linux/macOS).


## 2026-06-08T22:26:04-07:00 — Core parity tests recorded

Scribe recorded Brett's port of upstream errors, logger, policy, state-aware, and state-aware-types unit tests into the parity suite. Outcome: 70 parity cases, 0 red flags, and test-only adaptations for C# type assertions, cancellation-token behavior, and RuntimeInformation-bound policy checks.

## 2026-06-09T20:52:03Z — windows_sandbox documentation

Updated README.md with windows_sandbox containment backend documentation:
- Described windows_sandbox backend and its Windows-specific nature
- Documented elevation requirement (Administrator privileges needed)
- Added reboot requirement note after enabling Windows Sandbox feature
- Documented experimental.windows_sandbox configuration schema (idleTimeoutMs, daemonPipeName) with defaults
- Cross-referenced examples/11-windows-sandbox for working code sample

Documentation verified accurate by Coordinator's end-to-end testing on Windows 11 ARM64 build 26200.

- 2026-06-09T21:35:19-07:00 — Verified .NET SDK parity baseline: upstream v0.6.1 / `161598fd` is ALIGNED-WITH-SUPERSET with only intentional additive supersets (`windows_sandbox`, `UiCapabilities --probe`, pipe-mode convenience).
