using System.Text.Json;
using Sabbour.Mxc.Sdk;

const string SchemaVersion = "0.6.0-alpha";

var policy = new SandboxPolicy
{
    Version = SchemaVersion,
    Filesystem = new FilesystemPolicy
    {
        ReadwritePaths = [Environment.CurrentDirectory],
    },
    Network = new NetworkPolicy
    {
        AllowOutbound = true,
    },
};

var config = MxcSdk.CreateConfigFromPolicy(policy, containment: "process");

Console.WriteLine("Policy -> backend ContainerConfig transform. This example needs no native binary.");
Console.WriteLine(JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
