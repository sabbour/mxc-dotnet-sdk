using Sabbour.Mxc.Sdk;
using Sabbour.Mxc.Sdk.Errors;
using Sabbour.Mxc.Sdk.StateAware;

const string SchemaVersion = "0.7.0-alpha";

SandboxId<IsolationSessionBackend>? sandboxId = null;
var started = false;

try
{
    var backend = IsolationSessionBackend.Instance;
    var commands = new (string Label, string CommandLine)[]
    {
        ("Create demo file", @"cmd.exe /c echo first line>%TEMP%\mxc-demo.txt"),
        ("Append to demo file", @"cmd.exe /c echo second line>>%TEMP%\mxc-demo.txt"),
        ("Read persisted file", @"cmd.exe /c type %TEMP%\mxc-demo.txt"),
        ("List persisted file", @"cmd.exe /c dir /b %TEMP%\mxc-demo.txt"),
    };

    Console.WriteLine("Provisioning sandbox...");
    var provision = await MxcSdk.ProvisionSandboxAsync(
        backend,
        new IsolationSessionProvisionConfig { Version = SchemaVersion });
    sandboxId = provision.SandboxId;
    Console.WriteLine($"Provisioned: {sandboxId}");

    Console.WriteLine("Starting sandbox...");
    await MxcSdk.StartSandboxAsync(
        sandboxId.Value,
        new IsolationSessionStartConfig { Version = SchemaVersion });
    started = true;
    Console.WriteLine("Started.");

    foreach (var (label, commandLine) in commands)
    {
        Console.WriteLine($"Executing ({label}): {commandLine}");
        var exec = await MxcSdk.ExecInSandboxAsync(
            sandboxId.Value,
            new IsolationSessionExecConfig
            {
                Version = SchemaVersion,
                Process = new ProcessConfig { CommandLine = commandLine },
            });

        Console.WriteLine($"Stdout ({label}): {FormatOutput(exec.Stdout)}");
        Console.WriteLine($"Stderr ({label}): {FormatOutput(exec.Stderr)}");
        Console.WriteLine($"ExitCode ({label}): {exec.ExitCode}");

        if (exec.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command '{label}' exited with code {exec.ExitCode}.");
        }
    }
}
catch (MxcException ex)
{
    PrintExecutorHint(ex);
}
catch (Exception ex)
{
    PrintExecutorHint(ex);
}
finally
{
    if (sandboxId.HasValue)
    {
        if (started)
        {
            try
            {
                Console.WriteLine("Stopping sandbox...");
                await MxcSdk.StopSandboxAsync(
                    sandboxId.Value,
                    new IsolationSessionStopConfig { Version = SchemaVersion });
                Console.WriteLine("Stopped.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cleanup warning: failed to stop sandbox: {ex.Message}");
            }
        }

        try
        {
            Console.WriteLine("Deprovisioning sandbox...");
            await MxcSdk.DeprovisionSandboxAsync(
                sandboxId.Value,
                new IsolationSessionDeprovisionConfig { Version = SchemaVersion });
            Console.WriteLine("Deprovisioned.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cleanup warning: failed to deprovision sandbox: {ex.Message}");
        }
    }
}

static string FormatOutput(string value)
{
    var trimmed = value.TrimEnd();
    return string.IsNullOrEmpty(trimmed) ? "(empty)" : trimmed;
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
