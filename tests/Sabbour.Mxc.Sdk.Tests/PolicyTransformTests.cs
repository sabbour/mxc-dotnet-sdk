// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Xunit;
using Sabbour.Mxc.Sdk.Policy;

namespace Sabbour.Mxc.Sdk.Tests;

/// <summary>
/// Tests for PolicyTransform.CreateConfigFromPolicy — the central SandboxPolicy→ContainerConfig
/// transformation. Verifies defaults, honor matrix, version validation, legacy alias handling,
/// backend-specific branching, and golden byte-for-byte JSON fidelity with the TS SDK.
/// </summary>
public class PolicyTransformTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static string Serialize(ContainerConfig config) =>
        JsonSerializer.Serialize(config, s_jsonOptions);

    // -----------------------------------------------------------------------
    // Version validation
    // -----------------------------------------------------------------------

    [Fact]
    public void ThrowsOnEmptyVersion()
    {
        var policy = new SandboxPolicy { Version = "" };
        Assert.Throws<ArgumentException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy));
    }

    [Fact]
    public void ThrowsOnInvalidSemver()
    {
        var policy = new SandboxPolicy { Version = "not-a-version" };
        var ex = Assert.Throws<ArgumentException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy));
        Assert.Contains("must be valid semver", ex.Message);
    }

    [Fact]
    public void ThrowsOnTooOldVersion()
    {
        var policy = new SandboxPolicy { Version = "0.3.0" };
        var ex = Assert.Throws<ArgumentException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy));
        Assert.Contains("older than supported", ex.Message);
    }

    [Fact]
    public void ThrowsOnTooNewVersion()
    {
        var policy = new SandboxPolicy { Version = "1.0.0" };
        var ex = Assert.Throws<ArgumentException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy));
        Assert.Contains("newer than supported", ex.Message);
    }

    [Theory]
    [InlineData("0.4.0-alpha")]
    [InlineData("0.5.0")]
    [InlineData("0.6.0")]
    [InlineData("0.7.0-alpha")]
    public void AcceptsValidVersions(string version)
    {
        var policy = new SandboxPolicy { Version = version };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, platform: PolicyTransform.Platforms.Windows);
        Assert.Equal(version, config.Version);
    }

    // -----------------------------------------------------------------------
    // Default values (process backend, Windows)
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultsApplied_ProcessWindows()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "test-id", PolicyTransform.Platforms.Windows);

        // Version and containerId
        Assert.Equal("0.5.0", config.Version);
        Assert.Equal("test-id", config.ContainerId);

        // Lifecycle defaults
        Assert.True(config.Lifecycle!.DestroyOnExit);
        Assert.False(config.Lifecycle.PreservePolicy);

        // Process defaults
        Assert.Equal("", config.Process!.CommandLine);
        Assert.Equal(0, config.Process.Timeout);

        // UI defaults (most restrictive)
        Assert.True(config.Ui!.Disable);
        Assert.Equal(ClipboardPolicy.None, config.Ui.Clipboard);
        Assert.False(config.Ui.Injection);

        // Network defaults (block)
        Assert.Equal(NetworkDefaultPolicy.Block, config.Network!.DefaultPolicy);
        Assert.Equal(NetworkEnforcementMode.Capabilities, config.Network.EnforcementMode);

        // ProcessContainer defaults (Windows honor matrix)
        Assert.NotNull(config.ProcessContainer);
        Assert.Equal("test-id", config.ProcessContainer!.Name);
        Assert.False(config.ProcessContainer.LeastPrivilege);
        Assert.Empty(config.ProcessContainer.Capabilities!);
        Assert.Equal(UiIsolationLevel.Container, config.ProcessContainer.Ui!.Isolation);
        Assert.False(config.ProcessContainer.Ui.DesktopSystemControl);
        Assert.Equal("none", config.ProcessContainer.Ui.SystemSettings);
        Assert.False(config.ProcessContainer.Ui.Ime);
    }

    // -----------------------------------------------------------------------
    // Honor matrix: Windows ProcessContainer
    // -----------------------------------------------------------------------

    [Fact]
    public void HonorMatrix_Windows_NetworkCapabilities()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                AllowLocalNetwork = true,
            },
        };

        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "c1", PolicyTransform.Platforms.Windows);

        // AppContainer capabilities added
        Assert.Contains("internetClient", config.ProcessContainer!.Capabilities!);
        Assert.Contains("privateNetworkClientServer", config.ProcessContainer.Capabilities!);
        Assert.Equal(NetworkDefaultPolicy.Allow, config.Network!.DefaultPolicy);
        Assert.True(config.Network.AllowLocalNetwork);
    }

    [Fact]
    public void HonorMatrix_Windows_NetworkFirewallMode_WhenHostRules()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                AllowedHosts = ["example.com"],
            },
        };

        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "c1", PolicyTransform.Platforms.Windows);
        Assert.Equal(NetworkEnforcementMode.Both, config.Network!.EnforcementMode);
    }

    [Fact]
    public void HonorMatrix_Windows_UI_AllowWindows()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Ui = new UiPolicy
            {
                AllowWindows = true,
                Clipboard = ClipboardPolicy.Read,
                AllowInputInjection = true,
            },
        };

        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "c1", PolicyTransform.Platforms.Windows);
        Assert.False(config.Ui!.Disable);
        Assert.Equal(ClipboardPolicy.Read, config.Ui.Clipboard);
        Assert.True(config.Ui.Injection);
    }

    // -----------------------------------------------------------------------
    // Honor matrix: Linux Process (resolves to Bubblewrap)
    // -----------------------------------------------------------------------

    [Fact]
    public void HonorMatrix_Linux_Process_ResolvesToBubblewrap()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                AllowedHosts = ["api.example.com"],
            },
        };

        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "lx1", PolicyTransform.Platforms.Linux);

        // Containment set to abstract "process"
        Assert.Equal("process", config.Containment!.Value.Value);
        // Network enforcement promoted to firewall (has host rules, no proxy)
        Assert.Equal(NetworkEnforcementMode.Firewall, config.Network!.EnforcementMode);
        // No processContainer (Linux)
        Assert.Null(config.ProcessContainer);
    }

    [Fact]
    public void HonorMatrix_Linux_Bubblewrap_Explicit()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy { AllowOutbound = true, BlockedHosts = ["evil.com"] },
        };

        var config = PolicyTransform.CreateConfigFromPolicy(policy, "bubblewrap", "bw1", PolicyTransform.Platforms.Linux);
        Assert.Equal("bubblewrap", config.Containment!.Value.Value);
        Assert.Equal(NetworkEnforcementMode.Firewall, config.Network!.EnforcementMode);
    }

    [Fact]
    public void HonorMatrix_Linux_Lxc()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "lxc", "lxc1", PolicyTransform.Platforms.Linux);

        Assert.Equal("lxc", config.Containment!.Value.Value);
        Assert.NotNull(config.Lxc);
        Assert.Equal("lxc1", config.Lxc!.ContainerName);
        Assert.Equal("alpine", config.Lxc.Distribution);
        Assert.Equal("3.23", config.Lxc.Release);
        Assert.True(config.Lxc.DestroyOnExit);
    }

    // -----------------------------------------------------------------------
    // Honor matrix: macOS Process (resolves to Seatbelt)
    // -----------------------------------------------------------------------

    [Fact]
    public void HonorMatrix_MacOS_Process_ResolvesToSeatbelt()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "mac1", PolicyTransform.Platforms.MacOS);

        Assert.Equal("seatbelt", config.Containment!.Value.Value);
        Assert.NotNull(config.Experimental?.Seatbelt);
    }

    // -----------------------------------------------------------------------
    // Honor matrix: WSLC
    // -----------------------------------------------------------------------

    [Fact]
    public void HonorMatrix_Wslc()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "wslc", "wslc1", PolicyTransform.Platforms.Windows);

        Assert.Equal("wslc", config.Containment!.Value.Value);
        Assert.NotNull(config.Experimental?.Wslc);
        Assert.Equal("alpine:latest", config.Experimental!.Wslc!.Image);
    }

    // -----------------------------------------------------------------------
    // Honor matrix: MicroVM
    // -----------------------------------------------------------------------

    [Fact]
    public void HonorMatrix_MicroVm_Basic()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Filesystem = new FilesystemPolicy
            {
                ReadwritePaths = ["/data"],
                ReadonlyPaths = ["/tools"],
            },
        };

        var config = PolicyTransform.CreateConfigFromPolicy(policy, "microvm", "mv1", PolicyTransform.Platforms.Windows);

        Assert.Equal("microvm", config.Containment!.Value.Value);
        Assert.NotNull(config.Filesystem);
        Assert.Contains("/data", config.Filesystem!.ReadwritePaths!);
        Assert.Contains("/tools", config.Filesystem.ReadonlyPaths!);
        Assert.True(config.Filesystem.ClearPolicyOnExit);
        // No UI on microvm
        Assert.Null(config.Ui);
        // No network on microvm
        Assert.Null(config.Network);
    }

    [Fact]
    public void HonorMatrix_MicroVm_ThrowsOnNetwork()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy { AllowOutbound = true },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy, "microvm", "mv1", PolicyTransform.Platforms.Windows));
        Assert.Contains("does not support network policy", ex.Message);
    }

    [Fact]
    public void HonorMatrix_MicroVm_ThrowsOnNonWindows()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy, "microvm", "mv1", PolicyTransform.Platforms.Linux));
        Assert.Contains("only supported on Windows", ex.Message);
    }

    // -----------------------------------------------------------------------
    // Filesystem pass-through and clearPolicyOnExit
    // -----------------------------------------------------------------------

    [Fact]
    public void Filesystem_PassedThrough()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Filesystem = new FilesystemPolicy
            {
                ReadwritePaths = ["/rw1", "/rw2"],
                ReadonlyPaths = ["/ro1"],
                DeniedPaths = ["/denied"],
                ClearPolicyOnExit = false,
            },
        };

        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "fs1", PolicyTransform.Platforms.Windows);

        Assert.Equal(["/rw1", "/rw2"], config.Filesystem!.ReadwritePaths);
        Assert.Equal(["/ro1"], config.Filesystem.ReadonlyPaths);
        Assert.Equal(["/denied"], config.Filesystem.DeniedPaths);
        // clearPolicyOnExit=false → lifecycle.preservePolicy=true
        Assert.True(config.Lifecycle!.PreservePolicy);
    }

    // -----------------------------------------------------------------------
    // Proxy validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Proxy_ThrowsOnMacOS()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                Proxy = ProxyConfig.Localhost(8080),
            },
        };

        Assert.Throws<InvalidOperationException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy, "process", "p1", PolicyTransform.Platforms.MacOS));
    }

    [Fact]
    public void Proxy_ThrowsOnLinuxLxc()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                Proxy = ProxyConfig.Url("http://proxy.local:3128"),
            },
        };

        Assert.Throws<InvalidOperationException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy, "lxc", "p1", PolicyTransform.Platforms.Linux));
    }

    [Fact]
    public void Proxy_AllowedOnLinuxBubblewrap()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                Proxy = ProxyConfig.BuiltinTestServer(),
            },
        };

        var config = PolicyTransform.CreateConfigFromPolicy(policy, "bubblewrap", "p1", PolicyTransform.Platforms.Linux);
        Assert.NotNull(config.Network!.Proxy);
    }

    // -----------------------------------------------------------------------
    // Host filtering without allowOutbound
    // -----------------------------------------------------------------------

    [Fact]
    public void HostFiltering_RequiresAllowOutbound_OnWindows()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = false,
                AllowedHosts = ["host1.com"],
            },
        };

        Assert.Throws<InvalidOperationException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy, "process", "c1", PolicyTransform.Platforms.Windows));
    }

    [Fact]
    public void HostFiltering_AllowedWithoutOutbound_OnLinux()
    {
        // Linux backends support host filtering without allowOutbound
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = false,
                AllowedHosts = ["host1.com"],
            },
        };

        // Should NOT throw on Linux process
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "c1", PolicyTransform.Platforms.Linux);
        Assert.Contains("host1.com", config.Network!.AllowedHosts!);
    }

    [Fact]
    public void HostFiltering_AllowedWithoutOutbound_OnExplicitBubblewrap()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = false,
                BlockedHosts = ["blocked.example"],
            },
        };

        var config = PolicyTransform.CreateConfigFromPolicy(policy, "bubblewrap", "bw1", PolicyTransform.Platforms.Linux);

        Assert.Contains("blocked.example", config.Network!.BlockedHosts!);
    }

    [Fact]
    public void HostFiltering_RequiresAllowOutbound_OnWindowsNonProcessBackend()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = false,
                AllowedHosts = ["host1.com"],
            },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy, "appcontainer", "c1", PolicyTransform.Platforms.Windows));
        Assert.Contains("allowedHosts/blockedHosts require allowOutbound", ex.Message);
    }

    [Fact]
    public void HostFiltering_RequiresAllowOutbound_OnMicroVm()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = false,
                AllowedHosts = ["host1.com"],
            },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy, "microvm", "mv1", PolicyTransform.Platforms.Windows));
        Assert.Contains("allowedHosts/blockedHosts require allowOutbound", ex.Message);
    }

    // -----------------------------------------------------------------------
    // Timeout pass-through
    // -----------------------------------------------------------------------

    [Fact]
    public void Timeout_PassedThrough()
    {
        var policy = new SandboxPolicy { Version = "0.5.0", TimeoutMs = 30000 };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "t1", PolicyTransform.Platforms.Windows);
        Assert.Equal(30000, config.Process!.Timeout);
    }

    // -----------------------------------------------------------------------
    // Experimental gating path (via helper.ts resolveExecutableAndArgs logic)
    // -----------------------------------------------------------------------

    [Fact]
    public void ExperimentalBackends_AreKnown()
    {
        // Verify the ExperimentalBackends set includes the expected values
        Assert.True(ExperimentalBackends.RequiresExperimental("microvm"));
        Assert.True(ExperimentalBackends.RequiresExperimental("windows_sandbox"));
        Assert.True(ExperimentalBackends.RequiresExperimental("hyperlight"));
        Assert.True(ExperimentalBackends.RequiresExperimental("wslc"));
        Assert.True(ExperimentalBackends.RequiresExperimental("seatbelt"));
        Assert.True(ExperimentalBackends.RequiresExperimental("isolation_session"));

        // Non-experimental
        Assert.False(ExperimentalBackends.RequiresExperimental("processcontainer"));
        Assert.False(ExperimentalBackends.RequiresExperimental("bubblewrap"));
        Assert.False(ExperimentalBackends.RequiresExperimental("lxc"));
    }

    [Fact]
    public void LegacyAlias_MapsToExperimental()
    {
        // "appcontainer" → processcontainer (NOT experimental)
        Assert.False(ExperimentalBackends.RequiresExperimental("appcontainer"));
        // "macos_sandbox" → seatbelt (experimental)
        Assert.True(ExperimentalBackends.RequiresExperimental("macos_sandbox"));
    }

    // -----------------------------------------------------------------------
    // Legacy alias normalization (ContainmentValue)
    // -----------------------------------------------------------------------

    [Fact]
    public void LegacyAlias_Appcontainer_Accepted()
    {
        // ContainmentValue accepts legacy aliases
        var cv = ContainmentValue.FromString("appcontainer");
        Assert.Equal("appcontainer", cv.Value);
    }

    [Fact]
    public void LegacyAlias_MacosSandbox_Accepted()
    {
        var cv = ContainmentValue.FromString("macos_sandbox");
        Assert.Equal("macos_sandbox", cv.Value);
    }

    [Fact]
    public void LegacyAliasMap_ResolvesProperly()
    {
        Assert.Equal(ContainmentBackend.ProcessContainer, LegacyContainmentAliases.Map["appcontainer"]);
        Assert.Equal(ContainmentBackend.Seatbelt, LegacyContainmentAliases.Map["macos_sandbox"]);
    }

    // -----------------------------------------------------------------------
    // Unsupported containment — including explicit seatbelt (P4 handles rejection)
    // -----------------------------------------------------------------------

    [Fact]
    public void ThrowsOnUnsupportedContainment()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        Assert.Throws<InvalidOperationException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy, "unknown_backend", "c1", PolicyTransform.Platforms.Windows));
    }

    [Fact]
    public void ThrowsOnExplicitSeatbelt_NotHandledInP2()
    {
        // Explicit containment=="seatbelt" is NOT handled by createConfigFromPolicy in TS.
        // TS only handles wslc/bubblewrap/lxc/process/microvm. The rejection of explicit
        // seatbelt happens later in sandbox.ts (P4's job).
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy, "seatbelt", "s1", PolicyTransform.Platforms.MacOS));
        Assert.Contains("not yet supported", ex.Message);
    }

    // -----------------------------------------------------------------------
    // Auto-generated container name
    // -----------------------------------------------------------------------

    [Fact]
    public void AutoGeneratesContainerName_WhenNotProvided()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", platform: PolicyTransform.Platforms.Windows);

        Assert.NotNull(config.ContainerId);
        Assert.Equal(8, config.ContainerId!.Length); // 4 bytes → 8 hex chars
        Assert.Matches("^[0-9a-f]{8}$", config.ContainerId);
    }

    // =======================================================================
    // GOLDEN JSON TESTS — byte-for-byte fidelity with TS SDK output
    // =======================================================================
    // These tests verify that the serialized ContainerConfig JSON matches
    // the exact key order and values produced by the TS createConfigFromPolicy.
    // Key order follows TS insertion order per backend.

    [Fact]
    public void GoldenJson_ProcessWindows_DefaultPolicy()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "abc12345", PolicyTransform.Platforms.Windows);
        var json = Serialize(config);

        // TS insertion order: version, containerId, lifecycle, process, filesystem, ui, network, containment, processContainer
        const string expected =
            """{"version":"0.5.0","containerId":"abc12345","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"filesystem":{"readwritePaths":[],"readonlyPaths":[],"deniedPaths":[]},"ui":{"disable":true,"clipboard":"none","injection":false},"network":{"defaultPolicy":"block","enforcementMode":"capabilities"},"containment":"process","processContainer":{"name":"abc12345","leastPrivilege":false,"capabilities":[],"ui":{"isolation":"container","desktopSystemControl":false,"systemSettings":"none","ime":false}}}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_ProcessWindows_WithNetwork()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                AllowLocalNetwork = true,
                AllowedHosts = ["api.example.com"],
            },
        };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "net1", PolicyTransform.Platforms.Windows);
        var json = Serialize(config);

        const string expected =
            """{"version":"0.5.0","containerId":"net1","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"filesystem":{"readwritePaths":[],"readonlyPaths":[],"deniedPaths":[]},"ui":{"disable":true,"clipboard":"none","injection":false},"network":{"defaultPolicy":"allow","allowLocalNetwork":true,"allowedHosts":["api.example.com"],"enforcementMode":"both"},"containment":"process","processContainer":{"name":"net1","leastPrivilege":false,"capabilities":["internetClient","privateNetworkClientServer"],"ui":{"isolation":"container","desktopSystemControl":false,"systemSettings":"none","ime":false}}}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_ProcessLinux_DefaultPolicy()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "lx1", PolicyTransform.Platforms.Linux);
        var json = Serialize(config);

        // TS order: version, containerId, lifecycle, process, filesystem, ui, network, containment
        const string expected =
            """{"version":"0.5.0","containerId":"lx1","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"filesystem":{"readwritePaths":[],"readonlyPaths":[],"deniedPaths":[]},"ui":{"disable":true,"clipboard":"none","injection":false},"network":{"defaultPolicy":"block"},"containment":"process"}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_ProcessLinux_WithHostFiltering()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                AllowedHosts = ["api.example.com"],
            },
        };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "lx2", PolicyTransform.Platforms.Linux);
        var json = Serialize(config);

        // enforcementMode added after network object creation by applyLinuxNetworkPolicy
        const string expected =
            """{"version":"0.5.0","containerId":"lx2","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"filesystem":{"readwritePaths":[],"readonlyPaths":[],"deniedPaths":[]},"ui":{"disable":true,"clipboard":"none","injection":false},"network":{"defaultPolicy":"allow","allowedHosts":["api.example.com"],"enforcementMode":"firewall"},"containment":"process"}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_ProcessDarwin()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "mac1", PolicyTransform.Platforms.MacOS);
        var json = Serialize(config);

        // TS order: version, containerId, lifecycle, process, filesystem, ui, network, containment, experimental
        const string expected =
            """{"version":"0.5.0","containerId":"mac1","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"filesystem":{"readwritePaths":[],"readonlyPaths":[],"deniedPaths":[]},"ui":{"disable":true,"clipboard":"none","injection":false},"network":{"defaultPolicy":"block"},"containment":"seatbelt","experimental":{"seatbelt":{}}}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_Bubblewrap_DefaultPolicy()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "bubblewrap", "bw1", PolicyTransform.Platforms.Linux);
        var json = Serialize(config);

        // TS order: version, containerId, lifecycle, process, filesystem, ui, network, containment
        const string expected =
            """{"version":"0.5.0","containerId":"bw1","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"filesystem":{"readwritePaths":[],"readonlyPaths":[],"deniedPaths":[]},"ui":{"disable":true,"clipboard":"none","injection":false},"network":{"defaultPolicy":"block"},"containment":"bubblewrap"}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_Bubblewrap_WithHostFiltering()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                BlockedHosts = ["evil.com"],
            },
        };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "bubblewrap", "bw2", PolicyTransform.Platforms.Linux);
        var json = Serialize(config);

        const string expected =
            """{"version":"0.5.0","containerId":"bw2","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"filesystem":{"readwritePaths":[],"readonlyPaths":[],"deniedPaths":[]},"ui":{"disable":true,"clipboard":"none","injection":false},"network":{"defaultPolicy":"allow","blockedHosts":["evil.com"],"enforcementMode":"firewall"},"containment":"bubblewrap"}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_Lxc()
    {
        var policy = new SandboxPolicy { Version = "0.6.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "lxc", "lxc-test", PolicyTransform.Platforms.Linux);
        var json = Serialize(config);

        // TS order: version, containerId, lifecycle, process, filesystem, ui, network, containment, lxc
        const string expected =
            """{"version":"0.6.0","containerId":"lxc-test","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"filesystem":{"readwritePaths":[],"readonlyPaths":[],"deniedPaths":[]},"ui":{"disable":true,"clipboard":"none","injection":false},"network":{"defaultPolicy":"block"},"containment":"lxc","lxc":{"containerName":"lxc-test","distribution":"alpine","release":"3.23","destroyOnExit":true}}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_Wslc()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "wslc", "wslc1", PolicyTransform.Platforms.Windows);
        var json = Serialize(config);

        // TS order: version, containerId, lifecycle, process, filesystem, ui, network, containment, experimental
        const string expected =
            """{"version":"0.5.0","containerId":"wslc1","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"filesystem":{"readwritePaths":[],"readonlyPaths":[],"deniedPaths":[]},"ui":{"disable":true,"clipboard":"none","injection":false},"network":{"defaultPolicy":"block"},"containment":"wslc","experimental":{"wslc":{"image":"alpine:latest"}}}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_MicroVm()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Filesystem = new FilesystemPolicy
            {
                ReadwritePaths = ["/data"],
                ReadonlyPaths = ["/tools"],
            },
        };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "microvm", "mv1", PolicyTransform.Platforms.Windows);
        var json = Serialize(config);

        // Microvm TS order: version, containerId, lifecycle, process, containment, filesystem
        const string expected =
            """{"version":"0.5.0","containerId":"mv1","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"containment":"microvm","filesystem":{"readwritePaths":["/data"],"readonlyPaths":["/tools"],"clearPolicyOnExit":true}}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_MicroVm_NoFilesystem()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "microvm", "mv2", PolicyTransform.Platforms.Windows);
        var json = Serialize(config);

        // No filesystem block when no paths
        const string expected =
            """{"version":"0.5.0","containerId":"mv2","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"containment":"microvm"}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_ProcessWindows_UiAllowed()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Ui = new UiPolicy
            {
                AllowWindows = true,
                Clipboard = ClipboardPolicy.All,
                AllowInputInjection = true,
            },
        };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "ui1", PolicyTransform.Platforms.Windows);
        var json = Serialize(config);

        const string expected =
            """{"version":"0.5.0","containerId":"ui1","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"filesystem":{"readwritePaths":[],"readonlyPaths":[],"deniedPaths":[]},"ui":{"disable":false,"clipboard":"all","injection":true},"network":{"defaultPolicy":"block","enforcementMode":"capabilities"},"containment":"process","processContainer":{"name":"ui1","leastPrivilege":false,"capabilities":[],"ui":{"isolation":"container","desktopSystemControl":false,"systemSettings":"none","ime":false}}}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_Filesystem_WithDenied()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Filesystem = new FilesystemPolicy
            {
                ReadwritePaths = ["/rw"],
                ReadonlyPaths = ["/ro"],
                DeniedPaths = ["/denied"],
                ClearPolicyOnExit = false,
            },
        };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "fs1", PolicyTransform.Platforms.Linux);
        var json = Serialize(config);

        // preservePolicy=true because clearPolicyOnExit=false
        const string expected =
            """{"version":"0.5.0","containerId":"fs1","lifecycle":{"destroyOnExit":true,"preservePolicy":true},"process":{"commandLine":"","timeout":0},"filesystem":{"readwritePaths":["/rw"],"readonlyPaths":["/ro"],"deniedPaths":["/denied"]},"ui":{"disable":true,"clipboard":"none","injection":false},"network":{"defaultPolicy":"block"},"containment":"process"}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_Bubblewrap_WithProxy()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy
            {
                AllowOutbound = true,
                Proxy = ProxyConfig.Localhost(8080),
            },
        };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "bubblewrap", "bwp", PolicyTransform.Platforms.Linux);
        var json = Serialize(config);

        // Proxy present means no firewall promotion
        const string expected =
            """{"version":"0.5.0","containerId":"bwp","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"filesystem":{"readwritePaths":[],"readonlyPaths":[],"deniedPaths":[]},"ui":{"disable":true,"clipboard":"none","injection":false},"network":{"defaultPolicy":"allow","proxy":{"localhost":8080}},"containment":"bubblewrap"}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_ProcessWindows_Timeout()
    {
        var policy = new SandboxPolicy { Version = "0.5.0", TimeoutMs = 60000 };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "tm1", PolicyTransform.Platforms.Windows);
        var json = Serialize(config);

        const string expected =
            """{"version":"0.5.0","containerId":"tm1","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":60000},"filesystem":{"readwritePaths":[],"readonlyPaths":[],"deniedPaths":[]},"ui":{"disable":true,"clipboard":"none","injection":false},"network":{"defaultPolicy":"block","enforcementMode":"capabilities"},"containment":"process","processContainer":{"name":"tm1","leastPrivilege":false,"capabilities":[],"ui":{"isolation":"container","desktopSystemControl":false,"systemSettings":"none","ime":false}}}""";

        Assert.Equal(expected, json);
    }

    // -----------------------------------------------------------------------
    // JSON wire-format fidelity (legacy substring checks kept for coverage)
    // -----------------------------------------------------------------------

    [Fact]
    public void JsonOutput_MatchesExpectedWireFormat()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy { AllowOutbound = true },
            Ui = new UiPolicy { AllowWindows = true, Clipboard = ClipboardPolicy.All },
        };

        var config = PolicyTransform.CreateConfigFromPolicy(policy, "process", "abc12345", PolicyTransform.Platforms.Windows);
        var json = Serialize(config);

        // Verify key wire-format field names are present with correct casing
        Assert.Contains("\"version\":\"0.5.0\"", json);
        Assert.Contains("\"containerId\":\"abc12345\"", json);
        Assert.Contains("\"containment\":\"process\"", json);
        Assert.Contains("\"destroyOnExit\":true", json);
        Assert.Contains("\"preservePolicy\":false", json);
        Assert.Contains("\"commandLine\":\"\"", json);
        Assert.Contains("\"defaultPolicy\":\"allow\"", json);
        Assert.Contains("\"enforcementMode\":\"capabilities\"", json);
        Assert.Contains("\"disable\":false", json);
        Assert.Contains("\"clipboard\":\"all\"", json);
        Assert.Contains("\"injection\":false", json);
        Assert.Contains("\"internetClient\"", json);
        Assert.Contains("\"leastPrivilege\":false", json);
    }

    [Fact]
    public void JsonOutput_LxcBackend_WireFormat()
    {
        var policy = new SandboxPolicy { Version = "0.6.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "lxc", "lxc-test", PolicyTransform.Platforms.Linux);
        var json = Serialize(config);

        Assert.Contains("\"containment\":\"lxc\"", json);
        Assert.Contains("\"containerName\":\"lxc-test\"", json);
        Assert.Contains("\"distribution\":\"alpine\"", json);
        Assert.Contains("\"release\":\"3.23\"", json);
        Assert.Contains("\"destroyOnExit\":true", json);
    }

    // -----------------------------------------------------------------------
    // Windows Sandbox backend
    // -----------------------------------------------------------------------

    [Fact]
    public void HonorMatrix_WindowsSandbox_Basic()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Filesystem = new FilesystemPolicy
            {
                ReadwritePaths = ["/data"],
            },
        };

        var config = PolicyTransform.CreateConfigFromPolicy(policy, "windows_sandbox", "ws1", PolicyTransform.Platforms.Windows);

        Assert.Equal("windows_sandbox", config.Containment!.Value.Value);
        Assert.NotNull(config.Process);
        // No filesystem, ui, network, or processContainer on windows_sandbox
        Assert.Null(config.Filesystem);
        Assert.Null(config.Ui);
        Assert.Null(config.Network);
        Assert.Null(config.ProcessContainer);
        Assert.Null(config.Experimental);
    }

    [Fact]
    public void HonorMatrix_WindowsSandbox_ThrowsOnNonWindows()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PolicyTransform.CreateConfigFromPolicy(policy, "windows_sandbox", "ws1", PolicyTransform.Platforms.Linux));
        Assert.Contains("only supported on Windows", ex.Message);
    }

    [Fact]
    public void GoldenJson_WindowsSandbox_Minimal()
    {
        var policy = new SandboxPolicy { Version = "0.5.0" };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "windows_sandbox", "ws1", PolicyTransform.Platforms.Windows);
        var json = Serialize(config);

        const string expected =
            """{"version":"0.5.0","containerId":"ws1","lifecycle":{"destroyOnExit":true,"preservePolicy":false},"process":{"commandLine":"","timeout":0},"containment":"windows_sandbox"}""";

        Assert.Equal(expected, json);
    }

    [Fact]
    public void GoldenJson_WindowsSandbox_NoProcessContainer()
    {
        // Critical: the executor REJECTS configs with processContainer alongside windows_sandbox
        var policy = new SandboxPolicy
        {
            Version = "0.5.0",
            Network = new NetworkPolicy { AllowOutbound = true },
        };
        var config = PolicyTransform.CreateConfigFromPolicy(policy, "windows_sandbox", "ws1", PolicyTransform.Platforms.Windows);
        var json = Serialize(config);

        Assert.DoesNotContain("processContainer", json);
        Assert.DoesNotContain("filesystem", json);
        Assert.DoesNotContain("network", json);
        Assert.DoesNotContain("\"ui\"", json);
        Assert.Contains("\"containment\":\"windows_sandbox\"", json);
    }
}
