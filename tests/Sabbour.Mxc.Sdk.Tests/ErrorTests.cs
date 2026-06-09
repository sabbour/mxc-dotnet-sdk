// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Sabbour.Mxc.Sdk.Errors;
using Xunit;

namespace Sabbour.Mxc.Sdk.Tests;

public class ErrorCodeTests
{
    private static readonly JsonSerializerOptions s_options = new();

    [Theory]
    [InlineData(ErrorCode.MalformedRequest, "malformed_request")]
    [InlineData(ErrorCode.UnsupportedContainment, "unsupported_containment")]
    [InlineData(ErrorCode.UnsupportedPhase, "unsupported_phase")]
    [InlineData(ErrorCode.BackendUnavailable, "backend_unavailable")]
    [InlineData(ErrorCode.MalformedId, "malformed_id")]
    [InlineData(ErrorCode.StaleId, "stale_id")]
    [InlineData(ErrorCode.NotProvisioned, "not_provisioned")]
    [InlineData(ErrorCode.NotStarted, "not_started")]
    [InlineData(ErrorCode.AlreadyStarted, "already_started")]
    [InlineData(ErrorCode.AlreadyStopped, "already_stopped")]
    [InlineData(ErrorCode.PolicyValidation, "policy_validation")]
    [InlineData(ErrorCode.BackendError, "backend_error")]
    public void ErrorCode_SerializesToSnakeCase(ErrorCode code, string expectedWire)
    {
        var json = JsonSerializer.Serialize(code, s_options);
        Assert.Equal($"\"{expectedWire}\"", json);
    }

    [Theory]
    [InlineData("\"malformed_request\"", ErrorCode.MalformedRequest)]
    [InlineData("\"backend_error\"", ErrorCode.BackendError)]
    [InlineData("\"policy_validation\"", ErrorCode.PolicyValidation)]
    public void ErrorCode_DeserializesFromSnakeCase(string json, ErrorCode expected)
    {
        var result = JsonSerializer.Deserialize<ErrorCode>(json, s_options);
        Assert.Equal(expected, result);
    }
}

public class MxcExceptionTests
{
    [Fact]
    public void Constructor_SetsCodeAndMessage()
    {
        var ex = new MxcException(ErrorCode.BackendError, "something failed");
        Assert.Equal(ErrorCode.BackendError, ex.Code);
        Assert.Equal("backend_error", ex.RawCode);
        Assert.Equal("something failed", ex.Message);
        Assert.Null(ex.Details);
    }

    [Fact]
    public void Constructor_SetsDetails()
    {
        var details = new Dictionary<string, object> { ["key"] = "value" };
        var ex = new MxcException(ErrorCode.MalformedRequest, "bad input", details);
        Assert.Equal(ErrorCode.MalformedRequest, ex.Code);
        Assert.NotNull(ex.Details);
        Assert.Equal("value", ex.Details["key"]);
    }

    [Fact]
    public void FromCode_ParsesSnakeCaseString()
    {
        var ex = MxcException.FromCode("backend_unavailable", "not found");
        Assert.Equal(ErrorCode.BackendUnavailable, ex.Code);
        Assert.Equal("backend_unavailable", ex.RawCode);
        Assert.Equal("not found", ex.Message);
    }

    [Fact]
    public void FromCode_KnownCode_SetsTypedCode()
    {
        var ex = MxcException.FromCode("malformed_request", "bad");
        Assert.Equal(ErrorCode.MalformedRequest, ex.Code);
        Assert.Equal("malformed_request", ex.RawCode);
    }

    [Fact]
    public void FromCode_WithDetails()
    {
        var details = new Dictionary<string, object> { ["exitCode"] = 137 };
        var ex = MxcException.FromCode("backend_error", "process killed", details);
        Assert.Equal(ErrorCode.BackendError, ex.Code);
        Assert.Equal(137, ex.Details!["exitCode"]);
    }

    [Fact]
    public void FromCode_UnknownCode_PreservesRawCode_NoThrow()
    {
        var ex = MxcException.FromCode("some_new_code", "future error");
        Assert.Null(ex.Code);
        Assert.Equal("some_new_code", ex.RawCode);
        Assert.Equal("future error", ex.Message);
    }

    [Fact]
    public void FromCode_AnotherUnknownCode_ReturnsValidException()
    {
        var ex = MxcException.FromCode("rate_limit_exceeded", "slow down");
        Assert.Null(ex.Code);
        Assert.Equal("rate_limit_exceeded", ex.RawCode);
    }

    [Fact]
    public void InnerException_IsPreserved()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new MxcException(ErrorCode.BackendError, "outer", inner);
        Assert.Same(inner, ex.InnerException);
    }
}
