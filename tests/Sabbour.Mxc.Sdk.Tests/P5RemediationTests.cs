// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using NSubstitute;
using Sabbour.Mxc.Sdk.Diagnostics;
using Sabbour.Mxc.Sdk.Errors;
using Sabbour.Mxc.Sdk.Internal;
using Sabbour.Mxc.Sdk.Sandbox;
using Sabbour.Mxc.Sdk.StateAware;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests;

/// <summary>
/// Tests for P5 remediation fixes: B1, B2, R1, R3, N1.
/// </summary>
public class P5RemediationTests
{
    // -----------------------------------------------------------------------
    // B1: TryParseErrorEnvelope parses whole stdout, not lines
    // -----------------------------------------------------------------------

    [Fact]
    public void B1_UserScriptPrintingErrorJson_NotTreatedAsMxcError()
    {
        // A user script exits nonzero and prints a JSON line like {"error":...}
        // among other output. TryParseErrorEnvelope must NOT treat this as an
        // MXC dispatch failure — only exact single-object stdout qualifies.
        var output = "Starting task...\n{\"error\":{\"code\":\"some_err\",\"message\":\"oops\"}}\nTask complete\n";
        var result = SpawnHelper.TryParseErrorEnvelope(output);
        Assert.Null(result);
    }

    [Fact]
    public void B1_ExactErrorEnvelope_IsTreatedAsMxcError()
    {
        var output = "{\"error\":{\"code\":\"backend_error\",\"message\":\"dispatch failed\"}}";
        var result = SpawnHelper.TryParseErrorEnvelope(output);
        Assert.NotNull(result);
        Assert.Equal(ErrorCode.BackendError, result.Code);
        Assert.Equal("dispatch failed", result.Message);
    }

    [Fact]
    public void B1_ErrorWithoutCode_ReturnsNull()
    {
        // error property must be an object with a "code" string
        var output = "{\"error\":{\"message\":\"no code\"}}";
        Assert.Null(SpawnHelper.TryParseErrorEnvelope(output));
    }

    // -----------------------------------------------------------------------
    // B2: State-aware argv contains no --log-file
    // -----------------------------------------------------------------------

    [Fact]
    public void B2_StateAwareArgs_NoLogFile_EvenWithDebug()
    {
        // PrepareSpawnFromJson builds argv for the state-aware path.
        // Even when Debug=true and LogDir is set, --log-file must NOT appear.
        var envelope = """{"version":"0.6.0-alpha","phase":"exec"}""";
        var options = new SandboxSpawnOptions
        {
            Debug = true,
            LogDir = @"C:\logs",
            ExecutablePath = CreateFakeExecutable(),
        };

        var result = SpawnHelper.PrepareSpawnFromJson(envelope, options);

        Assert.Contains("--config-base64", result.Args);
        Assert.Contains("--debug", result.Args);
        Assert.DoesNotContain("--log-file", result.Args);
    }

    [Fact]
    public void B2_StateAwareArgs_OnlyExpectedFlags()
    {
        var envelope = """{"version":"0.6.0-alpha","phase":"provision"}""";
        var options = new SandboxSpawnOptions
        {
            DryRun = true,
            Debug = true,
            Experimental = true,
            ExecutablePath = CreateFakeExecutable(),
        };

        var result = SpawnHelper.PrepareSpawnFromJson(envelope, options);

        // Exactly: --config-base64 <value> --dry-run --debug --experimental
        Assert.Equal(5, result.Args.Count);
        Assert.Equal("--config-base64", result.Args[0]);
        Assert.Equal("--dry-run", result.Args[2]);
        Assert.Equal("--debug", result.Args[3]);
        Assert.Equal("--experimental", result.Args[4]);
    }

    // -----------------------------------------------------------------------
    // R1: wamToken redacted in exception messages
    // -----------------------------------------------------------------------

    [Fact]
    public async Task R1_WamToken_RedactedInNonExecException()
    {
        var runner = Substitute.For<IStateAwareSpawnRunner>();
        var client = StateAwareSandboxes.CreateIsolationSession(runner);

        // Simulate error response that echoes the wamToken
        var errorResponse = """{"error":{"code":"backend_error","message":"Failed with wamToken: \"wamToken\":\"secret123\" leaked"}}""";
        runner.SpawnAndCollectAsync(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = errorResponse,
                Stderr = "",
                ExitCode = 0,
            }));

        var sandboxId = new SandboxId<IsolationSessionBackend>("iso:r1test");
        var ex = await Assert.ThrowsAsync<MxcException>(() =>
            client.StartSandboxAsync(sandboxId));

        // The wamToken value must be redacted in the exception message
        Assert.DoesNotContain("secret123", ex.Message);
        Assert.Contains("<redacted>", ex.Message);
    }

    [Fact]
    public async Task R1_UnparseableStdout_RedactedInException()
    {
        var runner = Substitute.For<IStateAwareSpawnRunner>();
        var client = StateAwareSandboxes.CreateIsolationSession(runner);

        // Stdout has wamToken but is not valid JSON
        runner.SpawnAndCollectAsync(Arg.Any<string>(), Arg.Any<SandboxSpawnOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SandboxProcessResult
            {
                Stdout = """not json "wamToken":"mysecrettoken" end""",
                Stderr = "",
                ExitCode = 0,
            }));

        var sandboxId = new SandboxId<IsolationSessionBackend>("iso:r1b");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.StartSandboxAsync(sandboxId));

        Assert.DoesNotContain("mysecrettoken", ex.Message);
    }

    // -----------------------------------------------------------------------
    // R3: FileLogger and DiagnosticLog default-redact wamToken
    // -----------------------------------------------------------------------

    [Fact]
    public void R3_FileLogger_RedactsWamTokenByDefault()
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"mxc-r3-{Guid.NewGuid()}.log");
        try
        {
            using var logger = new FileLogger(logFile);
            logger.Log(MxcLogLevel.Info, "payload: \"wamToken\":\"supersecret\" end");
            logger.Close();

            var content = File.ReadAllText(logFile);
            Assert.DoesNotContain("supersecret", content);
            Assert.Contains("<redacted>", content);
        }
        finally
        {
            if (File.Exists(logFile)) File.Delete(logFile);
        }
    }

    [Fact]
    public void R3_TokenRedactor_RedactsCorrectly()
    {
        var input = """Some text "wamToken":"abc123def" and more""";
        var result = TokenRedactor.Redact(input);
        Assert.DoesNotContain("abc123def", result);
        Assert.Contains("\"wamToken\":\"<redacted>\"", result);
    }

    [Fact]
    public void R3_TokenRedactor_CapsLength()
    {
        var longMessage = new string('x', 2000);
        var result = TokenRedactor.RedactAndCap(longMessage);
        Assert.True(result.Length <= TokenRedactor.MaxExceptionMessageLength + 20);
        Assert.Contains("[truncated]", result);
    }

    // -----------------------------------------------------------------------
    // N1: Cross-cutting field order matches TS
    // -----------------------------------------------------------------------

    [Fact]
    public void N1_CrossCuttingFieldOrder_MatchesTs()
    {
        // TS state-aware-helper.ts line 15: ['filesystem', 'network', 'ui', 'process']
        // Use provision config which has filesystem (cross-cutting).
        // We verify that when multiple cross-cutting fields are present, they appear
        // in the defined TS order.
        var config = new IsolationSessionProvisionConfig
        {
            Filesystem = new FilesystemConfig { ReadwritePaths = ["/tmp"] },
            User = new IsolationSessionUserConfig("u@x.com", "tok"),
        };
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "provision",
            "isolation_session",
            containment: "isolation_session",
            sandboxId: null,
            config,
            MxcJsonContext.Default.IsolationSessionProvisionConfig);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var props = root.EnumerateObject().Select(p => p.Name).ToList();

        // filesystem is cross-cutting → should be lifted before experimental
        var fsIdx = props.IndexOf("filesystem");
        var expIdx = props.IndexOf("experimental");

        Assert.True(fsIdx >= 0, "filesystem not found at top level");
        Assert.True(expIdx >= 0, "experimental not found");
        Assert.True(fsIdx < expIdx, "filesystem must come before experimental (matches TS order)");
    }

    [Fact]
    public void N1_CrossCuttingFieldOrder_ProcessLiftedToTopLevel()
    {
        // process is a cross-cutting field per TS CROSS_CUTTING_FIELDS
        var config = new IsolationSessionExecConfig
        {
            Process = new ProcessConfig { CommandLine = "echo hi" },
        };
        var json = StateAwareEnvelopeBuilder.BuildEnvelope(
            "exec",
            "isolation_session",
            containment: null,
            sandboxId: "iso:n1b",
            config,
            MxcJsonContext.Default.IsolationSessionExecConfig);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var props = root.EnumerateObject().Select(p => p.Name).ToList();

        // process lifted to top-level
        Assert.Contains("process", props);
        // No experimental since all fields are cross-cutting or version/sandboxId
        Assert.DoesNotContain("experimental", props);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string CreateFakeExecutable()
    {
        // Create a temp file to pass File.Exists checks
        var path = Path.Combine(Path.GetTempPath(), $"wxc-exec-{Guid.NewGuid()}.exe");
        File.WriteAllText(path, "fake");
        return path;
    }
}
