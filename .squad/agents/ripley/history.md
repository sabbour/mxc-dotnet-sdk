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


## 2026-06-08T22:26:04-07:00 — GPT-5.5 fidelity review handoff recorded

Scribe recorded Ripley's report-only fidelity review of the C# port against the upstream TypeScript SDK. Output is `files\fidelity-review.md`; findings were 3 critical, 4 major, 0 minor, plus 9 verified intentional adaptations. No code changes were made from the report in this scribe pass.

## 2026-06-09T20:52:03Z — windows_sandbox rubber-duck review

Performed rubber-duck review of Parker's windows_sandbox containment backend implementation. **Verdict: SOUND**.

Confirmed:
- Both builders (SandboxFactory + PolicyTransform) early-return before stamping other backend sections, matching microvm pattern
- JSON casing correct (camelCase for experimental.windows_sandbox)
- Platform-guard split intentional
- Upstream parity preserved in wire format and behavior

No issues identified. Implementation follows established patterns and maintains fidelity with TypeScript source.

- 2026-06-09T21:35:19-07:00 — Verified .NET SDK parity baseline: upstream v0.6.1 / `161598fd` is ALIGNED-WITH-SUPERSET with only intentional additive supersets (`windows_sandbox`, `UiCapabilities --probe`, pipe-mode convenience).
