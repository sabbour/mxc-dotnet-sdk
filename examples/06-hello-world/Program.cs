using Sabbour.Mxc.Sdk;
using Sabbour.Mxc.Sdk.Errors;

const string SchemaVersion = "0.6.0-alpha";

var policy = new SandboxPolicy
{
    Version = SchemaVersion,
    Network = new NetworkPolicy { AllowOutbound = false },
};

try
{
    var result = await MxcSdk.SpawnSandboxAsync(
        "echo Hello from MXC!",
        policy,
        containerName: "CLI-HelloWorld");

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
}
