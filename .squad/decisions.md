# Squad Decisions

## Active Decisions

### 2026-06-08T16:06:29-07:00: MXC SDK .NET 10 port — locked scope decisions
**By:** Ahmed Sabbour (via Squad coordinator)

**Decisions:**
1. **Platform scope:** Full tri-platform parity — Windows (processcontainer + experimental backends), Linux (lxc/bubblewrap), macOS (seatbelt) — matching the TypeScript source.
2. **PTY strategy:** Mirror the source architecture. The source depends on `node-pty`; the .NET port depends on maintained .NET PTY library **Porta.Pty** behind an internal `IPtyConnection` abstraction seam. If Porta.Pty fails validation, the seam allows fallback with zero caller impact.
3. **Pipe mode** (`usePty: false`) ports via `System.Diagnostics.Process` with redirected stdout/stderr.

**Why:** Faithful port preserves the source architecture: thin policy/config + process-spawn layer over the native `wxc-exec`/mxc binary; PTY is a dependency, not reimplemented. The seam contains maintenance risk to one swappable adapter.

**Architecture finding:** The SDK contains no native sandbox code. It transforms `SandboxPolicy` to `ContainerConfig`, spawns the native `wxc-exec`/mxc binary, and probes the host. The native binary is separate Rust code and is not part of this port.

### 2026-06-08: MXC .NET port — open questions resolved
**By:** Ahmed Sabbour (via Squad)

- D4 revised: NuGet package id and root namespace = `Sabbour.Mxc.Sdk`; TFM = `net10.0`; projects = `Sabbour.Mxc.Sdk`, `Sabbour.Mxc.Sdk.Tests`, `Sabbour.Mxc.Sdk.IntegrationTests`.
- D5 revised: State-aware API = full generic parity, using C# generic interfaces and branded `SandboxId<TBackend>`; C# conditional/`never` types approximated via generic interfaces and backend marker types.
- D6: Logging = SDK-internal `IMxcLogger`; no `Microsoft.Extensions.Logging` dependency.
- D7: Semver = `NuGet.Versioning`, never `System.Version`.
- D8: Minimum wxc-exec version mirrors source SDK `SUPPORTED_VERSION` constant verbatim.

### 2026-06-09T01-40-53: P0 scaffold — package versions and integration-test gating
**By:** Ripley

- Package versions selected in `Directory.Packages.props`: `NuGet.Versioning` 6.13.2, `Porta.Pty` placeholder 0.1.0, `Microsoft.Win32.Registry` 5.0.0, xUnit/NSubstitute/test SDK versions.
- Integration tests use `[Trait("Category","Integration")]` and opt in with `MXC_INTEGRATION_TESTS=1`; default `dotnet test` passes without running real integration bodies.

### 2026-06-09T02-11-31: P1 post-build review fixes
**By:** Brett

- Replaced flat nullable `ProxyConfig` with an abstract one-of union plus sealed variants and a converter that emits exactly one JSON field and rejects empty objects.
- Added `ContainmentValue` validated value type that accepts only known containment types/backends/legacy aliases and serializes as a string.
- Added wire-string-aware `ExperimentalBackends.RequiresExperimental(string)` with legacy alias normalization.
- Added diagnostics redaction seam (`Func<string,string>? redact`) and message-length cap; failure logs only message and filename.
- Aligned diagnostic timestamp format with TS `new Date().toISOString()` (`yyyy-MM-ddTHH:mm:ss.fffZ`).

### 2026-06-08: P1 types/errors/logger decisions
**By:** Brett

- `ContainerConfig.Containment` initially used `string?` to preserve TS union wire fidelity.
- `ErrorCode.FromCode` uses JSON deserialization to honor wire-name attributes.
- `SandboxingMethod` exists as a merged enum for deprecated backwards compatibility.
- `DiagnosticLog` uses `WindowsIdentity.GetCurrent()` for SID instead of shelling out to `whoami`.
- Earlier flat `ProxyConfig` design was superseded by the P1 post-build one-of union fix above.

### 2026-06-08: P2 fix decisions
**By:** Brett

- Added custom `JsonConverter<ContainerConfig>` to write TS insertion-order JSON, including backend-specific ordering differences.
- Removed explicit `seatbelt` branch from `PolicyTransform`; unsupported seatbelt handling matches TS phase boundaries.
- Implemented Windows ACL check with `icacls`, fail-open semantics, and Windows guard to match TS behavior.
- Passed resolved environment into PowerShell policy generation so PSReadLine fallback paths use process env when `env` is null.

### 2026-06-09T02-52-00: P3 platform probing seams and cache
**By:** Parker

- Replaced TS mutable globals with injected seams: `IPlatformProbeRunner`, `IWindowsBuildQuery`, and instance-owned `PlatformSupportCache`.
- Added `ProbedPlatform` constructor parameter for cross-platform tests without OS mocking.
- `IPlatformProbeRunner` covers probe execution, tool checks, file existence, and registry query.
- Registry access uses `reg query` via process for TS fidelity and dependency simplicity.
- Probe failures are swallowed for isolation metadata, leaving tier/warnings/UI null while preserving platform support.
- Windows Sandbox detection is DISM-first with executable fallback; later P3 fixes corrected DISM stdout semantics.

### 2026-06-08: P3 post-build fixes
**By:** Parker

- `IsWindowsSandboxAvailable` now regex-matches DISM stdout for `State : Enabled` and only falls back on process failure/timeout.
- Added decimal/hex UBR parsing for `reg query` output.
- `RunProbe()` treats nonzero exit as probe failure.
- `FindWxcExecutable` search order: `MXC_BIN_DIR`, assembly-relative package bin, `AppContext.BaseDirectory`, dev Cargo target paths, then PATH last.
- Process launches drain stdout and stderr concurrently with 4 MB caps and timeouts to avoid deadlock.
- Process launches use `ProcessStartInfo.ArgumentList` with `UseShellExecute=false`.
- Added trust-boundary docs for caller-controlled executable search inputs.
- Cache uses single-flight get-or-compute locking.

### 2026-06-08: P4 design correction (D3 superseded)
**By:** Squad (from P4 pre-build rubber-duck)

- Corrected async one-shot spawn model: TS `spawnSandboxAsync` uses PTY internally and buffers combined output; it is not pipe mode.
- Pipe/streaming mode exists only via `spawnSandboxFromConfig(..., {usePty:false})`, returning a streaming child process (`ProcessConnection` in C#).
- Config wire format is `--config-base64 <base64(UTF-8 JSON)>` plus flags; env is injected into `config.process.env`, not OS process env.
- On nonzero one-shot PTY exit, scan output lines for MXC error envelopes.
- Explicit seatbelt rejection belongs in config creation, not spawn.
- `IPtyConnection` seam carries exit status/signal, raw chunk data, and SDK-owned options.
- .NET cancellation tokens are intentional additions and throw `OperationCanceledException` when used.

### 2026-06-09T03-23-00: P4 Spawn+PTY selected Porta.Pty 1.0.7
**By:** Parker

- Chose **Porta.Pty 1.0.7** (MIT, netstandard2.0) behind SDK-owned `IPtyConnection`.
- Day-1 Windows ConPTY validation passed: spawn, exit code, interactive write, resize, kill, dispose after exit, dispose while running.
- SDK adapter uses Porta.Pty spawn, raw streams, `ProcessExited`, `Kill`, `Resize`, `Pid`, and `ExitCode` internally.
- No Porta.Pty types leak into public API.

### 2026-06-08: P4 post-build fixes
**By:** Parker

- `SpawnSandboxFromConfigAsync` honors `UsePty` in one method, matching TS shape; pipe-mode wrapper remains convenience.
- `ProcessConnection` uses `WaitForExitAsync` directly to avoid event subscription races.
- `PortaPtyConnection` buffers early data until subscriber attach, then replays and releases the buffer.
- Output caps: 4 MB PTY combined, 4 MB per pipe stream, 8 KB error message, 64 KB raw JSON details.
- PTY cancellation uses per-waiter `.WaitAsync(ct)` and does not poison the shared exit task.
- Removed raw pipe stream exposure; callers use buffered `GetStdout()`/`GetStderr()` after exit.

### 2026-06-08: P5 state-aware lifecycle decisions
**By:** Brett

Archived because this entry is older than 30 days and the merged decisions file exceeded the archive threshold. See `log/2026-06-08T20-51-43-07-00-decisions-archive-older-than-30-days.md`.

### 2026-06-08: P5 remediation
**By:** Parker

- B1: `SpawnHelper.TryParseErrorEnvelope` now parses trimmed stdout once as a complete JSON object and requires root `error.code`; separate line-scanner remains for PTY one-shot.
- B2: State-aware argv now matches TS `resolveBinaryAndCommonArgs`: binary resolution through platform support, Linux/macOS/Windows-specific executable lookup, only `--config-base64`, `--dry-run`, `--debug`, `--experimental`; no `--log-file`.
- R1: Added `TokenRedactor` and applied redaction/capping at state-aware exception sites.
- R2: `DefaultStateAwareSpawnRunner` uses capped concurrent stdout/stderr reads with the 4 MB cap.
- R3: `FileLogger` and `DiagnosticLog` default to `TokenRedactor.Redact`.
- N1: Cross-cutting fields use a static ordered array: `filesystem`, `network`, `ui`, `process`.
- Advisory: documented `--config-base64` token exposure and `IsolationSessionUserConfig.ToString()` UPN behavior.

### 2026-06-08: P5-B1 pipe-mode error-envelope parsing removal
**By:** Parker

- Removed `TryParseErrorEnvelopeFromLines` scanning from `SandboxSpawner.SpawnSandboxProcessAsync` pipe-mode one-shot.
- Pipe mode now awaits exit and returns `SandboxProcessResult` with stdout/stderr/exit code without classifying printed JSON as MXC dispatch errors.
- `TryParseErrorEnvelopeFromLines` is now used only from PTY combined-output one-shot, matching TS.

### 2026-06-08: P6 implementation — facade, accessibility, packaging, docs
**By:** Ripley

- Added public static `MxcSdk` facade mirroring TS `index.ts` exports: platform support, policy/config helpers, spawn helpers, policy helpers, error factory, and state-aware lifecycle methods.
- Added sync `ExecInSandbox` delegating to state-aware isolation session sync exec.
- Demoted test seams/helper types to internal: platform seams/cache/probers, PTY/process factories, legacy aliases, `PolicyTransform`, and state-aware runner.
- Made `Phase` public because it is exported by TS.
- Added `InternalsVisibleTo` for `DynamicProxyGenAssembly2` so tests can proxy internal seams.
- Added packaging metadata: version 0.6.1, tags, docs, symbols, snupkg, README packing.
- Initial facade spawn names were superseded by the P6 fidelity fixes below.

### 2026-06-08: P6 fidelity fixes — MxcSdk facade
**By:** Ripley

- Fixed spawn naming/semantics: `SpawnSandbox` = live PTY; `SpawnSandboxAsync` = buffered one-shot.
- Fixed `SpawnSandboxFromConfig` to match TS naming; returns `Task<IPtyConnection>` because Porta.Pty spawn is async.
- Added containment-first state-aware methods: `ProvisionSandboxAsync`, `StartSandboxAsync`, `ExecInSandbox`, `ExecInSandboxAsync`, `StopSandboxAsync`, `DeprovisionSandboxAsync`.
- Fixed policy helper parameter parity: `GetAvailableToolsPolicy(env?, options?)`; `GetTemporaryFilesPolicy(env?)`.
- Added `MxcErrorFromCode(string code, string message, IReadOnlyDictionary<string, object?>? details = null)` while keeping typed overload.
- Updated README snippets and facade smoke tests.

### 2026-06-08: P6 XML docs — CS1591 elimination
**By:** Brett

- Internalized `ContainerConfigConverter`, `ProxyConfigConverter`, and `ContainmentValueConverter`; STJ source-generation and attributes still resolve internal converters in-assembly.
- Documented all public enums, error members, constructors, state-aware records/properties, user config, containment value methods/operators, logger members, diagnostics, and process connection members.
- Forced Release rebuild reduced CS1591 warnings from 188 to 0.
- Verification: 371 tests passing, no behavioral/wire/redaction/public-shape changes from documentation work.

### 2026-06-08T20:51:43-07:00: P5/P6 final verification
**By:** Coordinator

- P5 review/remediation ended green after B1/B2/R1/R2/R3/N1 fixes and pipe-mode follow-up.
- P6 review/remediation ended green after facade-shape fixes, XML doc cleanup, and security review.
- Coordinator verified CS1591 = 0 on forced Release rebuild, fixed a stray CS1734 `paramref`, and confirmed `dotnet pack` produces `Sabbour.Mxc.Sdk.0.6.1.nupkg` plus `.snupkg` with README and XML docs.
- Final state: 371 tests green, 0 build warnings, package builds.

## Archived Decisions

- 2025-07-24 P2 Policy Transform — Key Decisions: archived to `log/2026-06-08T20-51-43-07-00-decisions-archive-older-than-30-days.md`.
- 2025-07-20 P5 State-Aware Lifecycle — Implementation Decisions: archived to `log/2026-06-08T20-51-43-07-00-decisions-archive-older-than-30-days.md`.

## Governance

- All meaningful changes require team consensus.
- Document architectural decisions here.
- Keep history focused on work, decisions focused on direction.


## Additional Merged Decisions

- 2025-07-14 P6 facade-shape final — 2 remaining gaps fixed: archived to `log/2026-06-08T20-51-43-07-00-decisions-archive-older-than-30-days.md`.


## Session Merged Decisions — 2026-06-08T22-26-04-07-00

### 2026-06-09T05-17-20: Parity scaffold helper seam mapping
**By:** Ash
**What:** Parity scaffold helper seam mapping
**References:** tests\Sabbour.Mxc.Sdk.Tests\Parity\ParityTestHelpers.cs, tests\Sabbour.Mxc.Sdk.Tests\Parity\ParityScaffoldSmokeTests.cs
**Why:** Created the dedicated Parity xUnit scaffold under tests\Sabbour.Mxc.Sdk.Tests\Parity without touching existing tests. Ported upstream tests-unit\test-helpers.ts into ParityTestHelpers.cs: PlatformSkip, TestOptions, TestConfig, fakeSpawn-style FakeStateAwareSpawnRunner, FakePtyConnection/FakePtyConnectionFactory, FakePlatformProbeRunner, and FakeWindowsBuildQuery. Mapping decisions: node-pty mocks map to IPtyConnection/IPtyConnectionFactory; state-aware child_process.spawn mocks map to IStateAwareSpawnRunner; registry/DISM/tool/file platform mocks map to IPlatformProbeRunner and IWindowsBuildQuery. PARITY-GAP notes retained in helper file: pipe-mode child_process.spawn cannot be fully synthetic because IProcessConnectionFactory returns concrete ProcessConnection with a private constructor, and node child.kill() null close code maps to FakePtyConnection exit code -1 because PtyExitEvent requires int ExitCode. Verified with Release build and ParityScaffoldSmokeTests.

### 2026-06-09T05-25-51: Parity unit-test port adaptations
**By:** Brett
**What:** Parity unit-test port adaptations
**References:** tests\Sabbour.Mxc.Sdk.Tests\Parity\ErrorsParityTests.cs, tests\Sabbour.Mxc.Sdk.Tests\Parity\LoggerParityTests.cs, tests\Sabbour.Mxc.Sdk.Tests\Parity\PolicyParityTests.cs, tests\Sabbour.Mxc.Sdk.Tests\Parity\StateAwareTypesParityTests.cs, tests\Sabbour.Mxc.Sdk.Tests\Parity\StateAwareParityTests.cs, tests\Sabbour.Mxc.Sdk.Tests\Parity\ParityTestHelpers.cs
**Why:** Ported the five requested upstream TypeScript unit-test files into tests\Sabbour.Mxc.Sdk.Tests\Parity without production changes. Test-only adaptations: TypeScript compile-time `@ts-expect-error` checks in state-aware-types are represented with C# reflection/signature assertions because invalid C# calls must remain uncompilable; AbortSignal tests assert CancellationToken cancellation via a fake runner; PowerShell policy platform mocking is represented with OS-guarded assertions because PolicyDiscovery uses RuntimeInformation directly. Added only a test helper CancellableStateAwareSpawnRunner to ParityTestHelpers for cancellation parity.

### 2026-06-09T05-27-15: Ported upstream platform/sandbox parity tests with test-only platform gaps
**By:** Parker
**What:** Ported upstream platform/sandbox parity tests with test-only platform gaps
**References:** tests\Sabbour.Mxc.Sdk.Tests\Parity\PlatformParityTests.cs, tests\Sabbour.Mxc.Sdk.Tests\Parity\SandboxParityTests.cs, .squad\decisions.md
**Why:** Added test-only C# parity ports for upstream platform.test.ts and sandbox.test.ts. Because SandboxFactory uses RuntimeInformation directly and the task forbids new production seams, OS-mocking cases are guarded with PARITY-GAP comments and run only on matching hosts. Two Windows network-validation assertions are intentionally left failing as RED FLAGs: upstream rejects allowedHosts/blockedHosts without allowOutbound for processcontainer, while current C# builds a config without throwing. Linux/macOS proxy-rejection parity assertions are likewise documented as RED FLAGs for those hosts.

### 2026-06-08T22-26-04-07-00: GPT-5.5 fidelity review + upstream unit-test parity port session
**By:** Scribe (requested by Ahmed Sabbour)

- Parity suite placement is tests\Sabbour.Mxc.Sdk.Tests\Parity\ with namespace `Sabbour.Mxc.Sdk.Tests.Parity`.
- Two intentional red flags remain pending coordinator/user fix decision: Windows processcontainer `allowedHosts`/`blockedHosts` without `allowOutbound` currently builds a C# config, while upstream TypeScript throws.
- Ripley's GPT-5.5 fidelity review is report-only; no production or test code changes were made from that review in this scribe pass. Report outcome: 3 critical, 4 major, 0 minor, and 9 verified intentional adaptations in `files\fidelity-review.md`.
- Verification handoff: forced Release rebuild had 0 warnings; full suite reported 565 tests, 563 passing, and 2 failing by intentional red flags; the pre-existing 371 tests stayed green.


## Session Merged Decisions — 2026-06-09T11-04-37Z

### 2026-06-09T11:04:37-07:00: Examples policy schema pin
**By:** Scribe (requested by Ahmed Sabbour)

- Examples pin the latest stable policy schema, `0.6.0-alpha` from release `v0.6.1`, not the SDK max-readable / `SUPPORTED_VERSION` value `0.7.0-alpha`.
- README examples and Version Support prose distinguish the supported minimum (`0.4.0-alpha`), latest stable schema (`0.6.0-alpha`), and max-readable schema (`0.7.0-alpha`).
- Canonical upstream stable schema documentation link: https://github.com/microsoft/mxc/blob/v0.6.1/docs/sandbox-policy/v1/policy.md#5-sandboxpolicy
- This supersedes any earlier README guidance that described `0.7.0-alpha` as the current/default schema for examples; `0.7.0-alpha` remains documented as max-readable only.

### 2026-06-09T05-33-12: Documented upstream-parity test redundancy and red flags
**By:** Ash
**What:** Documented upstream-parity test redundancy and red flags
**References:** files/test-redundancy-assessment.md, files/parity-red-flags.md, files/fidelity-review.md#C3
**Why:** Created recommendation-only documentation for the upstream-parity unit-test port. Existing local tests were categorized conservatively as keep=4, redundant=1, partial-overlap=10; no repository tests or src files were modified. Documented the two intentional SandboxParityTests red flags for network host-filtering validation without allowOutbound, cross-referencing fidelity-review finding C3 and leaving them awaiting Ahmed's fix decision.

### 2026-06-08T23:15:00-07:00: Host filtering parity correction
**By:** Ash

- `PolicyTransform` and `SandboxFactory` are parallel create-config paths, not a delegation chain.
- `PolicyTransform.CreateConfigFromPolicy(policy, containment, containerId, platform)` is the canonical pure/testable transform with injected platform.
- `SandboxFactory.CreateConfigFromPolicy(policy, containment, containerName)` is the public runtime wrapper used by `MxcSdk` and runtime sandbox tests and resolves platform with `RuntimeInformation`.
- Faithful upstream gate from `sdk/src/sandbox.ts:311-322`: host filtering is allowed without `allowOutbound` only for `wslc`, `seatbelt`, `bubblewrap`, `lxc`, `process` on Linux, and `process` on Darwin. Every other containment/backend with `allowedHosts` or `blockedHosts` and `allowOutbound != true` throws `InvalidOperationException("allowedHosts/blockedHosts require allowOutbound to be true")`.
- Implemented the gate as a shared internal helper on `PolicyTransform` and used it from both paths; microvm is prevalidated before its early dedicated builder.
- Verification moved from 565 passed / 1 skipped / 0 failed to 570 passed / 1 skipped / 0 failed with the new tests.

### 2026-06-08T23:00:48-07:00: CI/release setup decisions
**By:** Parker

- CI uses a small OS matrix: `ubuntu-latest` and `windows-latest`.
- CI installs the .NET 10 preview SDK with `actions/setup-dotnet@v4`, `dotnet-version: 10.0.x`, and `dotnet-quality: preview`.
- CI caches NuGet packages with `actions/cache@v4` keyed by project files and central package props, avoiding a dependency on lock files.
- Release publishing packs only `src/Sabbour.Mxc.Sdk/Sabbour.Mxc.Sdk.csproj`, pushes `./artifacts/*.nupkg` to NuGet.org, and attaches both package and `.snupkg` artifacts to the GitHub Release.
- Release jobs are guarded to tag refs starting with `refs/tags/v`, including manual `workflow_dispatch` runs.
- Required repo setup: add `NUGET_API_KEY` as a repository secret before using the release workflow.
- Verification blocker fixed: SandboxFactory validation now matches host-filtering parity expectation for Windows `process`; integration smoke tests now use an env-gated `FactAttribute` so default CI reports integration tests as skipped.

### 2026-06-09T06-23-06: README prebuilt MXC executor guidance
**By:** Parker
**What:** README now documents prebuilt MXC executor as primary integration-test setup
**References:** README.md, VersionHelper.SupportedVersion=0.7.0-alpha (provided ground truth)
**Why:** Updated README guidance for tests and native binary discovery: Tier 1 unit tests need no native binary; Tier 2 integration/e2e tests use the prebuilt `microsoft/mxc` `v0.6.1` `mxc-release-binaries.zip`, set `MXC_BIN_DIR` to the directory containing `<arch>\wxc-exec.exe`, set `MXC_INTEGRATION_TESTS=1`, and note the Windows 11 24H2/build 26100+ processcontainer requirement. README No CLI guidance was corrected to say the SDK searches `MXC_BIN_DIR/<arch>/wxc-exec.exe` and not PATH. The earlier schema wording in this inbox item is superseded by the 2026-06-09T11:04:37 examples policy schema pin decision above.


## Session Merged Decisions — 2026-06-09T11-30-46Z

### 2026-06-09T11:30:46-07:00: ARM64 Windows backend availability probe
**By:** Scribe (requested by Ahmed Sabbour)

- Empirical host finding: on this ARM64 Windows host, only `processcontainer` / AppContainer with policy schema `0.4.0-alpha` fully launched and exited 0 in the temporary gitignored probe.
- `processcontainer` with `0.6.0-alpha` failed `E_NOTIMPL` with velocity keys `61389575` / `61155944` disabled.
- `wslc` and `microvm` invoked the executor but returned exit `-1` because the host stack was absent; `microvm` correctly rejected a network policy.
- `windows_sandbox` and `hyperlight` are `NotSupportedException` in the port; `isolation_session` is `BackendUnavailable` because the COM class is not registered.


### 2026-06-09T21-43-25: Wire proxy-containment rejection into SandboxFactory.ApplyNetworkConfig via a shared PolicyTransform validator (parity fix, user-approved)
**By:** Squad-Coordinator
**What:** Wire proxy-containment rejection into SandboxFactory.ApplyNetworkConfig via a shared PolicyTransform validator (parity fix, user-approved)
**References:** Ash, SandboxFactory.cs:174-202, PolicyTransform.cs:141-157, SandboxParityTests.cs:249-262,627-638, decision #4
**Why:** Restoring ubuntu-latest CI exposed two intentional RED-FLAG Linux parity tests (SandboxParityTests.CreateConfigFromPolicy_Linux_RejectsProxyForExplicitLxcContainment, BuildSandboxPayload_Linux_RejectsProxyForNonBubblewrapContainments). Root cause: PolicyTransform.BuildNetworkConfig throws for proxy + non-bubblewrap Linux containment, but SandboxFactory.ApplyNetworkConfig (the path these tests use) never replicated that check. User approved fixing the SDK rather than weakening/skipping the tests. Fix mirrors the already-approved host-filtering parity fix: extract proxy-rejection into a single shared PolicyTransform validator, call it from both BuildNetworkConfig and SandboxFactory.ApplyNetworkConfig. Owner: Ash.
