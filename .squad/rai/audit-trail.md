# RAI Audit Trail

> Append-only evidence log. Entries are redacted — never contains raw secrets or harmful content.

<!-- Rai appends findings below -->

## 2026-06-08T16:06:29-07:00 — MXC SDK .NET 10 P1 typed/data layer + logger review

Reviewer: Rai
Requested by: Ahmed Sabbour
Scope: src/Sabbour.Mxc.Sdk typed DTOs, errors, diagnostics/logger, JSON context; tests secret scan.
Verdict: 🟡 Yellow — advisory, proceed with recommendations.

Evidence reviewed: .squad/agents/Rai/charter.md; .squad/rai/policy.md; Enums.cs; SandboxPolicy.cs; ContainerConfig.cs; PlatformSupport.cs; Errors/ErrorCode.cs; Errors/MxcException.cs; Diagnostics/IMxcLogger.cs; Diagnostics/FileLogger.cs; Diagnostics/DiagnosticLog.cs; MxcJsonContext.cs; tests under tests/.

Redacted findings:
- No hardcoded credentials or secret-looking test values found by keyword/entropy scan. A sample proxy URL in tests is non-secret placeholder.
- Advisory: Diagnostics logging accepts raw messages/metadata and writes them to disk or pipe without redaction/size bounds. This is currently explicit/opt-in, but could leak future secret-bearing config fields or local paths if whole objects are logged.
- Advisory: FileLogger accepts an arbitrary caller-provided path and creates parent directories. Treat the path as trusted-only or validate/constrain it before wiring to external config.
- Advisory: FileLogger open-failure warning includes the full supplied path and exception text; redact path details if paths may contain user/profile identifiers.
- Advisory: MxcException preserves raw wire message/details; avoid automatically logging or returning details without redaction when native/backend errors include host information.

## 2026-06-08T16:06:29-07:00 — Rai review — P2 policy transform/discovery

Reviewer: Rai
Requested by: [redacted]
Scope: src/Sabbour.Mxc.Sdk/Policy/{PolicyTransform,PolicyDiscovery,FilesystemPolicyResult,ToolsPolicyOptions}.cs; src/Sabbour.Mxc.Sdk/Internal/{VersionHelper,NetworkPolicyHelper}.cs; test glance for committed secrets.
Verdict: 🟡 Yellow

Findings:
- PolicyDiscovery accepts environment-controlled paths into filesystem policy fragments that can flow into ContainerConfig/wxc-exec. Most tool paths are canonicalized and existence-checked, but USERPROFILE-derived PowerShell PSReadLine and TEMP/TMP/TMPDIR outputs are not consistently canonicalized/validated before being returned.
- Discovery intentionally exposes local absolute tool/profile/temp paths; callers should avoid logging serialized configs or sending them outside the local trust boundary because paths may contain usernames/project names. No hardcoded secrets found in reviewed code/tests.
Validation: dotnet test passed (186 total tests).

## 2026-06-08T16:06:29-07:00 — Rai review — P3 platform probing

Reviewer: Rai
Requested by: [redacted]
Scope: src/Sabbour.Mxc.Sdk/Platform/{DefaultPlatformProbeRunner,DefaultWindowsBuildQuery,IPlatformProbeRunner,IWindowsBuildQuery,PlatformSupportCache,PlatformProber}.cs; tests/Sabbour.Mxc.Sdk.Tests/PlatformProbeTests.cs secret glance.
Verdict: 🟡 Yellow

Findings:
- External probing launches resolved binaries from environment/PATH or unqualified names. The native probe path can come from MXC_BIN_DIR or PATH, and helper tools are invoked by name; a caller-controlled environment or planted earlier PATH entry can cause untrusted binary execution. This appears faithful to the source trust boundary, but document it and prefer pinned/validated paths for OS tools where possible.
- ProcessStartInfo uses single-string Arguments for probe/tool/registry invocations. Current production arguments are constants for tool checks and fixed registry keys, and UseShellExecute=false avoids shell metacharacter execution, but ArgumentList is the safer .NET idiom and prevents future argument-injection regressions.
- Probe/tool/registry stdout is read without a size cap, while stderr is redirected but not drained. A malicious or noisy resolved binary can hang the caller or consume memory, making the timeout less reliable. Read both streams asynchronously with cancellation and byte limits.
- Registry access is read-only and parsed as data; no registry writes or eval-style use found. No hardcoded credentials found in the reviewed platform tests by keyword scan/manual glance.
### 2026-06-08T16:06:29-07:00 — P4 spawn + PTY RAI/security review
**Reviewer:** Rai
**Requested by:** Ahmed Sabbour
**Scope:** `src/Sabbour.Mxc.Sdk/Sandbox/` spawn/PTY execution path and secret scan of tests.
**Verdict:** 🟡 Advisory findings; no confirmed shell command injection or committed real secrets.

**Redacted notes:**
- Argv construction uses separate argv values in pipe mode and no SDK logging of full config/env/base64 was found.
- Executor resolution can run caller/environment-selected binaries (`ExecutablePath`, `MXC_BIN_DIR`, `PATH` fallback); treat this as a trusted host boundary and consider reducing PATH/MXC override exposure.
- One-shot PTY and pipe captures buffer process output without a cap; error-envelope message/details are surfaced verbatim to `MxcException`.
- Process/PTY cancellation/exit handling has availability risks: pipe-mode exit event subscription race; PTY wait cancellation mutates the shared exit task.
- Secret scan found only placeholder test literals, not real committed credentials.

## 2026-06-08T16:06:29-07:00 — P5 state-aware lifecycle RAI/security review

Reviewer: Rai
Requested by: Ahmed Sabbour
Scope: `src/Sabbour.Mxc.Sdk/StateAware/` plus `SpawnHelper.PrepareSpawnFromJson` and StateAware tests.
Verdict: 🔴 Red

Redacted summary:
- Confirmed no committed real secrets found in P5 tests; only placeholder tokens/UPNs are used.
- `IsolationSessionUserConfig.ToString()` redacts `wamToken`, and nested record `ToString()` uses that override.
- No direct logger call in `DefaultStateAwareSpawnRunner`, and the spawn path uses `UseShellExecute=false` plus `ArgumentList`.
- Blocking issues: state-aware response parsing can include raw stdout/error-envelope message/details in exceptions without `wamToken` redaction or P4 caps; state-aware buffered runner uses unbounded `ReadToEndAsync`; no built-in `wamToken` redaction seam was found for logger diagnostics if an envelope is logged later.
- Documented risk: `--config-base64` necessarily places the envelope, including redacted-here `wamToken`, in process args to match the existing/TS wire protocol; local process command-line visibility remains an inherent exposure until the protocol changes.

## 2026-06-08T20:51:43-07:00 — P6 Facade / Packaging / Docs RAI/security review

Reviewer: Rai
Requested by: Ahmed Sabbour
Scope: `MxcSdk.cs` static facade, README.md, XML doc summaries, internal demotions, NuGet packaging.
Verdict: 🟢 Green — PASS

Findings:
1. Facade leak paths: PASS — pure thin delegation, zero logging/exception construction, all routes through redacted instance paths.
2. README / XML docs: PASS — no example logs wamToken or prints full config; logging section documents automatic redaction.
3. Visibility / accessibility: PASS — IsolationSessionUserConfig.ToString() still redacts wamToken; TokenRedactor remains internal.
4. Prior items regression: PASS — UseShellExecute=false (4 sites), ArgumentList (3 sites), no committed secrets.

Residual advisory: --config-base64 token-in-args (SpawnHelper.cs:139); faithful TS port, mitigated by short-lived token.

## 2026-06-08T22:26:04-07:00 — Parity test-file fast-path RAI review

Reviewer: Rai
Requested by: [redacted]
Scope: `tests\Sabbour.Mxc.Sdk.Tests\Parity\ParityTestHelpers.cs`; `ErrorsParityTests.cs`; `LoggerParityTests.cs`; `PolicyParityTests.cs`; `StateAwareTypesParityTests.cs`; `StateAwareParityTests.cs`; `PlatformParityTests.cs`; `SandboxParityTests.cs`; `ParityScaffoldSmokeTests.cs`.
Verdict: 🟡 Yellow — no hardcoded real secrets/PII or live traversal execution found; advisory coverage gap in scoped logger parity tests.

Redacted evidence:
- Secret scan/manual review found only clearly synthetic token placeholders in isolation-session user tests. String-inspection coverage requires the token field to be masked and verifies the raw placeholder is absent; wire-serialization tests intentionally preserve the field for protocol shape only.
- Synthetic UPN/profile placeholders are non-real test values; no real personal data found.
- Advisory: scoped logger parity tests exercise ordinary log output and invalid-path fallback, but do not include a default-redaction assertion for a secret-bearing diagnostic/log payload. Existing non-scoped remediation coverage still checks logger default redaction, but the new ported logger parity file does not preserve that assertion itself.
- Path strings and invalid-path probes are static test fixtures or fake runner inputs; no path-traversal payload is live-executed against the host.
