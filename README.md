# Sabbour.Mxc.Sdk

[![CI](https://github.com/sabbour/mxc-dotnet-sdk/actions/workflows/ci.yml/badge.svg)](https://github.com/sabbour/mxc-dotnet-sdk/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/Sabbour.Mxc.Sdk.svg)](https://www.nuget.org/packages/Sabbour.Mxc.Sdk) [![NuGet downloads](https://img.shields.io/nuget/dt/Sabbour.Mxc.Sdk.svg)](https://www.nuget.org/packages/Sabbour.Mxc.Sdk) [![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE) [![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)

A .NET 10 SDK for [MXC (Microsoft eXecution Containers)](https://github.com/microsoft/mxc) — a faithful port of the TypeScript `@microsoft/mxc-sdk` package.

> [!WARNING]
> This is experimental code. APIs, behavior, and packaging may change without notice, and it is not supported for production use. The underlying MXC executor is itself under active development.

## Install

The package is published on NuGet.org: **[Sabbour.Mxc.Sdk](https://www.nuget.org/packages/Sabbour.Mxc.Sdk)**.

```bash
dotnet add package Sabbour.Mxc.Sdk
```

Or add a `PackageReference` to your `.csproj`:

```xml
<PackageReference Include="Sabbour.Mxc.Sdk" Version="0.6.1" />
```

## Table of contents

- [Quickstart](#quickstart)
- [Execution examples](#execution-examples)
  - [Policy enforcement, side by side](#policy-enforcement-side-by-side)
- [API guide](#api-guide)
  - [Policy → ContainerConfig transform](#policy--containerconfig-transform)
  - [Spawning](#spawning)
  - [State-aware isolation session lifecycle](#state-aware-isolation-session-lifecycle)
  - [Platform support probing](#platform-support-probing)
  - [Error handling](#error-handling)
  - [Logging and diagnostics](#logging-and-diagnostics)
- [Version support](#version-support)
- [No CLI / executor resolution](#no-cli--executor-resolution)
- [Running the tests](#running-the-tests)
- [Enabling isolation backends (host setup)](#enabling-isolation-backends-host-setup)
- [License](#license)

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

Spawning a sandboxed process uses the same policy, but it also needs the native MXC executor. See [No CLI / executor resolution](#no-cli--executor-resolution) before running spawn examples.

## Execution examples

The [`examples/`](examples/) folder contains runnable console projects that reference the local SDK source. The table in [`examples/README.md`](examples/README.md) is the canonical scenario list and marks which examples need the native executor.

The commands below were run with `-c Release` from the repository root.

### Policy enforcement, side by side

[`10-policy-enforcement`](examples/10-policy-enforcement) is the demo worth seeing first. One small probe tries two things — fetch a URL over the network and read a secret file that lives *outside* its workspace:

```text
[network]    curl https://api.github.com/zen
[filesystem] cat /tmp/mxc-policy-demo/secret.txt
```

The SDK runs that **same probe twice** through `MxcSdk.SpawnSandboxAsync`, changing nothing but the [`SandboxPolicy`](src/Sabbour.Mxc.Sdk):

- **Permissive** — `Network.AllowOutbound = true`, and the secret's folder is in `Filesystem.ReadwritePaths`.
- **Restrictive** — `Network.AllowOutbound = false`, and only the workspace is in `ReadwritePaths`, so the secret folder is never exposed.

The output below is captured from a WSL2 (Ubuntu 24.04) run using the Linux `lxc-exec` executor with bubblewrap containment:

```text
$ dotnet run --project examples/10-policy-enforcement -c Release

secret file: /tmp/mxc-policy-demo/secret.txt (outside the workspace)
workspace:   /tmp/mxc-policy-demo/workspace

=== WITHOUT restrictions  (allowOutbound=true, secret folder readable) ===
[network]    curl https://api.github.com/zen
Anything added dilutes everything else.
[filesystem] cat /tmp/mxc-policy-demo/secret.txt
API_KEY=do-not-leak

=== WITH policy           (allowOutbound=false, only workspace readable) ===
[network]    curl https://api.github.com/zen
curl: (6) Could not resolve host: api.github.com
[filesystem] cat /tmp/mxc-policy-demo/secret.txt
cat: /tmp/mxc-policy-demo/secret.txt: No such file or directory
```

Without the policy, the probe reaches GitHub (the quote is live, so it changes per run) and prints the secret. With the policy, both attempts fail at the kernel boundary: outbound traffic is gone because the sandbox runs in its own network namespace, and the secret is reported missing because the file was never mounted into the sandbox — denial by absence, not by an error code. The probe binary is unchanged between the two runs; only the policy differs.

This example needs the native MXC executor (see [Running the tests](#running-the-tests) for setup). On Windows hosts it adapts the probe to `cmd`/`type`; backend availability is covered in [Enabling isolation backends](#enabling-isolation-backends-host-setup).

The remaining examples each focus on a single scenario.

### `01-policy-to-config` — policy to backend config

```powershell
dotnet run --project examples\01-policy-to-config -c Release
```

```text
Policy -> backend ContainerConfig transform. This example needs no native binary.
{
  "version": "0.6.0-alpha",
  "containerId": "5ce1e9ed",
  "lifecycle": {
    "destroyOnExit": true,
    "preservePolicy": false
  },
  "process": {
    "commandLine": "",
    "timeout": 0
  },
  "filesystem": {
    "readwritePaths": [
      "C:\\Users\\asabbour\\Git\\mxc-dotnet-sdk"
    ],
    "readonlyPaths": [],
    "deniedPaths": []
  },
  "ui": {
    "disable": true,
    "clipboard": "none",
    "injection": false
  },
  "network": {
    "defaultPolicy": "allow",
    "enforcementMode": "capabilities"
  },
  "containment": "process",
  "processContainer": {
    "name": "5ce1e9ed",
    "leastPrivilege": false,
    "capabilities": [
      "internetClient"
    ],
    "ui": {
      "isolation": "container",
      "desktopSystemControl": false,
      "systemSettings": "none",
      "ime": false
    }
  }
}
```

### `02-platform-support` — platform probe

```powershell
dotnet run --project examples\02-platform-support -c Release
```

```text
Platform support probe. This detects sandbox backends on the current host.
IsSupported: True
AvailableMethods: ProcessContainer
IsolationTier:
```

### `03-buffered-spawn` — buffered stdout/stderr

```powershell
dotnet run --project examples\03-buffered-spawn -c Release
```

This scenario needs the native MXC executor. Set `MXC_BIN_DIR` or `SandboxSpawnOptions.ExecutablePath` first; see [Running the tests](#running-the-tests) for executor setup.

### `04-network-policy` — host-filtering validation

```powershell
dotnet run --project examples\04-network-policy -c Release
```

```text
Valid policy succeeded: allowOutbound=true with allowedHosts=[api.contoso.com].
Caught expected validation error: allowedHosts/blockedHosts require allowOutbound to be true
Host filtering requires allowOutbound=true unless the selected backend can enforce per-host rules itself.
```

### `05-state-aware-lifecycle` — provision/start/exec/stop/deprovision

```powershell
dotnet run --project examples\05-state-aware-lifecycle -c Release
```

This scenario needs the native MXC executor. Set `MXC_BIN_DIR` or `SandboxSpawnOptions.ExecutablePath` first; see [Running the tests](#running-the-tests) for executor setup.

### `06-hello-world` — sandboxed command with a named container

```powershell
dotnet run --project examples\06-hello-world -c Release
```

With no executor installed, the example exits cleanly and prints the setup guidance:

```text
The native executor could not run this scenario: wxc-exec.exe not found. Set ExecutablePath or ensure it exists in a standard location.
This scenario needs the MXC executor.
Download mxc-release-binaries.zip from https://github.com/microsoft/mxc/releases (v0.6.1), unzip it, and set MXC_BIN_DIR to the folder containing <arch>\wxc-exec.exe.
Then run this example again.
```

### `07-filesystem-access` — read/write, read-only, and denied paths

```powershell
dotnet run --project examples\07-filesystem-access -c Release
```

```text
Filesystem access control policy -> backend ContainerConfig. This example needs no native binary.
readwritePaths grant read+write, readonlyPaths grant read, deniedPaths block all access, and clearPolicyOnExit resets the policy when the shell exits.
{
  "version": "0.6.0-alpha",
  "containerId": "1f10e57f",
  "lifecycle": {
    "destroyOnExit": true,
    "preservePolicy": false
  },
  "process": {
    "commandLine": "",
    "timeout": 0
  },
  "filesystem": {
    "readwritePaths": [
      "C:\\temp\\workspace"
    ],
    "readonlyPaths": [
      "C:\\ProgramData\\shared-config"
    ],
    "deniedPaths": [
      "C:\\Windows\\System32"
    ]
  },
  "ui": {
    "disable": true,
    "clipboard": "none",
    "injection": false
  },
  "network": {
    "defaultPolicy": "block",
    "enforcementMode": "capabilities"
  },
  "containment": "process",
  "processContainer": {
    "name": "1f10e57f",
    "leastPrivilege": false,
    "capabilities": [],
    "ui": {
      "isolation": "container",
      "desktopSystemControl": false,
      "systemSettings": "none",
      "ime": false
    }
  }
}
```

### `08-network-restricted` — outbound allow-list

```powershell
dotnet run --project examples\08-network-restricted -c Release
```

This scenario needs the native MXC executor. Set `MXC_BIN_DIR` or `SandboxSpawnOptions.ExecutablePath` first; see [Running the tests](#running-the-tests) for executor setup.

### `09-network-proxy` — localhost and built-in proxy config

```powershell
dotnet run --project examples\09-network-proxy -c Release
```

```text
Network proxy policy -> backend ContainerConfig. This example needs no native binary.
ProxyConfig is a discriminated union: choose exactly one of localhost, builtinTestServer, or url.

1) Route traffic through an external proxy on localhost:8080.
{
  "version": "0.6.0-alpha",
  "containerId": "da7fde8e",
  "lifecycle": {
    "destroyOnExit": true,
    "preservePolicy": false
  },
  "process": {
    "commandLine": "",
    "timeout": 0
  },
  "filesystem": {
    "readwritePaths": [],
    "readonlyPaths": [],
    "deniedPaths": []
  },
  "ui": {
    "disable": true,
    "clipboard": "none",
    "injection": false
  },
  "network": {
    "defaultPolicy": "allow",
    "proxy": {
      "localhost": 8080
    },
    "enforcementMode": "capabilities"
  },
  "containment": "process",
  "processContainer": {
    "name": "da7fde8e",
    "leastPrivilege": false,
    "capabilities": [
      "internetClient"
    ],
    "ui": {
      "isolation": "container",
      "desktopSystemControl": false,
      "systemSettings": "none",
      "ime": false
    }
  }
}

2) Route traffic through the built-in test proxy server.
{
  "version": "0.6.0-alpha",
  "containerId": "4d8bb604",
  "lifecycle": {
    "destroyOnExit": true,
    "preservePolicy": false
  },
  "process": {
    "commandLine": "",
    "timeout": 0
  },
  "filesystem": {
    "readwritePaths": [],
    "readonlyPaths": [],
    "deniedPaths": []
  },
  "ui": {
    "disable": true,
    "clipboard": "none",
    "injection": false
  },
  "network": {
    "defaultPolicy": "allow",
    "proxy": {
      "builtinTestServer": true
    },
    "enforcementMode": "capabilities"
  },
  "containment": "process",
  "processContainer": {
    "name": "4d8bb604",
    "leastPrivilege": false,
    "capabilities": [
      "internetClient"
    ],
    "ui": {
      "isolation": "container",
      "desktopSystemControl": false,
      "systemSettings": "none",
      "ime": false
    }
  }
}
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

## No CLI / executor resolution

This package is a library, not a command-line tool. Sandbox execution shells out to the native MXC executor (`wxc-exec.exe` on Windows, `lxc-exec` on Linux, and `mxc-exec-mac` for macOS seatbelt).

Use one of these resolution paths:

1. Set `SandboxSpawnOptions.ExecutablePath` for a single spawn.
2. Set `MXC_BIN_DIR` to the root directory that contains `<arch>\wxc-exec.exe` on Windows, or `<arch>/lxc-exec` on Linux (`<arch>` is `x64` or `arm64`).
3. Package/publish layouts can include `bin\<arch>\...` next to the SDK assembly or app base directory.
4. Development builds can be found under repo Cargo target paths.
5. On Windows, `PATH` is a last fallback. Prefer `ExecutablePath` or `MXC_BIN_DIR` for predictable behavior.

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

## License

MIT
