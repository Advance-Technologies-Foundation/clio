using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
public class HeadlessShellInstallException : InvalidOperationException {

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

/// <summary>
/// The download stopped part-way (the call's time ran out or the connection broke); what arrived is kept and the next
/// install resumes from it.
/// </summary>
public sealed class HeadlessShellDownloadIncompleteException : HeadlessShellInstallException {

	/// <summary>Creates the exception.</summary>
	/// <param name="message">What arrived and what to do next.</param>
	/// <param name="downloadedBytes">Bytes kept so far.</param>
	/// <param name="totalBytes">The archive's size.</param>
	public HeadlessShellDownloadIncompleteException(string message, long downloadedBytes, long totalBytes) : base(message) {
		DownloadedBytes = downloadedBytes;
		TotalBytes = totalBytes;
	}

	/// <summary>Bytes kept so far.</summary>
	public long DownloadedBytes { get; }

	/// <summary>The archive's size.</summary>
	public long TotalBytes { get; }
}

/// <inheritdoc cref="IHeadlessShellInstaller" />
public sealed class HeadlessShellInstaller : IHeadlessShellInstaller {

	/// <summary>The name of the <see cref="IHttpClientFactory"/> client the download uses.</summary>
	public const string HttpClientName = "clio-headless-shell";

	/// <summary>
	/// The handler of the <see cref="HttpClientName"/> client: it goes through <see cref="AmbientProxy"/>, so the download
	/// reaches the internet the way the user's machine does even in MCP mode, where the default proxy is cleared.
	/// </summary>
	public static HttpMessageHandler CreateHttpHandler() => new SocketsHttpHandler { Proxy = new AmbientProxy(), UseProxy = true };

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
		string partialPath = PartialPath;
		string extractDirectory = Path.Combine(_browsersRoot, $".extract-{Guid.NewGuid():N}");
		string host = _source.DisplayName;
		DownloadProgress progress = new();
		long keptBefore = 0;
		try {
			Uri url = _source.ArchiveUrl(_platformKey);
			host = HostOf(url);
			_fileSystem.Directory.CreateDirectory(_browsersRoot);
			DeleteLeftovers(partialPath);
			keptBefore = KeptBytes(partialPath);
			await DownloadAsync(url, host, archive, partialPath, progress, budget.Token).ConfigureAwait(false);
			VerifyChecksum(partialPath, archive);
			Extract(partialPath, extractDirectory, budget.Token);
			string relativeExecutable = _source.ExecutableRelativePath(_platformKey);
			if (!_fileSystem.File.Exists(Path.Combine(extractDirectory, relativeExecutable))) {
				DeletePartial(partialPath);
				throw new HeadlessShellInstallException($"The downloaded archive has no {relativeExecutable}.");
			}
			Publish(extractDirectory);
			DeletePartial(partialPath);
			return FindInstalled() ?? throw new HeadlessShellInstallException(
				$"The headless browser was extracted but is not at {InstallDirectory}.");
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
			throw Stopped(progress, keptBefore, archive, host, $"the call's {DownloadBudget.TotalSeconds:0} s ran out", null);
		}
		catch (HttpRequestException exception) {
			throw Stopped(progress, keptBefore, archive, host, $"the connection to {host} failed ({exception.Message})", exception);
		}
		catch (IOException exception) when (progress.Receiving) {
			throw Stopped(progress, keptBefore, archive, host, $"the transfer from {host} broke off ({exception.Message})", exception);
		}
		catch (InvalidDataException exception) {
			DeletePartial(partialPath);
			throw new HeadlessShellInstallException(
				$"{_source.DisplayName} {_source.Version} could not be unpacked: {exception.Message} The download was "
				+ "deleted; nothing was installed; call page-smoke again to retry.", exception);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			throw new HeadlessShellInstallException(
				$"{_source.DisplayName} {_source.Version} could not be installed into {_browsersRoot}: {exception.Message} "
				+ "Nothing was installed; call page-smoke again to retry.", exception);
		}
		finally {
			DeleteQuietly(extractDirectory);
		}
	}

	// The download is kept under ONE name per version and platform, so a call that runs out of time leaves what it got
	// and the next call resumes it with an HTTP Range request: on a slow link every call makes progress instead of
	// restarting a 100+ MB transfer from zero.
	private string PartialPath => Path.Combine(_browsersRoot, $".partial-{_source.Version}-{_platformKey}.zip");

	private sealed class DownloadProgress {
		public long Bytes { get; set; }
		public long AddedThisCall { get; set; }
		public bool Receiving { get; set; }
	}

	// Progress made on THIS call is resumable work: browser-downloading, call again. A call that added nothing (offline,
	// host blocked) is a failure even with an older partial kept, or the agent would call again forever.
	private HeadlessShellInstallException Stopped(DownloadProgress progress, long keptBefore, HeadlessShellRelease.Archive archive,
		string host, string reason, Exception cause) {
		if (progress.AddedThisCall > 0) {
			return Incomplete(progress, archive, reason);
		}
		string kept = keptBefore > 0 ? $" {Megabytes(keptBefore)} MB from an earlier call are kept for when it works again." : string.Empty;
		return new HeadlessShellInstallException(
			$"{_source.DisplayName} could not be downloaded from {host}: {reason}; nothing arrived on this call. The machine "
			+ $"may be offline or the host blocked.{kept} {NetworkHint}", cause);
	}

	private long KeptBytes(string partialPath) {
		try {
			return _fileSystem.File.Exists(partialPath) ? _fileSystem.FileInfo.New(partialPath).Length : 0;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			return 0;
		}
	}

	private HeadlessShellDownloadIncompleteException Incomplete(DownloadProgress progress, HeadlessShellRelease.Archive archive,
		string reason) =>
		new($"Downloaded {Megabytes(progress.Bytes)} of {Megabytes(archive.Size)} MB of {_source.DisplayName} "
			+ $"{_source.Version} before {reason}. What arrived is kept and the next call resumes from there: call page-smoke "
			+ "again with the same arguments, without browser, to continue.", progress.Bytes, archive.Size);

	private static long Megabytes(long bytes) => bytes / (1024 * 1024);

	// Appends to the kept partial file, resuming at its length when the server honours Range (206) and starting over
	// when it does not (200). The size is checked as bytes arrive, so a wrong or hostile mirror cannot fill the disk.
	private async Task DownloadAsync(Uri url, string host, HeadlessShellRelease.Archive archive, string partialPath,
		DownloadProgress progress, CancellationToken ct) {
		await using Stream target = _fileSystem.File.Open(partialPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		long existing = target.Length;
		if (existing > archive.Size) {
			target.SetLength(0);
			existing = 0;
		}
		progress.Bytes = existing;
		if (existing == archive.Size) {
			return;
		}
		HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
		using HttpRequestMessage request = new(HttpMethod.Get, url);
		if (existing > 0) {
			request.Headers.Range = new RangeHeaderValue(existing, null);
		}
		using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
			.ConfigureAwait(false);
		long resumeAt = ResumeOffset(response, existing, archive, host, target);
		progress.Bytes = resumeAt;
		target.Seek(resumeAt, SeekOrigin.Begin);
		await using Stream source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
		await CopyBodyAsync(source, target, archive, progress, ct).ConfigureAwait(false);
	}

	// Where the body continues the kept file: at its length for a matching 206, at zero (the file truncated) for a 200.
	private long ResumeOffset(HttpResponseMessage response, long existing, HeadlessShellRelease.Archive archive, string host,
		Stream target) {
		if (response.StatusCode == HttpStatusCode.PartialContent && existing > 0) {
			ContentRangeHeaderValue range = response.Content.Headers.ContentRange;
			if (range?.From != existing || range.Length != archive.Size) {
				target.SetLength(0);
				throw Mismatch($"the server resumed it as {range}, expected bytes {existing}-/{archive.Size}");
			}
			return existing;
		}
		if (!response.IsSuccessStatusCode) {
			// Any refusal of a resume (416 included) drops what was kept, so the next call starts clean.
			target.SetLength(0);
			throw new HeadlessShellInstallException(
				$"Downloading {_source.DisplayName} {_source.Version} from {host} failed: HTTP "
				+ $"{(int)response.StatusCode} {response.ReasonPhrase}. {NetworkHint}");
		}
		target.SetLength(0);
		if (response.Content.Headers.ContentLength is long announced && announced != archive.Size) {
			throw Mismatch($"its size is {announced} bytes, expected {archive.Size}");
		}
		return 0;
	}

	private async Task CopyBodyAsync(Stream source, Stream target, HeadlessShellRelease.Archive archive,
		DownloadProgress progress, CancellationToken ct) {
		byte[] buffer = new byte[CopyBufferBytes];
		progress.Receiving = true;
		int read;
		while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0) {
			if (progress.Bytes + read > archive.Size) {
				target.SetLength(0);
				throw Mismatch($"it is larger than the expected {archive.Size} bytes");
			}
			await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
			progress.Bytes += read;
			progress.AddedThisCall += read;
		}
		progress.Receiving = false;
		if (progress.Bytes != archive.Size) {
			throw new HttpRequestException($"the server ended the body at {progress.Bytes} of {archive.Size} bytes");
		}
	}

	private void VerifyChecksum(string partialPath, HeadlessShellRelease.Archive archive) {
		string actual;
		using (Stream stream = _fileSystem.File.OpenRead(partialPath)) {
			actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
		}
		if (!string.Equals(actual, archive.Sha256, StringComparison.Ordinal)) {
			DeletePartial(partialPath);
			throw Mismatch($"its SHA-256 is {actual}, expected {archive.Sha256}");
		}
	}

	private void DeletePartial(string partialPath) {
		try {
			_fileSystem.File.Delete(partialPath);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			// The next call overwrites or verifies it.
		}
	}

	// Scheme, host and port only: Uri.Authority (and GetLeftPart) keep a mirror's user:password@, which would then reach
	// the error text and the MCP reply.
	internal static string HostOf(Uri url) =>
		url.IsDefaultPort ? $"{url.Scheme}://{url.Host}" : $"{url.Scheme}://{url.Host}:{url.Port}";

	private static string NetworkHint =>
		"The download goes through HTTPS_PROXY / HTTP_PROXY when they are set and the system proxy (PAC included) "
		+ "otherwise, with NO_PROXY honoured. On a network that "
		+ $"reaches only a mirror of Chrome for Testing, set {HeadlessShellRelease.MirrorEnvVar} to its base URL.";

	private HeadlessShellInstallException Mismatch(string detail) =>
		new($"The downloaded {_source.DisplayName} {_source.Version} archive did not verify: {detail}. It was "
			+ "deleted and nothing was installed; calling page-smoke again downloads it from the start.");

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

	// Extraction folders a killed worker left, and partial downloads of another version or platform.
	private void DeleteLeftovers(string partialPath) {
		DateTime cutoff = _timeProvider.GetUtcNow().UtcDateTime - LeftoverAge;
		foreach (string leftover in _fileSystem.Directory.GetFileSystemEntries(_browsersRoot, ".*-*")) {
			string name = Path.GetFileName(leftover);
			bool staleExtract = name.StartsWith(".extract-", StringComparison.Ordinal)
				&& _fileSystem.Directory.GetLastWriteTimeUtc(leftover) < cutoff;
			bool otherPartial = name.StartsWith(".partial-", StringComparison.Ordinal)
				&& !string.Equals(leftover, partialPath, StringComparison.Ordinal);
			if (staleExtract || otherPartial) {
				DeleteQuietly(leftover);
			}
		}
	}

	private void DeleteQuietly(string path) {
		try {
			if (_fileSystem.File.Exists(path)) {
				_fileSystem.File.Delete(path);
			}
			if (_fileSystem.Directory.Exists(path)) {
				_fileSystem.Directory.Delete(path, recursive: true);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			// A leftover under ~/.clio/browsers is harmless; the next install sweeps it.
		}
	}
}
