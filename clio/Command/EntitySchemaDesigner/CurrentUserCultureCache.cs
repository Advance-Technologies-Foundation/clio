using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Cross-call cache of resolved profile cultures, keyed by Creatio environment URI.
/// </summary>
/// <remarks>
/// Registered as a singleton so a resolved culture survives across MCP tool calls within one
/// server process (the per-environment resolver instances built by
/// <see cref="ICurrentUserCultureResolverFactory"/> all share this store). The cache key is the
/// environment URI only — the rare same-URI-different-user case is out of scope.
/// </remarks>
public interface ICurrentUserCultureCache
{
	/// <summary>
	/// Returns a cached, non-expired resolution for the environment URI, if present.
	/// </summary>
	/// <param name="environmentUri">The environment URI key.</param>
	/// <param name="resolution">The cached resolution when the method returns <c>true</c>.</param>
	/// <returns><c>true</c> on a live cache hit; otherwise <c>false</c>.</returns>
	bool TryGet(string environmentUri, [MaybeNullWhen(false)] out CultureResolution resolution);

	/// <summary>
	/// Stores a resolution for the environment URI with the configured TTL.
	/// </summary>
	void Set(string environmentUri, CultureResolution resolution);
}

/// <summary>
/// Default <see cref="ICurrentUserCultureCache"/>: a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// keyed by environment URI with a <see cref="TimeProvider"/>-driven TTL (5 minutes, aligned with
/// the platform-version resolver precedent). Profile culture changes at login/profile time, not
/// within a session, so probing more often than once per TTL is pure overhead.
/// </summary>
public sealed class CurrentUserCultureCache : ICurrentUserCultureCache
{
	internal static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

	private readonly TimeProvider _timeProvider;
	private readonly ConcurrentDictionary<string, CacheEntry> _cache;

	/// <summary>Initializes the cache with the supplied time provider.</summary>
	public CurrentUserCultureCache(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
		_cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
	}

	/// <inheritdoc />
	public bool TryGet(string environmentUri, [MaybeNullWhen(false)] out CultureResolution resolution)
	{
		resolution = null;
		if (string.IsNullOrWhiteSpace(environmentUri))
		{
			return false;
		}

		if (_cache.TryGetValue(environmentUri, out CacheEntry? entry) && entry.ExpiresAt > _timeProvider.GetUtcNow())
		{
			resolution = entry.Resolution;
			return true;
		}

		return false;
	}

	/// <inheritdoc />
	public void Set(string environmentUri, CultureResolution resolution)
	{
		ArgumentNullException.ThrowIfNull(resolution);
		if (string.IsNullOrWhiteSpace(environmentUri))
		{
			return;
		}

		_cache[environmentUri] = new CacheEntry(resolution, _timeProvider.GetUtcNow() + CacheTtl);
	}

	private sealed record CacheEntry(CultureResolution Resolution, DateTimeOffset ExpiresAt);
}
