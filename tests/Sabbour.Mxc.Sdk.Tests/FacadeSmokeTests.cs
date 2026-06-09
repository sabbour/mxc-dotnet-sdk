// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Sabbour.Mxc.Sdk.Errors;
using Sabbour.Mxc.Sdk.Policy;
using Sabbour.Mxc.Sdk.Sandbox;
using Sabbour.Mxc.Sdk.StateAware;

namespace Sabbour.Mxc.Sdk.Tests;

/// <summary>
/// Smoke tests for the <see cref="MxcSdk"/> public facade.
/// Verifies delegation, method shapes, and synchronicity guarantees.
/// </summary>
public sealed class FacadeSmokeTests
{
    [Fact]
    public void GetPlatformSupport_IsSync_ReturnsNonNull()
    {
        // GetPlatformSupport must be synchronous (no Task return) and not null.
        PlatformSupport result = MxcSdk.GetPlatformSupport();
        Assert.NotNull(result);
    }

    [Fact]
    public void CreateConfigFromPolicy_RoundTrips()
    {
        var policy = new SandboxPolicy
        {
            Version = "0.4.0-alpha",
            Network = new NetworkPolicy { AllowOutbound = true },
        };

        ContainerConfig config = MxcSdk.CreateConfigFromPolicy(policy, "process", "test-container");
        Assert.Equal("test-container", config.ContainerId);
        Assert.Equal("0.4.0-alpha", config.Version);
    }

    [Fact]
    public void BuildSandboxPayload_SetsCommandLine()
    {
        var policy = new SandboxPolicy { Version = "0.4.0-alpha" };
        ContainerConfig config = MxcSdk.BuildSandboxPayload("echo hello", policy, "/tmp", "my-ctr");
        Assert.Equal("echo hello", config.Process!.CommandLine);
        Assert.Equal("/tmp", config.Process.Cwd);
    }

    // FIX 1 — SpawnSandbox is the LIVE PTY spawn (TS spawnSandbox)
    [Fact]
    public void SpawnSandbox_MethodExists_ReturnsTaskIPtyConnection()
    {
        var method = typeof(MxcSdk).GetMethod(
            nameof(MxcSdk.SpawnSandbox),
            [typeof(string), typeof(SandboxPolicy), typeof(SandboxSpawnOptions),
             typeof(string), typeof(string), typeof(IReadOnlyDictionary<string, string>)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IPtyConnection>), method!.ReturnType);
    }

    // FIX 1 — SpawnSandboxAsync is the BUFFERED one-shot (TS spawnSandboxAsync)
    [Fact]
    public void SpawnSandboxAsync_MethodExists_ReturnsTaskSandboxProcessResult()
    {
        var method = typeof(MxcSdk).GetMethod(
            nameof(MxcSdk.SpawnSandboxAsync),
            [typeof(string), typeof(SandboxPolicy), typeof(SandboxSpawnOptions),
             typeof(string), typeof(string), typeof(CancellationToken)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<SandboxProcessResult>), method!.ReturnType);
    }

    // FIX 2 — SpawnSandboxFromConfig (TS spawnSandboxFromConfig)
    [Fact]
    public void SpawnSandboxFromConfig_MethodExists_ReturnsTaskIPtyConnection()
    {
        var method = typeof(MxcSdk).GetMethod(
            nameof(MxcSdk.SpawnSandboxFromConfig),
            [typeof(ContainerConfig), typeof(SandboxSpawnOptions),
             typeof(string), typeof(IReadOnlyDictionary<string, string>)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IPtyConnection>), method!.ReturnType);
    }

    [Fact]
    public void SpawnSandboxProcessFromConfig_MethodExists_ReturnsProcessConnection()
    {
        var method = typeof(MxcSdk).GetMethod(nameof(MxcSdk.SpawnSandboxProcessFromConfig));
        Assert.NotNull(method);
        Assert.Equal(typeof(ProcessConnection), method!.ReturnType);
    }

    [Fact]
    public void ExecInSandbox_IsSync_ReturnsIPtyConnection()
    {
        // Verify ExecInSandbox is a sync method (no Task return)
        var method = typeof(MxcSdk).GetMethod(nameof(MxcSdk.ExecInSandbox));
        Assert.NotNull(method);
        Assert.Equal(typeof(IPtyConnection), method!.ReturnType);
    }

    // FIX 5 — MxcErrorFromCode typed overload
    [Fact]
    public void MxcErrorFromCode_TypedOverload_CreatesException()
    {
        var ex = MxcSdk.MxcErrorFromCode(ErrorCode.MalformedRequest, "bad input");
        Assert.Equal(ErrorCode.MalformedRequest, ex.Code);
        Assert.Equal("bad input", ex.Message);
    }

    // FIX 5 — MxcErrorFromCode string overload preserves unknown code + details
    [Fact]
    public void MxcErrorFromCode_StringOverload_PreservesUnknownCodeAndDetails()
    {
        var details = new Dictionary<string, object?> { ["retryAfter"] = 30 };
        var ex = MxcSdk.MxcErrorFromCode("unknown_future_code", "something new", details);

        Assert.Null(ex.Code); // unknown code does not map to enum
        Assert.Equal("unknown_future_code", ex.RawCode);
        Assert.Equal("something new", ex.Message);
        Assert.NotNull(ex.Details);
        Assert.True(ex.Details!.ContainsKey("retryAfter"));
    }

    // FIX 5 — MxcErrorFromCode string overload maps known code
    [Fact]
    public void MxcErrorFromCode_StringOverload_MapsKnownCode()
    {
        var ex = MxcSdk.MxcErrorFromCode("malformed_request", "bad", null);
        Assert.Equal(ErrorCode.MalformedRequest, ex.Code);
        Assert.Equal("malformed_request", ex.RawCode);
    }

    // FIX 4 — GetAvailableToolsPolicy(env, options) parameter order
    [Fact]
    public void GetAvailableToolsPolicy_EnvFirstOptionsSecond()
    {
        var env = new Dictionary<string, string> { ["PATH"] = "" };
        FilesystemPolicyResult result = MxcSdk.GetAvailableToolsPolicy(env, null);
        Assert.NotNull(result);
    }

    [Fact]
    public void GetAvailableToolsPolicy_DefaultOverload_ReturnsNonNull()
    {
        FilesystemPolicyResult result = MxcSdk.GetAvailableToolsPolicy();
        Assert.NotNull(result);
    }

    [Fact]
    public void GetUserProfilePolicy_ReturnsNonNull()
    {
        FilesystemPolicyResult result = MxcSdk.GetUserProfilePolicy();
        Assert.NotNull(result);
    }

    // FIX 4 — GetTemporaryFilesPolicy(env?) now accepts env
    [Fact]
    public void GetTemporaryFilesPolicy_AcceptsEnv()
    {
        var env = new Dictionary<string, string> { ["TEMP"] = @"C:\Temp" };
        FilesystemPolicyResult result = MxcSdk.GetTemporaryFilesPolicy(env);
        Assert.NotNull(result);
    }

    [Fact]
    public void GetTemporaryFilesPolicy_DefaultOverload_ReturnsNonNull()
    {
        FilesystemPolicyResult result = MxcSdk.GetTemporaryFilesPolicy();
        Assert.NotNull(result);
    }

    [Fact]
    public void Phase_IsPublic_WithExpectedConstants()
    {
        Assert.Equal("provision", Phase.Provision);
        Assert.Equal("start", Phase.Start);
        Assert.Equal("exec", Phase.Exec);
        Assert.Equal("stop", Phase.Stop);
        Assert.Equal("deprovision", Phase.Deprovision);
    }

    // FIX 3 — ProvisionSandboxAsync takes containment as first parameter
    [Fact]
    public void ProvisionSandboxAsync_TakesContainmentFirst()
    {
        var method = typeof(MxcSdk).GetMethod(nameof(MxcSdk.ProvisionSandboxAsync));
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(typeof(IsolationSessionBackend), parameters[0].ParameterType);
        Assert.Equal("containment", parameters[0].Name);
    }

    // FIX 3 — State-aware lifecycle methods take sandboxId first (no containment param)
    [Fact]
    public void StartSandboxAsync_TakesSandboxIdFirst()
    {
        var method = typeof(MxcSdk).GetMethod(nameof(MxcSdk.StartSandboxAsync));
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(typeof(SandboxId<IsolationSessionBackend>), parameters[0].ParameterType);
    }

    [Fact]
    public void StopSandboxAsync_TakesSandboxIdFirst()
    {
        var method = typeof(MxcSdk).GetMethod(nameof(MxcSdk.StopSandboxAsync));
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(typeof(SandboxId<IsolationSessionBackend>), parameters[0].ParameterType);
    }

    [Fact]
    public void DeprovisionSandboxAsync_TakesSandboxIdFirst()
    {
        var method = typeof(MxcSdk).GetMethod(nameof(MxcSdk.DeprovisionSandboxAsync));
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(typeof(SandboxId<IsolationSessionBackend>), parameters[0].ParameterType);
    }

    [Fact]
    public void IsolationSessionBackend_Instance_IsAccessible()
    {
        var instance = IsolationSessionBackend.Instance;
        Assert.NotNull(instance);
    }
}
