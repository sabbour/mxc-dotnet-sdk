// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Sabbour.Mxc.Sdk;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests;

/// <summary>
/// JSON wire-format fidelity tests. Verifies that C# DTOs serialize to byte-identical
/// JSON as the TypeScript source emits for wxc-exec consumption.
/// </summary>
public class JsonWireFormatTests
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    #region ContainerConfig serialization

    [Fact]
    public void ContainerConfig_MinimalConfig_SerializesCorrectly()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            Process = new ProcessConfig { CommandLine = "echo hello" }
        };

        var json = JsonSerializer.Serialize(config, s_options);
        var expected = "{\"version\":\"1.0.0\",\"process\":{\"commandLine\":\"echo hello\"}}";
        Assert.Equal(expected, json);
    }

    [Fact]
    public void ContainerConfig_WithContainment_SerializesBackendAsString()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            Containment = ContainmentValue.FromString("processcontainer"),
            Process = new ProcessConfig { CommandLine = "cmd /c dir" }
        };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Contains("\"containment\":\"processcontainer\"", json);
    }

    [Fact]
    public void ContainerConfig_WithFilesystem_SerializesFieldNames()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            Filesystem = new FilesystemConfig
            {
                ReadwritePaths = ["/home/user"],
                ReadonlyPaths = ["/usr"],
                DeniedPaths = ["/etc/shadow"],
                ClearPolicyOnExit = true
            }
        };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Contains("\"readwritePaths\":[\"/home/user\"]", json);
        Assert.Contains("\"readonlyPaths\":[\"/usr\"]", json);
        Assert.Contains("\"deniedPaths\":[\"/etc/shadow\"]", json);
        Assert.Contains("\"clearPolicyOnExit\":true", json);
    }

    [Fact]
    public void ContainerConfig_WithNetwork_SerializesEnumAsString()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            Network = new NetworkConfig
            {
                EnforcementMode = NetworkEnforcementMode.Both,
                DefaultPolicy = NetworkDefaultPolicy.Block,
                AllowLocalNetwork = false,
                AllowedHosts = ["example.com"]
            }
        };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Contains("\"enforcementMode\":\"both\"", json);
        Assert.Contains("\"defaultPolicy\":\"block\"", json);
        Assert.Contains("\"allowLocalNetwork\":false", json);
        Assert.Contains("\"allowedHosts\":[\"example.com\"]", json);
    }

    [Fact]
    public void ContainerConfig_WithLifecycle_SerializesCamelCase()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            Lifecycle = new LifecycleConfig
            {
                DestroyOnExit = false,
                PreservePolicy = true
            }
        };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Contains("\"destroyOnExit\":false", json);
        Assert.Contains("\"preservePolicy\":true", json);
    }

    [Fact]
    public void ContainerConfig_WithProcessContainer_SerializesCamelCase()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            ProcessContainer = new ProcessContainerConfig
            {
                Name = "CLI",
                LeastPrivilege = true,
                Capabilities = ["registryRead", "internetClient"]
            }
        };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Contains("\"processContainer\":{", json);
        Assert.Contains("\"name\":\"CLI\"", json);
        Assert.Contains("\"leastPrivilege\":true", json);
        Assert.Contains("\"capabilities\":[\"registryRead\",\"internetClient\"]", json);
    }

    [Fact]
    public void ContainerConfig_WithUi_SerializesClipboardEnum()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            Ui = new UiConfig
            {
                Disable = true,
                Clipboard = ClipboardPolicy.None,
                Injection = false
            }
        };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Contains("\"ui\":{\"disable\":true,\"clipboard\":\"none\",\"injection\":false}", json);
    }

    [Fact]
    public void ContainerConfig_WithExperimental_SerializesNested()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            Experimental = new ExperimentalConfig
            {
                Wslc = new WslcConfig
                {
                    Image = "ubuntu:22.04",
                    CpuCount = 4,
                    MemoryMb = 2048,
                    Gpu = true,
                    PortMappings = [new PortMapping { WindowsPort = 8080, ContainerPort = 80 }]
                }
            }
        };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Contains("\"experimental\":{\"wslc\":{", json);
        Assert.Contains("\"image\":\"ubuntu:22.04\"", json);
        Assert.Contains("\"cpuCount\":4", json);
        Assert.Contains("\"memoryMb\":2048", json);
        Assert.Contains("\"gpu\":true", json);
        Assert.Contains("\"windowsPort\":8080", json);
        Assert.Contains("\"containerPort\":80", json);
    }

    [Fact]
    public void ContainerConfig_WithSeatbelt_SerializesCorrectly()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            Experimental = new ExperimentalConfig
            {
                Seatbelt = new SeatbeltConfig
                {
                    NestedPty = true,
                    KeychainAccess = false,
                    ExtraMachLookups = ["com.apple.audio.audiohald"]
                }
            }
        };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Contains("\"seatbelt\":{", json);
        Assert.Contains("\"nestedPty\":true", json);
        Assert.Contains("\"keychainAccess\":false", json);
        Assert.Contains("\"extraMachLookups\":[\"com.apple.audio.audiohald\"]", json);
    }

    [Fact]
    public void ContainerConfig_WithBaseProcessUi_SerializesIsolationEnum()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            ProcessContainer = new ProcessContainerConfig
            {
                Ui = new BaseProcessUiConfig
                {
                    Isolation = UiIsolationLevel.Desktop,
                    DesktopSystemControl = false,
                    SystemSettings = "deny",
                    Ime = true
                }
            }
        };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Contains("\"isolation\":\"desktop\"", json);
        Assert.Contains("\"desktopSystemControl\":false", json);
        Assert.Contains("\"systemSettings\":\"deny\"", json);
        Assert.Contains("\"ime\":true", json);
    }

    [Fact]
    public void ContainerConfig_NullOptionalFields_OmittedFromJson()
    {
        var config = new ContainerConfig { Version = "1.0.0" };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Equal("{\"version\":\"1.0.0\"}", json);
        Assert.DoesNotContain("containment", json);
        Assert.DoesNotContain("process", json);
        Assert.DoesNotContain("filesystem", json);
        Assert.DoesNotContain("network", json);
    }

    #endregion

    #region Round-trip tests

    [Fact]
    public void ContainerConfig_RoundTrip_FullConfig()
    {
        var original = new ContainerConfig
        {
            Version = "1.0.0",
            ContainerId = "test-container-1",
            Containment = ContainmentValue.FromString("bubblewrap"),
            Lifecycle = new LifecycleConfig { DestroyOnExit = true },
            Process = new ProcessConfig
            {
                CommandLine = "python -c \"print('hello')\"",
                Cwd = "/home/user",
                Env = ["PATH=/usr/bin", "HOME=/home/user"],
                Timeout = 30000
            },
            Filesystem = new FilesystemConfig
            {
                ReadwritePaths = ["/workspace"],
                ReadonlyPaths = ["/usr/lib"],
                DeniedPaths = ["/etc/shadow"]
            },
            Network = new NetworkConfig
            {
                EnforcementMode = NetworkEnforcementMode.Firewall,
                DefaultPolicy = NetworkDefaultPolicy.Block,
                AllowedHosts = ["api.github.com", "registry.npmjs.org"]
            }
        };

        var json = JsonSerializer.Serialize(original, s_options);
        var deserialized = JsonSerializer.Deserialize<ContainerConfig>(json, s_options);
        var reserialized = JsonSerializer.Serialize(deserialized, s_options);

        Assert.Equal(json, reserialized);
    }

    [Fact]
    public void ContainerConfig_RoundTrip_FromKnownJson()
    {
        // Known-good JSON as wxc-exec would receive (arbitrary key order)
        var knownJson = "{\"version\":\"1.0.0\",\"containment\":\"processcontainer\",\"process\":{\"commandLine\":\"cmd /c echo hello\",\"cwd\":\"C:\\\\Users\\\\test\",\"env\":[\"PATH=C:\\\\Windows\"],\"timeout\":5000},\"filesystem\":{\"readwritePaths\":[\"C:\\\\Temp\"],\"clearPolicyOnExit\":true},\"network\":{\"enforcementMode\":\"both\",\"defaultPolicy\":\"block\",\"allowLocalNetwork\":false}}";

        var deserialized = JsonSerializer.Deserialize<ContainerConfig>(knownJson, s_options);
        Assert.NotNull(deserialized);
        Assert.Equal("1.0.0", deserialized.Version);
        Assert.Equal("processcontainer", deserialized.Containment?.Value);
        Assert.Equal("cmd /c echo hello", deserialized.Process!.CommandLine);
        Assert.Equal("C:\\Users\\test", deserialized.Process.Cwd);
        Assert.Equal(5000, deserialized.Process.Timeout);
        Assert.Equal(["C:\\Temp"], deserialized.Filesystem!.ReadwritePaths);
        Assert.True(deserialized.Filesystem.ClearPolicyOnExit);
        Assert.Equal(NetworkEnforcementMode.Both, deserialized.Network!.EnforcementMode);
        Assert.Equal(NetworkDefaultPolicy.Block, deserialized.Network.DefaultPolicy);

        // Re-serialize — output uses canonical TS insertion order
        var reserialized = JsonSerializer.Serialize(deserialized, s_options);
        // Expected order: version, process, filesystem, network, containment
        // (no containerId/lifecycle/ui in this config)
        var expectedJson = "{\"version\":\"1.0.0\",\"process\":{\"commandLine\":\"cmd /c echo hello\",\"cwd\":\"C:\\\\Users\\\\test\",\"env\":[\"PATH=C:\\\\Windows\"],\"timeout\":5000},\"filesystem\":{\"readwritePaths\":[\"C:\\\\Temp\"],\"clearPolicyOnExit\":true},\"network\":{\"defaultPolicy\":\"block\",\"allowLocalNetwork\":false,\"enforcementMode\":\"both\"},\"containment\":\"processcontainer\"}";
        Assert.Equal(expectedJson, reserialized);
    }

    [Fact]
    public void SandboxPolicy_RoundTrip()
    {
        var policy = new SandboxPolicy
        {
            Version = "1.0.0",
            Filesystem = new FilesystemPolicy
            {
                ReadwritePaths = ["/tmp"],
                ClearPolicyOnExit = true
            },
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                AllowedHosts = ["github.com"]
            },
            Ui = new UiPolicy
            {
                AllowWindows = false,
                Clipboard = ClipboardPolicy.Read,
                AllowInputInjection = false
            },
            TimeoutMs = 60000
        };

        var json = JsonSerializer.Serialize(policy, s_options);
        var deserialized = JsonSerializer.Deserialize<SandboxPolicy>(json, s_options);
        var reserialized = JsonSerializer.Serialize(deserialized, s_options);

        Assert.Equal(json, reserialized);
    }

    [Fact]
    public void SandboxPolicy_WireFormat_MatchesExpected()
    {
        var policy = new SandboxPolicy
        {
            Version = "1.0.0",
            TimeoutMs = 30000
        };

        var json = JsonSerializer.Serialize(policy, s_options);
        Assert.Equal("{\"version\":\"1.0.0\",\"timeoutMs\":30000}", json);
    }

    #endregion

    #region Enum wire-value tests

    [Theory]
    [InlineData(ContainmentBackend.ProcessContainer, "processcontainer")]
    [InlineData(ContainmentBackend.WindowsSandbox, "windows_sandbox")]
    [InlineData(ContainmentBackend.Wslc, "wslc")]
    [InlineData(ContainmentBackend.Lxc, "lxc")]
    [InlineData(ContainmentBackend.Microvm, "microvm")]
    [InlineData(ContainmentBackend.Hyperlight, "hyperlight")]
    [InlineData(ContainmentBackend.Seatbelt, "seatbelt")]
    [InlineData(ContainmentBackend.IsolationSession, "isolation_session")]
    [InlineData(ContainmentBackend.Bubblewrap, "bubblewrap")]
    public void ContainmentBackend_SerializesToExactWireString(ContainmentBackend value, string expected)
    {
        var json = JsonSerializer.Serialize(value, s_options);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(ContainmentType.Process, "process")]
    [InlineData(ContainmentType.Vm, "vm")]
    [InlineData(ContainmentType.Microvm, "microvm")]
    public void ContainmentType_SerializesToExactWireString(ContainmentType value, string expected)
    {
        var json = JsonSerializer.Serialize(value, s_options);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(IsolationTier.BaseContainer, "base-container")]
    [InlineData(IsolationTier.AppContainerBfs, "appcontainer-bfs")]
    [InlineData(IsolationTier.AppContainerDacl, "appcontainer-dacl")]
    public void IsolationTier_SerializesToExactWireString(IsolationTier value, string expected)
    {
        var json = JsonSerializer.Serialize(value, s_options);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(ClipboardPolicy.None, "none")]
    [InlineData(ClipboardPolicy.Read, "read")]
    [InlineData(ClipboardPolicy.Write, "write")]
    [InlineData(ClipboardPolicy.All, "all")]
    public void ClipboardPolicy_SerializesToExactWireString(ClipboardPolicy value, string expected)
    {
        var json = JsonSerializer.Serialize(value, s_options);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(NetworkEnforcementMode.Capabilities, "capabilities")]
    [InlineData(NetworkEnforcementMode.Firewall, "firewall")]
    [InlineData(NetworkEnforcementMode.Both, "both")]
    public void NetworkEnforcementMode_SerializesToExactWireString(NetworkEnforcementMode value, string expected)
    {
        var json = JsonSerializer.Serialize(value, s_options);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(NetworkDefaultPolicy.Allow, "allow")]
    [InlineData(NetworkDefaultPolicy.Block, "block")]
    public void NetworkDefaultPolicy_SerializesToExactWireString(NetworkDefaultPolicy value, string expected)
    {
        var json = JsonSerializer.Serialize(value, s_options);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(UiIsolationLevel.Desktop, "desktop")]
    [InlineData(UiIsolationLevel.Handles, "handles")]
    [InlineData(UiIsolationLevel.Atoms, "atoms")]
    [InlineData(UiIsolationLevel.Container, "container")]
    public void UiIsolationLevel_SerializesToExactWireString(UiIsolationLevel value, string expected)
    {
        var json = JsonSerializer.Serialize(value, s_options);
        Assert.Equal($"\"{expected}\"", json);
    }

    #endregion

    #region Enum deserialization

    [Theory]
    [InlineData("\"processcontainer\"", ContainmentBackend.ProcessContainer)]
    [InlineData("\"windows_sandbox\"", ContainmentBackend.WindowsSandbox)]
    [InlineData("\"isolation_session\"", ContainmentBackend.IsolationSession)]
    public void ContainmentBackend_DeserializesFromWireString(string json, ContainmentBackend expected)
    {
        var result = JsonSerializer.Deserialize<ContainmentBackend>(json, s_options);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("\"base-container\"", IsolationTier.BaseContainer)]
    [InlineData("\"appcontainer-bfs\"", IsolationTier.AppContainerBfs)]
    [InlineData("\"appcontainer-dacl\"", IsolationTier.AppContainerDacl)]
    public void IsolationTier_DeserializesFromWireString(string json, IsolationTier expected)
    {
        var result = JsonSerializer.Deserialize<IsolationTier>(json, s_options);
        Assert.Equal(expected, result);
    }

    #endregion

    #region PlatformSupport wire format

    [Fact]
    public void PlatformSupport_RoundTrip()
    {
        var ps = new PlatformSupport
        {
            IsSupported = true,
            AvailableMethods = [ContainmentBackend.ProcessContainer, ContainmentBackend.Bubblewrap],
            IsolationTier = IsolationTier.BaseContainer,
            IsolationWarnings = ["Tier degraded from base-container"],
            UiCapabilities = new UiCapabilitySupport
            {
                CanBlockClipboardRead = true,
                CanBlockClipboardWrite = true,
                CanBlockInputInjection = true,
                CanBlockInputMethodChanges = false,
                CanBlockExternalUiObjects = true,
                CanBlockGlobalUiNamespace = true,
                CanBlockDesktopSwitching = false,
                CanBlockLogoffOrShutdown = true,
                CanBlockSystemParameterChanges = false,
                CanBlockDisplaySettingsChanges = true
            }
        };

        var json = JsonSerializer.Serialize(ps, s_options);
        var deserialized = JsonSerializer.Deserialize<PlatformSupport>(json, s_options);
        var reserialized = JsonSerializer.Serialize(deserialized, s_options);

        Assert.Equal(json, reserialized);
        Assert.Contains("\"isSupported\":true", json);
        Assert.Contains("\"availableMethods\":[\"processcontainer\",\"bubblewrap\"]", json);
        Assert.Contains("\"isolationTier\":\"base-container\"", json);
        Assert.Contains("\"canBlockClipboardRead\":true", json);
    }

    [Fact]
    public void PlatformSupport_Unsupported_OmitsOptionalFields()
    {
        var ps = new PlatformSupport
        {
            IsSupported = false,
            Reason = "wxc-exec not found",
            AvailableMethods = []
        };

        var json = JsonSerializer.Serialize(ps, s_options);
        Assert.Equal("{\"isSupported\":false,\"reason\":\"wxc-exec not found\",\"availableMethods\":[]}", json);
    }

    #endregion

    #region Proxy config variants

    [Fact]
    public void ProxyConfig_BuiltinTestServer_SerializesCorrectly()
    {
        var proxy = ProxyConfig.BuiltinTestServer();
        var json = JsonSerializer.Serialize(proxy, s_options);
        Assert.Equal("{\"builtinTestServer\":true}", json);
    }

    [Fact]
    public void ProxyConfig_Localhost_SerializesCorrectly()
    {
        var proxy = ProxyConfig.Localhost(8080);
        var json = JsonSerializer.Serialize(proxy, s_options);
        Assert.Equal("{\"localhost\":8080}", json);
    }

    [Fact]
    public void ProxyConfig_Url_SerializesCorrectly()
    {
        var proxy = ProxyConfig.Url("http://proxy.corp:3128");
        var json = JsonSerializer.Serialize(proxy, s_options);
        Assert.Equal("{\"url\":\"http://proxy.corp:3128\"}", json);
    }

    #endregion

    #region LXC config

    [Fact]
    public void LxcConfig_RoundTrip()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            Lxc = new LxcConfig
            {
                ContainerName = "test-lxc",
                Distribution = "alpine",
                Release = "3.19",
                DestroyOnExit = true
            }
        };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Contains("\"lxc\":{", json);
        Assert.Contains("\"containerName\":\"test-lxc\"", json);
        Assert.Contains("\"distribution\":\"alpine\"", json);
        Assert.Contains("\"release\":\"3.19\"", json);
        Assert.Contains("\"destroyOnExit\":true", json);

        var rt = JsonSerializer.Deserialize<ContainerConfig>(json, s_options);
        Assert.Equal(json, JsonSerializer.Serialize(rt, s_options));
    }

    [Fact]
    public void ContainerConfig_WithWindowsSandboxExperimental_SerializesCorrectly()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            ContainerId = "ws-test",
            Containment = ContainmentValue.FromString("windows_sandbox"),
            Process = new ProcessConfig { CommandLine = "cmd /c echo hi" },
            Experimental = new ExperimentalConfig
            {
                WindowsSandbox = new WindowsSandboxConfig
                {
                    IdleTimeoutMs = 60000,
                    DaemonPipeName = "my-custom-pipe",
                },
            },
        };

        var json = JsonSerializer.Serialize(config, s_options);

        // Verify exact key casing for the windows_sandbox section
        Assert.Contains("\"windows_sandbox\":{\"idleTimeoutMs\":60000,\"daemonPipeName\":\"my-custom-pipe\"}", json);
        Assert.Contains("\"containment\":\"windows_sandbox\"", json);
        // No processContainer
        Assert.DoesNotContain("processContainer", json);

        // Round-trip
        var rt = JsonSerializer.Deserialize<ContainerConfig>(json, s_options);
        Assert.Equal(json, JsonSerializer.Serialize(rt, s_options));
    }

    [Fact]
    public void ContainerConfig_WindowsSandbox_AllFieldsSerialize()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            Containment = ContainmentValue.FromString("windows_sandbox"),
            Process = new ProcessConfig { CommandLine = "test.exe" },
            Experimental = new ExperimentalConfig
            {
                WindowsSandbox = new WindowsSandboxConfig
                {
                    IdleTimeout = 5000,
                    IdleTimeoutMs = 60000,
                    DaemonPipeName = "pipe1",
                },
            },
        };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Contains("\"idleTimeout\":5000", json);
        Assert.Contains("\"idleTimeoutMs\":60000", json);
        Assert.Contains("\"daemonPipeName\":\"pipe1\"", json);
    }

    [Fact]
    public void ContainerConfig_WindowsSandbox_OmitsNullFields()
    {
        var config = new ContainerConfig
        {
            Version = "1.0.0",
            Containment = ContainmentValue.FromString("windows_sandbox"),
            Process = new ProcessConfig { CommandLine = "test.exe" },
            Experimental = new ExperimentalConfig
            {
                WindowsSandbox = new WindowsSandboxConfig
                {
                    IdleTimeoutMs = 60000,
                },
            },
        };

        var json = JsonSerializer.Serialize(config, s_options);
        Assert.Contains("\"idleTimeoutMs\":60000", json);
        Assert.DoesNotContain("\"idleTimeout\"", json);
        Assert.DoesNotContain("\"daemonPipeName\"", json);
    }

    #endregion
}
