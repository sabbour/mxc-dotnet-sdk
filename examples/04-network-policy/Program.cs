using Sabbour.Mxc.Sdk;

const string SchemaVersion = "0.6.0-alpha";

var validPolicy = new SandboxPolicy
{
    Version = SchemaVersion,
    Network = new NetworkPolicy
    {
        AllowOutbound = true,
        AllowedHosts = ["api.contoso.com"],
    },
};

_ = MxcSdk.CreateConfigFromPolicy(validPolicy, containment: "process");
Console.WriteLine("Valid policy succeeded: allowOutbound=true with allowedHosts=[api.contoso.com].");

var invalidPolicy = new SandboxPolicy
{
    Version = SchemaVersion,
    Network = new NetworkPolicy
    {
        AllowedHosts = ["api.contoso.com"],
    },
};

try
{
    _ = MxcSdk.CreateConfigFromPolicy(invalidPolicy, containment: "process");
    Console.WriteLine("This host resolved process containment to a backend that supports per-host filtering without allowOutbound=true.");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Caught expected validation error: {ex.Message}");
    Console.WriteLine("Host filtering requires allowOutbound=true unless the selected backend can enforce per-host rules itself.");
}
