using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Clio.Common.BrowserSession;

/// <inheritdoc cref="IBrowserSessionCache" />
public sealed class BrowserSessionCache : IBrowserSessionCache {
	private const string SessionsDirName = "sessions";
	private const string FileSuffix = ".storageState.json";

	private readonly IFileSystem _fileSystem;
	private readonly IFileSecurityHardening _fileSecurityHardening;

	/// <summary>Initializes the cache over the clio home directory.</summary>
	/// <param name="fileSystem">File-system abstraction for read/write/delete.</param>
	/// <param name="fileSecurityHardening">Applies owner-only permissions to written artifacts.</param>
	public BrowserSessionCache(IFileSystem fileSystem, IFileSecurityHardening fileSecurityHardening) {
		_fileSystem = fileSystem;
		_fileSecurityHardening = fileSecurityHardening;
	}

	// Resolved lazily so a CLIO_HOME override (honored by AppSettingsFolderPath) takes effect,
	// which is also how integration tests point the cache at a temp directory.
	private static string SessionsRoot =>
		System.IO.Path.Combine(SettingsRepository.AppSettingsFolderPath, SessionsDirName);

	/// <inheritdoc />
	public string BuildKey(EnvironmentSettings env) {
		ArgumentNullException.ThrowIfNull(env);
		string slug = Sanitize(StripScheme(env.Uri));
		string discriminator = string.Concat(
			env.Login ?? string.Empty, "|",
			env.Password ?? string.Empty, "|",
			env.ClientId ?? string.Empty, "|",
			env.IsNetCore.ToString());
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(discriminator));
		return $"{slug}_{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
	}

	/// <inheritdoc />
	public string GetPath(string cacheKey) =>
		System.IO.Path.Combine(SessionsRoot, cacheKey + FileSuffix);

	/// <inheritdoc />
	public bool TryRead(string cacheKey, out string filePath) {
		string path = GetPath(cacheKey);
		if (_fileSystem.ExistsFile(path)) {
			filePath = path;
			return true;
		}
		filePath = null;
		return false;
	}

	/// <inheritdoc />
	public void Write(string cacheKey, string storageStateJson, string overridePath = null) {
		string targetPath = string.IsNullOrWhiteSpace(overridePath)
			? GetPath(cacheKey)
			: ValidateOverridePath(overridePath);
		string directory = System.IO.Path.GetDirectoryName(targetPath);
		if (!string.IsNullOrEmpty(directory)) {
			_fileSystem.CreateDirectoryIfNotExists(directory);
			_fileSecurityHardening.HardenDirectory(directory);
		}
		_fileSystem.WriteAllTextToFile(targetPath, storageStateJson);
		_fileSecurityHardening.HardenFile(targetPath);
	}

	/// <inheritdoc />
	public void Delete(string cacheKey) => _fileSystem.DeleteFileIfExists(GetPath(cacheKey));

	// Validates a caller-supplied --output-path: rejects traversal in the raw input, resolves to a
	// full path, and refuses an existing symlink target (a live bearer token must not be written
	// through a planted link). The owner-only write itself is enforced by HardenFile in Write().
	private static string ValidateOverridePath(string overridePath) {
		bool hasTraversalSegment = overridePath
			.Split(new[] { '/', '\\' })
			.Any(segment => segment == "..");
		if (hasTraversalSegment) {
			throw new ArgumentException(
				$"Invalid --output-path '{overridePath}': path traversal ('..') is not allowed.",
				nameof(overridePath));
		}
		string fullPath = System.IO.Path.GetFullPath(overridePath);
		if (System.IO.File.Exists(fullPath)
			&& (System.IO.File.GetAttributes(fullPath) & System.IO.FileAttributes.ReparsePoint) != 0) {
			throw new ArgumentException(
				$"Invalid --output-path '{overridePath}': refusing to write a session through an existing symlink.",
				nameof(overridePath));
		}
		return fullPath;
	}

	private static string StripScheme(string uri) {
		if (string.IsNullOrWhiteSpace(uri)) {
			return string.Empty;
		}
		int schemeIndex = uri.IndexOf("://", StringComparison.Ordinal);
		return schemeIndex >= 0 ? uri[(schemeIndex + 3)..] : uri;
	}

	private static string Sanitize(string value) {
		if (string.IsNullOrWhiteSpace(value)) {
			return "default";
		}
		var builder = new StringBuilder(value.Length);
		foreach (char c in value) {
			builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-');
		}
		string sanitized = builder.ToString().Trim('-', '.');
		return string.IsNullOrEmpty(sanitized) ? "default" : sanitized.ToLowerInvariant();
	}
}
