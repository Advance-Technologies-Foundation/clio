using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Clio.UserEnvironment;
using Ms = System.IO.Abstractions;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Downloads the pinned headless browser of an <see cref="IHeadlessShellSource"/> into
/// <c>~/.clio/browsers/&lt;version&gt;/</c>, verified by size and SHA-256, and finds it there afterwards. Nothing is
/// bundled with clio: the archive comes from the source (or its mirror) only when the user chose a download.
/// </summary>
public interface IHeadlessShellInstaller {

	/// <summary>The executable of the pinned version when it is installed; otherwise null.</summary>
	string FindInstalled();

	/// <summary>Where the pinned version is (or would be) installed.</summary>
	string InstallDirectory { get; }

	/// <summary>The pinned archive for this machine's platform; null when the source publishes no build for it.</summary>
	HeadlessShellRelease.Archive DownloadArchive { get; }

	/// <summary>Downloads, verifies and installs the pinned version unless it is already installed.</summary>
	/// <param name="ct">Cancels the download; nothing partial is left installed.</param>
	/// <returns>The executable.</returns>
	/// <exception cref="HeadlessShellInstallException">No build for this platform, the download failed, or the archive did not verify.</exception>
	Task<string> InstallAsync(CancellationToken ct);
}

/// <summary>chrome-headless-shell could not be installed; the message says why and what to do.</summary>
public sealed class HeadlessShellInstallException : InvalidOperationException {

	/// <summary>Creates the exception.</summary>
	/// <param name="message">What failed and the remedy.</param>
	public HeadlessShellInstallException(string message) : base(message) {
	}

	/// <summary>Creates the exception around the failure that caused it.</summary>
	/// <param name="message">What failed and the remedy.</param>
	/// <param name="innerException">The underlying failure.</param>
	public HeadlessShellInstallException(string message, Exception innerException) : base(message, innerException) {
	}
}

/// <inheritdoc cref="IHeadlessShellInstaller" />
public sealed class HeadlessShellInstaller : IHeadlessShellInstaller {

	/// <summary>The name of the <see cref="IHttpClientFactory"/> client the download uses.</summary>
	public const string HttpClientName = "clio-headless-shell";

	// Download AND extraction end inside the worker's 120 s budget, with room to answer.
	internal static readonly TimeSpan DownloadBudget = TimeSpan.FromSeconds(90);

	// A .download-*/.extract-* this old belongs to no running install (one ends within the budget): a worker killed
	// mid-install left it.
	private static readonly TimeSpan LeftoverAge = TimeSpan.FromMinutes(10);

	private const int CopyBufferBytes = 81_920;

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IHeadlessShellSource _source;
	private readonly Ms.IFileSystem _fileSystem;
	private readonly TimeProvider _timeProvider;
	private readonly string _browsersRoot;
	private readonly string _platformKey;

	/// <summary>Initializes the installer for this machine, under clio's home directory.</summary>
	/// <param name="httpClientFactory">Creates the download client.</param>
	/// <param name="source">Where the browser comes from.</param>
	/// <param name="fileSystem">File system the archive is written to and extracted into.</param>
	/// <param name="timeProvider">Clock of the download budget.</param>
	public HeadlessShellInstaller(IHttpClientFactory httpClientFactory, IHeadlessShellSource source, Ms.IFileSystem fileSystem,
		TimeProvider timeProvider)
		: this(httpClientFactory, source, fileSystem, timeProvider, Path.Combine(ClioRuntimePaths.Home, "browsers"),
			HeadlessShellRelease.CurrentPlatformKey()) {
	}

	/// <summary>Initializes the installer over an explicit root and platform, for tests.</summary>
	internal HeadlessShellInstaller(IHttpClientFactory httpClientFactory, IHeadlessShellSource source, Ms.IFileSystem fileSystem,
		TimeProvider timeProvider, string browsersRoot, string platformKey) {
		_httpClientFactory = httpClientFactory;
		_source = source;
		_fileSystem = fileSystem;
		_timeProvider = timeProvider;
		_browsersRoot = browsersRoot;
		_platformKey = platformKey;
	}

	/// <inheritdoc />
	public string InstallDirectory => Path.Combine(_browsersRoot, _source.Version);

	/// <inheritdoc />
	public HeadlessShellRelease.Archive DownloadArchive => _source.FindArchive(_platformKey);

	/// <inheritdoc />
	public string FindInstalled() {
		if (_platformKey is null) {
			return null;
		}
		string executable = Path.Combine(InstallDirectory, _source.ExecutableRelativePath(_platformKey));
		return _fileSystem.File.Exists(executable) ? executable : null;
	}

	/// <inheritdoc />
	public async Task<string> InstallAsync(CancellationToken ct) {
		string installed = FindInstalled();
		if (installed is not null) {
			return installed;
		}
		HeadlessShellRelease.Archive archive = DownloadArchive;
		if (archive is null) {
			throw new HeadlessShellInstallException(
				$"There is no {_source.DisplayName} build for {RuntimeInformation.OSDescription} "
				+ $"({RuntimeInformation.ProcessArchitecture}); install a Chromium-based browser and choose it instead.");
		}
		using CancellationTokenSource budgetTimer = new(DownloadBudget, _timeProvider);
		using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(ct, budgetTimer.Token);
		Uri url = _source.ArchiveUrl(_platformKey);
		string host = HostOf(url);
		string token = Guid.NewGuid().ToString("N");
		string archivePath = Path.Combine(_browsersRoot, $".download-{token}.zip");
		string extractDirectory = Path.Combine(_browsersRoot, $".extract-{token}");
		try {
			_fileSystem.Directory.CreateDirectory(_browsersRoot);
			DeleteLeftovers();
			await DownloadAsync(url, host, archive, archivePath, budget.Token).ConfigureAwait(false);
			Extract(archivePath, extractDirectory, budget.Token);
			string relativeExecutable = _source.ExecutableRelativePath(_platformKey);
			if (!_fileSystem.File.Exists(Path.Combine(extractDirectory, relativeExecutable))) {
				throw new HeadlessShellInstallException($"The downloaded archive has no {relativeExecutable}.");
			}
			Publish(extractDirectory);
			return FindInstalled() ?? throw new HeadlessShellInstallException(
				$"The headless browser was extracted but is not at {InstallDirectory}.");
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
			throw new HeadlessShellInstallException(
				$"Downloading {_source.DisplayName} {_source.Version} from {host} did not finish within "
				+ $"{DownloadBudget.TotalSeconds:0} s. Call page-smoke again to retry, or ask the user to choose an "
				+ $"existing browser. {MirrorHint}");
		}
		catch (HttpRequestException exception) {
			throw new HeadlessShellInstallException(
				$"{_source.DisplayName} could not be downloaded from {host}: {exception.Message} The machine may be "
				+ $"offline or the host blocked. {MirrorHint}", exception);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException) {
			throw new HeadlessShellInstallException(
				$"{_source.DisplayName} {_source.Version} could not be installed into {_browsersRoot}: {exception.Message} "
				+ "Nothing was installed; call page-smoke again to retry.", exception);
		}
		finally {
			DeleteQuietly(archivePath, extractDirectory);
		}
	}

	// Streams the archive to disk, hashing as it goes, and stops the moment it is larger than the pinned size: a wrong
	// or hostile mirror can neither fill the disk nor be installed.
	private async Task DownloadAsync(Uri url, string host, HeadlessShellRelease.Archive archive, string archivePath,
		CancellationToken ct) {
		HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
		using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
			.ConfigureAwait(false);
		if (!response.IsSuccessStatusCode) {
			throw new HeadlessShellInstallException(
				$"Downloading {_source.DisplayName} {_source.Version} from {host} failed: HTTP "
				+ $"{(int)response.StatusCode} {response.ReasonPhrase}. {MirrorHint}");
		}
		if (response.Content.Headers.ContentLength is long announced && announced != archive.Size) {
			throw Mismatch($"its size is {announced} bytes, expected {archive.Size}");
		}
		using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		long written = 0;
		await using (Stream source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
		await using (Stream target = _fileSystem.File.Create(archivePath)) {
			byte[] buffer = new byte[CopyBufferBytes];
			int read;
			while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0) {
				written += read;
				if (written > archive.Size) {
					throw Mismatch($"it is larger than the expected {archive.Size} bytes");
				}
				sha256.AppendData(buffer, 0, read);
				await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
			}
		}
		if (written != archive.Size) {
			throw Mismatch($"its size is {written} bytes, expected {archive.Size}");
		}
		string actual = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
		if (!string.Equals(actual, archive.Sha256, StringComparison.Ordinal)) {
			throw Mismatch($"its SHA-256 is {actual}, expected {archive.Sha256}");
		}
	}

	// Scheme, host and port only: Uri.Authority (and GetLeftPart) keep a mirror's user:password@, which would then reach
	// the error text and the MCP reply.
	internal static string HostOf(Uri url) =>
		url.IsDefaultPort ? $"{url.Scheme}://{url.Host}" : $"{url.Scheme}://{url.Host}:{url.Port}";

	private static string MirrorHint =>
		$"On a network that reaches only a mirror of Chrome for Testing, set {HeadlessShellRelease.MirrorEnvVar} to its base URL.";

	private HeadlessShellInstallException Mismatch(string detail) =>
		new($"The downloaded {_source.DisplayName} {_source.Version} archive did not verify: {detail}. It was "
			+ "deleted and nothing was installed.");

	// Every entry must land inside the extraction directory (no '..', no absolute path); the executable bits recorded in
	// the archive are restored on Unix, or the browser could not start.
	private void Extract(string archivePath, string extractDirectory, CancellationToken ct) {
		string root = Path.GetFullPath(extractDirectory) + Path.DirectorySeparatorChar;
		_fileSystem.Directory.CreateDirectory(extractDirectory);
		using Stream stream = _fileSystem.File.OpenRead(archivePath);
		using ZipArchive zip = new(stream, ZipArchiveMode.Read);
		foreach (ZipArchiveEntry entry in zip.Entries) {
			ct.ThrowIfCancellationRequested();
			string destination = Path.GetFullPath(Path.Combine(extractDirectory, entry.FullName));
			if (!destination.StartsWith(root, StringComparison.Ordinal)) {
				throw new HeadlessShellInstallException($"The chrome-headless-shell archive has an entry outside its folder: {entry.FullName}.");
			}
			if (entry.FullName.EndsWith('/')) {
				_fileSystem.Directory.CreateDirectory(destination);
				continue;
			}
			_fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(destination));
			using (Stream source = entry.Open())
			using (Stream target = _fileSystem.File.Create(destination)) {
				source.CopyTo(target);
			}
			int mode = (entry.ExternalAttributes >> 16) & 0x1FF;
			if (mode != 0 && !OperatingSystem.IsWindows()) {
				_fileSystem.File.SetUnixFileMode(destination, (UnixFileMode)mode);
			}
		}
	}

	// The version directory appears whole or not at all: a reader never sees a half-extracted browser. Another call that
	// installed it first wins, and this copy is dropped; a version directory WITHOUT the executable (quarantined, or
	// emptied by hand) is replaced, or the install could never repair itself.
	private void Publish(string extractDirectory) {
		if (_fileSystem.Directory.Exists(InstallDirectory)) {
			if (FindInstalled() is not null) {
				return;
			}
			_fileSystem.Directory.Delete(InstallDirectory, recursive: true);
		}
		try {
			_fileSystem.Directory.Move(extractDirectory, InstallDirectory);
		}
		catch (IOException) when (_fileSystem.Directory.Exists(InstallDirectory)) {
			// Installed concurrently.
		}
	}

	private void DeleteLeftovers() {
		DateTime cutoff = _timeProvider.GetUtcNow().UtcDateTime - LeftoverAge;
		foreach (string leftover in _fileSystem.Directory.GetFileSystemEntries(_browsersRoot, ".*-*")) {
			string name = Path.GetFileName(leftover);
			bool ours = name.StartsWith(".download-", StringComparison.Ordinal) || name.StartsWith(".extract-", StringComparison.Ordinal);
			if (ours && _fileSystem.Directory.GetLastWriteTimeUtc(leftover) < cutoff) {
				DeleteQuietly(leftover, leftover);
			}
		}
	}

	private void DeleteQuietly(string archivePath, string extractDirectory) {
		try {
			if (_fileSystem.File.Exists(archivePath)) {
				_fileSystem.File.Delete(archivePath);
			}
			if (_fileSystem.Directory.Exists(extractDirectory)) {
				_fileSystem.Directory.Delete(extractDirectory, recursive: true);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			// A leftover under ~/.clio/browsers is harmless; the next install uses new names.
		}
	}
}
