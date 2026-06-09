// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Sabbour.Mxc.Sdk;
using Sabbour.Mxc.Sdk.Diagnostics;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests;

/// <summary>
/// Tests proving each P1 post-build review fix (issues 1–6).
/// </summary>
public class P1FixTests
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    #region Issue 2: ProxyConfig one-of

    [Fact]
    public void ProxyConfig_BuiltinTestServer_RoundTrip()
    {
        var proxy = ProxyConfig.BuiltinTestServer();
        var json = JsonSerializer.Serialize(proxy, s_options);
        Assert.Equal("{\"builtinTestServer\":true}", json);

        var deserialized = JsonSerializer.Deserialize<ProxyConfig>(json, s_options);
        Assert.IsType<ProxyConfig.BuiltinTestServerProxy>(deserialized);
    }

    [Fact]
    public void ProxyConfig_Localhost_RoundTrip()
    {
        var proxy = ProxyConfig.Localhost(9090);
        var json = JsonSerializer.Serialize(proxy, s_options);
        Assert.Equal("{\"localhost\":9090}", json);

        var deserialized = JsonSerializer.Deserialize<ProxyConfig>(json, s_options);
        var lp = Assert.IsType<ProxyConfig.LocalhostProxy>(deserialized);
        Assert.Equal(9090, lp.Port);
    }

    [Fact]
    public void ProxyConfig_Url_RoundTrip()
    {
        var proxy = ProxyConfig.Url("http://myproxy:3128");
        var json = JsonSerializer.Serialize(proxy, s_options);
        Assert.Equal("{\"url\":\"http://myproxy:3128\"}", json);

        var deserialized = JsonSerializer.Deserialize<ProxyConfig>(json, s_options);
        var up = Assert.IsType<ProxyConfig.UrlProxy>(deserialized);
        Assert.Equal("http://myproxy:3128", up.ProxyUrl);
    }

    [Fact]
    public void ProxyConfig_EmptyObject_ThrowsOnDeserialization()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProxyConfig>("{}", s_options));
    }

    #endregion

    #region Issue 3: ContainmentValue validation

    [Theory]
    [InlineData("process")]
    [InlineData("vm")]
    [InlineData("microvm")]
    public void ContainmentValue_ValidType_Accepted(string value)
    {
        var cv = ContainmentValue.FromString(value);
        Assert.Equal(value, cv.Value);
    }

    [Theory]
    [InlineData("processcontainer")]
    [InlineData("windows_sandbox")]
    [InlineData("wslc")]
    [InlineData("lxc")]
    [InlineData("hyperlight")]
    [InlineData("seatbelt")]
    [InlineData("bubblewrap")]
    public void ContainmentValue_ValidBackend_Accepted(string value)
    {
        var cv = ContainmentValue.FromString(value);
        Assert.Equal(value, cv.Value);
    }

    [Theory]
    [InlineData("appcontainer")]
    [InlineData("macos_sandbox")]
    public void ContainmentValue_LegacyAlias_Accepted(string value)
    {
        var cv = ContainmentValue.FromString(value);
        Assert.Equal(value, cv.Value);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("docker")]
    [InlineData("")]
    public void ContainmentValue_InvalidValue_Rejected(string value)
    {
        Assert.Throws<ArgumentException>(() => ContainmentValue.FromString(value));
    }

    [Fact]
    public void ContainmentValue_SerializesAsPlainString()
    {
        var cv = ContainmentValue.FromString("microvm");
        var json = JsonSerializer.Serialize(cv);
        Assert.Equal("\"microvm\"", json);
    }

    [Fact]
    public void ContainmentValue_Deserializes()
    {
        var cv = JsonSerializer.Deserialize<ContainmentValue>("\"lxc\"");
        Assert.Equal("lxc", cv.Value);
    }

    #endregion

    #region Issue 4: ExperimentalBackends.RequiresExperimental

    [Theory]
    [InlineData("microvm", true)]       // Both ContainmentType and Backend
    [InlineData("windows_sandbox", true)]
    [InlineData("hyperlight", true)]
    [InlineData("wslc", true)]
    [InlineData("seatbelt", true)]
    [InlineData("isolation_session", true)]
    [InlineData("macos_sandbox", true)] // Legacy alias → seatbelt (experimental)
    [InlineData("processcontainer", false)]
    [InlineData("process", false)]
    [InlineData("vm", false)]
    [InlineData("lxc", false)]
    [InlineData("bubblewrap", false)]
    public void RequiresExperimental_CorrectlyGates(string containment, bool expected)
    {
        Assert.Equal(expected, ExperimentalBackends.RequiresExperimental(containment));
    }

    [Fact]
    public void RequiresExperimental_EmptyString_ReturnsFalse()
    {
        Assert.False(ExperimentalBackends.RequiresExperimental(""));
    }

    #endregion

    #region Issue 5 & 6: FileLogger — size cap, redaction, timestamp

    [Fact]
    public void FileLogger_TimestampFormat_MatchesTypeScript()
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"mxc-ts-ts-{Guid.NewGuid()}.log");
        try
        {
            using var logger = new FileLogger(logFile);
            logger.Log(MxcLogLevel.Info, "ts check");
            logger.Close();

            var content = File.ReadAllText(logFile);
            // TS: yyyy-MM-ddTHH:mm:ss.fffZ — exactly 3 fractional digits + Z
            Assert.Matches(@"\[\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z\]", content);
            // Must NOT contain 7 fractional digits (old "O" format)
            Assert.DoesNotMatch(@"\.\d{7}", content);
        }
        finally
        {
            if (File.Exists(logFile)) File.Delete(logFile);
        }
    }

    [Fact]
    public void FileLogger_SizeCap_TruncatesLongMessages()
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"mxc-cap-{Guid.NewGuid()}.log");
        try
        {
            using var logger = new FileLogger(logFile, maxMessageLength: 50);
            logger.Log(MxcLogLevel.Info, new string('x', 200));
            logger.Close();

            var content = File.ReadAllText(logFile);
            Assert.Contains("...[truncated]", content);
            // The full 200-char message should NOT be present
            Assert.DoesNotContain(new string('x', 200), content);
        }
        finally
        {
            if (File.Exists(logFile)) File.Delete(logFile);
        }
    }

    [Fact]
    public void FileLogger_RedactionHook_ScrubsSecrets()
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"mxc-redact-{Guid.NewGuid()}.log");
        try
        {
            Func<string, string> redact = line => line.Replace("SECRET_TOKEN", "[REDACTED]");
            using var logger = new FileLogger(logFile, redact: redact);
            logger.Log(MxcLogLevel.Info, "auth: SECRET_TOKEN");
            logger.Close();

            var content = File.ReadAllText(logFile);
            Assert.DoesNotContain("SECRET_TOKEN", content);
            Assert.Contains("[REDACTED]", content);
        }
        finally
        {
            if (File.Exists(logFile)) File.Delete(logFile);
        }
    }

    [Fact]
    public void FileLogger_InvalidPath_LogsOnlyMessageNotFullException()
    {
        // Build a path that cannot be opened on any platform: nest the log file under a
        // path segment that is an existing FILE, so directory creation fails (ENOTDIR / IOException).
        // A hardcoded Windows path like "Z:\..." is not portable — on Linux backslashes are valid
        // filename characters, so such a path would succeed instead of failing.
        var blockingFile = Path.Combine(Path.GetTempPath(), $"mxc-block-{Guid.NewGuid()}");
        File.WriteAllText(blockingFile, "x");
        var invalidPath = Path.Combine(blockingFile, "deep", "nested", "log.txt");

        // Capture stderr
        var oldErr = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            using var logger = new FileLogger(invalidPath);
            var output = sw.ToString();
            // Should contain only the filename
            Assert.Contains("log.txt", output);
            // Should NOT contain stack trace frames (full exception ToString() would include these)
            Assert.DoesNotContain("System.", output);
            Assert.DoesNotContain("  at ", output);
        }
        finally
        {
            Console.SetError(oldErr);
            if (File.Exists(blockingFile)) File.Delete(blockingFile);
        }
    }

    #endregion
}
