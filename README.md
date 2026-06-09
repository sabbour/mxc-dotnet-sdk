# Sabbour.Mxc.Sdk

[![CI](https://github.com/sabbour/mxc-dotnet-sdk/actions/workflows/ci.yml/badge.svg)](https://github.com/sabbour/mxc-dotnet-sdk/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/Sabbour.Mxc.Sdk.svg)](https://www.nuget.org/packages/Sabbour.Mxc.Sdk) [![NuGet downloads](https://img.shields.io/nuget/dt/Sabbour.Mxc.Sdk.svg)](https://www.nuget.org/packages/Sabbour.Mxc.Sdk) [![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE) [![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)

A .NET 10 SDK for [MXC (Microsoft eXecution Containers)](https://github.com/microsoft/mxc) — a faithful port of the TypeScript `@microsoft/mxc-sdk` package.

## Install

```bash
dotnet add package Sabbour.Mxc.Sdk
```

## Quickstart

Spawn a sandboxed process from a policy:

```csharp
using Sabbour.Mxc.Sdk;
using Sabbour.Mxc.Sdk.Sandbox;

var policy = new SandboxPolicy
{
    Version = "0.6.0-alpha",
    Network = new NetworkPolicy { AllowOutbound = false },
};

// Buffered one-shot — waits for exit, returns output (TS spawnSandboxAsync)
var result = await MxcSdk.SpawnSandboxAsync("echo hello from sandbox", policy);
Console.WriteLine($"Output: {result.Stdout}");
Console.WriteLine($"Exit code: {result.ExitCode}");
```

## Policy → ContainerConfig Transform

Convert a security-intent policy into a backend-specific container configuration:

```csharp
ContainerConfig config = MxcSdk.CreateConfigFromPolicy(policy, containment: "process");
// Customize further before spawning:
config = config with
{
    Process = config.Process! with { CommandLine = "python -c \"print('hi')\"" }
};
```

## Spawning

### Live PTY (interactive)

```csharp
// Live PTY spawn (TS spawnSandbox) — async due to Porta.Pty
await using var pty = await MxcSdk.SpawnSandbox(
    script: "python repl.py",
    policy: policy,
    workingDirectory: @"C:\workspace");

pty.DataReceived += chunk =>
    Console.Write(System.Text.Encoding.UTF8.GetString(chunk.Span));
pty.Write("print('hello')\n");
var exit = await pty.WaitForExitAsync();
```

### Buffered one-shot (TS spawnSandboxAsync)

```csharp
var result = await MxcSdk.SpawnSandboxAsync(
    "node -e \"console.log('done')\"",
    policy);
// result.Stdout, result.Stderr, result.ExitCode
```

### Pipe mode

When PTY overhead is unnecessary (CI, batch jobs), use pipe mode for separate stdout/stderr:

```csharp
using var conn = MxcSdk.SpawnSandboxProcessFromConfig(config,
    new SandboxSpawnOptions { UsePty = false });
int exitCode = await conn.WaitForExitAsync();
Console.WriteLine(conn.GetStdout());
```

## State-Aware Isolation Session Lifecycle

The state-aware API manages sandbox lifecycle phases: provision → start → exec → stop → deprovision.

```csharp
using Sabbour.Mxc.Sdk;
using Sabbour.Mxc.Sdk.Sandbox;
using Sabbour.Mxc.Sdk.StateAware;

// Containment marker — mirrors TS provisionSandbox(containment, config?, options?)
var backend = IsolationSessionBackend.Instance;

// 1. Provision
var provision = await MxcSdk.ProvisionSandboxAsync(backend,
    new IsolationSessionProvisionConfig { /* backend-specific */ });
var sandboxId = provision.SandboxId;

// 2. Start
await MxcSdk.StartSandboxAsync(sandboxId);

// 3. Exec (streaming PTY — sync call, no await)
using var pty = MxcSdk.ExecInSandbox(sandboxId,
    new IsolationSessionExecConfig { CommandLine = "dir" });
var exit = await pty.WaitForExitAsync();

// 3b. Exec (buffered — async)
var execResult = await MxcSdk.ExecInSandboxAsync(sandboxId,
    new IsolationSessionExecConfig { CommandLine = "echo done" });

// 4. Stop
await MxcSdk.StopSandboxAsync(sandboxId);

// 5. Deprovision
await MxcSdk.DeprovisionSandboxAsync(sandboxId);
```

## Platform Support Probing

Detect available containment backends on the current host:

```csharp
PlatformSupport support = MxcSdk.GetPlatformSupport();
if (support.IsSupported)
{
    Console.WriteLine($"Backends: {string.Join(", ", support.AvailableMethods)}");
    Console.WriteLine($"Isolation tier: {support.IsolationTier}");
}
```

## Error Handling

The SDK throws `MxcException` when the native executor reports structured errors:

```csharp
using Sabbour.Mxc.Sdk.Errors;

try
{
    var result = await MxcSdk.SpawnSandboxAsync("bad-cmd", policy);
}
catch (MxcException ex)
{
    Console.WriteLine($"Error code: {ex.Code}");
    Console.WriteLine($"Raw code: {ex.RawCode}");
    Console.WriteLine($"Message: {ex.Message}");
}
```

Error codes are defined in the `ErrorCode` enum (e.g., `MalformedRequest`, `BackendUnavailable`, `StaleId`).

## Logging & Diagnostics

Inject a logger via `IMxcLogger` for structured diagnostic output. The SDK automatically redacts sensitive tokens (e.g., `wamToken`) before they reach log sinks.

```csharp
using Sabbour.Mxc.Sdk.Diagnostics;
// IMxcLogger can be implemented for custom sinks
```

## Version Support

This SDK validates the policy `version` field. The example pins `0.6.0-alpha`, the schema shipped by the latest stable executor release (`v0.6.1`) — match it to the binary you install. The SDK accepts versions from `0.4.0-alpha` (minimum) up to `0.7.0-alpha` (the newest schema it understands); when you omit `version`, it fills in `0.7.0-alpha`. Policies outside that range are rejected at config-creation time.

For the canonical field reference — `version`, `filesystem`, `network`, `ui`, and `timeoutMs` — see the upstream [MXC Sandbox Policy Spec §5 (SandboxPolicy)](https://github.com/microsoft/mxc/blob/v0.6.1/docs/sandbox-policy/v1/policy.md#5-sandboxpolicy), pinned to the `v0.6.1` release.

## No CLI

This SDK shells out to the native `wxc-exec` binary for sandbox operations. It has no CLI of its own — it is a library, not a tool. Set `MXC_BIN_DIR` to the directory that contains `<arch>\wxc-exec.exe` (for example, `x64\wxc-exec.exe`), or specify `SandboxSpawnOptions.ExecutablePath` per spawn. The SDK does not search PATH.

## Running the tests

### Tier 1: unit tests

```powershell
dotnet test
```

Unit tests do not need the native executor and run on any OS supported by .NET 10.

### Tier 2: integration/e2e tests

Download the prebuilt executor from [microsoft/mxc releases](https://github.com/microsoft/mxc/releases). The latest release is `v0.6.1`, with the `mxc-release-binaries.zip` asset.

Unzip the archive, then set `MXC_BIN_DIR` to the folder that contains `<arch>\wxc-exec.exe`:

```powershell
$env:MXC_BIN_DIR = "C:\path\to\mxc-bin"
$env:MXC_INTEGRATION_TESTS = "1"
dotnet test
```

The SDK looks for `$env:MXC_BIN_DIR\<arch>\wxc-exec.exe`; it does not search PATH. On Windows, the default `processcontainer` backend needs Windows 11 24H2 or later (build 26100+) and does not require admin.

## Enabling isolation backends (host setup)

The SDK picks a containment backend, but the host has to have that backend lit up first. The default `processcontainer` path works out of the box on recent Windows builds; the other backends need one-time setup. The steps below are the ones that get each backend running with the `v0.6.1` executor binaries.

Check which tier the executor will select on the current host (read-only, no admin):

```powershell
& "$env:MXC_BIN_DIR\<arch>\wxc-exec.exe" --probe
# { "tier": "base-container", "needsDaclAugmentation": false, "probes": { ... } }
```

### processcontainer

`processcontainer` has three tiers (highest first): `base-container`, `appcontainer-bfs`, `appcontainer-dacl`. The probe reports which one applies.

- **`base-container`** uses an experimental kernel API that ships behind a Windows Feature Store gate on current builds. When the gate is closed, the executor returns `E_NOTIMPL` even though the API is present. Light it up with [ViVeTool](https://github.com/thebookisclosed/ViVe) (download the build that matches your CPU arch), then **reboot**:

  ```powershell
  # Run elevated. Use comma-separated IDs — repeated /id: flags are rejected.
  .\ViVeTool.exe /enable /id:61389575,61155944
  .\ViVeTool.exe /query /id:61389575   # State : Enabled (2)
  ```

  Older policy schemas (`0.4.0-alpha`) take the ungated AppContainer path instead, so they run without this gate.

- **`appcontainer-dacl`** needs a one-time host preparation that grants the AppContainer SIDs the ACEs they require. Run `wxc-host-prep` elevated (it exits non-zero if not):

  ```powershell
  & "$env:MXC_BIN_DIR\<arch>\wxc-host-prep.exe" prepare-system-drive   # one-time, persists
  & "$env:MXC_BIN_DIR\<arch>\wxc-host-prep.exe" prepare-null-device    # per-boot
  ```

### windows_sandbox

A real disposable-VM backend (host daemon + in-VM guest). Enable the Windows Sandbox feature elevated, then **reboot**:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All
```

It also needs hardware virtualization enabled in firmware and Python on the host. On Windows builds 26100 and newer there is a documented boot regression (zombie VM processes) that can keep the sandbox VM from starting even when the feature is enabled.

This backend is not selectable through `CreateConfigFromPolicy` (matching upstream — its `createConfigFromPolicy` has no branch for it either). Reach it with a prebuilt config plus the experimental flag:

```csharp
var config = MxcSdk.BuildSandboxPayload("echo hi", policy, containment: "windows_sandbox");
using var conn = MxcSdk.SpawnSandboxProcessFromConfig(config,
    new SandboxSpawnOptions { Experimental = true, UsePty = false });
```

### microvm (NanVix)

Requires the `nanvixd.exe` daemon, which is **not included** in the public `mxc-release-binaries.zip`, so it cannot run from the released binaries. This backend also rejects any policy that sets `network` (no network-policy enforcement).

### wslc

`wslc` runs Linux OCI containers in a dedicated WSL-managed Hyper-V VM. It needs three things beyond a working WSL2 distro.

**1. The WSLC client SDK (`wslcsdk.dll`).** Like the microvm daemon, it is not in the released binary zip. It ships in the upstream nupkg at [`external/wslc-sdk/Microsoft.WSL.Containers.<ver>.nupkg`](https://github.com/microsoft/mxc/tree/main/external/wslc-sdk). A `.nupkg` is a zip — extract it and copy the arch-matching DLL next to `wxc-exec.exe`:

```powershell
Expand-Archive Microsoft.WSL.Containers.2.8.1.nupkg -DestinationPath wslc-sdk
Copy-Item "wslc-sdk\runtimes\win-<arch>\wslcsdk.dll" "$env:MXC_BIN_DIR\<arch>\"
```

The DLL loads only when the wslc backend is invoked — the other backends do not need it.

**2. WSL 2.8.1 or newer.** The container runtime and the SDK surface it depends on only exist in recent WSL. Update WSL (the pre-release channel ships newer builds):

```powershell
wsl --update --pre-release
wsl --shutdown
wsl --version          # need 2.8.1.0 or later
```

If the published Store and pre-release channels are still below 2.8.1 (they were at 2.7.x at the time of writing), the only route to 2.8.1+ is building [microsoft/WSL](https://github.com/microsoft/WSL) from `master`. Until WSL is new enough, the backend fails its preflight with `WSLC runtime not available. Missing components: WslPackage` even when `wslcsdk.dll` is already in place.

**3. A pre-pulled image.** MXC never pulls at run time — populate the cache first:

```powershell
& "$env:MXC_BIN_DIR\<arch>\wxc-exec.exe" --setup-wslc --image alpine:latest
```

### hyperlight

Warm the published snapshot once before first use (pulls the kernel + initrd via docker/podman):

```powershell
& "$env:MXC_BIN_DIR\<arch>\wxc-exec.exe" --setup-hyperlight
```

Like `windows_sandbox`, `hyperlight` is reached through a prebuilt config with `Experimental = true`, not through `CreateConfigFromPolicy`.

## License

MIT
