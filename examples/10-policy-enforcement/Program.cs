using System.Runtime.InteropServices;
using Sabbour.Mxc.Sdk;

const string SchemaVersion = "0.6.0-alpha";
const string Url = "https://api.github.com/zen";

bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

// Lay out a workspace the sandbox is allowed to use, and a secret that sits OUTSIDE it.
string root = Path.Combine(Path.GetTempPath(), "mxc-policy-demo");
string workspace = Path.Combine(root, "workspace");
Directory.CreateDirectory(workspace);

string secretPath = Path.Combine(root, "secret.txt");
File.WriteAllText(secretPath, "API_KEY=do-not-leak");

// The same probe runs in every sandbox: reach the network, then read the secret.
string command = WriteProbe(workspace, secretPath, Url, isWindows);

// "Without restrictions": outbound is allowed and the secret's folder is in the read/write set.
var permissive = new SandboxPolicy
{
    Version = SchemaVersion,
    Network = new NetworkPolicy { AllowOutbound = true },
    Filesystem = new FilesystemPolicy { ReadwritePaths = [root] },
};

// "With policy": no outbound, and only the workspace is exposed — the secret is out of reach.
var restrictive = new SandboxPolicy
{
    Version = SchemaVersion,
    Network = new NetworkPolicy { AllowOutbound = false },
    Filesystem = new FilesystemPolicy { ReadwritePaths = [workspace] },
};

Console.WriteLine($"secret file: {secretPath} (outside the workspace)");
Console.WriteLine($"workspace:   {workspace}");
Console.WriteLine();

await RunUnder("WITHOUT restrictions  (allowOutbound=true, secret folder readable)", permissive);
await RunUnder("WITH policy           (allowOutbound=false, only workspace readable)", restrictive);

async Task RunUnder(string label, SandboxPolicy policy)
{
    Console.WriteLine($"=== {label} ===");
    try
    {
        var result = await MxcSdk.SpawnSandboxAsync(command, policy, containerName: "policy-demo");
        Console.WriteLine(result.Stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(result.Stderr))
            Console.WriteLine(result.Stderr.TrimEnd());
    }
    catch (Exception ex)
    {
        Console.WriteLine($"The native executor could not run this scenario: {ex.Message}");
        Console.WriteLine("Set MXC_BIN_DIR to the folder containing <arch>/wxc-exec.exe (or lxc-exec on Linux).");
    }

    Console.WriteLine();
}

static string WriteProbe(string workspace, string secretPath, string url, bool isWindows)
{
    if (isWindows)
    {
        string probe = Path.Combine(workspace, "probe.cmd");
        File.WriteAllText(probe,
            "@echo off\r\n" +
            "echo [network]    curl " + url + "\r\n" +
            "curl -sS --max-time 8 " + url + " 2>&1\r\n" +
            "echo.\r\n" +
            "echo [filesystem] type " + secretPath + "\r\n" +
            "type \"" + secretPath + "\" 2>&1\r\n");
        return "cmd /c " + probe;
    }

    string script = Path.Combine(workspace, "probe.sh");
    File.WriteAllText(script,
        "echo \"[network]    curl " + url + "\"\n" +
        "curl -sS --max-time 8 " + url + " 2>&1\n" +
        "echo \"\"\n" +
        "echo \"[filesystem] cat " + secretPath + "\"\n" +
        "cat \"" + secretPath + "\" 2>&1\n");
    return "sh " + script;
}
