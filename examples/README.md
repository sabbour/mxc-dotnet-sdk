# MXC SDK examples

These are minimal, self-contained console demos for common SDK scenarios. Each project references the local SDK source.

| Project | Scenario | Needs native binary? |
| --- | --- | --- |
| `01-policy-to-config` | Convert a sandbox policy into a backend `ContainerConfig`. | No |
| `02-platform-support` | Probe sandbox backend support on the current host. | No |
| `03-buffered-spawn` | Run a command with buffered stdout/stderr. | Yes |
| `04-network-policy` | Show host-filtering validation for allowed hosts. | No |
| `05-state-aware-lifecycle` | Provision, start, exec, stop, and deprovision a state-aware sandbox. | Yes |

Run one example:

```powershell
dotnet run --project examples\01-policy-to-config
```

The binary-dependent demos (`03-buffered-spawn` and `05-state-aware-lifecycle`) need the MXC executor. Download `mxc-release-binaries.zip` from [microsoft/mxc releases](https://github.com/microsoft/mxc/releases) (v0.6.1), unzip it, and set `MXC_BIN_DIR` to the folder containing `<arch>\wxc-exec.exe`. See the root README's [Running the tests](../README.md#running-the-tests) section for setup details.
