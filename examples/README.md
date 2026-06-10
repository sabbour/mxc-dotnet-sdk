# MXC SDK examples

These are minimal, self-contained console demos for common SDK scenarios. Each project references the local SDK source.

| Project | Scenario | Needs native binary? |
| --- | --- | --- |
| `01-policy-to-config` | Convert a sandbox policy into a backend `ContainerConfig`. | No |
| `02-platform-support` | Probe sandbox backend support on the current host. | No |
| `03-buffered-spawn` | Run a command with buffered stdout/stderr. | Yes |
| `04-network-policy` | Show host-filtering validation for allowed hosts. | No |
| `05-state-aware-lifecycle` | Provision, start, exec, stop, and deprovision a state-aware sandbox. | Yes |
| `06-hello-world` | Run a command in a sandbox with a named container. | Yes |
| `07-filesystem-access` | Grant, restrict, and deny filesystem paths in a policy. | No |
| `08-network-restricted` | Allow outbound traffic to a single host only. | Yes |
| `09-network-proxy` | Route sandbox traffic through a localhost or built-in test proxy. | No |
| `10-policy-enforcement` | Run one probe (network call + read of an SSH key outside the workspace) under a permissive then a restrictive policy, side by side. | Yes |
| `11-windows-sandbox` | Run a command in a disposable Windows Sandbox VM (Windows 11+ only, requires elevation). | Yes |

Run one example:

```powershell
dotnet run --project examples\01-policy-to-config
```

The binary-dependent demos (`03-buffered-spawn`, `05-state-aware-lifecycle`, `06-hello-world`, `08-network-restricted`, `10-policy-enforcement`, and `11-windows-sandbox`) need the MXC executor. Download `mxc-release-binaries.zip` from [microsoft/mxc releases](https://github.com/microsoft/mxc/releases) (v0.6.1), unzip it, and set `MXC_BIN_DIR` to the folder containing `<arch>\wxc-exec.exe`. See the root README's [Running the tests](../README.md#running-the-tests) section for setup details.

`11-windows-sandbox` is Windows 11+ only and must run elevated (Administrator) — the executor probes the Windows Sandbox feature with `dism`, which requires elevation. Running it non-elevated reports a misleading "not enabled" error even when the feature is on.
