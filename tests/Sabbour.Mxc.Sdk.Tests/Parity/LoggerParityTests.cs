// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using Sabbour.Mxc.Sdk.Diagnostics;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests.Parity;

public sealed class LoggerParityTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _logPath;

    public LoggerParityTests()
    {
        _tmpDir = Path.Combine(AppContext.BaseDirectory, "mxc-log-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _logPath = Path.Combine(_tmpDir, "test.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
    }

    [Fact]
    public void ShouldCreateLogFileAndWriteEntriesWithTimestamp()
    {
        var logger = new FileLogger(_logPath);
        logger.Log(MxcLogLevel.Info, "test message", new Dictionary<string, object> { ["key"] = "value" });
        logger.Close();

        var content = File.ReadAllText(_logPath);
        Assert.Contains("INFO", content);
        Assert.Contains("test message", content);
        Assert.Contains("\"key\":\"value\"", content);
        Assert.Matches(new Regex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}"), content);
    }

    [Fact]
    public void ShouldEmitWarningOnInvalidPathAndDegradeToNoOp()
    {
        using var capturedError = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(capturedError);
        try
        {
            var logger = new FileLogger(Path.Combine(_tmpDir, "\0invalid"));
            logger.Log(MxcLogLevel.Info, "this should not throw");
            logger.Close();

            Assert.Contains("Could not open log file", capturedError.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
