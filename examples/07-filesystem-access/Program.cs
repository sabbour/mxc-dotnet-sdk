using System.Text.Json;
using Sabbour.Mxc.Sdk;

const string SchemaVersion = "0.6.0-alpha";

var policy = new SandboxPolicy
{
    Version = SchemaVersion,
    Filesystem = new FilesystemPolicy
    {
        ReadwritePaths = [@"C:\temp\workspace"],
        ReadonlyPaths = [@"C:\ProgramData\shared-config"],
        DeniedPaths = [@"C:\Windows\System32"],
        ClearPolicyOnExit = true,
    },
};

var config = MxcSdk.CreateConfigFromPolicy(policy, containment: "process");

Console.WriteLine("Filesystem access control policy -> backend ContainerConfig. This example needs no native binary.");
Console.WriteLine("readwritePaths grant read+write, readonlyPaths grant read, deniedPaths block all access, and clearPolicyOnExit resets the policy when the shell exits.");
Console.WriteLine(JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
