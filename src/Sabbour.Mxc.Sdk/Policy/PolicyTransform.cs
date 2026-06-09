// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Cryptography;
using Sabbour.Mxc.Sdk.Internal;

namespace Sabbour.Mxc.Sdk.Policy;

/// <summary>
/// Pure, deterministic transformation of <see cref="SandboxPolicy"/> into
/// <see cref="ContainerConfig"/>. Port of sandbox.ts createConfigFromPolicy.
/// No I/O, no platform probing — the explicit platform argument on the
/// transform methods makes OS branching testable.
/// </summary>
internal static class PolicyTransform
{
    /// <summary>
    /// Supported platform identifiers for the transform's backend branching logic.
    /// Matches Node.js os.platform() values used in the TS source.
    /// </summary>
    public static class Platforms
    {
        public const string Windows = "win32";
        public const string Linux = "linux";
        public const string MacOS = "darwin";
    }

    /// <summary>
    /// Creates a <see cref="ContainerConfig"/> from a <see cref="SandboxPolicy"/>
    /// and optional containment type. This is the primary API for translating
    /// user-facing security intent into a backend-specific configuration.
    /// </summary>
    /// <param name="policy">The sandbox policy expressing security intent.</param>
    /// <param name="containment">
    /// Containment wire string (ContainmentType or ContainmentBackend). Default: "process".
    /// </param>
    /// <param name="containerName">Optional container name; auto-generated if null.</param>
    /// <param name="platform">
    /// OS platform identifier (win32/linux/darwin). Defaults to current OS.
    /// Exposed for testability — the TS version uses <c>os.platform()</c>.
    /// </param>
    /// <returns>A fully populated ContainerConfig ready for spawning.</returns>
    public static ContainerConfig CreateConfigFromPolicy(
        SandboxPolicy policy,
        string containment = "process",
        string? containerName = null,
        string? platform = null)
    {
        VersionHelper.ValidatePolicyVersion(policy.Version);

        platform ??= GetCurrentPlatform();
        var containerId = containerName ?? GenerateRandomContainerName();

        if (containment == "microvm" && policy.Network is not null)
        {
            ValidateHostFilteringRequiresAllowOutbound(policy.Network, containment, platform);
        }

        bool clearPolicy = policy.Filesystem?.ClearPolicyOnExit ?? true;

        var config = new ContainerConfig
        {
            Version = policy.Version,
            ContainerId = containerId,
            Lifecycle = new LifecycleConfig
            {
                DestroyOnExit = true,
                PreservePolicy = !clearPolicy,
            },
            Process = new ProcessConfig
            {
                CommandLine = "",
                Timeout = policy.TimeoutMs ?? 0,
            },
        };

        // Microvm: delegate to dedicated builder
        if (containment == "microvm")
        {
            return BuildMicroVmConfig(config, policy, platform);
        }

        // Filesystem (all non-microvm backends)
        config = config with
        {
            Filesystem = new FilesystemConfig
            {
                ReadwritePaths = [.. (policy.Filesystem?.ReadwritePaths ?? [])],
                ReadonlyPaths = [.. (policy.Filesystem?.ReadonlyPaths ?? [])],
                DeniedPaths = [.. (policy.Filesystem?.DeniedPaths ?? [])],
            },
        };

        // UI mapping (cross-platform)
        config = config with
        {
            Ui = new UiConfig
            {
                Disable = !(policy.Ui?.AllowWindows ?? false),
                Clipboard = policy.Ui?.Clipboard ?? ClipboardPolicy.None,
                Injection = policy.Ui?.AllowInputInjection ?? false,
            },
        };

        // Network mapping (cross-platform) — default-deny
        config = config with { Network = BuildNetworkConfig(policy, containment, platform) };

        // Backend-specific config based on containment type
        if (containment == "wslc")
            return BuildWslcContainerConfig(config, containerId);

        if (containment == "bubblewrap")
            return BuildBubblewrapConfig(config);

        if (containment == "lxc")
            return BuildLxcConfig(config, containerId);

        if (containment == "process")
            return BuildProcessConfig(config, policy, containerId, platform);

        // NOTE: Explicit containment=="seatbelt" is NOT handled here.
        // In TS, createConfigFromPolicy only handles wslc/bubblewrap/lxc/process/microvm.
        // The rejection of explicit "seatbelt" happens later in sandbox.ts (P4's job).

        throw new InvalidOperationException($"Containment type '{containment}' is not yet supported.");
    }

    // -----------------------------------------------------------------------
    // Network builder
    // -----------------------------------------------------------------------

    private static NetworkConfig BuildNetworkConfig(SandboxPolicy policy, string containment, string platform)
    {
        if (policy.Network is null)
        {
            return new NetworkConfig { DefaultPolicy = NetworkDefaultPolicy.Block };
        }

        var net = policy.Network;

        // Proxy validation: only Bubblewrap (or abstract process) on Linux, never macOS
        if (net.Proxy is not null && platform == Platforms.Linux)
        {
            bool linuxProxySupported = containment is "bubblewrap" or "process";
            if (!linuxProxySupported)
            {
                throw new InvalidOperationException(
                    $"Proxy configuration is not supported on Linux containment='{containment}'. " +
                    "Use containment 'bubblewrap' (or the abstract 'process') for proxy-based host filtering.");
            }
        }
        if (net.Proxy is not null && platform == Platforms.MacOS)
        {
            throw new InvalidOperationException("Proxy configuration is not supported on macOS");
        }

        ValidateHostFilteringRequiresAllowOutbound(net, containment, platform);

        return new NetworkConfig
        {
            DefaultPolicy = (net.AllowOutbound ?? false) ? NetworkDefaultPolicy.Allow : NetworkDefaultPolicy.Block,
            AllowLocalNetwork = net.AllowLocalNetwork,
            AllowedHosts = net.AllowedHosts,
            BlockedHosts = net.BlockedHosts,
            Proxy = net.Proxy,
        };
    }

    // -----------------------------------------------------------------------
    // Backend builders (pure, platform-parameterized)
    // -----------------------------------------------------------------------

    private static ContainerConfig BuildWslcContainerConfig(ContainerConfig config, string containerId)
    {
        return config with
        {
            Containment = ContainmentValue.FromString("wslc"),
            ContainerId = containerId,
            Experimental = new ExperimentalConfig
            {
                Wslc = new WslcConfig { Image = "alpine:latest" },
            },
        };
    }

    private static ContainerConfig BuildBubblewrapConfig(ContainerConfig config)
    {
        return config with
        {
            Containment = ContainmentValue.FromString("bubblewrap"),
            Network = NetworkPolicyHelper.ApplyLinuxNetworkPolicy(config.Network),
        };
    }

    private static ContainerConfig BuildLxcConfig(ContainerConfig config, string containerId)
    {
        return config with
        {
            Containment = ContainmentValue.FromString("lxc"),
            Lxc = new LxcConfig
            {
                ContainerName = containerId,
                Distribution = "alpine",
                Release = "3.23",
                DestroyOnExit = true,
            },
            Network = NetworkPolicyHelper.ApplyLinuxNetworkPolicy(config.Network),
        };
    }

    private static ContainerConfig BuildDarwinProcessConfig(ContainerConfig config)
    {
        return config with
        {
            Containment = ContainmentValue.FromString("seatbelt"),
            Experimental = new ExperimentalConfig
            {
                Seatbelt = new SeatbeltConfig(),
            },
        };
    }

    private static ContainerConfig BuildProcessConfig(
        ContainerConfig config, SandboxPolicy policy, string containerId, string platform)
    {
        config = config with { Containment = ContainmentValue.FromString("process") };

        if (platform == Platforms.Linux)
        {
            // Abstract 'process' on Linux resolves to Bubblewrap
            return config with
            {
                Network = NetworkPolicyHelper.ApplyLinuxNetworkPolicy(config.Network),
            };
        }

        if (platform == Platforms.MacOS)
        {
            return BuildDarwinProcessConfig(config);
        }

        // Windows: ProcessContainer (BaseContainer)
        return BuildProcessBaseContainerConfig(config, policy, containerId);
    }

    private static ContainerConfig BuildProcessBaseContainerConfig(
        ContainerConfig config, SandboxPolicy policy, string containerId)
    {
        var capabilities = new List<string>();
        if (policy.Network?.AllowOutbound ?? false)
            capabilities.Add("internetClient");
        if (policy.Network?.AllowLocalNetwork ?? false)
            capabilities.Add("privateNetworkClientServer");

        var processContainer = new ProcessContainerConfig
        {
            Name = containerId,
            LeastPrivilege = false,
            Capabilities = capabilities,
            Ui = new BaseProcessUiConfig
            {
                Isolation = UiIsolationLevel.Container,
                DesktopSystemControl = false,
                SystemSettings = "none",
                Ime = false,
            },
        };

        // Network enforcement mode for Windows
        NetworkConfig? network = config.Network;
        if (network is not null)
        {
            if ((network.AllowedHosts?.Count > 0) || (network.BlockedHosts?.Count > 0))
            {
                network = network with { EnforcementMode = NetworkEnforcementMode.Both };
            }
            else
            {
                network = network with { EnforcementMode = NetworkEnforcementMode.Capabilities };
            }
        }

        return config with
        {
            ProcessContainer = processContainer,
            Network = network,
        };
    }

    private static ContainerConfig BuildMicroVmConfig(ContainerConfig config, SandboxPolicy policy, string platform)
    {
        if (platform != Platforms.Windows)
            throw new InvalidOperationException("The microvm backend is only supported on Windows (requires WHP/Hyper-V).");

        if (policy.Network is not null)
        {
            throw new InvalidOperationException(
                "The microvm backend does not support network policy enforcement. " +
                "Remove policy.network or use a different containment backend.");
        }

        FilesystemConfig? filesystem = null;
        if ((policy.Filesystem?.ReadwritePaths?.Count ?? 0) > 0 ||
            (policy.Filesystem?.ReadonlyPaths?.Count ?? 0) > 0 ||
            (policy.Filesystem?.DeniedPaths?.Count ?? 0) > 0)
        {
            filesystem = new FilesystemConfig
            {
                ReadwritePaths = policy.Filesystem?.ReadwritePaths,
                ReadonlyPaths = policy.Filesystem?.ReadonlyPaths,
                DeniedPaths = policy.Filesystem?.DeniedPaths,
                ClearPolicyOnExit = policy.Filesystem?.ClearPolicyOnExit ?? true,
            };
        }

        return config with
        {
            Containment = ContainmentValue.FromString("microvm"),
            Filesystem = filesystem,
        };
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    internal static void ValidateHostFilteringRequiresAllowOutbound(
        NetworkPolicy network,
        string containment,
        string platform)
    {
        if (ResolvesToHostFilteringBackend(containment, platform))
        {
            return;
        }

        bool hasHostRules = (network.AllowedHosts?.Count ?? 0) > 0 ||
            (network.BlockedHosts?.Count ?? 0) > 0;
        if (hasHostRules && !(network.AllowOutbound ?? false))
        {
            throw new InvalidOperationException("allowedHosts/blockedHosts require allowOutbound to be true");
        }
    }

    internal static bool ResolvesToHostFilteringBackend(string containment, string platform)
    {
        return containment == "wslc" ||
            containment == "seatbelt" ||
            containment == "bubblewrap" ||
            containment == "lxc" ||
            (containment == "process" && platform == Platforms.Linux) ||
            (containment == "process" && platform == Platforms.MacOS);
    }

    private static string GenerateRandomContainerName()
    {
        // TS: randomBytes(4).toString("hex") → 8 hex chars
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    private static string GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return Platforms.Windows;
        if (OperatingSystem.IsLinux()) return Platforms.Linux;
        if (OperatingSystem.IsMacOS()) return Platforms.MacOS;
        return "unknown";
    }
}
