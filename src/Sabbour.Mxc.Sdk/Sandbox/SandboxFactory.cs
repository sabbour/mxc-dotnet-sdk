// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using Sabbour.Mxc.Sdk.Internal;
using Sabbour.Mxc.Sdk.Policy;

namespace Sabbour.Mxc.Sdk.Sandbox;

/// <summary>
/// Creates ContainerConfig from SandboxPolicy. Port of sandbox.ts createConfigFromPolicy.
/// Contains the explicit seatbelt rejection (deferred from P2):
/// createConfigFromPolicy(policy, 'seatbelt') → throws "not yet supported" on this call path.
/// A PREBUILT config whose containment=='seatbelt' is NOT rejected at spawn time (TS allows it).
/// </summary>
public static class SandboxFactory
{
    /// <summary>
    /// Creates a ContainerConfig from a SandboxPolicy and containment type/backend.
    /// Port of TS createConfigFromPolicy().
    /// </summary>
    /// <param name="policy">The sandbox policy expressing security intent.</param>
    /// <param name="containment">Containment backend/type wire string (default: "process").</param>
    /// <param name="containerName">Optional container name; auto-generated if null.</param>
    /// <returns>Fully populated ContainerConfig.</returns>
    public static ContainerConfig CreateConfigFromPolicy(
        SandboxPolicy policy,
        string containment = "process",
        string? containerName = null)
    {
        VersionHelper.ValidatePolicyVersion(policy.Version);

        var platform = GetCurrentPlatform();
        var containerId = containerName ?? SpawnHelper.GenerateRandomContainerName();
        var clearPolicy = policy.Filesystem?.ClearPolicyOnExit ?? true;

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
            if (policy.Network is not null)
            {
                PolicyTransform.ValidateHostFilteringRequiresAllowOutbound(policy.Network, containment, platform);
            }

            return BuildMicrovmConfig(config, policy);
        }

        config = config with
        {
            Filesystem = new FilesystemConfig
            {
                ReadwritePaths = [.. (policy.Filesystem?.ReadwritePaths ?? [])],
                ReadonlyPaths = [.. (policy.Filesystem?.ReadonlyPaths ?? [])],
                DeniedPaths = [.. (policy.Filesystem?.DeniedPaths ?? [])],
            },
            Ui = new UiConfig
            {
                Disable = !(policy.Ui?.AllowWindows ?? false),
                Clipboard = policy.Ui?.Clipboard ?? ClipboardPolicy.None,
                Injection = policy.Ui?.AllowInputInjection ?? false,
            },
        };

        // Network mapping
        config = ApplyNetworkConfig(config, policy, containment, platform);

        // Backend-specific config
        if (containment == "wslc")
        {
            return BuildWslcConfig(config, containerId);
        }
        if (containment == "bubblewrap")
        {
            config = config with { Containment = ContainmentValue.FromString("bubblewrap") };
            return ApplyLinuxNetworkEnforcement(config);
        }
        if (containment == "lxc")
        {
            config = config with
            {
                Containment = ContainmentValue.FromString("lxc"),
                Lxc = new LxcConfig
                {
                    ContainerName = containerId,
                    Distribution = "alpine",
                    Release = "3.23",
                    DestroyOnExit = true,
                },
            };
            return ApplyLinuxNetworkEnforcement(config);
        }
        if (containment == "process")
        {
            config = config with { Containment = ContainmentValue.FromString("process") };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return ApplyLinuxNetworkEnforcement(config);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return BuildDarwinProcessConfig(config);
            }
            // Windows
            return BuildProcessBaseContainerConfig(config, policy, containerId);
        }

        // EXPLICIT SEATBELT REJECTION (P2 deferred, P4 implements):
        // createConfigFromPolicy(policy, 'seatbelt') is NOT supported.
        // A prebuilt config whose containment=='seatbelt' IS allowed at spawn time.
        if (containment == "seatbelt")
        {
            throw new NotSupportedException("Containment type 'seatbelt' is not yet supported.");
        }

        throw new NotSupportedException($"Containment type '{containment}' is not yet supported.");
    }

    /// <summary>
    /// Builds a sandbox payload (convenience: sets commandLine + cwd on the config).
    /// Port of TS buildSandboxPayload().
    /// </summary>
    public static ContainerConfig BuildSandboxPayload(
        string script,
        SandboxPolicy policy,
        string? workingDirectory = null,
        string? containerName = null,
        string containment = "process")
    {
        var config = CreateConfigFromPolicy(policy, containment, containerName);
        return config with
        {
            Process = config.Process! with
            {
                CommandLine = script,
                Cwd = workingDirectory,
            },
        };
    }

    /// <summary>
    /// Injects environment variables into config.process.env as KEY=VALUE strings.
    /// Port of TS injectEnvIntoConfig(). Env goes INTO the config JSON, NOT ProcessStartInfo.
    /// </summary>
    public static ContainerConfig InjectEnvIntoConfig(
        ContainerConfig config,
        IReadOnlyDictionary<string, string> env)
    {
        var existing = config.Process?.Env?.ToList() ?? [];
        foreach (var (key, value) in env)
        {
            existing.Add($"{key}={value}");
        }

        var process = (config.Process ?? new ProcessConfig { CommandLine = "" }) with { Env = existing };
        return config with { Process = process };
    }

    private static ContainerConfig ApplyNetworkConfig(
        ContainerConfig config,
        SandboxPolicy policy,
        string containment,
        string platform)
    {
        if (policy.Network is null)
        {
            return config with
            {
                Network = new NetworkConfig { DefaultPolicy = NetworkDefaultPolicy.Block }
            };
        }

        var network = policy.Network;
        PolicyTransform.ValidateProxyContainmentSupport(network, containment, platform);
        PolicyTransform.ValidateHostFilteringRequiresAllowOutbound(network, containment, platform);

        return config with
        {
            Network = new NetworkConfig
            {
                DefaultPolicy = (network.AllowOutbound ?? false) ? NetworkDefaultPolicy.Allow : NetworkDefaultPolicy.Block,
                AllowLocalNetwork = network.AllowLocalNetwork,
                AllowedHosts = network.AllowedHosts,
                BlockedHosts = network.BlockedHosts,
                Proxy = network.Proxy,
            }
        };
    }

    private static ContainerConfig BuildMicrovmConfig(ContainerConfig config, SandboxPolicy policy)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("The microvm backend is only supported on Windows (requires WHP/Hyper-V).");

        if (policy.Network is not null)
            throw new InvalidOperationException(
                "The microvm backend does not support network policy enforcement. " +
                "Remove policy.network or use a different containment backend.");

        FilesystemConfig? fs = null;
        if (policy.Filesystem?.ReadwritePaths?.Count > 0 ||
            policy.Filesystem?.ReadonlyPaths?.Count > 0 ||
            policy.Filesystem?.DeniedPaths?.Count > 0)
        {
            fs = new FilesystemConfig
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
            Filesystem = fs,
        };
    }

    private static ContainerConfig BuildWslcConfig(ContainerConfig config, string containerId)
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

    private static ContainerConfig BuildProcessBaseContainerConfig(
        ContainerConfig config, SandboxPolicy policy, string containerId)
    {
        var capabilities = new List<string>();
        if (policy.Network?.AllowOutbound ?? false)
            capabilities.Add("internetClient");
        if (policy.Network?.AllowLocalNetwork ?? false)
            capabilities.Add("privateNetworkClientServer");

        var pcConfig = new ProcessContainerConfig
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
            ProcessContainer = pcConfig,
            Network = network,
        };
    }

    /// <summary>
    /// Apply Linux network enforcement mode. Port of helper.ts applyLinuxNetworkPolicy.
    /// </summary>
    private static ContainerConfig ApplyLinuxNetworkEnforcement(ContainerConfig config)
    {
        if (config.Network is null) return config;

        var hasHostRules = (config.Network.AllowedHosts?.Count > 0) ||
                           (config.Network.BlockedHosts?.Count > 0);
        var hasProxy = config.Network.Proxy is not null;

        if (hasHostRules && !hasProxy)
        {
            return config with
            {
                Network = config.Network with { EnforcementMode = NetworkEnforcementMode.Firewall }
            };
        }

        return config;
    }

    private static string GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return PolicyTransform.Platforms.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return PolicyTransform.Platforms.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return PolicyTransform.Platforms.MacOS;
        return "unknown";
    }
}
