using Sabbour.Mxc.Sdk;
using Sabbour.Mxc.Sdk.Errors;
using Sabbour.Mxc.Sdk.StateAware;

const string SchemaVersion = "0.6.0-alpha";

try
{
    var backend = IsolationSessionBackend.Instance;

    Console.WriteLine("Provisioning sandbox...");
    var provision = await MxcSdk.ProvisionSandboxAsync(
        backend,
        new IsolationSessionProvisionConfig { Version = SchemaVersion });
    var sandboxId = provision.SandboxId;
    Console.WriteLine($"Provisioned: {sandboxId}");

    Console.WriteLine("Starting sandbox...");
    await MxcSdk.StartSandboxAsync(sandboxId);
    Console.WriteLine("Started.");

    Console.WriteLine("Executing: echo done");
    var exec = await MxcSdk.ExecInSandboxAsync(
        sandboxId,
        new IsolationSessionExecConfig
        {
            Version = SchemaVersion,
            Process = new ProcessConfig { CommandLine = "echo done" },
        });
    Console.WriteLine($"Stdout: {exec.Stdout.TrimEnd()}");
    Console.WriteLine($"ExitCode: {exec.ExitCode}");

    Console.WriteLine("Stopping sandbox...");
    await MxcSdk.StopSandboxAsync(sandboxId);
    Console.WriteLine("Stopped.");

    Console.WriteLine("Deprovisioning sandbox...");
    await MxcSdk.DeprovisionSandboxAsync(sandboxId);
    Console.WriteLine("Deprovisioned.");
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
}
