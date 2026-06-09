// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Sabbour.Mxc.Sdk.Platform;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests.Parity;

public sealed class ParityScaffoldSmokeTests
{
    [Fact]
    public async Task PortedHelpers_CompileAndRun()
    {
        var options = ParityTestHelpers.TestOptions();
        Assert.True(options.Experimental);
        Assert.True(File.Exists(options.ExecutablePath));

        var config = ParityTestHelpers.TestConfig("echo smoke");
        Assert.Equal("echo smoke", config.Process!.CommandLine);

        var fakeSpawn = ParityTestHelpers.FakeSpawn(new FakeChildOptions
        {
            Stdout = "child stdout",
            Stderr = "child stderr",
            ExitCode = 3,
        });

        var childResult = await fakeSpawn.SpawnAndCollectAsync("""{"phase":"exec"}""", options);
        Assert.Equal(3, childResult.ExitCode);
        Assert.Equal("child stdout", childResult.Stdout);
        Assert.Equal("exec", fakeSpawn.LastCapture!.Envelope!.Value.GetProperty("phase").GetString());

        var pty = new FakePtyConnection(new FakeChildOptions { Stdout = "pty output", ExitCode = 7 });
        var ptyOutput = "";
        pty.DataReceived += chunk => ptyOutput += Encoding.UTF8.GetString(chunk.Span);
        var ptyExit = await pty.WaitForExitAsync();
        Assert.Equal(7, ptyExit.ExitCode);
        Assert.Equal("pty output", ptyOutput);

        var probeRunner = new FakePlatformProbeRunner()
            .WithDismFeatureState("Enabled")
            .WithProbeError(new InvalidOperationException("probe unavailable"));
        var prober = new PlatformProber(
            probeRunner,
            new FakeWindowsBuildQuery((26300, 8553)),
            platform: ProbedPlatform.Windows);

        Assert.True(prober.IsIsoSessionSupported());
        Assert.True(prober.IsWindowsSandboxAvailable());
    }
}
