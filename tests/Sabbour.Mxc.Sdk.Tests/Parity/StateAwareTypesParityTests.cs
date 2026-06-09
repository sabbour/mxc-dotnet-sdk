// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Sabbour.Mxc.Sdk.StateAware;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests.Parity;

public sealed class StateAwareTypesParityTests
{
    [Fact]
    public void SandboxId_RejectsBareStringsWhereSandboxIdIsExpected()
    {
        static void TakesIsolationSessionId(SandboxId<IsolationSessionBackend> _)
        {
        }

        var parameterType = ((Action<SandboxId<IsolationSessionBackend>>)TakesIsolationSessionId).Method.GetParameters()[0].ParameterType;
        Assert.Equal(typeof(SandboxId<IsolationSessionBackend>), parameterType);
        Assert.DoesNotContain(
            typeof(SandboxId<IsolationSessionBackend>).GetMethods(BindingFlags.Public | BindingFlags.Static),
            m => m.Name == "op_Implicit" && m.GetParameters().SingleOrDefault()?.ParameterType == typeof(string));
    }

    [Fact]
    public void SandboxId_RuntimeValueIsAString()
    {
        var id = new SandboxId<IsolationSessionBackend>("iso:abcd");

        Assert.IsType<string>(id.Value);
        Assert.Equal("iso:abcd", id.ToString());
    }

    [Fact]
    public void IsolationSessionProvisionConfig_AcceptsVersionAndFilesystem()
    {
        var cfg = new IsolationSessionProvisionConfig
        {
            Version = "0.6.0-alpha",
            Filesystem = new FilesystemConfig { ReadwritePaths = [@"C:\workspace"] },
        };

        Assert.Equal([@"C:\workspace"], cfg.Filesystem?.ReadwritePaths);
    }

    [Fact]
    public void IsolationSessionProvisionConfig_RejectsNetworkAndUiUntilThoseFeaturesLandRustSide()
    {
        var properties = typeof(IsolationSessionProvisionConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(properties, p => p.Name == "Network");
        Assert.DoesNotContain(properties, p => p.Name == "Ui");
    }

    [Fact]
    public void IsolationSessionProvisionConfig_AcceptsUserOnlyAsIsolationSessionUserConfigInstance()
    {
        var ok = new IsolationSessionProvisionConfig
        {
            User = new IsolationSessionUserConfig("alice@contoso.com", "tok"),
        };
        var userProperty = typeof(IsolationSessionProvisionConfig).GetProperty(nameof(IsolationSessionProvisionConfig.User));

        Assert.Equal("alice@contoso.com", ok.User?.Upn);
        Assert.Equal(typeof(IsolationSessionUserConfig), Nullable.GetUnderlyingType(userProperty!.PropertyType) ?? userProperty.PropertyType);
    }

    [Fact]
    public void IsolationSessionStartConfig_RejectsCrossCuttingFieldsTheMatrixMarksAsRejected()
    {
        var properties = typeof(IsolationSessionStartConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(properties, p => p.Name == "Filesystem");
    }

    [Fact]
    public void IsolationSessionStartConfig_AcceptsConfigurationIdOnlyFromClosedEnum()
    {
        var ok = new IsolationSessionStartConfig { ConfigurationId = IsolationSessionConfigurationId.Composable };
        var property = typeof(IsolationSessionStartConfig).GetProperty(nameof(IsolationSessionStartConfig.ConfigurationId));

        Assert.Equal(IsolationSessionConfigurationId.Composable, ok.ConfigurationId);
        Assert.Equal(typeof(IsolationSessionConfigurationId), Nullable.GetUnderlyingType(property!.PropertyType) ?? property.PropertyType);
    }

    [Fact]
    public void IsolationSessionStartConfig_AcceptsUserOnlyAsIsolationSessionUserConfigInstance()
    {
        var ok = new IsolationSessionStartConfig
        {
            ConfigurationId = IsolationSessionConfigurationId.Composable,
            User = new IsolationSessionUserConfig("alice@contoso.com", "tok"),
        };
        var userProperty = typeof(IsolationSessionStartConfig).GetProperty(nameof(IsolationSessionStartConfig.User));

        Assert.Equal("tok", ok.User?.WamToken);
        Assert.Equal(typeof(IsolationSessionUserConfig), Nullable.GetUnderlyingType(userProperty!.PropertyType) ?? userProperty.PropertyType);
    }

    [Fact]
    public void IsolationSessionUserConfig_RedactsWamTokenUnderStringInspection()
    {
        var user = new IsolationSessionUserConfig("alice@contoso.com", "super-secret");
        var inspected = user.ToString();

        Assert.Contains("alice@contoso.com", inspected);
        Assert.Contains("<redacted>", inspected);
        Assert.DoesNotContain("super-secret", inspected);
    }

    [Fact]
    public void IsolationSessionUserConfig_JsonStringifyPreservesBothFieldsForWireSerialisation()
    {
        var user = new IsolationSessionUserConfig("alice@contoso.com", "super-secret");
        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(user, MxcJsonContext.Default.IsolationSessionUserConfig));

        Assert.Equal("alice@contoso.com", json.GetProperty("upn").GetString());
        Assert.Equal("super-secret", json.GetProperty("wamToken").GetString());
    }

    [Fact]
    public void IsolationSessionExecConfig_RequiresProcess()
    {
        var cfg = new IsolationSessionExecConfig { Process = new ProcessConfig { CommandLine = "echo hi" } };
        var processProperty = typeof(IsolationSessionExecConfig).GetProperty(nameof(IsolationSessionExecConfig.Process));

        Assert.Equal("echo hi", cfg.Process.CommandLine);
        Assert.NotNull(processProperty!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    [Fact]
    public void IsolationSessionStopConfigAndIsolationSessionDeprovisionConfig_OnlyCarryVersion()
    {
        var stopCfg = new IsolationSessionStopConfig { Version = "0.6.0-alpha" };
        var deprovCfg = new IsolationSessionDeprovisionConfig();

        Assert.Equal("0.6.0-alpha", stopCfg.Version);
        Assert.Null(deprovCfg.Version);
        Assert.Equal([nameof(IsolationSessionStopConfig.Version)], PublicPropertyNames<IsolationSessionStopConfig>());
        Assert.Equal([nameof(IsolationSessionDeprovisionConfig.Version)], PublicPropertyNames<IsolationSessionDeprovisionConfig>());
    }

    [Fact]
    public void ConfigsForBackend_SelectsIsolationSessionBundleForIsolationSessionBackend()
    {
        var bundleTypeArguments = StateAwareSandboxes.IsolationSession.GetType().GenericTypeArguments;

        Assert.Contains(typeof(IsolationSessionBackend), bundleTypeArguments);
        Assert.Contains(typeof(IsolationSessionProvisionConfig), bundleTypeArguments);
        Assert.Contains(typeof(IsolationSessionStartConfig), bundleTypeArguments);
        Assert.Contains(typeof(IsolationSessionExecConfig), bundleTypeArguments);
        Assert.Contains(typeof(IsolationSessionStopConfig), bundleTypeArguments);
        Assert.Contains(typeof(IsolationSessionDeprovisionConfig), bundleTypeArguments);
    }

    [Fact]
    public void ProvisionResult_CarriesBackendTypedMetadataForIsolationSession()
    {
        var result = new ProvisionResult<IsolationSessionBackend, IsolationSessionProvisionMetadata>
        {
            SandboxId = new SandboxId<IsolationSessionBackend>("iso:abcd"),
            Metadata = new IsolationSessionProvisionMetadata { AgentUserName = @"iso\agent" },
        };

        Assert.Equal(@"iso\agent", result.Metadata?.AgentUserName);
    }

    private static string[] PublicPropertyNames<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToArray();
}
