using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Controls the opt-in clio-ring desktop companion.
/// </summary>
[Verb("ring", HelpText = "Install, update, launch, inspect, or uninstall the experimental clio-ring desktop companion.")]
[FeatureToggle("ring")]
public sealed class RingCommandOptions {

	/// <summary>
	/// Gets or sets the lifecycle action. Supported values are launch, install, update, version, status, and uninstall.
	/// </summary>
	[Value(0, Required = false, Default = "launch", MetaName = "action",
		HelpText = "Action: launch|install|update|version|status|uninstall (default: launch).")]
	public string Action { get; set; }

	/// <summary>
	/// Gets or sets the release-manifest URL.
	/// </summary>
	[Option("manifest-url", Required = false,
		Default = RingDistributionDefaults.ManifestUrl,
		HelpText = "HTTPS URL of the clio-ring release manifest.")]
	public string ManifestUrl { get; set; }
}

/// <summary>
/// Executes lifecycle operations for the clio-ring desktop companion.
/// </summary>
public sealed class RingCommand : Command<RingCommandOptions> {

	private readonly IRingDistributionService _distributionService;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="RingCommand"/> class.
	/// </summary>
	public RingCommand(IRingDistributionService distributionService, ILogger logger) {
		_distributionService = distributionService;
		_logger = logger;
	}

	/// <inheritdoc/>
	public override int Execute(RingCommandOptions options) {
		if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) {
			_logger.WriteError("clio-ring is currently available only on Windows x64.");
			return 1;
		}
		try {
			RingDistributionResult result = _distributionService.ExecuteAsync(
				options.Action ?? "launch", options.ManifestUrl, CancellationToken.None).GetAwaiter().GetResult();
			if (result.Success) {
				_logger.WriteInfo(result.Message);
			}
			else {
				_logger.WriteError(result.Message);
			}
			return result.Success ? 0 : 1;
		}
		catch (HttpRequestException exception) {
			_logger.WriteError($"Could not contact the clio-ring release host: {exception.Message}");
			return 1;
		}
		catch (InvalidDataException exception) {
			_logger.WriteError($"The clio-ring release is invalid: {exception.Message}");
			return 1;
		}
		catch (IOException exception) {
			_logger.WriteError($"Could not update the local clio-ring installation: {exception.Message}");
			return 1;
		}
	}
}

/// <summary>
/// Performs secure lifecycle operations for clio-ring releases.
/// </summary>
public interface IRingDistributionService {
	/// <summary>
	/// Executes a lifecycle action.
	/// </summary>
	Task<RingDistributionResult> ExecuteAsync(string action, string manifestUrl, CancellationToken cancellationToken);
}

/// <summary>
/// Provides default release locations for clio-ring.
/// </summary>
public static class RingDistributionDefaults {
	/// <summary>
	/// Stable manifest published by the clio-ring release workflow.
	/// </summary>
	public const string ManifestUrl =
		"https://github.com/Advance-Technologies-Foundation/clio/releases/download/ring-latest/clio-ring-manifest.json";
}

/// <summary>
/// Represents the outcome of a Ring lifecycle action.
/// </summary>
public sealed record RingDistributionResult(bool Success, string Message);

/// <summary>
/// Describes one downloadable Ring release.
/// </summary>
public sealed record RingReleaseManifest(
	[property: JsonPropertyName("schemaVersion")] int SchemaVersion,
	[property: JsonPropertyName("version")] string Version,
	[property: JsonPropertyName("channel")] string Channel,
	[property: JsonPropertyName("rid")] string Rid,
	[property: JsonPropertyName("assetUrl")] string AssetUrl,
	[property: JsonPropertyName("sha256")] string Sha256,
	[property: JsonPropertyName("entryPoint")] string EntryPoint);

/// <summary>
/// Stores the active installed Ring version.
/// </summary>
public sealed record RingCurrentVersion(
	[property: JsonPropertyName("version")] string Version,
	[property: JsonPropertyName("entryPoint")] string EntryPoint);

/// <summary>
/// GitHub-backed implementation of the Ring distribution lifecycle.
/// </summary>
public sealed class RingDistributionService : IRingDistributionService {
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	private const int SupportedManifestSchema = 1;
	private const string SupportedRid = "win-x64";
	private const long MaxManifestBytes = 64 * 1024;
	private const long MaxArchiveBytes = 256 * 1024 * 1024;
	private const long MaxExpandedBytes = 1024L * 1024 * 1024;
	private const int MaxArchiveEntries = 4096;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IProcessExecutor _processExecutor;
	private readonly string _root;
	private readonly HashSet<string> _trustedHosts;
	private readonly bool _allowCustomRepository;

	/// <summary>
	/// Initializes a new instance of the <see cref="RingDistributionService"/> class.
	/// </summary>
	public RingDistributionService(IHttpClientFactory httpClientFactory, IProcessExecutor processExecutor)
		: this(httpClientFactory, processExecutor, Path.Combine(Environment.GetFolderPath(
			Environment.SpecialFolder.LocalApplicationData), "Creatio", "clio-ring"),
			["github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"], false) { }

	internal RingDistributionService(IHttpClientFactory httpClientFactory, IProcessExecutor processExecutor, string root,
		IEnumerable<string> trustedHosts = null, bool allowCustomRepository = true) {
		_httpClientFactory = httpClientFactory;
		_processExecutor = processExecutor;
		_root = root;
		_trustedHosts = new HashSet<string>(trustedHosts ?? ["github.com"], StringComparer.OrdinalIgnoreCase);
		_allowCustomRepository = allowCustomRepository;
	}

	/// <inheritdoc/>
	public async Task<RingDistributionResult> ExecuteAsync(
		string action, string manifestUrl, CancellationToken cancellationToken) {
		return action.Trim().ToLowerInvariant() switch {
			"launch" => await ExecuteWithLifecycleLockAsync(() => Task.FromResult(Launch())),
			"install" => await ExecuteWithLifecycleLockAsync(
				() => InstallAsync(manifestUrl, cancellationToken)),
			"update" => await ExecuteWithLifecycleLockAsync(
				() => InstallAsync(manifestUrl, cancellationToken)),
			"version" => DescribeVersion(),
			"status" => DescribeStatus(),
			"uninstall" => await ExecuteWithLifecycleLockAsync(
				() => Task.FromResult(Uninstall())),
			_ => new RingDistributionResult(false,
				$"Unknown Ring action '{action}'. Use launch, install, update, version, status, or uninstall.")
		};
	}

	private async Task<RingDistributionResult> InstallAsync(string manifestUrl, CancellationToken cancellationToken) {
		Uri manifestUri = RequireTrustedHttpsUri(manifestUrl, "manifest");
		ValidateRepositoryManifestUri(manifestUri);
		HttpClient client = _httpClientFactory.CreateClient();
		byte[] manifestBytes = await DownloadBoundedAsync(client, manifestUri, MaxManifestBytes, cancellationToken);
		RingReleaseManifest manifest;
		try {
			manifest = JsonSerializer.Deserialize<RingReleaseManifest>(manifestBytes)
				?? throw new InvalidDataException("The manifest is empty.");
		}
		catch (JsonException exception) {
			throw new InvalidDataException("The manifest is not valid JSON.", exception);
		}
		ValidateManifest(manifest);

		RingCurrentVersion current = ReadCurrent();
		if (current is not null && CompareVersions(manifest.Version, current.Version) < 0) {
			return new RingDistributionResult(false,
				$"Refusing to downgrade clio-ring from {current.Version} to {manifest.Version}. Uninstall first to roll back explicitly.");
		}
		if (string.Equals(current?.Version, manifest.Version, StringComparison.Ordinal)
			&& File.Exists(GetInstalledEntryPoint(current))) {
			return new RingDistributionResult(true, $"clio-ring {manifest.Version} is already installed.");
		}

		byte[] archive = await DownloadBoundedAsync(client,
			RequireTrustedHttpsUri(manifest.AssetUrl, "asset"), MaxArchiveBytes, cancellationToken);
		string actualHash = Convert.ToHexString(SHA256.HashData(archive));
		if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidDataException("The downloaded ZIP checksum does not match the release manifest.");
		}

		string versionsRoot = Path.Combine(_root, "versions");
		string destination = Path.Combine(versionsRoot, manifest.Version);
		string staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
		Directory.CreateDirectory(staging);
		try {
			ExtractArchive(archive, staging);
			string entryPoint = Path.Combine(staging, manifest.EntryPoint);
			if (!File.Exists(entryPoint)) {
				throw new InvalidDataException($"Entry point '{manifest.EntryPoint}' is missing from the ZIP.");
			}
			Directory.CreateDirectory(versionsRoot);
			if (Directory.Exists(destination)) {
				Directory.Delete(destination, true);
			}
			Directory.Move(staging, destination);
			WriteCurrent(new RingCurrentVersion(manifest.Version, manifest.EntryPoint));
		}
		finally {
			if (Directory.Exists(staging)) {
				Directory.Delete(staging, true);
			}
		}
		return new RingDistributionResult(true, $"Installed clio-ring {manifest.Version}.");
	}

	private RingDistributionResult Launch() {
		RingCurrentVersion current = ReadCurrent();
		if (current is null) {
			return new RingDistributionResult(false, "clio-ring is not installed. Run 'clio ring install'.");
		}
		string entryPoint = GetInstalledEntryPoint(current);
		if (!File.Exists(entryPoint)) {
			return new RingDistributionResult(false, "The current clio-ring installation is incomplete. Run 'clio ring update'.");
		}
		ProcessLaunchResult launch = _processExecutor.FireAndForgetAsync(
			new ProcessExecutionOptions(entryPoint, string.Empty)).GetAwaiter().GetResult();
		if (!launch.Started) {
			return new RingDistributionResult(false, $"Could not launch clio-ring: {launch.ErrorMessage}");
		}
		return new RingDistributionResult(true, $"Launched clio-ring {current.Version}.");
	}

	private RingDistributionResult DescribeVersion() {
		RingCurrentVersion current = ReadCurrent();
		return current is null
			? new RingDistributionResult(false, "clio-ring is not installed.")
			: new RingDistributionResult(true, current.Version);
	}

	private RingDistributionResult DescribeStatus() {
		RingCurrentVersion current = ReadCurrent();
		if (current is null) {
			return new RingDistributionResult(true, "clio-ring is not installed.");
		}
		bool complete = File.Exists(GetInstalledEntryPoint(current));
		return new RingDistributionResult(complete,
			complete ? $"clio-ring {current.Version} is installed." : "The clio-ring installation is incomplete.");
	}

	private RingDistributionResult Uninstall() {
		if (!Directory.Exists(_root)) {
			return new RingDistributionResult(true, "clio-ring is not installed.");
		}
		RingCurrentVersion current = ReadCurrent();
		if (current is not null && File.Exists(GetInstalledEntryPoint(current)) && !CanOpenExclusively(GetInstalledEntryPoint(current))) {
			return new RingDistributionResult(false,
				"clio-ring is running. Close it, then run 'clio ring uninstall' again.");
		}
		Directory.Delete(_root, true);
		return new RingDistributionResult(true, "clio-ring was uninstalled.");
	}

	private Uri RequireTrustedHttpsUri(string value, string label) {
		if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps) {
			throw new InvalidDataException($"The {label} URL must be an absolute HTTPS URL.");
		}
		if (!_trustedHosts.Contains(uri.DnsSafeHost)) {
			throw new InvalidDataException($"The {label} URL host '{uri.DnsSafeHost}' is not a trusted clio-ring release host.");
		}
		return uri;
	}

	private void ValidateManifest(RingReleaseManifest manifest) {
		if (manifest.SchemaVersion != SupportedManifestSchema) {
			throw new InvalidDataException($"Unsupported manifest schema {manifest.SchemaVersion}.");
		}
		if (!string.Equals(manifest.Rid, SupportedRid, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidDataException($"Unsupported runtime identifier '{manifest.Rid}'.");
		}
		if (string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.EntryPoint)
			|| string.IsNullOrWhiteSpace(manifest.Sha256)) {
			throw new InvalidDataException("The manifest is missing required release fields.");
		}
		if (!Regex.IsMatch(manifest.Version, "^0\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
			RegexOptions.CultureInvariant, RegexTimeout)) {
			throw new InvalidDataException("The manifest version is not a valid clio-ring 0.x preview version.");
		}
		if (Path.IsPathRooted(manifest.EntryPoint)
			|| manifest.EntryPoint.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				.Contains("..", StringComparer.Ordinal)) {
			throw new InvalidDataException("The manifest entry point must stay inside the release directory.");
		}
		Uri assetUri = RequireTrustedHttpsUri(manifest.AssetUrl, "asset");
		ValidateRepositoryAssetUri(assetUri, manifest);
	}

	private static void ExtractArchive(byte[] archive, string destination) {
		string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
		using MemoryStream stream = new(archive);
		using ZipArchive zip = new(stream, ZipArchiveMode.Read);
		if (zip.Entries.Count > MaxArchiveEntries) {
			throw new InvalidDataException($"The ZIP contains more than {MaxArchiveEntries} entries.");
		}
		long expandedBytes = 0;
		foreach (ZipArchiveEntry entry in zip.Entries) {
			if (entry.Length > MaxExpandedBytes - expandedBytes) {
				throw new InvalidDataException("The ZIP expands beyond the clio-ring release size limit.");
			}
			expandedBytes += entry.Length;
			if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > 1000) {
				throw new InvalidDataException("The ZIP contains an unsafe compression ratio.");
			}
			string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
			if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
				throw new InvalidDataException("The ZIP contains a path outside the installation directory.");
			}
			if (string.IsNullOrEmpty(entry.Name)) {
				Directory.CreateDirectory(target);
				continue;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			entry.ExtractToFile(target, true);
		}
	}

	private RingCurrentVersion ReadCurrent() {
		string path = Path.Combine(_root, "current.json");
		if (!File.Exists(path)) {
			return null;
		}
		try {
			RingCurrentVersion current = JsonSerializer.Deserialize<RingCurrentVersion>(File.ReadAllText(path));
			if (current is null || string.IsNullOrWhiteSpace(current.Version) || string.IsNullOrWhiteSpace(current.EntryPoint)) {
				throw new InvalidDataException("The current clio-ring version pointer is incomplete.");
			}
			ValidateCurrentEntryPoint(current.EntryPoint);
			ParseSemanticVersion(current.Version);
			return current;
		}
		catch (JsonException exception) {
			throw new InvalidDataException("The current clio-ring version pointer is not valid JSON.", exception);
		}
	}

	private void WriteCurrent(RingCurrentVersion current) {
		Directory.CreateDirectory(_root);
		string path = Path.Combine(_root, "current.json");
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(current));
		File.Move(temporary, path, true);
	}

	private string GetInstalledEntryPoint(RingCurrentVersion current) =>
		Path.Combine(_root, "versions", current.Version, current.EntryPoint);

	private async Task<byte[]> DownloadBoundedAsync(HttpClient client, Uri uri, long maximumBytes,
		CancellationToken cancellationToken) {
		using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		Uri finalUri = response.RequestMessage?.RequestUri ?? uri;
		RequireTrustedHttpsUri(finalUri.AbsoluteUri, "redirected release");
		if (response.Content.Headers.ContentLength > maximumBytes) {
			throw new InvalidDataException($"The release payload exceeds the {maximumBytes} byte limit.");
		}
		await response.Content.LoadIntoBufferAsync(maximumBytes);
		return await response.Content.ReadAsByteArrayAsync(cancellationToken);
	}

	private static int CompareVersions(string left, string right) {
		(Version leftCore, string[] leftPreRelease) = ParseSemanticVersion(left);
		(Version rightCore, string[] rightPreRelease) = ParseSemanticVersion(right);
		int coreComparison = leftCore.CompareTo(rightCore);
		if (coreComparison != 0) {
			return coreComparison;
		}
		if (leftPreRelease.Length == 0 || rightPreRelease.Length == 0) {
			return leftPreRelease.Length == rightPreRelease.Length ? 0 : leftPreRelease.Length == 0 ? 1 : -1;
		}
		for (int index = 0; index < Math.Max(leftPreRelease.Length, rightPreRelease.Length); index++) {
			if (index >= leftPreRelease.Length || index >= rightPreRelease.Length) {
				return leftPreRelease.Length.CompareTo(rightPreRelease.Length);
			}
			bool leftNumeric = int.TryParse(leftPreRelease[index], out int leftNumber);
			bool rightNumeric = int.TryParse(rightPreRelease[index], out int rightNumber);
			int comparison = leftNumeric && rightNumeric
				? leftNumber.CompareTo(rightNumber)
				: leftNumeric != rightNumeric
					? leftNumeric ? -1 : 1
					: string.Compare(leftPreRelease[index], rightPreRelease[index], StringComparison.Ordinal);
			if (comparison != 0) {
				return comparison;
			}
		}
		return 0;
	}

	private static (Version Core, string[] PreRelease) ParseSemanticVersion(string value) {
		string[] parts = value.Split('-', 2);
		if (!Regex.IsMatch(value, "^0\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
			RegexOptions.CultureInvariant, RegexTimeout)
			|| !Version.TryParse(parts[0], out Version version)) {
			throw new InvalidDataException($"Invalid clio-ring version '{value}'.");
		}
		return (version, parts.Length == 1 ? [] : parts[1].Split('.'));
	}

	private static bool CanOpenExclusively(string path) {
		try {
			using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
			return true;
		}
		catch (IOException) {
			return false;
		}
		catch (UnauthorizedAccessException) {
			return false;
		}
	}

	private static void ValidateCurrentEntryPoint(string entryPoint) {
		if (Path.IsPathRooted(entryPoint)
			|| entryPoint.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				.Contains("..", StringComparer.Ordinal)) {
			throw new InvalidDataException("The current clio-ring entry point escapes its version directory.");
		}
	}

	private async Task<RingDistributionResult> ExecuteWithLifecycleLockAsync(
		Func<Task<RingDistributionResult>> operation) {
		string parent = Path.GetDirectoryName(_root) ?? throw new InvalidDataException("The clio-ring install root is invalid.");
		Directory.CreateDirectory(parent);
		string lockPath = _root + ".lifecycle.lock";
		FileStream lifecycleLock;
		try {
			lifecycleLock = File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		}
		catch (IOException) {
			return new RingDistributionResult(false,
				"Another clio-ring install, update, or uninstall is already running.");
		}
		using (lifecycleLock) {
			return await operation();
		}
	}

	private void ValidateRepositoryManifestUri(Uri uri) {
		if (_allowCustomRepository) {
			return;
		}
		const string expectedPath =
			"/Advance-Technologies-Foundation/clio/releases/download/ring-latest/clio-ring-manifest.json";
		if (!string.Equals(uri.DnsSafeHost, "github.com", StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(uri.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidDataException("The manifest URL must reference this repository's ring-latest release.");
		}
	}

	private void ValidateRepositoryAssetUri(Uri uri, RingReleaseManifest manifest) {
		if (_allowCustomRepository) {
			return;
		}
		string expectedPath =
			$"/Advance-Technologies-Foundation/clio/releases/download/ring/v{manifest.Version}/clio-ring-{manifest.Version}-{SupportedRid}.zip";
		string actualPath = Uri.UnescapeDataString(uri.AbsolutePath);
		if (!string.Equals(uri.DnsSafeHost, "github.com", StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidDataException("The asset URL does not match this repository's versioned clio-ring release.");
		}
	}
}
