# Sabbour.Mxc.Sdk — unofficial, experimental .NET SDK for MXC (Microsoft eXecution Containers)

[![CI](https://github.com/sabbour/mxc-dotnet-sdk/actions/workflows/ci.yml/badge.svg)](https://github.com/sabbour/mxc-dotnet-sdk/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/Sabbour.Mxc.Sdk.svg)](https://www.nuget.org/packages/Sabbour.Mxc.Sdk) [![NuGet downloads](https://img.shields.io/nuget/dt/Sabbour.Mxc.Sdk.svg)](https://www.nuget.org/packages/Sabbour.Mxc.Sdk) [![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE) [![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)

A .NET 10 SDK for [MXC (Microsoft eXecution Containers)](https://github.com/microsoft/mxc) — a faithful port of the TypeScript `@microsoft/mxc-sdk` package that brings the same policy-driven sandboxing to .NET applications. MXC runs a command inside OS-level isolation governed by a single policy: it decides which filesystem paths the process can read or write and whether it can reach the network, and enforces those limits at the kernel boundary.

> [!WARNING]
> This is experimental code. APIs, behavior, and packaging may change without notice, and it is not supported for production use. The underlying MXC executor is itself under active development.

## Table of contents

- [Policy enforcement in action](#policy-enforcement-in-action)
- [Install](#install)
- [Enabling isolation backends (host setup)](#enabling-isolation-backends-host-setup)
- [Quickstart](#quickstart)
- [Examples](#examples)
- [API guide](#api-guide)
  - [Policy → ContainerConfig transform](#policy--containerconfig-transform)
  - [Spawning](#spawning)
  - [State-aware isolation session lifecycle](#state-aware-isolation-session-lifecycle)
  - [Platform support probing](#platform-support-probing)
  - [Error handling](#error-handling)
  - [Logging and diagnostics](#logging-and-diagnostics)
- [Version support](#version-support)
- [Running the tests](#running-the-tests)
- [Troubleshooting](#troubleshooting)
- [License](#license)

## Policy enforcement in action

One small probe does two things a workload usually should not: it reaches the network, then reads an SSH private key that lives *outside* its workspace. The SDK runs that **same probe twice** through `MxcSdk.SpawnSandboxAsync`, changing nothing but the `SandboxPolicy`:

```csharp
// probe.sh, run unchanged under both policies:
//   curl https://api.github.com/zen     # reach the network
//   cat  ~/.ssh/id_ed25519              # read a credential outside the workspace
string command = "sh probe.sh";

// WITHOUT restrictions: outbound allowed, the credential's folder is readable.
var permissive = new SandboxPolicy
{
    Version = "0.6.0-alpha",
    Network = new NetworkPolicy { AllowOutbound = true },
    Filesystem = new FilesystemPolicy { ReadwritePaths = [root] },
};

// WITH policy: no outbound, only the workspace is exposed.
var restrictive = new SandboxPolicy
{
    Version = "0.6.0-alpha",
    Network = new NetworkPolicy { AllowOutbound = false },
    Filesystem = new FilesystemPolicy { ReadwritePaths = [workspace] },
};

// Same command, same call — only the policy changes.
foreach (var policy in new[] { permissive, restrictive })
{
    var result = await MxcSdk.SpawnSandboxAsync(command, policy);
    Console.WriteLine(result.Stdout);
}
```

Running it prints the two outcomes side by side:

```text
$ dotnet run --project examples/10-policy-enforcement -c Release

credential:  /tmp/mxc-policy-demo/home/.ssh/id_ed25519 (SSH private key, outside the workspace)
workspace:   /tmp/mxc-policy-demo/workspace

=== WITHOUT restrictions  (allowOutbound=true, credential folder readable) ===
[network]    curl https://api.github.com/zen
Non-blocking is better than blocking.
[filesystem] cat /tmp/mxc-policy-demo/home/.ssh/id_ed25519
-----BEGIN OPENSSH PRIVATE KEY-----
b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZW
ThisIsAFakeDemoKeyThatExistsOnlyToBeBlockedByPolicyDoNotUseItAnywhere==
-----END OPENSSH PRIVATE KEY-----

=== WITH policy           (allowOutbound=false, only workspace readable) ===
[network]    curl https://api.github.com/zen
curl: (6) Could not resolve host: api.github.com

[filesystem] cat /tmp/mxc-policy-demo/home/.ssh/id_ed25519
cat: /tmp/mxc-policy-demo/home/.ssh/id_ed25519: No such file or directory
```

Without the policy, the probe reaches GitHub (the quote is live, so it changes per run) and prints the private key. With the policy, both attempts fail at the kernel boundary: outbound traffic is gone because the sandbox runs in its own network namespace, and the key reads as missing because its folder was never mounted into the sandbox — denial by absence, not an error code. Same binary in both runs; only the policy differs. Full walkthrough: [`examples/10-policy-enforcement`](examples/10-policy-enforcement). Output captured on WSL2 (Ubuntu 24.04) with the Linux `lxc-exec` executor.

## Install

The package is published on NuGet.org: **[Sabbour.Mxc.Sdk](https://www.nuget.org/packages/Sabbour.Mxc.Sdk)**.

```bash
dotnet add package Sabbour.Mxc.Sdk
```

Or add a `PackageReference` to your `.csproj`:

```xml
<PackageReference Include="Sabbour.Mxc.Sdk" Version="0.6.1" />
```

## Enabling isolation backends (host setup)

The SDK picks a containment backend, but the host has to have that backend lit up first. Which backends are available depends on the host OS — Windows and WSL/Linux are covered separately below.

### Windows host

The default `processcontainer` path works out of the box on recent Windows builds; the other backends need one-time setup. The steps below are the ones that get each backend running with the `v0.6.1` executor binaries.

Check which tier the executor will select on the current host (read-only, no admin):

```powershell
$arch = "x64" # use "arm64" on ARM64 hosts
& "$env:MXC_BIN_DIR\$arch\wxc-exec.exe" --probe
```

#### processcontainer

`processcontainer` has three tiers (highest first): `base-container`, `appcontainer-bfs`, `appcontainer-dacl`. The probe reports which one applies.

- **`base-container`** uses an experimental kernel API that ships behind a Windows Feature Store gate on current builds. When the gate is closed, the executor returns `E_NOTIMPL` even though the API is present. Light it up with [ViVeTool](https://github.com/thebookisclosed/ViVe) (download the build that matches your CPU arch), then **reboot**:

  ```powershell
  # Run elevated. Use comma-separated IDs — repeated /id: flags are rejected.
  .\ViVeTool.exe /enable /id:61389575,61155944
  .\ViVeTool.exe /query /id:61389575
  ```

  The query command should report the feature as enabled before you retry the executor.

  Older policy schemas (`0.4.0-alpha`) take the ungated AppContainer path instead, so they run without this gate.

- **`appcontainer-dacl`** needs a one-time host preparation that grants the AppContainer SIDs the ACEs they require. Run `wxc-host-prep` elevated (it exits non-zero if not):

  ```powershell
  $arch = "x64" # use "arm64" on ARM64 hosts
  & "$env:MXC_BIN_DIR\$arch\wxc-host-prep.exe" prepare-system-drive   # one-time, persists
  & "$env:MXC_BIN_DIR\$arch\wxc-host-prep.exe" prepare-null-device    # per-boot
  ```

#### windows_sandbox

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

#### microvm (NanVix)

Requires the `nanvixd.exe` daemon, which is **not included** in the public `mxc-release-binaries.zip`, so it cannot run from the released binaries. This backend also rejects any policy that sets `network` (no network-policy enforcement).

#### wslc

`wslc` runs Linux OCI containers in a dedicated WSL-managed Hyper-V VM. It is still under development and requires WSL 2.8.1 or newer.

#### hyperlight

`hyperlight` runs workloads as x86_64 guest code inside a hardware micro-VM (WHP on Windows, KVM on Linux). It requires an **x86_64 host** — the snapshot tooling has no arm64 guest, so on an arm64 machine `--setup-hyperlight` exits with `requires x86_64 (Hyperlight needs KVM or WHP)`.

On an x86_64 host, warm the published snapshot once before first use (pulls the kernel + initrd via docker/podman):

```powershell
& "$env:MXC_BIN_DIR\x64\wxc-exec.exe" --setup-hyperlight
```

Like `windows_sandbox`, `hyperlight` is reached through a prebuilt config with `Experimental = true`, not through `CreateConfigFromPolicy`.

### WSL / Linux host

On WSL2 / Linux the SDK uses the Linux executor (`lxc-exec`). Two things light it up — verified on Ubuntu-24.04 (arm64) under WSL2, where the default `process` containment runs cleanly:

1. **Place the Linux executor where the SDK looks for it.** The released `mxc-release-binaries.zip` ships the Windows `wxc-exec.exe`; on Linux the SDK resolves `lxc-exec` from `MXC_BIN_DIR/<arch>/` (`<arch>` is `arm64` or `x64`). Copy the Linux `lxc-exec` build there and mark it executable:

   ```bash
   mkdir -p "$HOME/mxc-bin/arm64"
   cp ./lxc-exec "$HOME/mxc-bin/arm64/lxc-exec"
   chmod +x "$HOME/mxc-bin/arm64/lxc-exec"
   export MXC_BIN_DIR="$HOME/mxc-bin"
   ```

2. **Install bubblewrap.** The default `process` containment runs the workload under `bwrap`. Without it the executor exits with `Bubblewrap (bwrap) is not installed or not on PATH`:

   ```bash
   sudo apt-get install -y bubblewrap
   ```

The `process`, `lxc`, and `bubblewrap` containments target this Linux executor. The Windows-only backends (`windows_sandbox`, `microvm`, `hyperlight`) are not reachable from WSL — `microvm`/`hyperlight` need an x86_64 host with KVM, which is not exposed inside this WSL2 VM.

## Quickstart

Start with a policy and turn it into the backend config that the native executor understands:

```csharp
using Sabbour.Mxc.Sdk;

var policy = new SandboxPolicy
{
    Version = "0.6.0-alpha",
    Network = new NetworkPolicy { AllowOutbound = false },
};

ContainerConfig config = MxcSdk.CreateConfigFromPolicy(policy, containment: "process");
Console.WriteLine(config.Containment);
```

Spawning a sandboxed process uses the same policy, but it also needs the native MXC executor. See [Troubleshooting](#troubleshooting) before running spawn examples.

## Examples

The [`examples/`](examples/) folder has runnable console projects that reference the local SDK source, one per scenario — policy-to-config transforms, platform probing, buffered spawns, filesystem and network policy, the state-aware lifecycle, and the side-by-side [policy enforcement demo](examples/10-policy-enforcement) shown above. See [`examples/README.md`](examples/README.md) for the full list and which ones need the native executor.

Run any example with:

```powershell
dotnet run --project examples\01-policy-to-config -c Release
```

## API guide

### Policy → ContainerConfig transform

Convert a security-intent policy into a backend-specific container configuration:

```csharp
ContainerConfig config = MxcSdk.CreateConfigFromPolicy(policy, containment: "process");
// Customize further before spawning:
config = config with
{
    Process = config.Process! with { CommandLine = "python -c \"print('hi')\"" }
};
```

### Spawning

#### Live PTY (interactive)

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

#### Buffered one-shot (TS spawnSandboxAsync)

```csharp
var result = await MxcSdk.SpawnSandboxAsync(
    "node -e \"console.log('done')\"",
    policy);
// result.Stdout, result.Stderr, result.ExitCode
```

#### Pipe mode

When PTY overhead is unnecessary (CI, batch jobs), use pipe mode for separate stdout/stderr:

```csharp
using var conn = MxcSdk.SpawnSandboxProcessFromConfig(config,
    new SandboxSpawnOptions { UsePty = false });
int exitCode = await conn.WaitForExitAsync();
Console.WriteLine(conn.GetStdout());
```

### State-aware isolation session lifecycle

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
    new IsolationSessionExecConfig
    {
        Process = new ProcessConfig { CommandLine = "dir" }
    });
var exit = await pty.WaitForExitAsync();

// 3b. Exec (buffered — async)
var execResult = await MxcSdk.ExecInSandboxAsync(sandboxId,
    new IsolationSessionExecConfig
    {
        Process = new ProcessConfig { CommandLine = "echo done" }
    });

// 4. Stop
await MxcSdk.StopSandboxAsync(sandboxId);

// 5. Deprovision
await MxcSdk.DeprovisionSandboxAsync(sandboxId);
```

### Platform support probing

Detect available containment backends on the current host:

```csharp
PlatformSupport support = MxcSdk.GetPlatformSupport();
if (support.IsSupported)
{
    Console.WriteLine($"Backends: {string.Join(", ", support.AvailableMethods)}");
    Console.WriteLine($"Isolation tier: {support.IsolationTier}");
}
```

### Error handling

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

### Logging and diagnostics

The diagnostics layer exposes `IMxcLogger` and `FileLogger` for custom sinks. Spawn diagnostics are enabled through `SandboxSpawnOptions.Debug`; set `LogDir` when you want deterministic log placement. Sensitive tokens such as `wamToken` are redacted before they reach log files.

```csharp
using Sabbour.Mxc.Sdk.Sandbox;

var options = new SandboxSpawnOptions
{
    Debug = true,
    LogDir = @"C:\mxc-logs"
};
```

## Version support

This SDK validates the policy `version` field. The example pins `0.6.0-alpha`, the schema shipped by the latest stable executor release (`v0.6.1`) — match it to the binary you install. The SDK accepts versions from `0.4.0-alpha` (minimum) up to `0.7.0-alpha` (the newest schema it understands); when you omit `version`, it fills in `0.7.0-alpha`. Policies outside that range are rejected at config-creation time.

For the canonical field reference — `version`, `filesystem`, `network`, `ui`, and `timeoutMs` — see the upstream [MXC Sandbox Policy Spec §5 (SandboxPolicy)](https://github.com/microsoft/mxc/blob/v0.6.1/docs/sandbox-policy/v1/policy.md#5-sandboxpolicy), pinned to the `v0.6.1` release.

## Running the tests

### Tier 1: unit tests

```powershell
dotnet test
```

Unit tests do not need the native executor and run on any OS supported by .NET 10.

### Tier 2: integration/e2e tests

Download the prebuilt executor from [microsoft/mxc releases](https://github.com/microsoft/mxc/releases). The latest release is `v0.6.1`, with the `mxc-release-binaries.zip` asset.

Unzip the archive, then set `MXC_BIN_DIR` to the folder that contains the architecture-specific executor directory (`x64` or `arm64`):

```powershell
$env:MXC_BIN_DIR = "C:\mxc-bin"
$env:MXC_INTEGRATION_TESTS = "1"
dotnet test
```

For test runs, prefer `$env:MXC_BIN_DIR\x64\wxc-exec.exe` or `$env:MXC_BIN_DIR\arm64\wxc-exec.exe` so the executor path is deterministic. On Windows, the default `processcontainer` backend needs Windows 11 24H2 or later (build 26100+) and does not require admin.

## Troubleshooting

### The SDK cannot find the executor

This package is a library, not a command-line tool. Sandbox execution shells out to the native MXC executor (`wxc-exec.exe` on Windows, `lxc-exec` on Linux, and `mxc-exec-mac` for macOS seatbelt).

Use one of these resolution paths:

1. Set `SandboxSpawnOptions.ExecutablePath` for a single spawn.
2. Set `MXC_BIN_DIR` to the root directory that contains `<arch>\wxc-exec.exe` on Windows, or `<arch>/lxc-exec` on Linux (`<arch>` is `x64` or `arm64`).
3. Package/publish layouts can include `bin\<arch>\...` next to the SDK assembly or app base directory.
4. Development builds can be found under repo Cargo target paths.
5. On Windows, `PATH` is a last fallback. Prefer `ExecutablePath` or `MXC_BIN_DIR` for predictable behavior.

### Common errors

| Symptom | Cause | Fix |
| --- | --- | --- |
| `wxc-exec.exe not found` / `lxc-exec not found` | The SDK cannot locate the executor. | Set `MXC_BIN_DIR` or `ExecutablePath` (see above). |
| `Bubblewrap (bwrap) is not installed or not on PATH` | The Linux `process` containment runs the workload under `bwrap`. | `sudo apt-get install -y bubblewrap`. |
| `E_NOTIMPL` from the Windows `base-container` tier | The Feature Store gate for the experimental kernel API is closed on this build. | Enable the velocity keys with ViVeTool and reboot — see [Enabling isolation backends](#enabling-isolation-backends-host-setup). |
| `iptables ... Permission denied (you must be root)` | Host-based outbound allowlisting (`AllowedHosts`) programs `iptables`, which needs `CAP_NET_ADMIN`. | Run the host process elevated (`sudo`) on Linux/WSL. |
| Policy rejected at config creation | The policy `version` is outside the supported range. | Use a `version` between `0.4.0-alpha` and `0.7.0-alpha` — see [Version support](#version-support). |

## License

MIT
