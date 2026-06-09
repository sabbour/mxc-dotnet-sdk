// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;

namespace Sabbour.Mxc.Sdk.Tests;

/// <summary>
/// Groups tests that temporarily swap the process-global <see cref="System.Console.Error"/>
/// (or <see cref="System.Console.Out"/>) writer to capture stderr output. xUnit runs separate
/// test classes in parallel by default; without this shared collection two classes swapping the
/// same global writer can clobber each other's capture, producing intermittent empty-output
/// failures. Classes in one collection run sequentially relative to each other while still
/// running in parallel with the rest of the suite.
/// </summary>
[CollectionDefinition("ConsoleRedirection")]
public sealed class ConsoleRedirectionCollection
{
}
