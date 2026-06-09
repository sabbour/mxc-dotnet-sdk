// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using Sabbour.Mxc.Sdk.Policy;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests.Parity;

public sealed class PolicyParityTests : IDisposable
{
    private readonly string _tmpRoot;

    public PolicyParityTests()
    {
        _tmpRoot = Path.Combine(AppContext.BaseDirectory, "mxc-policy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpRoot))
        {
            Directory.Delete(_tmpRoot, recursive: true);
        }
    }

    [Fact]
    public void ShouldAddSystemRootToReadonlyPathsWhenPwshExeIsOnPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pwshDir = CreateFakePwshDir();
        var env = new Dictionary<string, string> { ["PATH"] = pwshDir, ["USERPROFILE"] = @"C:\Users\TestUser" };
        var result = PolicyDiscovery.GetAvailableToolsPolicy(env);

        Assert.Contains(result.ReadonlyPaths, p => Regex.IsMatch(p, @"^[a-z]:\\$", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void ShouldAddPSReadLineDirToReadwritePathsWhenPwshExeIsOnPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pwshDir = CreateFakePwshDir();
        var env = new Dictionary<string, string> { ["PATH"] = pwshDir, ["USERPROFILE"] = @"C:\Users\TestUser" };
        var result = PolicyDiscovery.GetAvailableToolsPolicy(env);
        var expected = Path.Combine(@"C:\Users\TestUser", "AppData", "Roaming", "Microsoft", "Windows", "PowerShell", "PSReadLine");

        Assert.Contains(result.ReadwritePaths, p => string.Equals(p, expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldNotAddPowerShellPathsWhenPwshExeIsNotOnPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var env = new Dictionary<string, string> { ["PATH"] = @"C:\Windows\System32", ["USERPROFILE"] = @"C:\Users\TestUser" };
        var result = PolicyDiscovery.GetAvailableToolsPolicy(env);

        Assert.DoesNotContain(result.ReadonlyPaths, p => Regex.IsMatch(p, @"^[a-z]:\\$", RegexOptions.IgnoreCase));
        Assert.Empty(result.ReadwritePaths);
    }

    [Fact]
    public void ShouldReturnEmptyPowerShellPolicyOnNonWindowsEvenWhenPwshExeIsOnPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var pwshDir = CreateFakePwshDir();
        var env = new Dictionary<string, string> { ["PATH"] = pwshDir, ["USERPROFILE"] = @"C:\Users\TestUser" };
        var result = PolicyDiscovery.GetAvailableToolsPolicy(env);

        Assert.DoesNotContain(result.ReadonlyPaths, p => Regex.IsMatch(p, @"^[a-z]:\\$", RegexOptions.IgnoreCase));
        Assert.Empty(result.ReadwritePaths);
    }

    [Fact]
    public void ShouldNotAddPSReadLinePathWhenUserProfileIsNotSet()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pwshDir = CreateFakePwshDir();
        var env = new Dictionary<string, string> { ["PATH"] = pwshDir };
        var result = PolicyDiscovery.GetAvailableToolsPolicy(env);

        Assert.Contains(result.ReadonlyPaths, p => Regex.IsMatch(p, @"^[a-z]:\\$", RegexOptions.IgnoreCase));
        Assert.Empty(result.ReadwritePaths);
    }

    private string CreateFakePwshDir()
    {
        var dir = Path.Combine(_tmpRoot, "pwsh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pwsh.exe"), "");
        return dir;
    }
}
