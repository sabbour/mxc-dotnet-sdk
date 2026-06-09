using Xunit;

namespace Sabbour.Mxc.Sdk.IntegrationTests;

/// <summary>
/// Integration tests are skipped by default.
/// To run them: set environment variable MXC_INTEGRATION_TESTS=1
///   then: dotnet test --filter "Category=Integration"
/// </summary>
public class ScaffoldSmokeTests
{
    [IntegrationFact]
    [Trait("Category", "Integration")]
    public void Integration_Scaffold_Smoke()
    {
        Assert.True(true);
    }
}

internal sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MXC_INTEGRATION_TESTS") != "1")
        {
            Skip = "Integration tests require MXC_INTEGRATION_TESTS=1";
        }
    }
}
