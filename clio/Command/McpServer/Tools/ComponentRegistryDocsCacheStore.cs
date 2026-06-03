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
/// Stores fetched component documentation (markdown) payloads on disk under the same
/// cache root as <see cref="ComponentRegistryCacheStore"/>. Per-version, per-doc-path
/// files plus a sidecar carrying ETag / Last-Modified / SHA-256. The TTL matches the
/// registry payload (5 minutes), and stale entries are still returned to support the
/// stale-while-revalidate flow on top of this store.
/// </summary>
public interface IComponentRegistryDocsCacheStore {
	/// <summary>Reads a cached documentation payload (fresh or stale) when one exists.</summary>
	/// <param name="version">Resolved platform version, or <c>"latest"</c>.</param>
	/// <param name="docPath">Validated documentation path (e.g. <c>docs/data-grid.component.md</c>).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task<ComponentRegistryDocsCacheReadResult?> TryReadAsync(
		string version,
		string docPath,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Atomically writes a payload and its provenance sidecar to the cache directory.
	/// The <paramref name="cdnBaseUrl"/> is recorded verbatim in the metadata's
	/// <c>SourceUrl</c> so diagnostics show the URL the writer actually fetched from
	/// (honors the <c>CLIO_COMPONENT_REGISTRY_CDN_BASE_URL</c> override; review #4 on
	/// PR #599).
	/// </summary>
	Task WriteAsync(
		string version,
		string docPath,
		byte[] payload,
		EntityTagHeaderValue? etag,
		DateTimeOffset? lastModified,
		string cdnBaseUrl,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a docs-cache lookup. Holds the raw UTF-8 bytes; callers decode to text.
/// </summary>
public sealed record ComponentRegistryDocsCacheReadResult(byte[] Content, bool IsFresh, DateTimeOffset ExpiresAt);

/// <summary>
/// Disk-backed implementation of <see cref="IComponentRegistryDocsCacheStore"/>. Files
/// live under <c><clio-home>/cache/component-registry/{version}/{docPath}</c> with a
/// <c>.meta.json</c> sidecar of the same name. The cache root and TTL are shared with
/// the registry-payload store so a single <c><clio-home>/cache/component-registry/</c>
/// delete resets every layer in one go.
/// </summary>
public sealed class ComponentRegistryDocsCacheStore : IComponentRegistryDocsCacheStore {
	internal static readonly TimeSpan EntryTtl = ComponentRegistryCacheStore.EntryTtl;
	private const string MetaSuffix = ".meta.json";

	private readonly IFileSystem _fileSystem;
	private readonly TimeProvider _timeProvider;
	private readonly string _root;

	public ComponentRegistryDocsCacheStore(IFileSystem fileSystem, TimeProvider timeProvider)
		: this(fileSystem, timeProvider, DefaultRoot(fileSystem)) {
	}

	internal ComponentRegistryDocsCacheStore(IFileSystem fileSystem, TimeProvider timeProvider, string rootDirectory) {
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
		_root = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
	}

	/// <inheritdoc />
	public async Task<ComponentRegistryDocsCacheReadResult?> TryReadAsync(
		string version,
		string docPath,
		CancellationToken cancellationToken = default) {
		if (!TryGetPaths(version, docPath, out string mdPath, out string metaPath)) {
			return null;
		}

		if (!_fileSystem.File.Exists(mdPath) || !_fileSystem.File.Exists(metaPath)) {
			return null;
		}

		ComponentRegistryDocsCacheMetadata? metadata;
		try {
			byte[] metaBytes = await _fileSystem.File.ReadAllBytesAsync(metaPath, cancellationToken).ConfigureAwait(false);
			metadata = JsonSerializer.Deserialize<ComponentRegistryDocsCacheMetadata>(metaBytes, MetadataSerializerOptions);
		} catch (JsonException) {
			DeleteSilently(mdPath, metaPath);
			return null;
		} catch (IOException) {
			DeleteSilently(mdPath, metaPath);
			return null;
		}

		if (metadata is null) {
			DeleteSilently(mdPath, metaPath);
			return null;
		}

		byte[] payload;
		try {
			payload = await _fileSystem.File.ReadAllBytesAsync(mdPath, cancellationToken).ConfigureAwait(false);
		} catch (IOException) {
			DeleteSilently(mdPath, metaPath);
			return null;
		}

		bool isFresh = metadata.ExpiresAt > _timeProvider.GetUtcNow();
		return new ComponentRegistryDocsCacheReadResult(payload, isFresh, metadata.ExpiresAt);
	}

	/// <inheritdoc />
	public async Task WriteAsync(
		string version,
		string docPath,
		byte[] payload,
		EntityTagHeaderValue? etag,
		DateTimeOffset? lastModified,
		string cdnBaseUrl,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(payload);
		if (!TryGetPaths(version, docPath, out string mdPath, out string metaPath)) {
			// Silent no-op for invalid paths: callers should have validated via
			// ComponentRegistryDocsPath.TryNormalise first; defence in depth.
			return;
		}

		string? targetDir = _fileSystem.Path.GetDirectoryName(mdPath);
		if (!string.IsNullOrEmpty(targetDir)) {
			_fileSystem.Directory.CreateDirectory(targetDir);
		}

		// Unique tmp suffix per writer: two concurrent refreshes of the same
		// docPath (e.g. two MCP sessions calling get-component-info on the same
		// component within seconds) would otherwise collide on a shared
		// `.tmp` file or on the atomic move — review #3 on PR #599. The Guid
		// suffix keeps tmp writes independent; the final atomic move into
		// place is naturally last-writer-wins on the target.
		string tmpSuffix = ".tmp." + Guid.NewGuid().ToString("N");
		string tmpMdPath = mdPath + tmpSuffix;
		string tmpMetaPath = metaPath + tmpSuffix;

		DateTimeOffset fetchedAt = _timeProvider.GetUtcNow();
		ComponentRegistryDocsCacheMetadata metadata = new(
			FetchedAt: fetchedAt,
			ExpiresAt: fetchedAt + EntryTtl,
			SourceUrl: BuildSourceUrl(cdnBaseUrl, version, docPath),
			Etag: etag?.Tag,
			LastModified: lastModified,
			ContentSha256: Convert.ToHexString(SHA256.HashData(payload)));

		await _fileSystem.File.WriteAllBytesAsync(tmpMdPath, payload, cancellationToken).ConfigureAwait(false);
		byte[] metadataBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata, MetadataSerializerOptions));
		await _fileSystem.File.WriteAllBytesAsync(tmpMetaPath, metadataBytes, cancellationToken).ConfigureAwait(false);

		// Move payload first so a reader never sees stale meta + new payload.
		_fileSystem.File.Move(tmpMdPath, mdPath, overwrite: true);
		_fileSystem.File.Move(tmpMetaPath, metaPath, overwrite: true);
	}

	/// <summary>
	/// Composes the metadata <c>SourceUrl</c> from the actual CDN base URL the
	/// writer used, so the sidecar reflects reality when
	/// <c>CLIO_COMPONENT_REGISTRY_CDN_BASE_URL</c> points at staging/QA — review #4
	/// on PR #599. Tolerates a missing trailing slash on the base URL.
	/// </summary>
	private static string BuildSourceUrl(string cdnBaseUrl, string version, string docPath) {
		string normalisedBase;
		if (string.IsNullOrEmpty(cdnBaseUrl)) {
			normalisedBase = "https://academy.creatio.com/api/mcp/";
		} else if (cdnBaseUrl.EndsWith('/')) {
			normalisedBase = cdnBaseUrl;
		} else {
			normalisedBase = cdnBaseUrl + "/";
		}
		return $"{normalisedBase}{version}/{docPath}";
	}

	/// <summary>
	/// Resolves cache paths and re-verifies that the result stays inside the cache
	/// root after canonicalisation. The validator at
	/// <see cref="ComponentRegistryDocsPath.TryNormalise"/> already rejects path
	/// traversal at the string level; this check is the second line of defence in
	/// case the validator is bypassed or a future call site forgets to use it.
	/// </summary>
	private bool TryGetPaths(string version, string docPath, out string mdPath, out string metaPath) {
		mdPath = string.Empty;
		metaPath = string.Empty;

		if (!ComponentRegistryDocsPath.TryNormalise(docPath, out string normalisedDocPath)) {
			return false;
		}
		string safeVersion = SanitizeVersion(version);

		string baseDir = _fileSystem.Path.Combine(_root, safeVersion);
		string candidate = _fileSystem.Path.Combine(baseDir, normalisedDocPath.Replace('/', _fileSystem.Path.DirectorySeparatorChar));
		string fullCandidate = _fileSystem.Path.GetFullPath(candidate);
		string fullRoot = _fileSystem.Path.GetFullPath(_root) + _fileSystem.Path.DirectorySeparatorChar;
		if (!fullCandidate.StartsWith(fullRoot, StringComparison.Ordinal)) {
			return false;
		}

		mdPath = fullCandidate;
		metaPath = fullCandidate + MetaSuffix;
		return true;
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
		return fileSystem.Path.Combine(ClioRuntimePaths.CacheRoot, ComponentRegistryCacheStore.CacheDirectoryName);
	}

	private static readonly JsonSerializerOptions MetadataSerializerOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private sealed record ComponentRegistryDocsCacheMetadata(
		[property: JsonPropertyName("fetchedAt")] DateTimeOffset FetchedAt,
		[property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
		[property: JsonPropertyName("sourceUrl")] string SourceUrl,
		[property: JsonPropertyName("etag")] string? Etag,
		[property: JsonPropertyName("lastModified")] DateTimeOffset? LastModified,
		[property: JsonPropertyName("contentSha256")] string ContentSha256);
}
