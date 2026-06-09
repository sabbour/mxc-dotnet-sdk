// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Sabbour.Mxc.Sdk.Platform;

/// <summary>
/// Thread-safe single-flight cache for <see cref="PlatformSupport"/>.
/// Replaces the TS module-level <c>cachedSupport</c> + <c>_resetPlatformSupportCache()</c>.
/// Ensures concurrent callers only compute once (single-flight semantics).
/// </summary>
internal sealed class PlatformSupportCache
{
    private PlatformSupport? _cached;
    private readonly object _lock = new();

    /// <summary>
    /// Gets the cached value, or null if not yet computed.
    /// </summary>
    public PlatformSupport? Get()
    {
        lock (_lock)
        {
            return _cached;
        }
    }

    /// <summary>
    /// Gets the cached value, or computes and caches it using the given factory.
    /// Thread-safe: only one concurrent caller runs the factory (single-flight).
    /// </summary>
    public PlatformSupport GetOrCompute(Func<PlatformSupport> factory)
    {
        lock (_lock)
        {
            if (_cached is not null)
                return _cached;

            _cached = factory();
            return _cached;
        }
    }

    /// <summary>
    /// Sets the cached value.
    /// </summary>
    public void Set(PlatformSupport value)
    {
        lock (_lock)
        {
            _cached = value;
        }
    }

    /// <summary>
    /// Clears the cached value, forcing the next call to recompute.
    /// Analogue of TS <c>_resetPlatformSupportCache()</c>.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _cached = null;
        }
    }
}
