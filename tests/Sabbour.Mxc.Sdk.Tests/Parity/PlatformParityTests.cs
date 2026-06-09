// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Sabbour.Mxc.Sdk.Platform;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests.Parity;

public sealed class PlatformParityTests
{
    [Fact]
    public void ProbeIntegration_ReturnsIsolationTierWhenProbeSucceeds()
    {
        var runner = new CountingProbeRunner
        {
            Probe = () => JsonSerializer.Serialize(new
            {
                tier = "appcontainer-bfs",
                needsDaclAugmentation = false,
                warnings = new[] { "BaseContainer API not present" },
                probes = new { baseContainerApiPresent = false, bfscfgPresent = true },
            }),
        };
        var prober = WindowsProber(runner);

        var support = prober.GetPlatformSupport();

        Assert.Equal(IsolationTier.AppContainerBfs, support.IsolationTier);
        Assert.Equal(["BaseContainer API not present"], support.IsolationWarnings);
        Assert.Equal(1, runner.ProbeCalls);
    }

    [Fact]
    public void ProbeIntegration_OmitsIsolationTierWhenProbeThrows()
    {
        var runner = new FakePlatformProbeRunner().WithProbeError(new InvalidOperationException("boom"));
        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Null(support.IsolationTier);
        Assert.Null(support.IsolationWarnings);
    }

    [Fact]
    public void ProbeIntegration_OmitsIsolationTierWhenProbeReturnsMalformedJson()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout("not json");
        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Null(support.IsolationTier);
        Assert.Null(support.IsolationWarnings);
    }

    [Fact]
    public void ProbeIntegration_RejectsUnknownTierStringsViaTypeNarrowing()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout(JsonSerializer.Serialize(new
        {
            tier = "future-tier",
            warnings = Array.Empty<string>(),
            probes = new { baseContainerApiPresent = true, bfscfgPresent = true },
        }));

        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Null(support.IsolationTier);
    }

    [Fact]
    public void ProbeIntegration_CachesThePlatformSupportResult()
    {
        var runner = new CountingProbeRunner
        {
            Probe = () => JsonSerializer.Serialize(new
            {
                tier = "appcontainer-bfs",
                warnings = Array.Empty<string>(),
                probes = new { baseContainerApiPresent = false, bfscfgPresent = true },
            }),
        };
        var prober = WindowsProber(runner);

        var a = prober.GetPlatformSupport();
        var b = prober.GetPlatformSupport();

        Assert.Same(a, b);
        Assert.Equal(1, runner.ProbeCalls);
    }

    [Fact]
    public void ProbeIntegration_StillReturnsBasePlatformSupportShapeOnNonWindows()
    {
        var support = new PlatformProber(new FakePlatformProbeRunner(), platform: ProbedPlatform.Linux).GetPlatformSupport();

        Assert.Null(support.IsolationTier);
        Assert.Null(support.IsolationWarnings);
        Assert.Null(support.UiCapabilities);
        Assert.NotNull(support.AvailableMethods);
    }

    [Fact]
    public void ProbeIntegration_HandlesProbeJsonWithOnlyTier()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout(JsonSerializer.Serialize(new { tier = "appcontainer-dacl" }));
        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Equal(IsolationTier.AppContainerDacl, support.IsolationTier);
        Assert.Null(support.IsolationWarnings);
    }

    [Fact]
    public void ProbeIntegration_HandlesProbeJsonWithOnlyWarnings()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout(JsonSerializer.Serialize(new { warnings = new[] { "msg-1", "msg-2" } }));
        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Null(support.IsolationTier);
        Assert.Equal(["msg-1", "msg-2"], support.IsolationWarnings);
    }

    [Fact]
    public void ProbeIntegration_HandlesEmptyProbeJsonObject()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout("{}");
        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Null(support.IsolationTier);
        Assert.Null(support.IsolationWarnings);
    }

    [Fact]
    public void ProbeIntegration_FiltersNonStringEntriesOutOfWarningsArray()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout("""
        {
          "tier": "appcontainer-bfs",
          "warnings": ["ok", 42, null, {"not":"a string"}, "ok2"]
        }
        """);

        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Equal(["ok", "ok2"], support.IsolationWarnings);
    }

    [Fact]
    public void ProbeIntegration_OmitsIsolationWarningsWhenFilteredWarningsArrayIsEmpty()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout("""
        {
          "tier": "appcontainer-bfs",
          "warnings": [42, null]
        }
        """);

        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Equal(IsolationTier.AppContainerBfs, support.IsolationTier);
        Assert.Null(support.IsolationWarnings);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"a string\"")]
    [InlineData("null")]
    public void ProbeIntegration_TreatsProbeJsonThatIsNonObjectAsUnparseable(string payload)
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout(payload);
        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Null(support.IsolationTier);
        Assert.Null(support.IsolationWarnings);
    }

    [Fact]
    public void ProbeIntegration_SurfacesPortableUiCapabilitiesFromProbes()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout(ProbeJson("appcontainer-dacl", AllUiCapabilities()));
        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Equal(AllUiCapabilitiesRecord(), support.UiCapabilities);
    }

    [Fact]
    public void ProbeIntegration_ReportsInputInjectionBlockingUnsupportedFromProbeCapabilities()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout(ProbeJson("appcontainer-dacl", AllUiCapabilities(canBlockInputInjection: false)));
        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.False(support.UiCapabilities?.CanBlockInputInjection);
        Assert.True(support.UiCapabilities?.CanBlockInputMethodChanges);
    }

    [Fact]
    public void ProbeIntegration_ReportsInputMethodAndInputInjectionBlockingUnsupportedFromProbeCapabilities()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout(ProbeJson(
            "appcontainer-dacl",
            AllUiCapabilities(canBlockInputInjection: false, canBlockInputMethodChanges: false)));
        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.False(support.UiCapabilities?.CanBlockInputInjection);
        Assert.False(support.UiCapabilities?.CanBlockInputMethodChanges);
        Assert.True(support.UiCapabilities?.CanBlockClipboardRead);
        Assert.True(support.UiCapabilities?.CanBlockDisplaySettingsChanges);
    }

    [Fact]
    public void ProbeIntegration_OmitsUiCapabilitiesWhenProbesBlockIsAbsent()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout(JsonSerializer.Serialize(new { tier = "appcontainer-dacl" }));
        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Null(support.UiCapabilities);
    }

    [Fact]
    public void ProbeIntegration_OmitsUiCapabilitiesWhenProbeOmitsThem()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout(JsonSerializer.Serialize(new
        {
            tier = "appcontainer-dacl",
            probes = new { baseContainerApiPresent = false, bfscfgPresent = false },
        }));
        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Null(support.UiCapabilities);
    }

    [Fact]
    public void ProbeIntegration_OmitsUiCapabilitiesWhenProbeReturnsAPartialCapabilityObject()
    {
        var runner = new FakePlatformProbeRunner().WithProbeStdout("""
        {
          "tier": "appcontainer-dacl",
          "probes": {
            "baseContainerApiPresent": false,
            "bfscfgPresent": false,
            "uiCapabilities": {
              "canBlockClipboardRead": true
            }
          }
        }
        """);
        var support = WindowsProber(runner).GetPlatformSupport();

        Assert.Null(support.UiCapabilities);
    }

    [Fact]
    public void FindWxcExecutable_ReturnsStringOrNullAndNeverThrowsUnderNonexistentMxcBinDir()
    {
        var previous = Environment.GetEnvironmentVariable("MXC_BIN_DIR");
        try
        {
            Environment.SetEnvironmentVariable(
                "MXC_BIN_DIR",
                Path.Combine(Directory.GetCurrentDirectory(), $"mxc-sdk-unit-no-such-dir-{Guid.NewGuid():N}"));

            var result = DefaultPlatformProbeRunner.FindWxcExecutable();

            Assert.True(result is null || result is string, $"got: {result}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MXC_BIN_DIR", previous);
        }
    }

    [Fact]
    public void FindWxcExecutable_ReturnsStringOrNullWhenMxcBinDirIsEmpty()
    {
        var previous = Environment.GetEnvironmentVariable("MXC_BIN_DIR");
        try
        {
            Environment.SetEnvironmentVariable("MXC_BIN_DIR", "");

            var result = DefaultPlatformProbeRunner.FindWxcExecutable();

            Assert.True(result is null || result is string);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MXC_BIN_DIR", previous);
        }
    }

    [Fact]
    public void IsolationSessionAvailabilityGate_OmitsIsolationSessionWhenMinorBuildIsBelow8553()
    {
        var support = WindowsProber(new FakePlatformProbeRunner(), new FakeWindowsBuildQuery((26300, 8552))).GetPlatformSupport();

        Assert.True(support.IsSupported);
        Assert.DoesNotContain(ContainmentBackend.IsolationSession, support.AvailableMethods);
    }

    [Fact]
    public void IsolationSessionAvailabilityGate_IncludesIsolationSessionWhenBuildIsExactly26300_8553()
    {
        var support = WindowsProber(new FakePlatformProbeRunner(), new FakeWindowsBuildQuery((26300, 8553))).GetPlatformSupport();

        Assert.Contains(ContainmentBackend.IsolationSession, support.AvailableMethods);
    }

    [Fact]
    public void IsolationSessionAvailabilityGate_IncludesIsolationSessionWhenMinorIsNewer26300_9999()
    {
        var support = WindowsProber(new FakePlatformProbeRunner(), new FakeWindowsBuildQuery((26300, 9999))).GetPlatformSupport();

        Assert.Contains(ContainmentBackend.IsolationSession, support.AvailableMethods);
    }

    [Fact]
    public void IsolationSessionAvailabilityGate_OmitsIsolationSessionWhenMajorIsNewerThan26300()
    {
        var support = WindowsProber(new FakePlatformProbeRunner(), new FakeWindowsBuildQuery((26400, 0))).GetPlatformSupport();

        Assert.DoesNotContain(ContainmentBackend.IsolationSession, support.AvailableMethods);
    }

    [Fact]
    public void IsolationSessionAvailabilityGate_OmitsIsolationSessionWhenRegistryQueryReturnsNull()
    {
        var support = WindowsProber(new FakePlatformProbeRunner(), new FakeWindowsBuildQuery(null)).GetPlatformSupport();

        Assert.DoesNotContain(ContainmentBackend.IsolationSession, support.AvailableMethods);
    }

    [Fact]
    public void IsolationSessionAvailabilityGate_AlwaysReportsProcessContainerAsDefaultOnWindows()
    {
        var support = WindowsProber(new FakePlatformProbeRunner(), new FakeWindowsBuildQuery((22000, 0))).GetPlatformSupport();

        Assert.True(support.IsSupported);
        Assert.Equal(ContainmentBackend.ProcessContainer, support.AvailableMethods[0]);
    }

    private static PlatformProber WindowsProber(IPlatformProbeRunner runner, IWindowsBuildQuery? buildQuery = null)
    {
        return new PlatformProber(runner, buildQuery, platform: ProbedPlatform.Windows);
    }

    private static string ProbeJson(string tier, object uiCapabilities)
    {
        return JsonSerializer.Serialize(new
        {
            tier,
            probes = new
            {
                baseContainerApiPresent = false,
                bfscfgPresent = false,
                uiCapabilities,
            },
        });
    }

    private static object AllUiCapabilities(
        bool canBlockClipboardRead = true,
        bool canBlockClipboardWrite = true,
        bool canBlockInputInjection = true,
        bool canBlockInputMethodChanges = true,
        bool canBlockExternalUiObjects = true,
        bool canBlockGlobalUiNamespace = true,
        bool canBlockDesktopSwitching = true,
        bool canBlockLogoffOrShutdown = true,
        bool canBlockSystemParameterChanges = true,
        bool canBlockDisplaySettingsChanges = true)
    {
        return new
        {
            canBlockClipboardRead,
            canBlockClipboardWrite,
            canBlockInputInjection,
            canBlockInputMethodChanges,
            canBlockExternalUiObjects,
            canBlockGlobalUiNamespace,
            canBlockDesktopSwitching,
            canBlockLogoffOrShutdown,
            canBlockSystemParameterChanges,
            canBlockDisplaySettingsChanges,
        };
    }

    private static UiCapabilitySupport AllUiCapabilitiesRecord() => new()
    {
        CanBlockClipboardRead = true,
        CanBlockClipboardWrite = true,
        CanBlockInputInjection = true,
        CanBlockInputMethodChanges = true,
        CanBlockExternalUiObjects = true,
        CanBlockGlobalUiNamespace = true,
        CanBlockDesktopSwitching = true,
        CanBlockLogoffOrShutdown = true,
        CanBlockSystemParameterChanges = true,
        CanBlockDisplaySettingsChanges = true,
    };

    private sealed class CountingProbeRunner : IPlatformProbeRunner
    {
        public Func<string> Probe { get; init; } = () => "{}";

        public int ProbeCalls { get; private set; }

        public string RunProbe()
        {
            ProbeCalls++;
            return Probe();
        }

        public ProcessResult RunCommand(string command, IReadOnlyList<string> arguments, int timeoutMs = 10000) => new(1, "", "");

        public bool IsToolAvailable(string command, string arguments) => false;

        public bool FileExists(string path) => false;

        public string? QueryRegistry(string key, string valueName) => null;
    }
}
