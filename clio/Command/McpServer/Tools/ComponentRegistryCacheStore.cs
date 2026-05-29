using System;
using System.IO;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Stores fetched component-registry payloads on disk so that AI requests survive a
/// transient CDN outage and so that warm starts do not always hit the network.
/// Lives at <c>~/.clio/cache/component-registry/</c>; one <c>{version}.json</c> file per
/// resolved platform version plus a sidecar <c>{version}.meta.json</c>.
/// </summary>
public interface IComponentRegistryCacheStore {
	/// <summary>Reads a cached payload (fresh or stale) when one exists.</summary>
	/// <param name="version">Resolved platform version, or <c>"latest"</c>.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task<ComponentRegistryCacheReadResult?> TryReadAsync(string version, CancellationToken cancellationToken = default);

	/// <summary>
	/// Atomically writes a payload and its provenance sidecar to the cache directory.
	/// The <paramref name="sourceUrl"/> is recorded verbatim in the metadata's
	/// <c>SourceUrl</c> field so diagnostics reflect the actual URL the writer
	/// fetched from — including <c>CLIO_COMPONENT_REGISTRY_CDN_BASE_URL</c> overrides
	/// and the flavor-specific registry filename (review #4 on PR #599).
	/// </summary>
	Task WriteAsync(
		string version,
		byte[] payload,
		EntityTagHeaderValue? etag,
		DateTimeOffset? lastModified,
		string sourceUrl,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a cache lookup. Owns the stream — the caller is expected to dispose it.
/// </summary>
public sealed record ComponentRegistryCacheReadResult(Stream Content, bool IsFresh, DateTimeOffset ExpiresAt);

/// <summary>
/// Disk-backed implementation of <see cref="IComponentRegistryCacheStore"/>.
/// </summary>
public sealed class ComponentRegistryCacheStore : IComponentRegistryCacheStore {
	internal const string CacheDirectoryName = "component-registry";
	internal static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(5);

	private readonly IFileSystem _fileSystem;
	private readonly TimeProvider _timeProvider;
	private readonly string _root;

	public ComponentRegistryCacheStore(IFileSystem fileSystem, TimeProvider timeProvider)
		: this(fileSystem, timeProvider, DefaultRoot(fileSystem)) {
	}

	/// <summary>
	/// Flavor-aware factory. The <paramref name="subdirectory"/> isolates payloads
	/// per flavor (e.g. <c>"mobile"</c> → <c>~/.clio/cache/component-registry/mobile/</c>)
	/// so web and mobile cannot collide on the same <c>latest.json</c> key. Pass an
	/// empty string for the web flavor — keeps the cache layout that pre-mobile builds
	/// already populated on user disks.
	/// </summary>
	public static ComponentRegistryCacheStore WithSubdirectory(
		IFileSystem fileSystem, TimeProvider timeProvider, string subdirectory) =>
		new(fileSystem, timeProvider, ComposeRoot(fileSystem, subdirectory));

	internal ComponentRegistryCacheStore(IFileSystem fileSystem, TimeProvider timeProvider, string rootDirectory) {
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
		_root = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
	}

	/// <inheritdoc />
	public async Task<ComponentRegistryCacheReadResult?> TryReadAsync(string version, CancellationToken cancellationToken = default) {
		(string jsonPath, string metaPath) = GetPaths(version);
		if (!_fileSystem.File.Exists(jsonPath) || !_fileSystem.File.Exists(metaPath)) {
			return null;
		}

		ComponentRegistryCacheMetadata? metadata;
		try {
			byte[] metaBytes = await _fileSystem.File.ReadAllBytesAsync(metaPath, cancellationToken).ConfigureAwait(false);
			metadata = JsonSerializer.Deserialize<ComponentRegistryCacheMetadata>(metaBytes, MetadataSerializerOptions);
		} catch (JsonException) {
			DeleteSilently(jsonPath, metaPath);
			return null;
		} catch (IOException) {
			DeleteSilently(jsonPath, metaPath);
			return null;
		}

		if (metadata is null) {
			DeleteSilently(jsonPath, metaPath);
			return null;
		}

		byte[] payload;
		try {
			payload = await _fileSystem.File.ReadAllBytesAsync(jsonPath, cancellationToken).ConfigureAwait(false);
		} catch (IOException) {
			DeleteSilently(jsonPath, metaPath);
			return null;
		}

		bool isFresh = metadata.ExpiresAt > _timeProvider.GetUtcNow();
		return new ComponentRegistryCacheReadResult(
			new MemoryStream(payload, writable: false),
			isFresh,
			metadata.ExpiresAt);
	}

	/// <inheritdoc />
	public async Task WriteAsync(
		string version,
		byte[] payload,
		EntityTagHeaderValue? etag,
		DateTimeOffset? lastModified,
		string sourceUrl,
		CancellationToken cancellationToken = default) {
		if (payload is null) {
			throw new ArgumentNullException(nameof(payload));
		}

		_fileSystem.Directory.CreateDirectory(_root);
		(string jsonPath, string metaPath) = GetPaths(version);
		// Unique tmp suffix per writer so concurrent refreshes for the same
		// version (across flavors via different cache-store instances, or two
		// in-flight stale-refresh threads on the same instance) cannot collide
		// on the same tmp file — mirrors the docs-cache fix in review #3.
		string tmpSuffix = ".tmp." + Guid.NewGuid().ToString("N");
		string tmpJsonPath = jsonPath + tmpSuffix;
		string tmpMetaPath = metaPath + tmpSuffix;

		DateTimeOffset fetchedAt = _timeProvider.GetUtcNow();
		ComponentRegistryCacheMetadata metadata = new(
			FetchedAt: fetchedAt,
			ExpiresAt: fetchedAt + EntryTtl,
			SourceUrl: sourceUrl,
			Etag: etag?.Tag,
			LastModified: lastModified,
			ContentSha256: Convert.ToHexString(SHA256.HashData(payload)));

		await _fileSystem.File.WriteAllBytesAsync(tmpJsonPath, payload, cancellationToken).ConfigureAwait(false);
		byte[] metadataBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata, MetadataSerializerOptions));
		await _fileSystem.File.WriteAllBytesAsync(tmpMetaPath, metadataBytes, cancellationToken).ConfigureAwait(false);

		// Atomically rename payload first so a reader never sees stale meta + new payload.
		_fileSystem.File.Move(tmpJsonPath, jsonPath, overwrite: true);
		_fileSystem.File.Move(tmpMetaPath, metaPath, overwrite: true);
	}

	private (string Json, string Meta) GetPaths(string version) {
		string safeVersion = SanitizeVersion(version);
		return (
			_fileSystem.Path.Combine(_root, $"{safeVersion}.json"),
			_fileSystem.Path.Combine(_root, $"{safeVersion}.meta.json"));
	}

	private void DeleteSilently(params string[] paths) {
		foreach (string path in paths) {
			try {
				if (_fileSystem.File.Exists(path)) {
					_fileSystem.File.Delete(path);
				}
			} catch (IOException) {
				// Best-effort cleanup; a future write will overwrite.
			}
		}
	}

	private static string SanitizeVersion(string version) {
		if (string.IsNullOrWhiteSpace(version)) {
			throw new ArgumentException("Version must be non-empty.", nameof(version));
		}

		// Defence in depth: callers (the resolver / client) are expected to pass either
		// a semver such as "8.2.1" or the literal "latest". Strip anything that could
		// escape the cache directory.
		Span<char> buffer = stackalloc char[version.Length];
		int length = 0;
		foreach (char c in version) {
			if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_') {
				buffer[length++] = c;
			}
		}

		if (length == 0) {
			throw new ArgumentException($"Version '{version}' contains no usable characters.", nameof(version));
		}

		return new string(buffer[..length]);
	}

	private static string DefaultRoot(IFileSystem fileSystem) {
		string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return fileSystem.Path.Combine(profile, ".clio", "cache", CacheDirectoryName);
	}

	private static string ComposeRoot(IFileSystem fileSystem, string subdirectory) {
		string baseRoot = DefaultRoot(fileSystem);
		return string.IsNullOrEmpty(subdirectory)
			? baseRoot
			: fileSystem.Path.Combine(baseRoot, subdirectory);
	}

	private static readonly JsonSerializerOptions MetadataSerializerOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private sealed record ComponentRegistryCacheMetadata(
		[property: JsonPropertyName("fetchedAt")] DateTimeOffset FetchedAt,
		[property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
		[property: JsonPropertyName("sourceUrl")] string SourceUrl,
		[property: JsonPropertyName("etag")] string? Etag,
		[property: JsonPropertyName("lastModified")] DateTimeOffset? LastModified,
		[property: JsonPropertyName("contentSha256")] string ContentSha256);
}
