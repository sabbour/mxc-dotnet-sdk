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


## 2026-06-08T22:26:04-07:00 — Platform and sandbox parity tests recorded

Scribe recorded Parker's port of upstream platform and sandbox unit tests into the parity suite. Outcome: 110 parity cases and 2 intentional Windows network-validation red flags: processcontainer `allowedHosts`/`blockedHosts` without `allowOutbound` currently builds in C# while upstream TypeScript throws, corroborating fidelity finding C3.

- 2026-06-09T11:04:37-07:00: `examples/` landed with five console samples and the repository received its initial commit (`7f02054`) on `main`.

- 2026-06-09T11:30:46-07:00: Added examples 06-09 from upstream docs scenarios; backend probe found only ARM64 Windows AppContainer/processcontainer at schema 0.4.0-alpha fully launches, while 0.6.0-alpha processcontainer fails E_NOTIMPL and other probed backends are unavailable/unsupported/host-absent.
