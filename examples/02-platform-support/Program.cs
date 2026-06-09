using Sabbour.Mxc.Sdk;

var support = MxcSdk.GetPlatformSupport();
var availableMethods = support.AvailableMethods.Count == 0
    ? "none"
    : string.Join(", ", support.AvailableMethods);

Console.WriteLine("Platform support probe. This detects sandbox backends on the current host.");
Console.WriteLine($"IsSupported: {support.IsSupported}");
Console.WriteLine($"AvailableMethods: {availableMethods}");
Console.WriteLine($"IsolationTier: {support.IsolationTier}");
