using System;
using System.IO.Abstractions;
using System.Net.Http;
using System.Text.RegularExpressions;
using Clio.UserEnvironment;

namespace Clio.Common;

/// <summary>
/// Provides cached local access to code-server release archives used by bundled Docker templates.
/// </summary>
public interface ICodeServerArchiveCache {
	/// <summary>
	/// Default bundled code-server version used when the caller does not specify one explicitly.
	/// </summary>
	public const string DefaultCodeServerVersion = "4.112.0";

	/// <summary>
	/// Ensures that the requested code-server archive is available locally and returns its absolute path.
	/// </summary>
	/// <param name="version">Requested code-server version or empty to use the default bundled version.</param>
	/// <returns>Absolute path to the cached archive file.</returns>
	string EnsureArchiveAvailable(string version);
}

/// <summary>
/// Downloads and caches code-server release archives under the clio settings directory.
/// </summary>
public sealed class CodeServerArchiveCache(
	HttpClient httpClient,
	IFileSystem fileSystem,
	System.IO.Abstractions.IFileSystem msFileSystem,
	ILogger logger,
	string settingsRootPath = null)
	: ICodeServerArchiveCache {
	private const string CacheFolderName = "docker-assets";
	private const string CodeServerFolderName = "code-server";
	private static readonly Regex SupportedVersionPattern = new(
		@"^\d+(?:\.\d+){2}(?:[-+][A-Za-z0-9.-]+)?$",
		RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
	private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
	private readonly System.IO.Abstractions.IFileSystem _msFileSystem =
		msFileSystem ?? throw new ArgumentNullException(nameof(msFileSystem));
	private readonly string _settingsRootPath = settingsRootPath ?? string.Empty;

	/// <inheritdoc />
	public string EnsureArchiveAvailable(string version) {
		string effectiveVersion = NormalizeVersion(version);
		string archiveFileName = $"code-server-{effectiveVersion}-linux-amd64.tar.gz";
		string versionDirectory = _fileSystem.Combine(GetCacheRootPath(), effectiveVersion);
		string archivePath = _fileSystem.Combine(versionDirectory, archiveFileName);
		if (_fileSystem.ExistsFile(archivePath)) {
			_logger.WriteInfo($"Using cached code-server archive: {archivePath}");
			return archivePath;
		}

		_fileSystem.CreateDirectoryIfNotExists(versionDirectory);
		string downloadUrl = $"https://github.com/coder/code-server/releases/download/v{effectiveVersion}/{archiveFileName}";
		string tempArchivePath = _fileSystem.Combine(versionDirectory, $"{archiveFileName}.tmp");
		_logger.WriteInfo($"Downloading code-server v{effectiveVersion} to local cache: {archivePath}");
		try {
			using HttpResponseMessage response =
				_httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
			response.EnsureSuccessStatusCode();
			using System.IO.Stream sourceStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
			using (FileSystemStream destinationStream = _fileSystem.CreateFile(tempArchivePath)) {
				sourceStream.CopyTo(destinationStream);
				destinationStream.Flush();
			}

			if (_fileSystem.ExistsFile(archivePath)) {
				_fileSystem.DeleteFileIfExists(archivePath);
			}

			_fileSystem.MoveFile(tempArchivePath, archivePath);
			return archivePath;
		}
		catch {
			_fileSystem.DeleteFileIfExists(tempArchivePath);
			throw;
		}
	}

	private string GetCacheRootPath() {
		string settingsRoot = string.IsNullOrWhiteSpace(_settingsRootPath)
			? SettingsRepository.AppSettingsFolderPath
			: _settingsRootPath;
		return _fileSystem.Combine(settingsRoot, CacheFolderName, CodeServerFolderName);
	}

	private string NormalizeVersion(string version) {
		string effectiveVersion = string.IsNullOrWhiteSpace(version)
			? ICodeServerArchiveCache.DefaultCodeServerVersion
			: version.Trim();
		if (!SupportedVersionPattern.IsMatch(effectiveVersion)) {
			throw new InvalidOperationException(
				$"Unsupported code-server version '{effectiveVersion}'. Use a semantic version like '{ICodeServerArchiveCache.DefaultCodeServerVersion}'.");
		}

		return effectiveVersion;
	}
}
