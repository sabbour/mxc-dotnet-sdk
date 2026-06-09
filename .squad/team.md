# Squad Team

> mxc-dotnet-sdk — porting `@microsoft/mxc-sdk` (TypeScript) to .NET 10

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Ripley | Lead / Architect | .squad/agents/ripley/charter.md | Active |
| Parker | Systems Dev | .squad/agents/parker/charter.md | Active |
| Brett | Core Dev | .squad/agents/brett/charter.md | Active |
| Ash | Tester | .squad/agents/ash/charter.md | Active |
| Scribe | Session Logger | .squad/agents/scribe/charter.md | Silent |
| Ralph | Work Monitor | .squad/agents/ralph/charter.md | Monitor |
| Rai | RAI Reviewer | .squad/agents/Rai/charter.md | Background |

## Project Context

- **Owner:** Ahmed Sabbour
- **Project:** Port the MXC SDK (Microsoft eXecution Containers) from TypeScript/Node.js to .NET 10.
- **Source:** https://github.com/microsoft/mxc/tree/main/sdk (`@microsoft/mxc-sdk` v0.6.1)
- **Stack:** .NET 10, C#, xUnit. Source is TypeScript on Node >=18 (deps: node-pty, semver).
- **Architecture note:** The SDK is a thin policy/config + process-spawn layer over a separate native `wxc-exec` (mxc) binary. It does not contain native sandbox code — it transforms SandboxPolicy -> ContainerConfig, spawns the native binary (pipe via child_process / PTY via node-pty), and probes the host (registry / dism / `wxc-exec --probe`).
- **Created:** 2026-06-08
