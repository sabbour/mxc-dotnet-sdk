// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sabbour.Mxc.Sdk.Platform;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests;

/// <summary>
/// Tests for <see cref="PlatformProber"/> with injected fakes.
/// No real external binaries required — all seams are injected.
/// Platform is injected via the <see cref="ProbedPlatform"/> parameter,
/// so all tests run on any host OS.
/// </summary>
public class PlatformProbeTests
{
    #region Windows: Build Gating (IsolationSession)

    [Fact]
    public void Windows_IsoSession_SupportedOnBuild26300_8553()
    {
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns((26300, 8553));

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        Assert.True(prober.IsIsoSessionSupported());
    }

    [Fact]
    public void Windows_IsoSession_SupportedOnBuild26300_9000()
    {
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns((26300, 9000));

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        Assert.True(prober.IsIsoSessionSupported());
    }

    [Fact]
    public void Windows_IsoSession_NotSupportedOnBuild26300_8552()
    {
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns((26300, 8552));

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        Assert.False(prober.IsIsoSessionSupported());
    }

    [Fact]
    public void Windows_IsoSession_NotSupportedOnDifferentMajorBuild()
    {
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns((22631, 9000));

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        Assert.False(prober.IsIsoSessionSupported());
    }

    [Fact]
    public void Windows_IsoSession_NotSupportedWhenBuildNull()
    {
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        Assert.False(prober.IsIsoSessionSupported());
    }

    #endregion

    #region Bug 1: DISM false positive — checks stdout for State:Enabled

    [Fact]
    public void Windows_WindowsSandbox_DismStateEnabled_ReturnsTrue()
    {
        // DISM exits 0 with "State : Enabled" in stdout → sandbox available
        var probeRunner = CreateWindowsProbeRunner(
            dismStdout: "Feature Name : Containers-DisposableClientVM\r\nState : Enabled\r\n",
            sandboxExeExists: false);
        probeRunner.RunProbe().Throws(new InvalidOperationException("wxc-exec not found"));

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        var support = prober.GetPlatformSupport();

        Assert.Contains(ContainmentBackend.WindowsSandbox, support.AvailableMethods);
    }

    [Fact]
    public void Windows_WindowsSandbox_DismStateDisabled_ReturnsFalse()
    {
        // DISM exits 0 with "State : Disabled" → sandbox NOT available (no exe fallback either)
        var probeRunner = CreateWindowsProbeRunner(
            dismStdout: "Feature Name : Containers-DisposableClientVM\r\nState : Disabled\r\n",
            sandboxExeExists: false);
        probeRunner.RunProbe().Throws(new InvalidOperationException("wxc-exec not found"));

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        var support = prober.GetPlatformSupport();

        Assert.DoesNotContain(ContainmentBackend.WindowsSandbox, support.AvailableMethods);
    }

    [Fact]
    public void Windows_WindowsSandbox_DismFailure_FallsBackToExe()
    {
        // DISM throws (e.g., not elevated) → falls back to exe existence
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.RunCommand("dism", Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>())
            .Throws(new TimeoutException("dism timed out"));
        probeRunner.FileExists(Arg.Is<string>(p => p.Contains("WindowsSandbox.exe"))).Returns(true);
        probeRunner.RunProbe().Throws(new InvalidOperationException("wxc-exec not found"));

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        var support = prober.GetPlatformSupport();

        Assert.Contains(ContainmentBackend.WindowsSandbox, support.AvailableMethods);
    }

    #endregion

    #region Bug 2: UBR hex parsing

    [Fact]
    public void DefaultWindowsBuildQuery_ParsesHexUBR_0x2169()
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.QueryRegistry(@"HKLM\Software\Microsoft\Windows NT\CurrentVersion", "CurrentBuild")
            .Returns("26300");
        probeRunner.QueryRegistry(@"HKLM\Software\Microsoft\Windows NT\CurrentVersion", "UBR")
            .Returns("0x2169");

        var query = new DefaultWindowsBuildQuery(probeRunner);
        var result = query.GetWindowsBuild();

        Assert.NotNull(result);
        Assert.Equal(26300, result!.Value.Major);
        Assert.Equal(0x2169, result.Value.Minor); // 8553 decimal
    }

    [Fact]
    public void DefaultWindowsBuildQuery_ParsesDecimalUBR()
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.QueryRegistry(@"HKLM\Software\Microsoft\Windows NT\CurrentVersion", "CurrentBuild")
            .Returns("26300");
        probeRunner.QueryRegistry(@"HKLM\Software\Microsoft\Windows NT\CurrentVersion", "UBR")
            .Returns("8553");

        var query = new DefaultWindowsBuildQuery(probeRunner);
        var result = query.GetWindowsBuild();

        Assert.NotNull(result);
        Assert.Equal(26300, result!.Value.Major);
        Assert.Equal(8553, result.Value.Minor);
    }

    [Fact]
    public void DefaultWindowsBuildQuery_HexUBR_GatedBuildPasses()
    {
        // Hex 0x2169 == 8553 decimal, which is the IsolationSession threshold
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        probeRunner.RunProbe().Throws(new InvalidOperationException("wxc-exec not found"));

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        // Simulate what DefaultWindowsBuildQuery would return after hex parse fix
        buildQuery.GetWindowsBuild().Returns((26300, 0x2169)); // 8553

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        Assert.True(prober.IsIsoSessionSupported());
    }

    [Fact]
    public void TryParseIntOrHex_ParsesHexValues()
    {
        Assert.True(DefaultWindowsBuildQuery.TryParseIntOrHex("0x2169", out var result));
        Assert.Equal(8553, result);

        Assert.True(DefaultWindowsBuildQuery.TryParseIntOrHex("0X2169", out result));
        Assert.Equal(8553, result);

        Assert.True(DefaultWindowsBuildQuery.TryParseIntOrHex("0xFF", out result));
        Assert.Equal(255, result);
    }

    [Fact]
    public void TryParseIntOrHex_ParsesDecimalValues()
    {
        Assert.True(DefaultWindowsBuildQuery.TryParseIntOrHex("8553", out var result));
        Assert.Equal(8553, result);

        Assert.True(DefaultWindowsBuildQuery.TryParseIntOrHex("0", out result));
        Assert.Equal(0, result);
    }

    [Fact]
    public void TryParseIntOrHex_FailsOnInvalid()
    {
        Assert.False(DefaultWindowsBuildQuery.TryParseIntOrHex("", out _));
        Assert.False(DefaultWindowsBuildQuery.TryParseIntOrHex("not-a-number", out _));
        Assert.False(DefaultWindowsBuildQuery.TryParseIntOrHex("0xZZZZ", out _));
    }

    #endregion

    #region Bug 3: wxc-exec nonzero exit treated as probe failure

    [Fact]
    public void Windows_ProbeNonzeroExit_IsolationFieldsNotPopulated()
    {
        // wxc-exec exits with code 1 and partial stdout — must NOT populate isolation fields
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        probeRunner.RunProbe().Throws(new InvalidOperationException(
            "wxc-exec --probe exited with code 1"));

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        var support = prober.GetPlatformSupport();

        Assert.True(support.IsSupported);
        Assert.Null(support.IsolationTier);
        Assert.Null(support.IsolationWarnings);
        Assert.Null(support.UiCapabilities);
    }

    #endregion

    #region Windows: Full Platform Support

    [Fact]
    public void Windows_ProcessContainerAlwaysAvailable()
    {
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        probeRunner.RunProbe().Throws(new InvalidOperationException("wxc-exec not found"));

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        var support = prober.GetPlatformSupport();

        Assert.True(support.IsSupported);
        Assert.Contains(ContainmentBackend.ProcessContainer, support.AvailableMethods);
    }

    [Fact]
    public void Windows_WindowsSandbox_AvailableViaExeFallback()
    {
        // DISM fails → falls back to exe check
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.RunCommand("dism", Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>())
            .Throws(new InvalidOperationException("dism not found"));
        probeRunner.FileExists(Arg.Is<string>(p => p.Contains("WindowsSandbox.exe"))).Returns(true);
        probeRunner.RunProbe().Throws(new InvalidOperationException("wxc-exec not found"));

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        var support = prober.GetPlatformSupport();

        Assert.Contains(ContainmentBackend.WindowsSandbox, support.AvailableMethods);
    }

    [Fact]
    public void Windows_ProbePopulatesIsolationTierAndUiCapabilities()
    {
        var probeJson = """
        {
          "tier": "appcontainer-dacl",
          "warnings": ["Reduced isolation: some DACLs unavailable"],
          "probes": {
            "uiCapabilities": {
              "canBlockClipboardRead": true,
              "canBlockClipboardWrite": true,
              "canBlockInputInjection": false,
              "canBlockInputMethodChanges": true,
              "canBlockExternalUiObjects": true,
              "canBlockGlobalUiNamespace": false,
              "canBlockDesktopSwitching": true,
              "canBlockLogoffOrShutdown": true,
              "canBlockSystemParameterChanges": false,
              "canBlockDisplaySettingsChanges": true
            }
          }
        }
        """;

        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        probeRunner.RunProbe().Returns(probeJson);

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        var support = prober.GetPlatformSupport();

        Assert.Equal(IsolationTier.AppContainerDacl, support.IsolationTier);
        Assert.NotNull(support.IsolationWarnings);
        Assert.Single(support.IsolationWarnings!);
        Assert.Contains("Reduced isolation", support.IsolationWarnings![0]);

        Assert.NotNull(support.UiCapabilities);
        Assert.True(support.UiCapabilities!.CanBlockClipboardRead);
        Assert.True(support.UiCapabilities.CanBlockClipboardWrite);
        Assert.False(support.UiCapabilities.CanBlockInputInjection);
        Assert.True(support.UiCapabilities.CanBlockDesktopSwitching);
        Assert.False(support.UiCapabilities.CanBlockGlobalUiNamespace);
    }

    #endregion

    #region Linux: Backend Availability

    [Fact]
    public void Linux_BothLxcAndBubblewrapAvailable()
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.IsToolAvailable("lxc-ls", "--version").Returns(true);
        probeRunner.IsToolAvailable("bwrap", "--version").Returns(true);

        var prober = new PlatformProber(probeRunner, platform: ProbedPlatform.Linux);
        var support = prober.GetPlatformSupport();

        Assert.True(support.IsSupported);
        Assert.Contains(ContainmentBackend.Lxc, support.AvailableMethods);
        Assert.Contains(ContainmentBackend.Bubblewrap, support.AvailableMethods);
    }

    [Fact]
    public void Linux_OnlyLxcAvailable()
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.IsToolAvailable("lxc-ls", "--version").Returns(true);
        probeRunner.IsToolAvailable("bwrap", "--version").Returns(false);

        var prober = new PlatformProber(probeRunner, platform: ProbedPlatform.Linux);
        var support = prober.GetPlatformSupport();

        Assert.True(support.IsSupported);
        Assert.Contains(ContainmentBackend.Lxc, support.AvailableMethods);
        Assert.DoesNotContain(ContainmentBackend.Bubblewrap, support.AvailableMethods);
    }

    [Fact]
    public void Linux_OnlyBubblewrapAvailable()
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.IsToolAvailable("lxc-ls", "--version").Returns(false);
        probeRunner.IsToolAvailable("bwrap", "--version").Returns(true);

        var prober = new PlatformProber(probeRunner, platform: ProbedPlatform.Linux);
        var support = prober.GetPlatformSupport();

        Assert.True(support.IsSupported);
        Assert.DoesNotContain(ContainmentBackend.Lxc, support.AvailableMethods);
        Assert.Contains(ContainmentBackend.Bubblewrap, support.AvailableMethods);
    }

    [Fact]
    public void Linux_NeitherAvailable_NotSupported()
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.IsToolAvailable("lxc-ls", "--version").Returns(false);
        probeRunner.IsToolAvailable("bwrap", "--version").Returns(false);

        var prober = new PlatformProber(probeRunner, platform: ProbedPlatform.Linux);
        var support = prober.GetPlatformSupport();

        Assert.False(support.IsSupported);
        Assert.Equal("Neither LXC nor Bubblewrap is available on this system", support.Reason);
    }

    #endregion

    #region macOS: Seatbelt Availability

    [Fact]
    public void MacOS_SeatbeltAvailable()
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.FileExists("/usr/bin/sandbox-exec").Returns(true);

        var prober = new PlatformProber(probeRunner, platform: ProbedPlatform.Darwin);
        var support = prober.GetPlatformSupport();

        Assert.True(support.IsSupported);
        Assert.Contains(ContainmentBackend.Seatbelt, support.AvailableMethods);
    }

    [Fact]
    public void MacOS_SeatbeltMissing_NotSupported()
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.FileExists("/usr/bin/sandbox-exec").Returns(false);

        var prober = new PlatformProber(probeRunner, platform: ProbedPlatform.Darwin);
        var support = prober.GetPlatformSupport();

        Assert.False(support.IsSupported);
        Assert.Contains("/usr/bin/sandbox-exec not found", support.Reason);
    }

    #endregion

    #region Probe Failure -> Capability False

    [Fact]
    public void Windows_ProbeFailure_GracefulDegradation()
    {
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        probeRunner.RunProbe().Throws(new InvalidOperationException("wxc-exec not found"));

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        var support = prober.GetPlatformSupport();

        Assert.True(support.IsSupported);
        Assert.Null(support.IsolationTier);
        Assert.Null(support.IsolationWarnings);
        Assert.Null(support.UiCapabilities);
    }

    [Fact]
    public void Windows_ProbeMalformedJson_GracefulDegradation()
    {
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        probeRunner.RunProbe().Returns("not valid json {{{");

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        var support = prober.GetPlatformSupport();

        Assert.True(support.IsSupported);
        Assert.Null(support.IsolationTier);
    }

    [Fact]
    public void Windows_ProbeUnknownTier_Ignored()
    {
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        probeRunner.RunProbe().Returns("""{"tier": "unknown-tier", "warnings": []}""");

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        var support = prober.GetPlatformSupport();

        Assert.Null(support.IsolationTier);
    }

    #endregion

    #region Bug 8: Thread-safe single-flight cache

    [Fact]
    public void Cache_ProbeRunsOnce_SecondCallReturnsCached()
    {
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        probeRunner.RunProbe().Throws(new InvalidOperationException("wxc-exec not found"));

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);

        var first = prober.GetPlatformSupport();
        var second = prober.GetPlatformSupport();

        Assert.Same(first, second);
        probeRunner.Received(1).RunProbe();
    }

    [Fact]
    public void Cache_ResetForceRecompute()
    {
        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        probeRunner.RunProbe().Throws(new InvalidOperationException("wxc-exec not found"));

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);

        var first = prober.GetPlatformSupport();
        prober.Cache.Reset();
        var second = prober.GetPlatformSupport();

        probeRunner.Received(2).RunProbe();
        Assert.NotSame(first, second);
        Assert.Equal(first.IsSupported, second.IsSupported);
    }

    [Fact]
    public async Task Cache_ConcurrentCallers_RunProbeOnce()
    {
        var callCount = 0;
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.RunCommand("dism", Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>())
            .Throws(new InvalidOperationException("dism not found"));
        probeRunner.FileExists(Arg.Any<string>()).Returns(false);
        probeRunner.RunProbe().Returns(_ =>
        {
            Interlocked.Increment(ref callCount);
            Thread.Sleep(50); // simulate work
            return """{"tier": "base-container", "warnings": [], "probes": {}}""";
        });

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);

        // Launch concurrent callers
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => prober.GetPlatformSupport()))
            .ToArray();
        await Task.WhenAll(tasks);

        // Single-flight: probe should run exactly once
        Assert.Equal(1, callCount);
        var results = tasks.Select(t => t.Result).ToArray();
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public void Cache_AfterReset_ConcurrentCallersRunAgain()
    {
        var callCount = 0;
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.RunCommand("dism", Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>())
            .Throws(new InvalidOperationException("dism not found"));
        probeRunner.FileExists(Arg.Any<string>()).Returns(false);
        probeRunner.RunProbe().Returns(_ =>
        {
            Interlocked.Increment(ref callCount);
            return """{"tier": "base-container", "warnings": [], "probes": {}}""";
        });

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);

        _ = prober.GetPlatformSupport();
        Assert.Equal(1, callCount);

        prober.Cache.Reset();
        _ = prober.GetPlatformSupport();
        Assert.Equal(2, callCount);
    }

    #endregion

    #region DefaultWindowsBuildQuery (existing + extended)

    [Fact]
    public void DefaultWindowsBuildQuery_ParsesValidRegistryValues()
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.QueryRegistry(@"HKLM\Software\Microsoft\Windows NT\CurrentVersion", "CurrentBuild")
            .Returns("26300");
        probeRunner.QueryRegistry(@"HKLM\Software\Microsoft\Windows NT\CurrentVersion", "UBR")
            .Returns("8553");

        var query = new DefaultWindowsBuildQuery(probeRunner);
        var result = query.GetWindowsBuild();

        Assert.NotNull(result);
        Assert.Equal(26300, result!.Value.Major);
        Assert.Equal(8553, result.Value.Minor);
    }

    [Fact]
    public void DefaultWindowsBuildQuery_ReturnsNull_WhenRegistryEmpty()
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.QueryRegistry(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        var query = new DefaultWindowsBuildQuery(probeRunner);
        var result = query.GetWindowsBuild();

        Assert.Null(result);
    }

    [Fact]
    public void DefaultWindowsBuildQuery_ReturnsNull_WhenNonNumeric()
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        probeRunner.QueryRegistry(@"HKLM\Software\Microsoft\Windows NT\CurrentVersion", "CurrentBuild")
            .Returns("not-a-number");
        probeRunner.QueryRegistry(@"HKLM\Software\Microsoft\Windows NT\CurrentVersion", "UBR")
            .Returns("8553");

        var query = new DefaultWindowsBuildQuery(probeRunner);
        var result = query.GetWindowsBuild();

        Assert.Null(result);
    }

    #endregion

    #region Probe JSON Parsing

    [Fact]
    public void Windows_ProbeWithBaseContainerTier()
    {
        var probeJson = """{"tier": "base-container", "warnings": [], "probes": {}}""";

        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        probeRunner.RunProbe().Returns(probeJson);

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        var support = prober.GetPlatformSupport();

        Assert.Equal(IsolationTier.BaseContainer, support.IsolationTier);
        Assert.Null(support.IsolationWarnings);
    }

    [Fact]
    public void Windows_ProbeWithIncompleteUiCapabilities_OmitsUi()
    {
        var probeJson = """
        {
          "tier": "appcontainer-bfs",
          "probes": {
            "uiCapabilities": {
              "canBlockClipboardRead": true,
              "canBlockClipboardWrite": true
            }
          }
        }
        """;

        var probeRunner = CreateWindowsProbeRunner(dismStdout: null, sandboxExeExists: false);
        probeRunner.RunProbe().Returns(probeJson);

        var buildQuery = Substitute.For<IWindowsBuildQuery>();
        buildQuery.GetWindowsBuild().Returns(((int, int)?)null);

        var prober = new PlatformProber(probeRunner, buildQuery, platform: ProbedPlatform.Windows);
        var support = prober.GetPlatformSupport();

        Assert.Equal(IsolationTier.AppContainerBfs, support.IsolationTier);
        Assert.Null(support.UiCapabilities);
    }

    [Fact]
    public void UnknownPlatform_NotSupported()
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();
        var prober = new PlatformProber(probeRunner, platform: ProbedPlatform.Unknown);
        var support = prober.GetPlatformSupport();

        Assert.False(support.IsSupported);
        Assert.Equal("MXC is not supported on this platform", support.Reason);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a Windows probe runner fake. dismStdout=null means DISM throws (falls back to exe);
    /// non-null means DISM exits 0 with the given stdout.
    /// </summary>
    private static IPlatformProbeRunner CreateWindowsProbeRunner(string? dismStdout, bool sandboxExeExists)
    {
        var probeRunner = Substitute.For<IPlatformProbeRunner>();

        if (dismStdout is not null)
        {
            probeRunner.RunCommand("dism", Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>())
                .Returns(new ProcessResult(0, dismStdout, ""));
        }
        else
        {
            probeRunner.RunCommand("dism", Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>())
                .Throws(new InvalidOperationException("dism not available"));
        }

        probeRunner.FileExists(Arg.Is<string>(p => p.Contains("WindowsSandbox.exe")))
            .Returns(sandboxExeExists);
        return probeRunner;
    }

    #endregion
}
