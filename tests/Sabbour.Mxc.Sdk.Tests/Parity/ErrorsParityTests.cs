// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Sabbour.Mxc.Sdk.Errors;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests.Parity;

public sealed class ErrorsParityTests
{
    public static TheoryData<ErrorCode, string> KnownCodes => new()
    {
        { ErrorCode.MalformedRequest, "malformed_request" },
        { ErrorCode.UnsupportedContainment, "unsupported_containment" },
        { ErrorCode.UnsupportedPhase, "unsupported_phase" },
        { ErrorCode.BackendUnavailable, "backend_unavailable" },
        { ErrorCode.MalformedId, "malformed_id" },
        { ErrorCode.StaleId, "stale_id" },
        { ErrorCode.NotProvisioned, "not_provisioned" },
        { ErrorCode.NotStarted, "not_started" },
        { ErrorCode.AlreadyStarted, "already_started" },
        { ErrorCode.AlreadyStopped, "already_stopped" },
        { ErrorCode.PolicyValidation, "policy_validation" },
        { ErrorCode.BackendError, "backend_error" },
    };

    [Theory]
    [MemberData(nameof(KnownCodes))]
    public void MxcError_ConstructsWithCodeExtendsErrorExposesMessageAndCode(ErrorCode code, string wireCode)
    {
        var err = new MxcException(code, "boom");

        Assert.Equal(code, err.Code);
        Assert.Equal(wireCode, err.RawCode);
        Assert.Equal("boom", err.Message);
        Assert.Equal(nameof(MxcException), err.GetType().Name);
        Assert.IsType<MxcException>(err);
        Assert.IsAssignableFrom<Exception>(err);
    }

    [Fact]
    public void MxcError_RoundTripsDetails()
    {
        var details = new Dictionary<string, object> { ["hresult"] = "0x80004005" };
        var err = new MxcException(ErrorCode.BackendError, "boom", details);

        Assert.NotNull(err.Details);
        Assert.Equal("0x80004005", err.Details!["hresult"]);
    }

    [Fact]
    public void MxcError_OmitsDetailsWhenNotSupplied()
    {
        var err = new MxcException(ErrorCode.StaleId, "boom");

        Assert.Null(err.Details);
    }

    [Theory]
    [MemberData(nameof(KnownCodes))]
    public void MxcErrorFromCode_MapsWireCodeToMxcErrorWithThatCode(ErrorCode expectedCode, string wireCode)
    {
        var err = MxcException.FromCode(wireCode, "boom");

        Assert.IsType<MxcException>(err);
        Assert.Equal(expectedCode, err.Code);
        Assert.Equal(wireCode, err.RawCode);
        Assert.Equal("boom", err.Message);
    }

    [Fact]
    public void MxcErrorFromCode_PassesDetailsThroughToConstructedInstance()
    {
        var details = new Dictionary<string, object> { ["hresult"] = "0x80004005" };
        var err = MxcException.FromCode("backend_error", "boom", details);

        Assert.IsType<MxcException>(err);
        Assert.NotNull(err.Details);
        Assert.Equal("0x80004005", err.Details!["hresult"]);
    }

    [Fact]
    public void MxcErrorFromCode_ReturnsMxcErrorCarryingUnknownCodeVerbatim()
    {
        var err = MxcException.FromCode("not_a_real_code", "boom");

        Assert.IsType<MxcException>(err);
        Assert.Null(err.Code);
        Assert.Equal("not_a_real_code", err.RawCode);
    }
}
