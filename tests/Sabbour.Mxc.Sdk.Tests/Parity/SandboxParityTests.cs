// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Text.Json;
using Sabbour.Mxc.Sdk.Sandbox;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests.Parity;

public sealed class SandboxParityTests
{
    [Fact]
    public void BuildSandboxPayload_Windows_SetsProcessCommandLineFromScriptParameter()
    {
        if (!IsWindows) return; // PARITY-GAP: C# uses RuntimeInformation directly; no production hook exists for os.platform mocking.

        var payload = SandboxFactory.BuildSandboxPayload("echo hello", DefaultPolicy());

        Assert.Equal("echo hello", payload.Process!.CommandLine);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_MapsNetworkPolicyToProcessContainerCapabilities()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { AllowOutbound = true, AllowLocalNetwork = true },
        };

        var payload = SandboxFactory.BuildSandboxPayload("echo hi", policy);

        Assert.Contains("internetClient", payload.ProcessContainer!.Capabilities!);
        Assert.Contains("privateNetworkClientServer", payload.ProcessContainer.Capabilities!);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_PassesFilesystemPolicyThrough()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Filesystem = new FilesystemPolicy
            {
                ReadwritePaths = [@"C:\temp"],
                ReadonlyPaths = [@"C:\data"],
            },
        };

        var payload = SandboxFactory.BuildSandboxPayload("echo hi", policy);

        Assert.Equal(@"C:\temp", payload.Filesystem!.ReadwritePaths![0]);
        Assert.Equal(new[] { @"C:\data" }, payload.Filesystem.ReadonlyPaths!);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_SetsContainerConfigVersionToPolicyVersion()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var payload = SandboxFactory.BuildSandboxPayload("echo hi", new SandboxPolicy { Version = "0.4.0-alpha" });

        Assert.Equal("0.4.0-alpha", payload.Version);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_AcceptsCompatibleVersion()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var exception = Record.Exception(() => SandboxFactory.BuildSandboxPayload("echo hi", new SandboxPolicy { Version = "0.5.0-alpha" }));

        Assert.Null(exception);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_AcceptsOlderVersion040Alpha()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var exception = Record.Exception(() => SandboxFactory.BuildSandboxPayload("echo hi", new SandboxPolicy { Version = "0.4.0-alpha" }));

        Assert.Null(exception);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_AcceptsVersion060Alpha()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var exception = Record.Exception(() => SandboxFactory.BuildSandboxPayload("echo hi", new SandboxPolicy { Version = "0.6.0-alpha" }));

        Assert.Null(exception);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_AcceptsVersion070Alpha()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var exception = Record.Exception(() => SandboxFactory.BuildSandboxPayload("echo hi", new SandboxPolicy { Version = "0.7.0-alpha" }));

        Assert.Null(exception);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_RejectsNewerMinorVersionWithinSameMajor()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var ex = Assert.Throws<ArgumentException>(() => SandboxFactory.BuildSandboxPayload("echo hi", new SandboxPolicy { Version = "0.99.0" }));
        Assert.Contains("newer than supported", ex.Message);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_RejectsDifferentMajorVersion()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var ex = Assert.Throws<ArgumentException>(() => SandboxFactory.BuildSandboxPayload("echo hi", new SandboxPolicy { Version = "1.0.0" }));
        Assert.Contains("newer than supported", ex.Message);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_RejectsVersionOlderThanMinimum()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var ex = Assert.Throws<ArgumentException>(() => SandboxFactory.BuildSandboxPayload("echo hi", new SandboxPolicy { Version = "0.3.0-alpha" }));
        Assert.Contains("older than supported", ex.Message);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_RejectsInvalidSemverString()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var ex = Assert.Throws<ArgumentException>(() => SandboxFactory.BuildSandboxPayload("echo hi", new SandboxPolicy { Version = "not-a-version" }));
        Assert.Contains("Invalid policy version", ex.Message);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_RejectsEmptyVersionString()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var ex = Assert.Throws<ArgumentException>(() => SandboxFactory.BuildSandboxPayload("echo hi", new SandboxPolicy { Version = "" }));
        Assert.Contains("version is required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_PassesBuiltinTestServerProxyThroughToNetworkConfig()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { Proxy = ProxyConfig.BuiltinTestServer() },
        };

        var payload = SandboxFactory.BuildSandboxPayload("echo hi", policy);

        Assert.IsType<ProxyConfig.BuiltinTestServerProxy>(payload.Network!.Proxy);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_PassesLocalhostProxyThroughToNetworkConfig()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { Proxy = ProxyConfig.Localhost(8080) },
        };

        var payload = SandboxFactory.BuildSandboxPayload("echo hi", policy);
        var proxy = Assert.IsType<ProxyConfig.LocalhostProxy>(payload.Network!.Proxy);

        Assert.Equal(8080, proxy.Port);
    }

    [Fact]
    public void BuildSandboxPayload_Windows_DoesNotSetNetworkProxyWhenProxyIsNotSpecified()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { AllowOutbound = true },
        };

        var payload = SandboxFactory.BuildSandboxPayload("echo hi", policy);

        Assert.Null(payload.Network?.Proxy);
    }

    [Fact]
    public void BuildSandboxPayload_Linux_DefaultsToProcessContainmentResolvedByBinaryToBubblewrap()
    {
        if (!IsLinux) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var payload = SandboxFactory.BuildSandboxPayload("echo hi", DefaultPolicy());

        Assert.Equal("process", payload.Containment!.Value.ToString());
        Assert.Null(payload.Lxc);
    }

    [Fact]
    public void BuildSandboxPayload_Linux_AcceptsProxyForDefaultProcessContainment()
    {
        if (!IsLinux) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { Proxy = ProxyConfig.BuiltinTestServer() },
        };

        var config = SandboxFactory.BuildSandboxPayload("echo hi", policy);

        Assert.Equal("process", config.Containment!.Value.ToString());
        Assert.IsType<ProxyConfig.BuiltinTestServerProxy>(config.Network!.Proxy);
    }

    [Fact]
    public void BuildSandboxPayload_Linux_AcceptsProxyForExplicitBubblewrapContainment()
    {
        if (!IsLinux) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { Proxy = ProxyConfig.BuiltinTestServer() },
        };

        var config = SandboxFactory.BuildSandboxPayload("echo hi", policy, containment: "bubblewrap");

        Assert.Equal("bubblewrap", config.Containment!.Value.ToString());
        Assert.IsType<ProxyConfig.BuiltinTestServerProxy>(config.Network!.Proxy);
    }

    [Fact]
    public void BuildSandboxPayload_Linux_RejectsProxyForNonBubblewrapContainments()
    {
        if (!IsLinux) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { Proxy = ProxyConfig.BuiltinTestServer() },
        };

        // RED FLAG: upstream expects Linux containment='lxc' + proxy to throw; C# currently accepts the proxy in ApplyNetworkConfig.
        var ex = Assert.Throws<InvalidOperationException>(() => SandboxFactory.BuildSandboxPayload("echo hi", policy, containment: "lxc"));
        Assert.Contains("not supported on Linux containment='lxc'", ex.Message);
    }

    [Fact]
    public void BuildSandboxPayload_ContainmentOverride_ReturnsMinimalConfigForMicrovmWithoutFilesystem()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var payload = SandboxFactory.BuildSandboxPayload("print(42)", DefaultPolicy(), containment: "microvm");

        Assert.Equal("microvm", payload.Containment!.Value.ToString());
        Assert.Null(payload.Filesystem);
        Assert.Null(payload.ProcessContainer);
    }

    [Fact]
    public void BuildSandboxPayload_ContainmentOverride_IncludesFilesystemWithClearPolicyOnExitForMicrovmWhenPolicyHasPaths()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Filesystem = new FilesystemPolicy { ReadwritePaths = ["/tmp"] },
        };

        var payload = SandboxFactory.BuildSandboxPayload("print(42)", policy, containment: "microvm");

        Assert.Equal("microvm", payload.Containment!.Value.ToString());
        Assert.Equal(new[] { "/tmp" }, payload.Filesystem!.ReadwritePaths!);
        Assert.True(payload.Filesystem.ClearPolicyOnExit);
    }

    [Fact]
    public void BuildSandboxPayload_ContainmentOverride_HonorsClearPolicyOnExitFalseForMicrovm()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Filesystem = new FilesystemPolicy { ReadwritePaths = ["/tmp"], ClearPolicyOnExit = false },
        };

        var payload = SandboxFactory.BuildSandboxPayload("print(42)", policy, containment: "microvm");

        Assert.False(payload.Filesystem!.ClearPolicyOnExit);
    }

    [Fact]
    public void BuildSandboxPayload_ContainmentOverride_BuildsProcessContainerConfigOnWindowsWithDefaultProcessContainment()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { AllowOutbound = true },
        };

        var payload = SandboxFactory.BuildSandboxPayload("echo hi", policy);

        Assert.NotNull(payload.ProcessContainer);
        Assert.Contains("internetClient", payload.ProcessContainer!.Capabilities!);
    }

    [Fact]
    public void BuildSandboxPayload_ContainmentOverride_RejectsNetworkPoliciesForMicrovm()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { AllowOutbound = true },
        };

        var ex = Assert.Throws<InvalidOperationException>(() => SandboxFactory.BuildSandboxPayload("print(42)", policy, containment: "microvm"));
        Assert.Contains("does not support network policy", ex.Message);
    }

    [Fact]
    public void BuildSandboxPayload_ContainmentOverride_RejectsMicrovmOnNonWindowsPlatforms()
    {
        if (IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var ex = Assert.Throws<PlatformNotSupportedException>(() => SandboxFactory.BuildSandboxPayload("print(42)", DefaultPolicy(), containment: "microvm"));
        Assert.Contains("only supported on Windows", ex.Message);
    }

    [Fact]
    public void BuildSandboxPayload_ContainmentOverride_PreservesLifecycleConfigForMicrovm()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Filesystem = new FilesystemPolicy { ClearPolicyOnExit = false },
        };

        var payload = SandboxFactory.BuildSandboxPayload("print(42)", policy, containment: "microvm");

        Assert.True(payload.Lifecycle!.DestroyOnExit);
        Assert.True(payload.Lifecycle.PreservePolicy);
    }

    [Fact]
    public void BuildSandboxPayload_ContainmentOverride_SetsProcessCommandLineAndContainerIdForMicrovm()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var payload = SandboxFactory.BuildSandboxPayload("print(42)", DefaultPolicy(), containerName: "my-container", containment: "microvm");

        Assert.Equal("print(42)", payload.Process!.CommandLine);
        Assert.Equal("my-container", payload.ContainerId);
    }

    [Fact]
    public void BuildSandboxPayload_Wslc_SetsContainmentToWslcWhenOptionIsPassed()
    {
        var payload = SandboxFactory.BuildSandboxPayload("echo hello", new SandboxPolicy { Version = "0.5.0-alpha" }, containment: "wslc");

        Assert.Equal("wslc", payload.Containment!.Value.ToString());
        Assert.Equal("echo hello", payload.Process!.CommandLine);
    }

    [Fact]
    public void BuildSandboxPayload_Wslc_PopulatesExperimentalWslcWithDefaultImage()
    {
        var payload = SandboxFactory.BuildSandboxPayload("echo hello", new SandboxPolicy { Version = "0.5.0-alpha" }, containment: "wslc");

        Assert.NotNull(payload.Experimental?.Wslc);
        Assert.Equal("alpine:latest", payload.Experimental!.Wslc!.Image);
    }

    [Fact]
    public void BuildSandboxPayload_Wslc_DoesNotSetProcessContainerOrLxcConfig()
    {
        var payload = SandboxFactory.BuildSandboxPayload("echo hello", new SandboxPolicy { Version = "0.5.0-alpha" }, containment: "wslc");

        Assert.Null(payload.ProcessContainer);
        Assert.Null(payload.Lxc);
    }

    [Fact]
    public void BuildSandboxPayload_Wslc_SetsDefaultDenyNetwork()
    {
        var payload = SandboxFactory.BuildSandboxPayload("echo hello", new SandboxPolicy { Version = "0.5.0-alpha" }, containment: "wslc");

        Assert.Equal(NetworkDefaultPolicy.Block, payload.Network!.DefaultPolicy);
    }

    [Fact]
    public void CreateConfigFromPolicy_ProducesLockedDownConfigWhenOnlyVersionIsSet()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(DefaultPolicy());

        Assert.Equal("0.4.0-alpha", config.Version);
        Assert.Empty(config.Filesystem!.ReadwritePaths!);
        Assert.Empty(config.Filesystem.ReadonlyPaths!);
        Assert.Empty(config.Filesystem.DeniedPaths!);
        Assert.True(config.Ui!.Disable);
        Assert.Equal(ClipboardPolicy.None, config.Ui.Clipboard);
        Assert.False(config.Ui.Injection);
        Assert.Equal(0, config.Process!.Timeout);
        Assert.Equal("", config.Process.CommandLine);
        Assert.True(config.Lifecycle!.DestroyOnExit);
        Assert.False(config.Lifecycle.PreservePolicy);
    }

    [Fact]
    public void CreateConfigFromPolicy_PassesFilesystemPathsThrough()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Filesystem = new FilesystemPolicy
            {
                ReadwritePaths = ["/workspace"],
                ReadonlyPaths = ["/tools"],
                DeniedPaths = ["/secrets"],
            },
        });

        Assert.Equal(new[] { "/workspace" }, config.Filesystem!.ReadwritePaths!);
        Assert.Equal(new[] { "/tools" }, config.Filesystem.ReadonlyPaths!);
        Assert.Equal(new[] { "/secrets" }, config.Filesystem.DeniedPaths!);
    }

    [Fact]
    public void CreateConfigFromPolicy_MapsUiFieldsCorrectly()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Ui = new UiPolicy { AllowWindows = true, Clipboard = ClipboardPolicy.Read, AllowInputInjection = true },
        });

        Assert.False(config.Ui!.Disable);
        Assert.Equal(ClipboardPolicy.Read, config.Ui.Clipboard);
        Assert.True(config.Ui.Injection);
    }

    [Fact]
    public void CreateConfigFromPolicy_MapsTimeoutMsToProcessTimeout()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy { Version = "0.4.0-alpha", TimeoutMs = 30000 });

        Assert.Equal(30000, config.Process!.Timeout);
    }

    [Fact]
    public void CreateConfigFromPolicy_Windows_SetsProcessContainerWithUiDefaultsForProcessContainment()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var config = SandboxFactory.CreateConfigFromPolicy(DefaultPolicy(), "process");

        Assert.NotNull(config.ProcessContainer);
        Assert.Empty(config.ProcessContainer!.Capabilities!);
        Assert.Equal(UiIsolationLevel.Container, config.ProcessContainer.Ui!.Isolation);
        Assert.False(config.ProcessContainer.Ui.DesktopSystemControl);
    }

    [Fact]
    public void CreateConfigFromPolicy_Windows_MapsNetworkPolicyToCapabilitiesAndHosts()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                AllowLocalNetwork = true,
                AllowedHosts = ["example.com"],
                BlockedHosts = ["evil.com"],
            },
        });

        Assert.Contains("internetClient", config.ProcessContainer!.Capabilities!);
        Assert.Contains("privateNetworkClientServer", config.ProcessContainer.Capabilities!);
        Assert.Equal(new[] { "example.com" }, config.Network!.AllowedHosts!);
        Assert.Equal(new[] { "evil.com" }, config.Network.BlockedHosts!);
    }

    [Fact]
    public void CreateConfigFromPolicy_Windows_PassesProxyThroughToConfig()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var builtin = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { Proxy = ProxyConfig.BuiltinTestServer() },
        });
        Assert.IsType<ProxyConfig.BuiltinTestServerProxy>(builtin.Network!.Proxy);

        var url = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { Proxy = ProxyConfig.Url("http://localhost:8080") },
        });
        var proxy = Assert.IsType<ProxyConfig.UrlProxy>(url.Network!.Proxy);
        Assert.Equal("http://localhost:8080", proxy.ProxyUrl);
    }

    [Fact]
    public void CreateConfigFromPolicy_Linux_DefaultsToProcessContainmentResolvedByBinaryToBubblewrap()
    {
        if (!IsLinux) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var config = SandboxFactory.CreateConfigFromPolicy(DefaultPolicy());

        Assert.Equal("process", config.Containment!.Value.ToString());
        Assert.Null(config.Lxc);
    }

    [Fact]
    public void CreateConfigFromPolicy_Linux_ForcesFirewallWhenHostFilteringRequestedForProcess()
    {
        if (!IsLinux) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Network = new NetworkPolicy { AllowOutbound = true, AllowedHosts = ["example.com"] },
        });

        Assert.Equal("process", config.Containment!.Value.ToString());
        Assert.Null(config.Lxc);
        Assert.Equal(NetworkEnforcementMode.Firewall, config.Network!.EnforcementMode);
    }

    [Fact]
    public void CreateConfigFromPolicy_Linux_AllowsAllowedHostsWithoutAllowOutboundForBubblewrapProcess()
    {
        if (!IsLinux) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Network = new NetworkPolicy { AllowedHosts = ["example.com"] },
        });

        Assert.Equal("process", config.Containment!.Value.ToString());
        Assert.Equal(new[] { "example.com" }, config.Network!.AllowedHosts!);
        Assert.Equal(NetworkDefaultPolicy.Block, config.Network.DefaultPolicy);
        Assert.Equal(NetworkEnforcementMode.Firewall, config.Network.EnforcementMode);
    }

    [Fact]
    public void CreateConfigFromPolicy_Linux_AcceptsProxyForDefaultProcessContainment()
    {
        if (!IsLinux) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { Proxy = ProxyConfig.BuiltinTestServer() },
        });

        Assert.Equal("process", config.Containment!.Value.ToString());
        Assert.IsType<ProxyConfig.BuiltinTestServerProxy>(config.Network!.Proxy);
    }

    [Fact]
    public void CreateConfigFromPolicy_Linux_AcceptsProxyForExplicitBubblewrapContainment()
    {
        if (!IsLinux) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { Proxy = ProxyConfig.BuiltinTestServer() },
        }, "bubblewrap");

        Assert.Equal("bubblewrap", config.Containment!.Value.ToString());
        Assert.IsType<ProxyConfig.BuiltinTestServerProxy>(config.Network!.Proxy);
    }

    [Fact]
    public void CreateConfigFromPolicy_Linux_DoesNotForceFirewallWhenProxyAndHostFilteringAreCombinedOnBubblewrap()
    {
        if (!IsLinux) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                AllowedHosts = ["example.com"],
                Proxy = ProxyConfig.BuiltinTestServer(),
            },
        }, "bubblewrap");

        Assert.Equal("bubblewrap", config.Containment!.Value.ToString());
        Assert.Null(config.Network!.EnforcementMode);
        Assert.IsType<ProxyConfig.BuiltinTestServerProxy>(config.Network.Proxy);
        Assert.Equal(new[] { "example.com" }, config.Network.AllowedHosts!);
    }

    [Fact]
    public void CreateConfigFromPolicy_Linux_RejectsProxyForExplicitLxcContainment()
    {
        if (!IsLinux) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        // RED FLAG: upstream expects Linux containment='lxc' + proxy to throw; C# currently accepts the proxy in ApplyNetworkConfig.
        var ex = Assert.Throws<InvalidOperationException>(() => SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { Proxy = ProxyConfig.BuiltinTestServer() },
        }, "lxc"));
        Assert.Contains("not supported on Linux containment='lxc'", ex.Message);
    }

    [Fact]
    public void CreateConfigFromPolicy_MacOS_AllowsAllowedHostsWithoutAllowOutbound()
    {
        if (!IsMacOS) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Network = new NetworkPolicy { AllowedHosts = ["api.github.com"] },
        });

        Assert.Equal("seatbelt", config.Containment!.Value.ToString());
        Assert.Equal(new[] { "api.github.com" }, config.Network!.AllowedHosts!);
        Assert.Equal(NetworkDefaultPolicy.Block, config.Network.DefaultPolicy);
    }

    [Fact]
    public void CreateConfigFromPolicy_MacOS_AllowsBlockedHostsWithoutAllowOutbound()
    {
        if (!IsMacOS) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Network = new NetworkPolicy { BlockedHosts = ["evil.com"] },
        });

        Assert.Equal("seatbelt", config.Containment!.Value.ToString());
        Assert.Equal(new[] { "evil.com" }, config.Network!.BlockedHosts!);
        Assert.Equal(NetworkDefaultPolicy.Block, config.Network.DefaultPolicy);
    }

    [Fact]
    public void CreateConfigFromPolicy_MacOS_PropagatesAllowLocalNetworkToNetworkAllowLocalNetwork()
    {
        if (!IsMacOS) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Network = new NetworkPolicy { AllowOutbound = true, AllowLocalNetwork = true },
        });

        Assert.Equal("seatbelt", config.Containment!.Value.ToString());
        Assert.True(config.Network!.AllowLocalNetwork);
    }

    [Fact]
    public void CreateConfigFromPolicy_MacOS_OmitsAllowLocalNetworkWhenNotSet()
    {
        if (!IsMacOS) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Network = new NetworkPolicy { AllowOutbound = true },
        });

        Assert.Null(config.Network!.AllowLocalNetwork);
    }

    [Fact]
    public void CreateConfigFromPolicy_MacOS_RejectsProxyConfiguration()
    {
        if (!IsMacOS) return; // PARITY-GAP: platform is not injectable for SandboxFactory.

        // RED FLAG: upstream expects macOS proxy configuration to throw; C# currently accepts it in ApplyNetworkConfig.
        var ex = Assert.Throws<InvalidOperationException>(() => SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { Proxy = ProxyConfig.BuiltinTestServer() },
        }));
        Assert.Contains("not supported on macOS", ex.Message);
    }

    [Fact]
    public void CreateConfigFromPolicy_NetworkValidation_RejectsAllowedHostsWithoutAllowOutbound()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory; this gate applies to Windows/processcontainer.

        // Upstream sandbox.ts:311-322 requires allowOutbound for non-host-filtering Windows processcontainer.
        var ex = Assert.Throws<InvalidOperationException>(() => SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { AllowedHosts = ["example.com"] },
        }));
        Assert.Contains("allowedHosts/blockedHosts require allowOutbound", ex.Message);
    }

    [Fact]
    public void CreateConfigFromPolicy_NetworkValidation_RejectsBlockedHostsWithoutAllowOutbound()
    {
        if (!IsWindows) return; // PARITY-GAP: platform is not injectable for SandboxFactory; this gate applies to Windows/processcontainer.

        // Upstream sandbox.ts:311-322 requires allowOutbound for non-host-filtering Windows processcontainer.
        var ex = Assert.Throws<InvalidOperationException>(() => SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { BlockedHosts = ["evil.com"] },
        }));
        Assert.Contains("allowedHosts/blockedHosts require allowOutbound", ex.Message);
    }

    [Fact]
    public void CreateConfigFromPolicy_Wslc_SetsContainmentToWslcAndPopulatesExperimentalWslc()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy { Version = "0.5.0-alpha" }, "wslc");

        Assert.Equal("wslc", config.Containment!.Value.ToString());
        Assert.NotNull(config.Experimental?.Wslc);
        Assert.Equal("alpine:latest", config.Experimental!.Wslc!.Image);
    }

    [Fact]
    public void CreateConfigFromPolicy_Wslc_SetsDefaultDenyNetworkWhenNoNetworkPolicyIsSpecified()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy { Version = "0.5.0-alpha" }, "wslc");

        Assert.Equal(NetworkDefaultPolicy.Block, config.Network!.DefaultPolicy);
    }

    [Fact]
    public void CreateConfigFromPolicy_Wslc_MapsAllowOutboundToNetworkAllowPolicy()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Network = new NetworkPolicy { AllowOutbound = true },
        }, "wslc");

        Assert.Equal(NetworkDefaultPolicy.Allow, config.Network!.DefaultPolicy);
    }

    [Fact]
    public void CreateConfigFromPolicy_Wslc_DoesNotSetEnforcementModeForWslc()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Network = new NetworkPolicy { AllowOutbound = true },
        }, "wslc");

        Assert.Null(config.Network!.EnforcementMode);
    }

    [Fact]
    public void CreateConfigFromPolicy_Wslc_AllowsAllowedHostsWithoutAllowOutboundBlockAllowlist()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Network = new NetworkPolicy { AllowedHosts = ["example.com"] },
        }, "wslc");

        Assert.Equal(NetworkDefaultPolicy.Block, config.Network!.DefaultPolicy);
        Assert.Equal(new[] { "example.com" }, config.Network.AllowedHosts!);
    }

    [Fact]
    public void CreateConfigFromPolicy_Wslc_DoesNotSetProcessContainerConfigForWslc()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy { Version = "0.5.0-alpha" }, "wslc");

        Assert.Null(config.ProcessContainer);
    }

    [Fact]
    public void CreateConfigFromPolicy_Wslc_DoesNotSetLxcConfigForWslc()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy { Version = "0.5.0-alpha" }, "wslc");

        Assert.Null(config.Lxc);
    }

    [Fact]
    public void CreateConfigFromPolicy_Wslc_MapsFilesystemPathsCorrectly()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Filesystem = new FilesystemPolicy
            {
                ReadwritePaths = [@"C:\workspace"],
                ReadonlyPaths = [@"C:\data"],
                DeniedPaths = [@"C:\secrets"],
            },
        }, "wslc");

        Assert.Equal(new[] { @"C:\workspace" }, config.Filesystem!.ReadwritePaths!);
        Assert.Equal(new[] { @"C:\data" }, config.Filesystem.ReadonlyPaths!);
        Assert.Equal(new[] { @"C:\secrets" }, config.Filesystem.DeniedPaths!);
    }

    [Fact]
    public void CreateConfigFromPolicy_Wslc_MapsTimeoutMsToProcessTimeout()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy { Version = "0.5.0-alpha", TimeoutMs = 30000 }, "wslc");

        Assert.Equal(30000, config.Process!.Timeout);
    }

    [Fact]
    public void CreateConfigFromPolicy_Wslc_SetsContainerId()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy { Version = "0.5.0-alpha" }, "wslc", "my-container");

        Assert.Equal("my-container", config.ContainerId);
    }

    [Fact]
    public async Task CreateConfigFromPolicy_Wslc_ThrowsFromSpawnSandboxWhenExperimentalBackendIsUsedViaConfig()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy { Version = "0.5.0-alpha" }, "wslc") with
        {
            Process = new ProcessConfig { CommandLine = "echo hello" },
        };
        var spawner = new SandboxSpawner(new FakePtyConnectionFactory(), new ThrowingProcessConnectionFactory());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => spawner.SpawnSandboxFromConfigAsync(config, SpawnOptions(experimental: false)));
        Assert.Contains("experimental mode", ex.Message);
    }

    [Fact]
    public async Task CreateConfigFromPolicy_Wslc_ThrowsFromSpawnSandboxFromConfigWhenExperimentalIsNotSet()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy { Version = "0.5.0-alpha" }, "wslc") with
        {
            Process = new ProcessConfig { CommandLine = "echo hello" },
        };
        var spawner = new SandboxSpawner(new FakePtyConnectionFactory(), new ThrowingProcessConnectionFactory());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => spawner.SpawnSandboxFromConfigAsync(config, SpawnOptions(experimental: false)));
        Assert.Contains("experimental mode", ex.Message);
    }

    [Fact]
    public void CreateConfigFromPolicy_Bubblewrap_SetsContainmentToBubblewrap()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy { Version = "0.5.0-alpha" }, "bubblewrap");

        Assert.Equal("bubblewrap", config.Containment!.Value.ToString());
    }

    [Fact]
    public void CreateConfigFromPolicy_Bubblewrap_MapsFilesystemAndNetworkPolicyFieldsThroughToContainerConfig()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Filesystem = new FilesystemPolicy
            {
                ReadwritePaths = ["/workspace"],
                ReadonlyPaths = ["/data"],
                DeniedPaths = ["/secrets"],
            },
            Network = new NetworkPolicy { AllowOutbound = true, AllowedHosts = ["example.com"] },
        }, "bubblewrap");

        Assert.Equal(new[] { "/workspace" }, config.Filesystem!.ReadwritePaths!);
        Assert.Equal(new[] { "/data" }, config.Filesystem.ReadonlyPaths!);
        Assert.Equal(new[] { "/secrets" }, config.Filesystem.DeniedPaths!);
        Assert.Equal(NetworkEnforcementMode.Firewall, config.Network!.EnforcementMode);
    }

    [Fact]
    public void CreateConfigFromPolicy_Lxc_SetsContainmentToLxcAndPopulatesBackendBlock()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy { Version = "0.5.0-alpha" }, "lxc");

        Assert.Equal("lxc", config.Containment!.Value.ToString());
        Assert.NotNull(config.Lxc);
        Assert.Equal("alpine", config.Lxc!.Distribution);
    }

    [Fact]
    public void CreateConfigFromPolicy_Lxc_ForcesFirewallWhenHostFilteringIsRequested()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Network = new NetworkPolicy { AllowOutbound = true, AllowedHosts = ["example.com"] },
        }, "lxc");

        Assert.Equal("lxc", config.Containment!.Value.ToString());
        Assert.Equal(NetworkEnforcementMode.Firewall, config.Network!.EnforcementMode);
    }

    [Fact]
    public void CreateConfigFromPolicy_Lxc_AllowsAllowedHostsWithoutAllowOutbound()
    {
        var config = SandboxFactory.CreateConfigFromPolicy(new SandboxPolicy
        {
            Version = "0.5.0-alpha",
            Network = new NetworkPolicy { AllowedHosts = ["example.com"] },
        }, "lxc");

        Assert.Equal("lxc", config.Containment!.Value.ToString());
        Assert.Equal(new[] { "example.com" }, config.Network!.AllowedHosts!);
        Assert.Equal(NetworkDefaultPolicy.Block, config.Network.DefaultPolicy);
        Assert.Equal(NetworkEnforcementMode.Firewall, config.Network.EnforcementMode);
    }

    [Fact]
    public void Schema060Vocabulary_AcceptsIsolationSessionAsSandboxingMethod()
    {
        var method = SandboxingMethod.IsolationSession;

        Assert.Equal("isolation_session", Wire(method));
    }

    [Fact]
    public void Schema060Vocabulary_AcceptsIsolationSessionAsContainerConfigContainmentValue()
    {
        var config = new ContainerConfig
        {
            Version = "0.6.0-alpha",
            Containment = ContainmentValue.FromString("isolation_session"),
        };

        Assert.Equal("isolation_session", config.Containment!.Value.ToString());
    }

    [Fact]
    public void ResolveExecutableAndArgs_AcceptsAbstractIntentProcessWithoutThrowing()
    {
        if (ParityTestHelpers.PlatformSkip is not null) return;

        var exception = Record.Exception(() => SpawnHelper.PrepareSpawn(MakeConfig("process"), SpawnOptions(experimental: false)));

        Assert.Null(exception);
    }

    [Fact]
    public void ResolveExecutableAndArgs_AcceptsAbstractIntentMicrovmWithExperimentalFlagOnWindowsOnly()
    {
        if (!IsWindows) return;
        if (ParityTestHelpers.PlatformSkip is not null) return;

        var exception = Record.Exception(() => SpawnHelper.PrepareSpawn(MakeConfig("microvm"), SpawnOptions(experimental: true)));

        Assert.Null(exception);
    }

    [Fact]
    public void ResolveExecutableAndArgs_DoesNotRequireExperimentalModeForNonExperimentalProcessIntent()
    {
        if (ParityTestHelpers.PlatformSkip is not null) return;

        var exception = Record.Exception(() => SpawnHelper.PrepareSpawn(MakeConfig("process"), SpawnOptions(experimental: false)));

        Assert.Null(exception);
    }

    [Fact]
    public void ResolveExecutableAndArgs_AcceptsAbstractIntentVmWithoutThrowing()
    {
        if (ParityTestHelpers.PlatformSkip is not null) return;

        var exception = Record.Exception(() => SpawnHelper.PrepareSpawn(MakeConfig("vm"), SpawnOptions(experimental: false)));

        Assert.Null(exception);
    }

    [Fact]
    public void ResolveExecutableAndArgs_StillRejectsGenuinelyUnknownContainmentValues()
    {
        // PARITY-GAP: decisions.md intentionally adapted TS's loose union cast to a validated C# ContainmentValue.
        var ex = Assert.Throws<ArgumentException>(() => ContainmentValue.FromString("bogus_backend"));

        Assert.Contains("Unknown containment value", ex.Message);
    }

    [Fact]
    public void ResolveExecutableAndArgs_StillRequiresExperimentalModeForExperimentalBackendsLikeWslc()
    {
        if (ParityTestHelpers.PlatformSkip is not null) return;

        var ex = Assert.Throws<InvalidOperationException>(() => SpawnHelper.PrepareSpawn(MakeConfig("wslc"), SpawnOptions(experimental: false)));
        Assert.Contains("experimental mode", ex.Message);
    }

    [Fact]
    public void ResolveExecutableAndArgs_DoesNotRequireExperimentalModeForExplicitLxcContainmentOnLinux()
    {
        if (!IsLinux) return;
        if (ParityTestHelpers.PlatformSkip is not null) return;

        var exception = Record.Exception(() => SpawnHelper.PrepareSpawn(MakeConfig("lxc"), SpawnOptions(experimental: false)));

        Assert.Null(exception);
    }

    [Fact]
    public void ResolveExecutableAndArgs_LegacyAliases_AcceptsAppcontainerAsAliasOfProcesscontainerOnWindows()
    {
        if (!IsWindows) return;
        if (ParityTestHelpers.PlatformSkip is not null) return;

        var exception = Record.Exception(() => SpawnHelper.PrepareSpawn(MakeConfig("appcontainer"), SpawnOptions(experimental: false)));

        Assert.Null(exception);
    }

    [Fact]
    public void ResolveExecutableAndArgs_LegacyAliases_RejectsAppcontainerOnNonWindowsHostsWithCanonicalError()
    {
        if (IsWindows) return;
        if (ParityTestHelpers.PlatformSkip is not null) return;

        // RED FLAG: upstream validates non-experimental concrete backends against availableMethods; C# PrepareSpawn currently has no platform-availability check.
        var ex = Assert.Throws<InvalidOperationException>(() => SpawnHelper.PrepareSpawn(MakeConfig("appcontainer"), SpawnOptions(experimental: false)));
        Assert.Contains("'appcontainer' is not available on this platform", ex.Message);
    }

    [Fact]
    public void ResolveExecutableAndArgs_LegacyAliases_RequiresExperimentalModeForMacosSandbox()
    {
        if (ParityTestHelpers.PlatformSkip is not null) return;

        var ex = Assert.Throws<InvalidOperationException>(() => SpawnHelper.PrepareSpawn(MakeConfig("macos_sandbox"), SpawnOptions(experimental: false)));
        Assert.Contains("experimental mode", ex.Message);
    }

    [Fact]
    public void ResolveExecutableAndArgs_LegacyAliases_AcceptsMacosSandboxOnMacOsWithExperimentalFlagSet()
    {
        if (!IsMacOS) return;
        if (ParityTestHelpers.PlatformSkip is not null) return;

        var exception = Record.Exception(() => SpawnHelper.PrepareSpawn(MakeConfig("macos_sandbox"), SpawnOptions(experimental: true)));

        Assert.Null(exception);
    }

    [Fact]
    public void ResolveExecutableAndArgs_LegacyAliases_ForwardsLegacyWireValueToBinaryUnchanged()
    {
        var result = SpawnHelper.PrepareSpawn(MakeConfig("appcontainer"), SpawnOptions(experimental: true, skipPlatformCheck: true));
        var envelope = ParityTestHelpers.TryDecodeConfigBase64Envelope(result.Args);

        Assert.NotNull(envelope);
        Assert.Equal("appcontainer", envelope!.Value.GetProperty("containment").GetString());
    }

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    private static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    private static SandboxPolicy DefaultPolicy() => new() { Version = "0.4.0-alpha" };

    private static ContainerConfig MakeConfig(string containment) => new()
    {
        Version = "0.5.0-alpha",
        Containment = ContainmentValue.FromString(containment),
        Process = new ProcessConfig { CommandLine = "echo hi" },
    };

    private static SandboxSpawnOptions SpawnOptions(bool experimental, bool skipPlatformCheck = false)
    {
        return ParityTestHelpers.TestOptions(o => o with
        {
            Experimental = experimental,
            SkipPlatformCheck = skipPlatformCheck,
        });
    }

    private static string Wire<T>(T value) => JsonSerializer.Serialize(value).Trim('"');

    private sealed class ThrowingProcessConnectionFactory : IProcessConnectionFactory
    {
        public ProcessConnection Spawn(string executablePath, IReadOnlyList<string> args, string? workingDirectory)
        {
            throw new NotSupportedException("Pipe mode should not be reached by this parity test.");
        }
    }
}
