# Work Routing

How to decide who handles what for the MXC SDK .NET 10 port.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Solution layout, public API surface, packaging, scope | Ripley | Project structure, namespaces, NuGet metadata, TS->C# module map, architecture decisions |
| Sandbox lifecycle, platform probes, native interop, PTY | Parker | sandbox.ts, platform.ts, diagnostic.ts, ConPTY/process spawn, node-pty replacement |
| Types, policy engine, state-aware model, errors, logging | Brett | types.ts, policy.ts, state-aware*.ts, errors.ts, logger.ts, helper.ts |
| Testing & parity | Ash | Port unit + integration suites to xUnit, behavioral parity, edge cases |
| Code review | Ripley | Review the public surface and behavioral parity against the source |
| Scope & priorities | Ripley | What to build next, trade-offs, decisions |
| Session logging | Scribe | Automatic — never needs routing |
| RAI review | Rai | Content safety, bias checks, credential detection, ethical review |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Ripley (Lead) |
| `squad:{name}` | Pick up issue and complete the work | Named member |

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts -> coordinator answers directly.** Don't spawn an agent for trivial lookups.
4. **When two agents could handle it**, pick the one whose domain is the primary concern. Types/policy -> Brett; anything touching the OS or the native binary -> Parker.
5. **"Team, ..." -> fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** When a module is being ported, spawn Ash to port its source tests in parallel.
7. **Reviewer gate.** Ripley reviews the public API surface; Ash gates behavioral parity. On rejection, a different agent revises.
