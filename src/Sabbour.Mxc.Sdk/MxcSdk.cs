// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Sabbour.Mxc.Sdk.Errors;
using Sabbour.Mxc.Sdk.Platform;
using Sabbour.Mxc.Sdk.Policy;
using Sabbour.Mxc.Sdk.Sandbox;
using Sabbour.Mxc.Sdk.StateAware;

namespace Sabbour.Mxc.Sdk;

/// <summary>
/// Public facade for the MXC .NET SDK. Mirrors the top-level exports of the
/// TypeScript <c>@microsoft/mxc-sdk</c> package (index.ts) as static methods.
/// </summary>
public static class MxcSdk
{
    private static readonly PlatformProber s_prober = new();
    private static readonly SandboxSpawner s_spawner = new();

    // -----------------------------------------------------------------------
    // Platform
    // -----------------------------------------------------------------------

    /// <summary>
    /// Probes the host for platform capabilities and available containment backends.
    /// Results are cached for the process lifetime (thread-safe).
    /// Analogue of TS <c>getPlatformSupport()</c>.
    /// </summary>
    /// <returns>A <see cref="PlatformSupport"/> describing available backends and isolation tier.</returns>
    public static PlatformSupport GetPlatformSupport()
    {
        return s_prober.GetPlatformSupport();
    }

    // -----------------------------------------------------------------------
    // Sandbox — config builders
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="ContainerConfig"/> from a <see cref="SandboxPolicy"/> and containment type.
    /// Analogue of TS <c>createConfigFromPolicy()</c>.
    /// </summary>
    /// <param name="policy">The sandbox policy expressing security intent.</param>
    /// <param name="containment">Containment wire string (default: "process").</param>
    /// <param name="containerName">Optional container name; auto-generated if null.</param>
    /// <returns>Fully populated <see cref="ContainerConfig"/>.</returns>
    public static ContainerConfig CreateConfigFromPolicy(
        SandboxPolicy policy,
        string containment = "process",
        string? containerName = null)
    {
        return SandboxFactory.CreateConfigFromPolicy(policy, containment, containerName);
    }

    /// <summary>
    /// Builds a sandbox payload (sets commandLine + cwd on the config).
    /// Analogue of TS <c>buildSandboxPayload()</c>.
    /// </summary>
    /// <param name="script">The command line to execute.</param>
    /// <param name="policy">The sandbox policy.</param>
    /// <param name="workingDirectory">Optional working directory for the process.</param>
    /// <param name="containerName">Optional container name.</param>
    /// <param name="containment">Containment wire string (default: "process").</param>
    /// <returns>A <see cref="ContainerConfig"/> with commandLine set.</returns>
    public static ContainerConfig BuildSandboxPayload(
        string script,
        SandboxPolicy policy,
        string? workingDirectory = null,
        string? containerName = null,
        string containment = "process")
    {
        return SandboxFactory.BuildSandboxPayload(script, policy, workingDirectory, containerName, containment);
    }

    // -----------------------------------------------------------------------
    // Sandbox — spawn (live PTY)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Spawns a sandboxed process from a script and policy, returning a live PTY connection.
    /// Analogue of TS <c>spawnSandbox(script, policy, options?, workingDirectory?, containerName?, env?)</c>
    /// (sandbox.ts:561-568, index.ts:48-55).
    /// </summary>
    /// <remarks>
    /// The TS <c>spawnSandbox</c> is synchronous. In .NET the underlying Porta.Pty spawn is async,
    /// so this method returns <c>Task&lt;IPtyConnection&gt;</c>. The semantics (live interactive PTY)
    /// are faithful to the TS counterpart.
    /// <para><see cref="SandboxSpawnOptions"/> uses <see cref="System.Threading.CancellationToken"/>
    /// in lieu of the TS <c>AbortSignal</c> (signal).</para>
    /// </remarks>
    /// <param name="script">The command line script to execute.</param>
    /// <param name="policy">The sandbox policy.</param>
    /// <param name="options">Spawn options (debug, experimental, etc.).</param>
    /// <param name="workingDirectory">Optional working directory path.</param>
    /// <param name="containerName">Optional container name.</param>
    /// <param name="env">Optional environment variables to inject into the sandbox config.</param>
    /// <returns>A live <see cref="IPtyConnection"/>.</returns>
    public static async Task<IPtyConnection> SpawnSandbox(
        string script,
        SandboxPolicy policy,
        SandboxSpawnOptions? options = null,
        string? workingDirectory = null,
        string? containerName = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var config = SandboxFactory.BuildSandboxPayload(script, policy, workingDirectory, containerName);
        if (env is not null)
        {
            config = SandboxFactory.InjectEnvIntoConfig(config, env);
        }

        var opts = options ?? new SandboxSpawnOptions();
        if (workingDirectory is not null && opts.PtyOptions?.Cwd is null)
        {
            opts = opts with { PtyOptions = (opts.PtyOptions ?? new PtyOptions()) with { Cwd = workingDirectory } };
        }

        return await s_spawner.SpawnSandboxFromConfigAsync(config, opts).ConfigureAwait(false);
    }

    /// <summary>
    /// Spawns a sandboxed process from a pre-built <see cref="ContainerConfig"/>.
    /// PTY mode (default) returns <see cref="IPtyConnection"/>; pipe mode (UsePty=false)
    /// returns a <see cref="ProcessConnectionPtyAdapter"/> wrapping <see cref="ProcessConnection"/>.
    /// Analogue of TS <c>spawnSandboxFromConfig(config, options?, workingDirectory?, env?)</c>
    /// (sandbox.ts:599-616).
    /// </summary>
    /// <remarks>
    /// The TS <c>spawnSandboxFromConfig</c> is synchronous. In .NET the PTY branch uses async
    /// Porta.Pty spawn, so this method returns <c>Task&lt;IPtyConnection&gt;</c>.
    /// The pipe branch (UsePty=false) is synchronous internally but unified under the async return.
    /// <para><see cref="SandboxSpawnOptions"/> uses <see cref="System.Threading.CancellationToken"/>
    /// in lieu of the TS <c>AbortSignal</c> (signal).</para>
    /// </remarks>
    /// <param name="config">The container configuration.</param>
    /// <param name="options">Spawn options.</param>
    /// <param name="workingDirectory">Optional working directory path.</param>
    /// <param name="env">Optional environment variables to inject.</param>
    /// <returns>An <see cref="IPtyConnection"/> (PTY or pipe adapter).</returns>
    public static async Task<IPtyConnection> SpawnSandboxFromConfig(
        ContainerConfig config,
        SandboxSpawnOptions? options = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        if (env is not null)
        {
            config = SandboxFactory.InjectEnvIntoConfig(config, env);
        }

        var opts = options ?? new SandboxSpawnOptions();
        if (workingDirectory is not null && opts.PtyOptions?.Cwd is null)
        {
            opts = opts with { PtyOptions = (opts.PtyOptions ?? new PtyOptions()) with { Cwd = workingDirectory } };
        }

        return await s_spawner.SpawnSandboxFromConfigAsync(config, opts).ConfigureAwait(false);
    }

    /// <summary>
    /// Spawns a sandboxed process from a pre-built config in pipe mode (usePty:false).
    /// Returns a <see cref="ProcessConnection"/> with separate stdout/stderr streams.
    /// This is a .NET convenience method; use <see cref="SpawnSandboxFromConfig"/> with
    /// <c>UsePty=false</c> for the shape matching the TS export.
    /// </summary>
    /// <param name="config">The container configuration.</param>
    /// <param name="options">Spawn options.</param>
    /// <param name="workingDirectory">Optional working directory path.</param>
    /// <param name="env">Optional environment variables to inject.</param>
    /// <returns>A <see cref="ProcessConnection"/>.</returns>
    public static ProcessConnection SpawnSandboxProcessFromConfig(
        ContainerConfig config,
        SandboxSpawnOptions? options = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        if (env is not null)
        {
            config = SandboxFactory.InjectEnvIntoConfig(config, env);
        }

        var opts = options ?? new SandboxSpawnOptions();
        if (workingDirectory is not null)
        {
            opts = opts with { PtyOptions = (opts.PtyOptions ?? new PtyOptions()) with { Cwd = workingDirectory } };
        }

        return s_spawner.SpawnSandboxProcessFromConfig(config, opts);
    }

    // -----------------------------------------------------------------------
    // Sandbox — buffered one-shot (async)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Spawns a sandboxed process and waits for it to exit, returning buffered output.
    /// Analogue of TS <c>spawnSandboxAsync(script, policy, options?, workingDirectory?, containerName?)</c>
    /// (sandbox.ts:675-681). The TS version is the BUFFERED one-shot returning {stdout, stderr, exitCode}.
    /// </summary>
    /// <remarks>
    /// <see cref="SandboxSpawnOptions"/> uses <see cref="System.Threading.CancellationToken"/>
    /// in lieu of the TS <c>AbortSignal</c> (signal).
    /// </remarks>
    /// <param name="script">The command line script to execute.</param>
    /// <param name="policy">The sandbox policy.</param>
    /// <param name="options">Spawn options.</param>
    /// <param name="workingDirectory">Optional working directory path.</param>
    /// <param name="containerName">Optional container name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="SandboxProcessResult"/> with stdout, stderr, and exit code.</returns>
    public static async Task<SandboxProcessResult> SpawnSandboxAsync(
        string script,
        SandboxPolicy policy,
        SandboxSpawnOptions? options = null,
        string? workingDirectory = null,
        string? containerName = null,
        CancellationToken cancellationToken = default)
    {
        var config = SandboxFactory.BuildSandboxPayload(script, policy, workingDirectory, containerName);
        return await s_spawner.SpawnSandboxAsync(config, options, cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Policy discovery
    // -----------------------------------------------------------------------

    /// <summary>
    /// Discovers tool directories from environment variables and returns them as policy paths.
    /// Analogue of TS <c>getAvailableToolsPolicy(env?, options?)</c> (policy.ts:269-272).
    /// </summary>
    /// <param name="env">Optional environment variables dictionary (defaults to current process env).</param>
    /// <param name="options">Options controlling which tool categories to include.</param>
    /// <returns>A <see cref="FilesystemPolicyResult"/> with discovered paths.</returns>
    public static FilesystemPolicyResult GetAvailableToolsPolicy(IDictionary<string, string>? env = null, ToolsPolicyOptions? options = null)
    {
        return PolicyDiscovery.GetAvailableToolsPolicy(env, options);
    }

    /// <summary>
    /// Returns the user profile directory as a policy path.
    /// Analogue of TS <c>getUserProfilePolicy()</c>.
    /// </summary>
    /// <returns>A <see cref="FilesystemPolicyResult"/> for the user profile.</returns>
    public static FilesystemPolicyResult GetUserProfilePolicy()
    {
        return PolicyDiscovery.GetUserProfilePolicy();
    }

    /// <summary>
    /// Returns the system temporary directory as a policy path.
    /// Analogue of TS <c>getTemporaryFilesPolicy(env?)</c> (policy.ts:386-388).
    /// </summary>
    /// <param name="env">Optional environment variables dictionary (defaults to current process env).</param>
    /// <returns>A <see cref="FilesystemPolicyResult"/> for the temp directory.</returns>
    public static FilesystemPolicyResult GetTemporaryFilesPolicy(IDictionary<string, string>? env = null)
    {
        return PolicyDiscovery.GetTemporaryFilesPolicy(env);
    }

    // -----------------------------------------------------------------------
    // Errors
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates an <see cref="MxcException"/> from a typed error code and message.
    /// .NET ergonomic overload — for typed <see cref="ErrorCode"/> values.
    /// </summary>
    /// <param name="code">The typed error code.</param>
    /// <param name="message">Human-readable error message.</param>
    /// <returns>An <see cref="MxcException"/> instance.</returns>
    public static MxcException MxcErrorFromCode(ErrorCode code, string message)
    {
        return new MxcException(code, message);
    }

    /// <summary>
    /// Creates an <see cref="MxcException"/> from a wire-format error code string, message,
    /// and optional structured details. Unknown codes are preserved in <see cref="MxcException.RawCode"/>.
    /// Analogue of TS <c>mxcErrorFromCode(code: string, message: string, details?)</c>
    /// (errors.ts:52-56, index.ts:66-71).
    /// </summary>
    /// <param name="code">The wire-format error code string (e.g. "backend_error").</param>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="details">Optional structured details from the wire envelope.</param>
    /// <returns>An <see cref="MxcException"/> instance preserving raw code and details.</returns>
    public static MxcException MxcErrorFromCode(string code, string message, IReadOnlyDictionary<string, object?>? details = null)
    {
        // Normalize nullable details to the non-nullable interface expected by MxcException.FromCode
        IReadOnlyDictionary<string, object>? normalized = null;
        if (details is not null)
        {
            normalized = details as IReadOnlyDictionary<string, object>
                ?? details.ToDictionary(kv => kv.Key, kv => kv.Value as object ?? "null")
                    .AsReadOnly();
        }

        return MxcException.FromCode(code, message, normalized);
    }

    // -----------------------------------------------------------------------
    // State-aware lifecycle
    // -----------------------------------------------------------------------

    /// <summary>
    /// Provisions a state-aware sandbox. Returns a branded sandbox id and provision metadata.
    /// Analogue of TS <c>provisionSandbox(containment, config?, options?)</c>
    /// (state-aware.ts:36-40, index.ts:103-110).
    /// </summary>
    /// <remarks>
    /// The <paramref name="containment"/> parameter mirrors the TS union parameter shape.
    /// Currently the only supported value is <see cref="IsolationSessionBackend"/>
    /// (wire: "isolation_session"). The marker is accepted as the first parameter to match
    /// the TS export signature where containment is positionally first.
    /// </remarks>
    /// <param name="containment">The containment backend marker (must be <see cref="IsolationSessionBackend"/>).</param>
    /// <param name="config">Backend-specific provision config.</param>
    /// <param name="options">Spawn options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ProvisionResult{TBackend, TMetadata}"/> with the sandbox ID.</returns>
    public static Task<ProvisionResult<IsolationSessionBackend, IsolationSessionProvisionMetadata>> ProvisionSandboxAsync(
        IsolationSessionBackend containment,
        IsolationSessionProvisionConfig? config = default,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = containment; // marker parameter — backend is determined by TBackend type
        return StateAwareSandboxes.IsolationSession.ProvisionSandboxAsync(config, options, cancellationToken);
    }

    /// <summary>
    /// Starts a previously provisioned sandbox.
    /// Analogue of TS <c>startSandbox(sandboxId, config?, options?)</c>.
    /// </summary>
    /// <param name="sandboxId">The branded sandbox ID from provisioning.</param>
    /// <param name="config">Backend-specific start config.</param>
    /// <param name="options">Spawn options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="PhaseResult{TMetadata}"/>.</returns>
    public static Task<PhaseResult<NoMetadata>> StartSandboxAsync(
        SandboxId<IsolationSessionBackend> sandboxId,
        IsolationSessionStartConfig? config = default,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return StateAwareSandboxes.IsolationSession.StartSandboxAsync(sandboxId, config, options, cancellationToken);
    }

    /// <summary>
    /// Streaming exec — returns a live <see cref="IPtyConnection"/> without buffering output.
    /// Analogue of TS <c>execInSandbox(sandboxId, config, options?)</c>.
    /// Synchronous (no await needed).
    /// </summary>
    /// <param name="sandboxId">The branded sandbox ID from provisioning.</param>
    /// <param name="config">Exec configuration (commandLine, etc.).</param>
    /// <param name="options">Spawn options.</param>
    /// <returns>A live <see cref="IPtyConnection"/>.</returns>
    public static IPtyConnection ExecInSandbox(
        SandboxId<IsolationSessionBackend> sandboxId,
        IsolationSessionExecConfig config,
        SandboxSpawnOptions? options = null)
    {
        return StateAwareSandboxes.IsolationSession.ExecSandbox(sandboxId, config, options);
    }

    /// <summary>
    /// Buffered exec — resolves with stdout/stderr/exitCode.
    /// Analogue of TS <c>execInSandboxAsync(sandboxId, config, options?)</c>.
    /// </summary>
    /// <param name="sandboxId">The branded sandbox ID from provisioning.</param>
    /// <param name="config">Exec configuration.</param>
    /// <param name="options">Spawn options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="ExecResult"/> with stdout, stderr, and exit code.</returns>
    public static Task<ExecResult> ExecInSandboxAsync(
        SandboxId<IsolationSessionBackend> sandboxId,
        IsolationSessionExecConfig config,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return StateAwareSandboxes.IsolationSession.ExecSandboxAsync(sandboxId, config, options, cancellationToken);
    }

    /// <summary>
    /// Stops a started sandbox without releasing provision-side resources.
    /// Analogue of TS <c>stopSandbox(sandboxId, config?, options?)</c>.
    /// </summary>
    /// <param name="sandboxId">The branded sandbox ID from provisioning.</param>
    /// <param name="config">Backend-specific stop config.</param>
    /// <param name="options">Spawn options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="PhaseResult{TMetadata}"/>.</returns>
    public static Task<PhaseResult<NoMetadata>> StopSandboxAsync(
        SandboxId<IsolationSessionBackend> sandboxId,
        IsolationSessionStopConfig? config = default,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return StateAwareSandboxes.IsolationSession.StopSandboxAsync(sandboxId, config, options, cancellationToken);
    }

    /// <summary>
    /// Deprovisions a sandbox, releasing all resources.
    /// Analogue of TS <c>deprovisionSandbox(sandboxId, config?, options?)</c>.
    /// </summary>
    /// <param name="sandboxId">The branded sandbox ID from provisioning.</param>
    /// <param name="config">Backend-specific deprovision config.</param>
    /// <param name="options">Spawn options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="PhaseResult{TMetadata}"/>.</returns>
    public static Task<PhaseResult<NoMetadata>> DeprovisionSandboxAsync(
        SandboxId<IsolationSessionBackend> sandboxId,
        IsolationSessionDeprovisionConfig? config = default,
        SandboxSpawnOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return StateAwareSandboxes.IsolationSession.DeprovisionSandboxAsync(sandboxId, config, options, cancellationToken);
    }
}
