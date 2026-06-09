// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace Sabbour.Mxc.Sdk.Policy;

/// <summary>
/// Discovers tool and SDK directories from the host environment and returns
/// them as policy path fragments. Port of policy.ts.
/// </summary>
public static class PolicyDiscovery
{
    /// <summary>
    /// Registry of well-known environment variables that point to tool
    /// installations, SDK roots, or language-specific resource directories.
    /// </summary>
    private static readonly (string Name, Func<string, string[]> ExtractPaths)[] KnownEnvVars =
    [
        // Python
        ("PYTHONPATH", SplitPathList),
        ("PYTHONHOME", SinglePath),
        // Visual Studio / MSVC
        ("VCINSTALLDIR", SinglePath),
        ("VSINSTALLDIR", SinglePath),
        // PowerShell modules
        ("PSModulePath", SplitPathList),
        // vcpkg
        ("VCPKG_ROOT", SinglePath),
        // Go
        ("GOPATH", SinglePath),
        ("GOROOT", SinglePath),
        // Rust
        ("CARGO_HOME", SinglePath),
        ("RUSTUP_HOME", SinglePath),
        // Java
        ("JAVA_HOME", SinglePath),
        // Node.js
        ("NVM_HOME", SinglePath),
        ("NVM_SYMLINK", SinglePath),
        ("NODE_PATH", SplitPathList),
        // .NET
        ("DOTNET_ROOT", SinglePath),
        // Conda / Anaconda
        ("CONDA_PREFIX", SinglePath),
        // Linux-specific
        ("LD_LIBRARY_PATH", SplitPathList),
        ("VIRTUAL_ENV", SinglePath),
        ("PYENV_ROOT", SinglePath),
    ];

    private static string[] SplitPathList(string value)
    {
        char separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        return value.Split(separator, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] SinglePath(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > 0 ? [trimmed] : [];
    }

    private static bool IsSystemCriticalPath(string dirPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var winDir = (Environment.GetEnvironmentVariable("WINDIR")
                         ?? Environment.GetEnvironmentVariable("windir")
                         ?? @"C:\Windows").ToLowerInvariant();
            var normalized = Path.GetFullPath(dirPath).ToLowerInvariant();
            return normalized == winDir || normalized.StartsWith(winDir + @"\", StringComparison.Ordinal);
        }

        var resolved = Path.GetFullPath(dirPath);
        string[] criticalPaths = ["/bin", "/sbin", "/usr/bin", "/usr/sbin", "/boot", "/proc", "/sys", "/dev"];
        return criticalPaths.Any(cp =>
            resolved == cp || resolved.StartsWith(cp + "/", StringComparison.Ordinal));
    }

    private static bool DirectoryExists(string dirPath)
    {
        try { return Directory.Exists(dirPath); }
        catch { return false; }
    }

    /// <summary>
    /// Checks whether the directory ACL already grants access to the
    /// ALL_APPLICATION_PACKAGES well-known SID (S-1-15-2-1).
    /// Only applicable on Windows. Fail-open: returns false on any error
    /// (access denied, timeout, etc.) so the path is included in the result.
    /// Matches TS behavior exactly.
    /// </summary>
    private static bool HasAllApplicationPackagesAccess(string dirPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "icacls",
                Arguments = $"\"{dirPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return false;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            return output.Contains("ALL APPLICATION PACKAGES") ||
                   output.Contains("S-1-15-2-1");
        }
        catch
        {
            // Fail-open: if the check fails, assume not accessible → include the path
            return false;
        }
    }

    private static List<string> DeduplicatePaths(IEnumerable<string> paths)
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var seen = new HashSet<string>(isWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var p in paths)
        {
            var resolved = Path.GetFullPath(p);
            if (seen.Add(isWindows ? resolved.ToLowerInvariant() : resolved))
            {
                result.Add(resolved);
            }
        }
        return result;
    }

    private static FilesystemPolicyResult GetPowerShellPolicy(
        IReadOnlyList<string> pathDirs,
        IDictionary<string, string>? env)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new FilesystemPolicyResult();

        bool pwshFound = pathDirs.Any(dir =>
        {
            try { return File.Exists(Path.Combine(dir, "pwsh.exe")); }
            catch { return false; }
        });

        if (!pwshFound)
            return new FilesystemPolicyResult();

        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var systemRoot = systemDrive + @"\";
        var readonlyPaths = new List<string> { systemRoot };
        var readwritePaths = new List<string>();

        string? userProfile = null;
        env?.TryGetValue("USERPROFILE", out userProfile);
        if (userProfile is not null)
        {
            var psReadLineDir = Path.Combine(
                userProfile, "AppData", "Roaming", "Microsoft", "Windows", "PowerShell", "PSReadLine");
            readwritePaths.Add(psReadLineDir);
        }

        return new FilesystemPolicyResult
        {
            ReadonlyPaths = readonlyPaths,
            ReadwritePaths = readwritePaths,
        };
    }

    /// <summary>
    /// Discover tool and SDK directories from the environment and return them as
    /// policy paths. Reads PATH and well-known tool env vars, filters non-existent
    /// and system-critical directories.
    /// </summary>
    /// <remarks>
    /// The <paramref name="env"/> parameter is treated as trusted caller input — it represents
    /// the calling process's own environment. Env-derived paths (USERPROFILE, SystemDrive, TEMP,
    /// TMP, TMPDIR) are used as-is to match the TS reference implementation's wire output.
    /// </remarks>
    /// <param name="env">Environment variable map. Defaults to process environment.</param>
    /// <param name="options">Filtering options.</param>
    public static FilesystemPolicyResult GetAvailableToolsPolicy(
        IDictionary<string, string>? env = null,
        ToolsPolicyOptions? options = null)
    {
        var environment = env ?? GetProcessEnvironment();
        var collected = new List<string>();

        // PATH directories
        string pathValue = GetEnvValue(environment, "PATH") ?? GetEnvValue(environment, "Path") ?? "";
        var pathDirs = SplitPathList(pathValue);
        collected.AddRange(pathDirs);

        // Known environment variables
        foreach (var (name, extractPaths) in KnownEnvVars)
        {
            var value = GetEnvValue(environment, name);
            if (value is not null)
            {
                collected.AddRange(extractPaths(value));
            }
        }

        var unique = DeduplicatePaths(collected);

        // Filter out non-existent, system-critical, and (optionally) already-accessible paths
        var filtered = unique.Where(dirPath =>
        {
            if (!DirectoryExists(dirPath)) return false;
            if (IsSystemCriticalPath(dirPath)) return false;
            // When containerType is 'processcontainer', exclude directories already
            // accessible to ALL_APPLICATION_PACKAGES (Windows only).
            // Matches TS: hasAllApplicationPackagesAccess check with fail-open semantics.
            if (options?.ContainerType == "processcontainer" && HasAllApplicationPackagesAccess(dirPath))
                return false;
            return true;
        }).ToList();

        // Pass resolved environment (not raw env param) so PSReadLine can resolve USERPROFILE
        var pwshPolicy = GetPowerShellPolicy(pathDirs, environment);

        return new FilesystemPolicyResult
        {
            ReadonlyPaths = DeduplicatePaths(filtered.Concat(pwshPolicy.ReadonlyPaths)),
            ReadwritePaths = DeduplicatePaths(pwshPolicy.ReadwritePaths),
        };
    }

    /// <summary>
    /// Build read-only policy for standard user profile application data locations.
    /// </summary>
    /// <remarks>
    /// Environment variables (LOCALAPPDATA, HOME) are treated as trusted caller input.
    /// Paths are used as-is to match TS wire output — no additional normalization is applied.
    /// </remarks>
    public static FilesystemPolicyResult GetUserProfilePolicy()
    {
        var readonlyPaths = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (localAppData is not null && DirectoryExists(localAppData))
            {
                var programsDir = Path.Combine(localAppData, "Programs");
                if (DirectoryExists(programsDir))
                {
                    try
                    {
                        foreach (var entry in Directory.EnumerateDirectories(programsDir))
                        {
                            readonlyPaths.Add(entry);
                        }
                    }
                    catch { /* Ignore enumeration errors */ }
                }
            }
        }
        else
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (home is not null)
            {
                var localBin = Path.Combine(home, ".local", "bin");
                if (DirectoryExists(localBin)) readonlyPaths.Add(localBin);
                var localLib = Path.Combine(home, ".local", "lib");
                if (DirectoryExists(localLib)) readonlyPaths.Add(localLib);
            }
        }

        return new FilesystemPolicyResult { ReadonlyPaths = readonlyPaths };
    }

    /// <summary>
    /// Generate policy for a container temporary directory.
    /// </summary>
    /// <remarks>
    /// Environment variables (TEMP, TMP, TMPDIR) are treated as trusted caller input.
    /// Paths are used as-is to match TS wire output — no additional normalization is applied.
    /// </remarks>
    /// <param name="env">Environment variable map. Defaults to process environment.</param>
    public static FilesystemPolicyResult GetTemporaryFilesPolicy(
        IDictionary<string, string>? env = null)
    {
        var environment = env ?? GetProcessEnvironment();

        string? tempRoot;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            tempRoot = GetEnvValue(environment, "TEMP") ?? GetEnvValue(environment, "TMP");
        }
        else
        {
            tempRoot = GetEnvValue(environment, "TMPDIR") ?? "/tmp";
        }

        if (tempRoot is null || !DirectoryExists(tempRoot))
            return new FilesystemPolicyResult();

        return new FilesystemPolicyResult { ReadwritePaths = [tempRoot] };
    }

    private static string? GetEnvValue(IDictionary<string, string> env, string key)
    {
        return env.TryGetValue(key, out var val) ? val : null;
    }

    private static IDictionary<string, string> GetProcessEnvironment()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string k && entry.Value is string v)
                dict[k] = v;
        }
        return dict;
    }
}
