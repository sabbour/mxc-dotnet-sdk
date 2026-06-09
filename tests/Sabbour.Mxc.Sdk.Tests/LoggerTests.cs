// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Sabbour.Mxc.Sdk.Diagnostics;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests;

public class FileLoggerTests
{
    [Fact]
    public void Log_WritesTimestampedLine()
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"mxc-test-{Guid.NewGuid()}.log");
        try
        {
            using var logger = new FileLogger(logFile);
            logger.Log(MxcLogLevel.Info, "test message");
            logger.Close();

            var content = File.ReadAllText(logFile);
            Assert.Contains("INFO", content);
            Assert.Contains("test message", content);
            Assert.Matches(@"\[\d{4}-\d{2}-\d{2}T", content);
        }
        finally
        {
            if (File.Exists(logFile)) File.Delete(logFile);
        }
    }

    [Fact]
    public void Log_WithData_SerializesJson()
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"mxc-test-{Guid.NewGuid()}.log");
        try
        {
            using var logger = new FileLogger(logFile);
            logger.Log(MxcLogLevel.Warn, "warning", new Dictionary<string, object> { ["key"] = "value" });
            logger.Close();

            var content = File.ReadAllText(logFile);
            Assert.Contains("WARN", content);
            Assert.Contains("warning", content);
            Assert.Contains("\"key\"", content);
        }
        finally
        {
            if (File.Exists(logFile)) File.Delete(logFile);
        }
    }

    [Fact]
    public void Log_ErrorLevel_WritesERROR()
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"mxc-test-{Guid.NewGuid()}.log");
        try
        {
            using var logger = new FileLogger(logFile);
            logger.Log(MxcLogLevel.Error, "bad thing");
            logger.Close();

            var content = File.ReadAllText(logFile);
            Assert.Contains("ERROR", content);
            Assert.Contains("bad thing", content);
        }
        finally
        {
            if (File.Exists(logFile)) File.Delete(logFile);
        }
    }

    [Fact]
    public void Close_CalledMultipleTimes_DoesNotThrow()
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"mxc-test-{Guid.NewGuid()}.log");
        try
        {
            var logger = new FileLogger(logFile);
            logger.Close();
            logger.Close(); // second call should be safe
        }
        finally
        {
            if (File.Exists(logFile)) File.Delete(logFile);
        }
    }

    [Fact]
    public void Constructor_InvalidPath_DegradesToNoOp()
    {
        // Use an invalid path that cannot be opened
        using var logger = new FileLogger("Z:\\nonexistent\\path\\to\\impossible\\log.txt");
        // Should not throw
        logger.Log(MxcLogLevel.Info, "this is silently dropped");
    }

    [Fact]
    public void Log_AfterClose_DoesNotThrow()
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"mxc-test-{Guid.NewGuid()}.log");
        try
        {
            var logger = new FileLogger(logFile);
            logger.Close();
            logger.Log(MxcLogLevel.Info, "should not throw");
        }
        finally
        {
            if (File.Exists(logFile)) File.Delete(logFile);
        }
    }

    [Fact]
    public void Constructor_CreatesDirectoryIfNeeded()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mxc-test-dir-{Guid.NewGuid()}");
        var logFile = Path.Combine(dir, "test.log");
        try
        {
            using var logger = new FileLogger(logFile);
            logger.Log(MxcLogLevel.Info, "directory created");
            logger.Close();

            Assert.True(Directory.Exists(dir));
            Assert.True(File.Exists(logFile));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
