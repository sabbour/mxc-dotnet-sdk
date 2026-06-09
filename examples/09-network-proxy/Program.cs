using System.Text.Json;
using Sabbour.Mxc.Sdk;

const string SchemaVersion = "0.6.0-alpha";

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

Console.WriteLine("Network proxy policy -> backend ContainerConfig. This example needs no native binary.");
Console.WriteLine("ProxyConfig is a discriminated union: choose exactly one of localhost, builtinTestServer, or url.");
Console.WriteLine();

var localhostPolicy = new SandboxPolicy
{
    Version = SchemaVersion,
    Network = new NetworkPolicy
    {
        AllowOutbound = true,
        Proxy = ProxyConfig.Localhost(8080),
    },
};

Console.WriteLine("1) Route traffic through an external proxy on localhost:8080.");
Console.WriteLine(JsonSerializer.Serialize(
    MxcSdk.CreateConfigFromPolicy(localhostPolicy, containment: "process"),
    jsonOptions));
Console.WriteLine();

var builtinPolicy = new SandboxPolicy
{
    Version = SchemaVersion,
    Network = new NetworkPolicy
    {
        AllowOutbound = true,
        Proxy = ProxyConfig.BuiltinTestServer(),
    },
};

Console.WriteLine("2) Route traffic through the built-in test proxy server.");
Console.WriteLine(JsonSerializer.Serialize(
    MxcSdk.CreateConfigFromPolicy(builtinPolicy, containment: "process"),
    jsonOptions));
