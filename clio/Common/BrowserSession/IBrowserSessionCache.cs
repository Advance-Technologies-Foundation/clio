namespace Clio.Common.BrowserSession;

/// <summary>
/// On-disk cache for Playwright-compatible storageState files, keyed by a stable identifier
/// derived from the environment URI and a credential discriminator (never an environment name,
/// which <see cref="EnvironmentSettings"/> does not carry). Files are written owner-only because
/// a storageState holds live session cookies.
/// </summary>
public interface IBrowserSessionCache {
	/// <summary>
	/// Builds a stable, filesystem-safe cache key from the environment URI plus a hash of
	/// <c>Login|Password|ClientId|IsNetCore</c>, so different credentials on the same URI never
	/// collide and no credential value leaks into the file name.
	/// </summary>
	/// <param name="env">The environment whose session is cached.</param>
	/// <returns>A sanitized cache key safe to use as a file-name stem.</returns>
	string BuildKey(EnvironmentSettings env);

	/// <summary>Returns the cached session file for <paramref name="cacheKey"/> if it exists.</summary>
	/// <param name="cacheKey">The key from <see cref="BuildKey"/>.</param>
	/// <param name="filePath">The absolute path when found; otherwise <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when a cached file exists.</returns>
	bool TryRead(string cacheKey, out string filePath);

	/// <summary>
	/// Writes <paramref name="storageStateJson"/> to the cache file for <paramref name="cacheKey"/>
	/// (or to <paramref name="overridePath"/> when supplied) with owner-only permissions.
	/// </summary>
	/// <param name="cacheKey">The key from <see cref="BuildKey"/>.</param>
	/// <param name="storageStateJson">The Playwright storageState JSON to persist.</param>
	/// <param name="overridePath">Optional explicit destination (from the CLI <c>--output-path</c>);
	/// validated against traversal and symlinks. When <see langword="null"/> the default cache path is used.</param>
	void Write(string cacheKey, string storageStateJson, string overridePath = null);

	/// <summary>Deletes the cached session file for <paramref name="cacheKey"/> (idempotent).</summary>
	/// <param name="cacheKey">The key from <see cref="BuildKey"/>.</param>
	void Delete(string cacheKey);

	/// <summary>Returns the absolute cache path for <paramref name="cacheKey"/> without creating it.</summary>
	/// <param name="cacheKey">The key from <see cref="BuildKey"/>.</param>
	/// <returns>The absolute file path the cache would use.</returns>
	string GetPath(string cacheKey);
}
