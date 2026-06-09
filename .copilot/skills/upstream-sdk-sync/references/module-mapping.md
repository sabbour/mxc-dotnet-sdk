# Upstream TypeScript → C# port mapping

This is the canonical map from each upstream `sdk/src/*.ts` module to its C# counterpart(s)
in `src/Sabbour.Mxc.Sdk/`, plus the unit-test file mapping. Use it to translate an upstream
change into the right C# target instead of re-deriving the layout each run.

The map was established by a GPT-5.5 fidelity review of the port against the pinned upstream
sources. Treat it as a starting point, not gospel: if the upstream layout has shifted (a module
split, a file renamed), confirm against the live tree and update this file as part of the sync.

## Source modules

| Upstream module (`sdk/src/`) | C# counterpart(s) (`src/Sabbour.Mxc.Sdk/`) | Notes |
|---|---|---|
| `diagnostic.ts` | `Diagnostics/DiagnosticLog.cs` | Pipe-name/SID + best-effort diagnostic console. |
| `errors.ts` | `Errors/ErrorCode.cs`, `Errors/MxcException.cs`, `MxcSdk.cs` (`MxcErrorFromCode`) | Error-code set; `MxcException` carries `RawCode` for unknown wire codes. |
| `helper.ts` | `Sandbox/SpawnHelper.cs`, `Internal/NetworkPolicyHelper.cs`, `Platform/DefaultPlatformProbeRunner.cs`, `Diagnostics/FileLogger.cs`, `Internal/TokenRedactor.cs` | Spawn-arg construction + platform binary resolution, split across files. |
| `index.ts` | `MxcSdk.cs` + public model/type files | Static facade mirrors top-level exports rather than TS named exports. |
| `logger.ts` | `Diagnostics/FileLogger.cs`, `Diagnostics/IMxcLogger.cs` | Logger implementation + interface. |
| `platform.ts` | `PlatformSupport.cs`, `Platform/PlatformProber.cs`, `Platform/DefaultPlatformProbeRunner.cs`, `Platform/DefaultWindowsBuildQuery.cs`, `Platform/PlatformSupportCache.cs`, `Enums.cs` | Platform probing + caching + capability facts. |
| `policy.ts` | `Policy/PolicyDiscovery.cs`, `Policy/FilesystemPolicyResult.cs`, `Policy/ToolsPolicyOptions.cs` | Filesystem/tools policy discovery API + return types. |
| `sandbox.ts` | `Sandbox/SandboxFactory.cs`, `Policy/PolicyTransform.cs`, `Sandbox/SandboxSpawner.cs`, `Sandbox/SandboxSpawnOptions.cs`, `Sandbox/SandboxProcessResult.cs`, `ContainerConfig*.cs`, `SandboxPolicy.cs`, `Enums.cs` | Public facade is `SandboxFactory`; `PolicyTransform` is internal. |
| `state-aware-helper.ts` | `StateAware/StateAwareEnvelopeBuilder.cs`, `StateAware/DefaultStateAwareSpawnRunner.cs`, `Sandbox/SpawnHelper.cs`, `StateAware/StateAwareSandboxClient.cs` | Envelope construction + response parsing. |
| `state-aware-types.ts` | `StateAware/Phase.cs`, `StateAware/SandboxId.cs`, `StateAware/IsolationSessionBackend.cs`, `StateAware/IStateAwareBackend.cs`, `StateAware/IsolationSessionConfigs.cs`, `StateAware/Results.cs`, `StateAware/IsolationSessionUserConfig.cs`, `StateAware/IsolationSessionConfigurationId.cs` | TS conditional/helper types approximated via C# generics + marker types. |
| `state-aware.ts` | `StateAware/StateAwareSandboxClient.cs`, `StateAware/StateAwareSandboxes.cs`, `MxcSdk.cs` state-aware facade methods | Lifecycle methods. |
| `types.ts` | `SandboxPolicy.cs`, `ContainerConfig.cs`, `ContainmentValue.cs`, `Enums.cs`, `ProxyConfigConverter.cs`, `MxcJsonContext.cs` | Wire DTOs + enum wire names. |

## Unit-test modules

Ported upstream unit tests live in `tests/Sabbour.Mxc.Sdk.Tests/Parity/`, namespace
`Sabbour.Mxc.Sdk.Tests.Parity`, mirroring upstream filenames. They sit alongside — not inside —
the pre-existing local suite.

| Upstream (`sdk/tests/unit/`) | C# parity file |
|---|---|
| `errors.test.ts` | `ErrorsParityTests.cs` |
| `logger.test.ts` | `LoggerParityTests.cs` |
| `policy.test.ts` | `PolicyParityTests.cs` |
| `state-aware-types.test.ts` | `StateAwareTypesParityTests.cs` |
| `state-aware.test.ts` | `StateAwareParityTests.cs` |
| `platform.test.ts` | `PlatformParityTests.cs` |
| `sandbox.test.ts` | `SandboxParityTests.cs` |
| `test-helpers.ts` | `ParityTestHelpers.cs` (support, not a test class) |

## C# files with no direct upstream origin (do not "sync away")

These exist for .NET packaging, AOT/source-gen, test seams, and security hardening. They have no
upstream counterpart and must not be deleted or "reverted" because they are missing upstream:

`Sabbour.Mxc.Sdk.csproj`, `MxcJsonContext.cs`, `ContainmentValue.cs`, `ProxyConfigConverter.cs`,
`ContainerConfigConverter.cs`, `Platform/IPlatformProbeRunner.cs`, `Platform/IWindowsBuildQuery.cs`,
`Sandbox/IPtyConnection.cs`, `Sandbox/PortaPtyConnection.cs`, `Sandbox/ProcessConnection.cs`,
`Sandbox/ProcessConnectionPtyAdapter.cs`, `Internal/TokenRedactor.cs`, `Diagnostics/IMxcLogger.cs`.

## Out of scope for direct porting

- **Rust / native backend changes** (anything outside `sdk/` — e.g. Bubblewrap, Lxc, seatbelt,
  the `wxc-exec` binary). These ride along in the same repo but are not part of the managed SDK
  surface. Flag them as context, do not attempt to port.
- **Integration tests** (`sdk/tests/integration/**`). They need the native binary and OS backends.
  Flag changes for awareness; porting them is a separate, explicitly-requested effort.
- **Build/config churn** (`package-lock.json`, `.npmrc`, `tsconfig.json`). Note version bumps —
  especially `SUPPORTED_VERSION` / package `version` — but there is rarely a 1:1 C# edit.
