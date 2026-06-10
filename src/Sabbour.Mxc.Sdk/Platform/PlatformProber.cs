// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Text.Json;

namespace Sabbour.Mxc.Sdk.Platform;

/// <summary>
/// Identifies the host OS for platform probing. Injectable to allow cross-platform testing.
/// Maps to TS <c>os.platform()</c> return values: 'win32', 'linux', 'darwin'.
/// </summary>
internal enum ProbedPlatform
{
    Windows,
    Linux,
    Darwin,
    Unknown
}

/// <summary>
/// Probes the host for platform capabilities and available containment backends.
/// Faithful port of platform.ts <c>getPlatformSupport()</c> / <c>computeSupport()</c>.
///
/// TS mutable-global seams are replaced by constructor-injected interfaces:
/// - <see cref="IPlatformProbeRunner"/> (replaces <c>_setProbeRunner</c>)
/// - <see cref="IWindowsBuildQuery"/> (replaces <c>_setWindowsBuildQuery</c>)
/// - <see cref="PlatformSupportCache"/> (replaces <c>cachedSupport</c> + <c>_resetPlatformSupportCache</c>)
/// </summary>
public sealed class PlatformProber
{
    private readonly IPlatformProbeRunner _probeRunner;
    private readonly IWindowsBuildQuery _buildQuery;
    private readonly PlatformSupportCache _cache;
    private readonly ProbedPlatform _platform;

    /// <summary>
    /// Creates a new PlatformProber with production defaults (detects current OS).
    /// </summary>
    public PlatformProber()
        : this(new DefaultPlatformProbeRunner(), null, null, null)
    {
    }

    /// <summary>
    /// Creates a PlatformProber with injected dependencies (test seam).
    /// </summary>
    /// <param name="probeRunner">Probe runner implementation.</param>
    /// <param name="buildQuery">Windows build query (null = use default backed by probeRunner).</param>
    /// <param name="cache">Cache instance (null = create new).</param>
    /// <param name="platform">Override the detected OS (null = auto-detect via RuntimeInformation).</param>
    internal PlatformProber(
        IPlatformProbeRunner probeRunner,
        IWindowsBuildQuery? buildQuery = null,
        PlatformSupportCache? cache = null,
        ProbedPlatform? platform = null)
    {
        _probeRunner = probeRunner ?? throw new ArgumentNullException(nameof(probeRunner));
        _buildQuery = buildQuery ?? new DefaultWindowsBuildQuery(probeRunner);
        _cache = cache ?? new PlatformSupportCache();
        _platform = platform ?? DetectCurrentPlatform();
    }

    private static ProbedPlatform DetectCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return ProbedPlatform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return ProbedPlatform.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return ProbedPlatform.Darwin;
        return ProbedPlatform.Unknown;
    }

    /// <summary>
    /// Gets the cache instance for external reset (test seam, analogue of _resetPlatformSupportCache).
    /// </summary>
    internal PlatformSupportCache Cache => _cache;

    /// <summary>
    /// Get platform support information. Results are cached for the prober's lifetime.
    /// Thread-safe: concurrent callers only run probes once (single-flight).
    /// Analogue of TS <c>getPlatformSupport()</c>.
    /// </summary>
    public PlatformSupport GetPlatformSupport()
    {
        return _cache.GetOrCompute(ComputeSupport);
    }

    /// <summary>
    /// Computes platform support by probing the current OS. Analogue of TS <c>computeSupport()</c>.
    /// </summary>
    private PlatformSupport ComputeSupport()
    {
        return _platform switch
        {
            ProbedPlatform.Darwin => ComputeDarwinSupport(),
            ProbedPlatform.Linux => ComputeLinuxSupport(),
            ProbedPlatform.Windows => ComputeWindowsSupport(),
            _ => new PlatformSupport
            {
                IsSupported = false,
                Reason = "MXC is not supported on this platform",
                AvailableMethods = []
            }
        };
    }

    /// <summary>
    /// macOS: check for /usr/bin/sandbox-exec (seatbelt backend).
    /// </summary>
    private PlatformSupport ComputeDarwinSupport()
    {
        if (IsSeatbeltAvailable())
        {
            return new PlatformSupport
            {
                IsSupported = true,
                AvailableMethods = [ContainmentBackend.Seatbelt]
            };
        }

        return new PlatformSupport
        {
            IsSupported = false,
            Reason = "/usr/bin/sandbox-exec not found; macOS install is incomplete",
            AvailableMethods = []
        };
    }

    /// <summary>
    /// Linux: check for LXC and/or Bubblewrap availability.
    /// </summary>
    private PlatformSupport ComputeLinuxSupport()
    {
        var methods = new List<ContainmentBackend>();

        if (IsLxcAvailable())
            methods.Add(ContainmentBackend.Lxc);

        if (IsBubblewrapAvailable())
            methods.Add(ContainmentBackend.Bubblewrap);

        if (methods.Count > 0)
        {
            return new PlatformSupport
            {
                IsSupported = true,
                AvailableMethods = methods
            };
        }

        return new PlatformSupport
        {
            IsSupported = false,
            Reason = "Neither LXC nor Bubblewrap is available on this system",
            AvailableMethods = []
        };
    }

    /// <summary>
    /// Windows: processcontainer is always available; conditionally add windows_sandbox
    /// and isolation_session; run wxc-exec --probe for isolation tier + UI capabilities.
    /// Also merges WSL2-based backends when WSL2 is available.
    /// </summary>
    private PlatformSupport ComputeWindowsSupport()
    {
        var methods = new List<ContainmentBackend> { ContainmentBackend.ProcessContainer };

        if (IsWindowsSandboxAvailable())
            methods.Add(ContainmentBackend.WindowsSandbox);

        if (IsIsoSessionSupported())
            methods.Add(ContainmentBackend.IsolationSession);

        // Merge WSL2 backends when WSL2 is available.
        var wsl2Support = GetWsl2PlatformSupport();
        if (wsl2Support.IsSupported)
        {
            foreach (var method in wsl2Support.AvailableMethods)
                methods.Add(method);
        }

        var support = new PlatformSupport
        {
            IsSupported = true,
            AvailableMethods = methods
        };

        // Populate isolation tier, warnings, and UI capabilities from probe
        return PopulateIsolationFromProbe(support);
    }

    /// <summary>
    /// Check whether the host supports the IsolationSession backend.
    /// Requires Windows Insider Preview build 26300.8553 or later.
    /// </summary>
    internal bool IsIsoSessionSupported()
    {
        var build = _buildQuery.GetWindowsBuild();
        if (build is null)
            return false;

        // Pin to build 26300 with UBR >= 8553
        return build.Value.Major == 26300 && build.Value.Minor >= 8553;
    }

    /// <summary>
    /// Check if Windows Sandbox feature is enabled.
    /// First tries DISM and checks stdout for "State : Enabled" (TS: /State\s*:\s*Enabled/i);
    /// on failure/timeout falls back to checking WindowsSandbox.exe existence.
    /// </summary>
    internal bool IsWindowsSandboxAvailable()
    {
        try
        {
            var result = _probeRunner.RunCommand("dism",
                ["/online", "/get-featureinfo", "/featurename:Containers-DisposableClientVM"],
                timeoutMs: 10000);

            if (result.ExitCode == 0)
            {
                // TS checks: /State\s*:\s*Enabled/i — DISM exits 0 even when Disabled.
                return System.Text.RegularExpressions.Regex.IsMatch(
                    result.Stdout,
                    @"State\s*:\s*Enabled",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
        }
        catch
        {
            // DISM failed/threw/timed out → fall through to exe fallback.
        }

        // Fallback: check for WindowsSandbox.exe (same as TS catch branch)
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        var sandboxExe = Path.Combine(systemRoot, "System32", "WindowsSandbox.exe");
        return _probeRunner.FileExists(sandboxExe);
    }

    /// <summary>
    /// Check if LXC is available (Linux).
    /// </summary>
    internal bool IsLxcAvailable()
    {
        return _probeRunner.IsToolAvailable("lxc-ls", "--version");
    }

    /// <summary>
    /// Check if Bubblewrap (bwrap) is available (Linux).
    /// </summary>
    internal bool IsBubblewrapAvailable()
    {
        return _probeRunner.IsToolAvailable("bwrap", "--version");
    }

    /// <summary>
    /// Check if macOS sandbox-exec is available.
    /// </summary>
    internal bool IsSeatbeltAvailable()
    {
        return _probeRunner.FileExists("/usr/bin/sandbox-exec");
    }

    /// <summary>
    /// Check whether wsl.exe is present and responds successfully.
    /// Runs <c>wsl.exe --status</c> with a 5-second timeout.
    /// </summary>
    internal bool IsWsl2Available()
    {
        try
        {
            var result = _probeRunner.RunCommand("wsl.exe", ["--status"], timeoutMs: 5000);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check whether bubblewrap (bwrap) is available inside WSL2.
    /// </summary>
    internal bool IsWsl2BwrapAvailable()
    {
        return _probeRunner.IsToolAvailableInWsl2("bwrap");
    }

    /// <summary>
    /// Check whether unshare is available inside WSL2.
    /// </summary>
    internal bool IsWsl2UnshareAvailable()
    {
        return _probeRunner.IsToolAvailableInWsl2("unshare");
    }

    /// <summary>
    /// Probes for WSL2 and returns a <see cref="PlatformSupport"/> describing which
    /// WSL2-backed isolation tools (<see cref="ContainmentBackend.WslBubblewrap"/>,
    /// <see cref="ContainmentBackend.WslUnshare"/>) are available on the Windows host.
    /// Returns <c>IsSupported = false</c> when wsl.exe is not found or no tools are present.
    /// </summary>
    public PlatformSupport GetWsl2PlatformSupport()
    {
        if (!IsWsl2Available())
        {
            return new PlatformSupport
            {
                IsSupported = false,
                Reason = "wsl.exe not found or WSL2 is not available",
                AvailableMethods = []
            };
        }

        var methods = new List<ContainmentBackend>();

        if (IsWsl2BwrapAvailable())
            methods.Add(ContainmentBackend.WslBubblewrap);

        if (IsWsl2UnshareAvailable())
            methods.Add(ContainmentBackend.WslUnshare);

        if (methods.Count == 0)
        {
            return new PlatformSupport
            {
                IsSupported = false,
                Reason = "WSL2 is available but neither bwrap nor unshare was found inside WSL2",
                AvailableMethods = []
            };
        }

        return new PlatformSupport
        {
            IsSupported = true,
            AvailableMethods = methods
        };
    }

    /// <summary>
    /// Run the probe binary and merge its results into support.
    /// On any failure (binary missing, timeout, malformed JSON, unknown tier),
    /// silently leaves isolationTier/isolationWarnings/uiCapabilities unset.
    /// Faithful port of TS <c>populateIsolationFromProbe</c>.
    /// </summary>
    private PlatformSupport PopulateIsolationFromProbe(PlatformSupport support)
    {
        try
        {
            var stdout = _probeRunner.RunProbe();
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            IsolationTier? tier = null;
            IReadOnlyList<string>? warnings = null;
            UiCapabilitySupport? uiCapabilities = null;

            // Parse tier
            if (root.TryGetProperty("tier", out var tierEl) && tierEl.ValueKind == JsonValueKind.String)
            {
                var tierStr = tierEl.GetString();
                if (IsValidTier(tierStr))
                {
                    tier = ParseTier(tierStr!);
                }
            }

            // Parse warnings
            if (root.TryGetProperty("warnings", out var warningsEl) && warningsEl.ValueKind == JsonValueKind.Array)
            {
                var warnList = new List<string>();
                foreach (var item in warningsEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var w = item.GetString();
                        if (w is not null)
                            warnList.Add(w);
                    }
                }
                if (warnList.Count > 0)
                    warnings = warnList;
            }

            // Parse probes.uiCapabilities
            if (root.TryGetProperty("probes", out var probesEl) &&
                probesEl.TryGetProperty("uiCapabilities", out var uiEl) &&
                uiEl.ValueKind == JsonValueKind.Object)
            {
                uiCapabilities = TryParseUiCapabilities(uiEl);
            }

            // Return new record with merged fields
            return support with
            {
                IsolationTier = tier,
                IsolationWarnings = warnings,
                UiCapabilities = uiCapabilities
            };
        }
        catch
        {
            // Graceful degradation: leave isolation fields unset (same as TS).
            return support;
        }
    }

    private static bool IsValidTier(string? s)
    {
        return s is "base-container" or "appcontainer-bfs" or "appcontainer-dacl";
    }

    private static IsolationTier ParseTier(string s) => s switch
    {
        "base-container" => IsolationTier.BaseContainer,
        "appcontainer-bfs" => IsolationTier.AppContainerBfs,
        "appcontainer-dacl" => IsolationTier.AppContainerDacl,
        _ => throw new ArgumentException($"Unknown tier: {s}")
    };

    private static UiCapabilitySupport? TryParseUiCapabilities(JsonElement el)
    {
        if (!TryGetBool(el, "canBlockClipboardRead", out var clipRead) ||
            !TryGetBool(el, "canBlockClipboardWrite", out var clipWrite) ||
            !TryGetBool(el, "canBlockInputInjection", out var inputInj) ||
            !TryGetBool(el, "canBlockInputMethodChanges", out var inputMethod) ||
            !TryGetBool(el, "canBlockExternalUiObjects", out var extUi) ||
            !TryGetBool(el, "canBlockGlobalUiNamespace", out var globalUi) ||
            !TryGetBool(el, "canBlockDesktopSwitching", out var desktop) ||
            !TryGetBool(el, "canBlockLogoffOrShutdown", out var logoff) ||
            !TryGetBool(el, "canBlockSystemParameterChanges", out var sysParam) ||
            !TryGetBool(el, "canBlockDisplaySettingsChanges", out var display))
        {
            return null;
        }

        return new UiCapabilitySupport
        {
            CanBlockClipboardRead = clipRead,
            CanBlockClipboardWrite = clipWrite,
            CanBlockInputInjection = inputInj,
            CanBlockInputMethodChanges = inputMethod,
            CanBlockExternalUiObjects = extUi,
            CanBlockGlobalUiNamespace = globalUi,
            CanBlockDesktopSwitching = desktop,
            CanBlockLogoffOrShutdown = logoff,
            CanBlockSystemParameterChanges = sysParam,
            CanBlockDisplaySettingsChanges = display
        };
    }

    private static bool TryGetBool(JsonElement el, string name, out bool value)
    {
        value = false;
        if (el.TryGetProperty(name, out var prop) &&
            (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
        {
            value = prop.GetBoolean();
            return true;
        }
        return false;
    }
}
