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

## License

MIT
