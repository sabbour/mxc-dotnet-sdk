// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sabbour.Mxc.Sdk.Errors;
using Sabbour.Mxc.Sdk.Platform;

namespace Sabbour.Mxc.Sdk.Sandbox;

/// <summary>
/// Internal helper: resolves the executor binary and builds CLI arguments.
/// Port of helper.ts prepareSpawn / resolveBinaryAndCommonArgs / resolveExecutableAndArgs.
/// </summary>
internal static class SpawnHelper
{
    /// <summary>
    /// Resolves the executable path and builds CLI arguments for a sandbox invocation.
    /// Validates invariants before building the command line.
    /// </summary>
    internal static PrepareSpawnResult PrepareSpawn(
        ContainerConfig config,
        SandboxSpawnOptions options,
        IPlatformProbeRunner? probeRunner = null)
    {
        // Validate commandLine is set
        if (string.IsNullOrEmpty(config.Process?.CommandLine))
        {
            throw new InvalidOperationException(
                "script is required. Set process.commandLine on the config or pass a script to SpawnSandbox().");
        }

        // Resolve executable
        var executablePath = ResolveExecutable(config, options, probeRunner);

        // Build args
        var args = BuildArgs(config, options);

        return new PrepareSpawnResult(executablePath, args);
    }

    /// <summary>
    /// Resolves executable + args for state-aware lifecycle operations (P5)
    /// that may not have commandLine set on the config. Skips commandLine validation only.
    /// NOT used for user-facing spawn-from-config paths (those use PrepareSpawn).
    /// </summary>
    internal static PrepareSpawnResult PrepareSpawnFromConfig(
        ContainerConfig config,
        SandboxSpawnOptions options,
        IPlatformProbeRunner? probeRunner = null)
    {
        var executablePath = ResolveExecutable(config, options, probeRunner);
        var args = BuildArgs(config, options);
        return new PrepareSpawnResult(executablePath, args);
    }

    /// <summary>
    /// Resolves executable + args for a state-aware lifecycle envelope (already JSON).
    /// Base64-encodes the envelope and builds argv with P4's flag logic.
    /// </summary>
    internal static PrepareSpawnResult PrepareSpawnFromJson(
        string envelopeJson,
        SandboxSpawnOptions options,
        IPlatformProbeRunner? probeRunner = null)
    {
        var executablePath = ResolveExecutableForStateAware(options, probeRunner);
        var args = BuildArgsFromJson(envelopeJson, options);
        return new PrepareSpawnResult(executablePath, args);
    }

    private static string ResolveExecutableForStateAware(
        SandboxSpawnOptions options,
        IPlatformProbeRunner? probeRunner)
    {
        // Platform support check — matches TS resolveBinaryAndCommonArgs (lines 153-155)
        if (!options.SkipPlatformCheck)
        {
            var prober = new PlatformProber();
            var platformSupport = prober.GetPlatformSupport();
            if (!platformSupport.IsSupported)
            {
                throw new InvalidOperationException(
                    $"MXC is not supported on this platform: {platformSupport.Reason}");
            }
        }

        if (!string.IsNullOrEmpty(options.ExecutablePath))
        {
            if (!File.Exists(options.ExecutablePath))
                throw new FileNotFoundException($"File not found: {options.ExecutablePath}");
            return options.ExecutablePath;
        }

        // Platform-specific binary resolution — matches TS resolveBinaryAndCommonArgs (lines 157-172)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var path = DefaultPlatformProbeRunner.FindLxcExecutable();
            if (path is null)
            {
                throw new InvalidOperationException(
                    "lxc-exec not found. Ensure it is built and available in a standard location.");
            }
            return path;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var path = DefaultPlatformProbeRunner.FindSeatbeltExecutable();
            if (path is null)
            {
                throw new InvalidOperationException(
                    "mxc-exec-mac not found. Ensure it is built and available in a standard location.");
            }
            return path;
        }

        // Windows (default)
        {
            var path = DefaultPlatformProbeRunner.FindWxcExecutable();
            if (path is null)
            {
                throw new InvalidOperationException(
                    "wxc-exec.exe not found. Set ExecutablePath or ensure it exists in a standard location.");
            }
            return path;
        }
    }

    /// <summary>
    /// Builds CLI args from pre-built envelope JSON (state-aware path).
    /// Port of helper.ts resolveBinaryAndCommonArgs (lines 151-171):
    /// emits ONLY --config-base64, --dry-run, --debug, --experimental.
    /// Does NOT add --log-file (that is a one-shot prepareSpawn concern).
    /// </summary>
    private static List<string> BuildArgsFromJson(string envelopeJson, SandboxSpawnOptions options)
    {
        // ADVISORY: --config-base64 exposes the token in process args (faithful wire port).
        // This is a known risk inherited from the TS SDK design.
        var configBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(envelopeJson));

        var args = new List<string>
        {
            "--config-base64",
            configBase64
        };

        if (options.DryRun) args.Add("--dry-run");
        if (options.Debug) args.Add("--debug");
        if (options.Experimental) args.Add("--experimental");

        return args;
    }

    private static string ResolveExecutable(
        ContainerConfig config,
        SandboxSpawnOptions options,
        IPlatformProbeRunner? probeRunner)
    {
        // Check experimental gating
        var containmentWire = config.Containment?.ToString();
        if (!string.IsNullOrEmpty(containmentWire) &&
            ExperimentalBackends.RequiresExperimental(containmentWire) &&
            !options.Experimental)
        {
            throw new InvalidOperationException(
                $"'{containmentWire}' containment requires experimental mode. Set 'Experimental = true' in SandboxSpawnOptions.");
        }

        // Explicit path — caller-provided, trusted by definition
        if (!string.IsNullOrEmpty(options.ExecutablePath))
        {
            if (!File.Exists(options.ExecutablePath))
                throw new FileNotFoundException($"File not found: {options.ExecutablePath}");
            return options.ExecutablePath;
        }

        // Platform-specific binary resolution — matches TS resolveBinaryAndCommonArgs (helper.ts:157-172):
        // linux -> lxc-exec, darwin -> mxc-exec-mac, win32 -> wxc-exec.exe.
        // SECURITY (#10 trust boundary): each finder searches packaged/assembly-relative
        // paths BEFORE PATH, ensuring a local attacker cannot shadow the binary via PATH injection.
        // See Platform/DefaultPlatformProbeRunner.cs for the search order.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var path = DefaultPlatformProbeRunner.FindLxcExecutable();
            if (path is null)
            {
                throw new InvalidOperationException(
                    "lxc-exec not found. Ensure it is built and available in a standard location.");
            }
            return path;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var path = DefaultPlatformProbeRunner.FindSeatbeltExecutable();
            if (path is null)
            {
                throw new InvalidOperationException(
                    "mxc-exec-mac not found. Ensure it is built and available in a standard location.");
            }
            return path;
        }

        // Windows (default)
        {
            var path = DefaultPlatformProbeRunner.FindWxcExecutable();
            if (path is null)
            {
                throw new InvalidOperationException(
                    "wxc-exec.exe not found. Set ExecutablePath or ensure it exists in a standard location.");
            }
            return path;
        }
    }

    /// <summary>
    /// Builds the CLI arguments array. Config is passed as --config-base64 (UTF-8 JSON → base64).
    /// Flags: --dry-run, --debug, --experimental, --log-file.
    ///
    /// Fix #5: When Debug==true and LogDir==null, generates a temp mxc-logs dir (matching TS behavior).
    /// </summary>
    private static List<string> BuildArgs(ContainerConfig config, SandboxSpawnOptions options)
    {
        var configJson = JsonSerializer.Serialize(config, MxcJsonContext.Default.ContainerConfig);
        var configBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(configJson));

        var args = new List<string>
        {
            "--config-base64",
            configBase64
        };

        if (options.DryRun) args.Add("--dry-run");
        if (options.Debug) args.Add("--debug");
        if (options.Experimental) args.Add("--experimental");

        // Log file: generate timestamped path in logDir.
        // Fix #5: when Debug==true and LogDir is not set, use temp mxc-logs dir (matches TS).
        var logDir = options.LogDir;
        if (string.IsNullOrEmpty(logDir) && options.Debug)
        {
            logDir = Path.Combine(Path.GetTempPath(), "mxc-logs");
        }

        if (!string.IsNullOrEmpty(logDir))
        {
            var logFile = MakeLogFilePath(logDir);
            args.Add("--log-file");
            args.Add(logFile);
        }

        return args;
    }

    /// <summary>
    /// Generate a timestamped log file path. Port of helper.ts makeLogFilePath.
    /// </summary>
    internal static string MakeLogFilePath(string dir)
    {
        var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss-fff");
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        return Path.Combine(dir, $"mxc-diag-{ts}-{suffix}.log");
    }

    /// <summary>
    /// Attempts to parse stdout as a complete JSON error envelope from wxc-exec.
    /// Format: {"error": {"code": "...", "message": "...", "details": ...}}
    /// Returns MxcException ONLY when stdout.Trim() is exactly one JSON object
    /// whose "error" property is an object containing a string "code".
    /// Returns null on any parse failure or non-envelope shape.
    ///
    /// Port of state-aware-helper.ts tryParseErrorEnvelope (lines 109-122).
    /// </summary>
    internal static MxcException? TryParseErrorEnvelope(string output)
    {
        const int MaxMessageLength = 8192;
        const int MaxDetailsRawLength = 64 * 1024; // 64KB for structured details

        try
        {
            using var doc = JsonDocument.Parse(output.Trim());
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!root.TryGetProperty("error", out var errorElement))
                return null;

            if (errorElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!errorElement.TryGetProperty("code", out var codeEl) ||
                codeEl.ValueKind != JsonValueKind.String)
                return null;

            var code = codeEl.GetString()!;
            var message = errorElement.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                ? msgEl.GetString()!
                : "";

            // Cap excessively long messages
            if (message.Length > MaxMessageLength)
            {
                message = message[..MaxMessageLength] + " [truncated]";
            }

            IReadOnlyDictionary<string, object>? details = null;
            if (errorElement.TryGetProperty("details", out var detailsEl) &&
                detailsEl.ValueKind == JsonValueKind.Object)
            {
                var rawDetails = detailsEl.GetRawText();
                if (rawDetails.Length <= MaxDetailsRawLength)
                {
                    details = ParseDetails(detailsEl);
                }
            }

            return MxcException.FromCode(code, message, details);
        }
        catch (JsonException)
        {
            // Not valid JSON — definitely not an error envelope.
            return null;
        }
    }

    /// <summary>
    /// Line-scanning error envelope parser for the PTY one-shot path.
    /// Port of sandbox.ts tryParseErrorEnvelopeFromLines (lines 720-737).
    /// Used only by P4 PTY spawn paths where stdout is interleaved with user output.
    /// </summary>
    internal static MxcException? TryParseErrorEnvelopeFromLines(string output)
    {
        const int MaxMessageLength = 8192;
        const int MaxDetailsRawLength = 64 * 1024;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('{')) continue;
            if (trimmed.Length > MaxDetailsRawLength * 2) continue;

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var errorElement) &&
                    errorElement.ValueKind == JsonValueKind.Object &&
                    errorElement.TryGetProperty("code", out var codeEl) &&
                    codeEl.ValueKind == JsonValueKind.String &&
                    errorElement.TryGetProperty("message", out var msgEl) &&
                    msgEl.ValueKind == JsonValueKind.String)
                {
                    var code = codeEl.GetString()!;
                    var message = msgEl.GetString()!;

                    if (message.Length > MaxMessageLength)
                    {
                        message = message[..MaxMessageLength] + " [truncated]";
                    }

                    IReadOnlyDictionary<string, object>? details = null;
                    if (errorElement.TryGetProperty("details", out var detailsEl) &&
                        detailsEl.ValueKind == JsonValueKind.Object)
                    {
                        var rawDetails = detailsEl.GetRawText();
                        if (rawDetails.Length <= MaxDetailsRawLength)
                        {
                            details = ParseDetails(detailsEl);
                        }
                    }

                    return MxcException.FromCode(code, message, details);
                }
            }
            catch (JsonException)
            {
                // Not valid JSON on this line, continue scanning.
            }
        }

        return null;
    }

    /// <summary>
    /// Fix #7: Preserves nested details as-is. Objects and arrays are kept as their
    /// raw JSON text (matching TS pass-through semantics) so structured diagnostics
    /// keep shape. Scalars are materialized to native types.
    /// </summary>
    private static IReadOnlyDictionary<string, object>? ParseDetails(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = MaterializeValue(prop.Value);
        }
        return dict.Count > 0 ? dict : null;
    }

    /// <summary>
    /// Recursively materializes a JsonElement to a .NET object.
    /// Objects → Dictionary&lt;string, object&gt;, Arrays → List&lt;object&gt;,
    /// Scalars → string/double/bool/null.
    /// </summary>
    private static object MaterializeValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Object => MaterializeObject(value),
            JsonValueKind.Array => MaterializeArray(value),
            _ => value.GetRawText()
        };
    }

    private static Dictionary<string, object> MaterializeObject(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = MaterializeValue(prop.Value);
        }
        return dict;
    }

    private static List<object> MaterializeArray(JsonElement element)
    {
        var list = new List<object>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(MaterializeValue(item));
        }
        return list;
    }

    /// <summary>
    /// Generates a random 8-character hex string for container IDs.
    /// Port of TS generateRandomContainerName().
    /// </summary>
    internal static string GenerateRandomContainerName()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
    }
}

/// <summary>
/// Result of preparing a spawn — resolved binary path and CLI arguments.
/// </summary>
internal sealed record PrepareSpawnResult(string ExecutablePath, IReadOnlyList<string> Args);
