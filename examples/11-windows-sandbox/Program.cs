// IMPORTANT: This example demonstrates the windows_sandbox containment backend.
// 
// Prerequisites (Windows 11+ only):
// 1. Enable the Windows Sandbox feature: run in PowerShell as Administrator:
//      Enable-WindowsOptionalFeature -FeatureName Containers-DisposableClientVM -Online
//    then reboot.
// 2. Ensure hardware virtualization is enabled in firmware/BIOS.
// 3. Python must be installed on the host (required by the native executor for windows_sandbox).
//
// *** MUST RUN ELEVATED ***
// The native executor calls 'dism /online /get-featureinfo' to verify the Windows Sandbox
// feature, which requires Administrator privileges. If you run this example non-elevated,
// you will see "Error: 740" or a misleading "Windows Sandbox is not enabled" message EVEN
// if the feature IS enabled. To avoid confusion, always run this example as Administrator.
//
// Performance notes:
// - First VM boot: ~30 seconds
// - Subsequent calls reuse a warm daemon VM (much faster)
//
// This example was verified working on Windows 11 arm64 build 26200 (exit code 0).

using Sabbour.Mxc.Sdk;
using Sabbour.Mxc.Sdk.Sandbox;
using Sabbour.Mxc.Sdk.Errors;

const string SchemaVersion = "0.6.0-alpha";

var policy = new SandboxPolicy
{
    Version = SchemaVersion,
    Network = new NetworkPolicy { AllowOutbound = false },
};

try
{
    // windows_sandbox requires the experimental flag AND a config-based spawn, because the
    // buffered MxcSdk.SpawnSandboxAsync(script, policy, ...) convenience overload does NOT
    // take a containment parameter.
    var config = MxcSdk.BuildSandboxPayload(
        @"C:\Windows\System32\cmd.exe /c echo hello from windows_sandbox",
        policy,
        containment: "windows_sandbox");

    var result = await new SandboxSpawner().SpawnSandboxAsync(
        config,
        new SandboxSpawnOptions { Experimental = true, UsePty = false });

    Console.WriteLine($"Stdout: {result.Stdout.TrimEnd()}");
    Console.WriteLine($"ExitCode: {result.ExitCode}");
}
catch (MxcException ex)
{
    PrintExecutorHint(ex);
}
catch (Exception ex)
{
    PrintExecutorHint(ex);
}

static void PrintExecutorHint(Exception ex)
{
    Console.WriteLine($"The native executor could not run this scenario: {ex.Message}");

    if (ex is MxcException mxcException)
    {
        var code = mxcException.Code?.ToString() ?? mxcException.RawCode;
        if (!string.IsNullOrWhiteSpace(code))
        {
            Console.WriteLine($"MXC error code: {code}");
        }
    }

    Console.WriteLine("This scenario needs the MXC executor.");
    Console.WriteLine("Download mxc-release-binaries.zip from https://github.com/microsoft/mxc/releases (v0.6.1), unzip it, and set MXC_BIN_DIR to the folder containing <arch>\\wxc-exec.exe.");
    Console.WriteLine("Then run this example again.");
    Console.WriteLine();
    Console.WriteLine("*** REMINDER: windows_sandbox requires Administrator privileges. ***");
    Console.WriteLine("If you see 'Error: 740' or 'Windows Sandbox is not enabled' but you've");
    Console.WriteLine("already enabled the feature, run this example as Administrator.");
}
