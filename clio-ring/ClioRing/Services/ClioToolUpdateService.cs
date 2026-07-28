using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClioRing.Ipc;

namespace ClioRing.Services;

/// <summary>Runs a bounded child process and returns its captured exit result.</summary>
public interface IClioToolProcessRunner {
	/// <summary>Runs the exact executable and arguments without shell interpretation.</summary>
	Task<ClioToolProcessRunResult> RunAsync(string executable, IReadOnlyList<string> arguments,
		TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Captured child-process result.</summary>
/// <param name="ExitCode">Process exit code, or -1 after timeout.</param>
/// <param name="StandardOutput">Captured standard output.</param>
/// <param name="StandardError">Captured standard error.</param>
public sealed record ClioToolProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Finds and terminates processes bound to the trusted clio shim.</summary>
public interface IClioToolProcessInspector {
	/// <summary>Returns trusted processes corroborated by Windows Restart Manager as locking the shim.</summary>
	IReadOnlyList<ClioToolProcess> FindLockingTrustedProcesses(string trustedExecutablePath);

	/// <summary>Terminates a process only if its path and start identity still match the snapshot.</summary>
	Task<bool> TerminateRevalidatedAsync(ClioToolProcess process, CancellationToken cancellationToken);
}

/// <summary>Resolves and validates the trusted Release global-tool installation.</summary>
public interface IClioToolInstallation {
	/// <summary>Absolute trusted clio global-tool shim path.</summary>
	string TargetPath { get; }

	/// <summary>Absolute trusted dotnet host used for global-tool management, when installed.</summary>
	string? DotNetHostPath { get; }

	/// <summary>True only when the trusted shim and its global-tool store exist.</summary>
	bool IsInstalled { get; }
}

/// <summary>Production implementation of global clio tool update discovery and installation.</summary>
public sealed class ClioToolUpdateService : IClioToolUpdateService {
	private static readonly Uri RegistrationIndex = new(
		"https://api.nuget.org/v3/registration5-semver1/clio/index.json");
	private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(2);
	private static readonly TimeSpan InventoryTimeout = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(8);
	private const string IsolatedNuGetConfig = """
		<?xml version="1.0" encoding="utf-8"?>
		<configuration>
		  <packageSources>
		    <clear />
		    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
		  </packageSources>
		</configuration>
		""";
	private readonly HttpClient _httpClient;
	private readonly IClioToolProcessRunner _runner;
	private readonly IClioToolProcessInspector _processInspector;
	private readonly IClioToolInstallation _installation;
	private readonly IClioUpdateStateStore _stateStore;
	private readonly TimeProvider _timeProvider;
	private readonly IClioProcessGate _processGate;

	/// <summary>Creates the service from its network, process, and inspection boundaries.</summary>
	public ClioToolUpdateService(HttpClient httpClient, IClioToolProcessRunner runner,
		IClioToolProcessInspector processInspector, IClioToolInstallation installation,
		IClioUpdateStateStore stateStore, TimeProvider timeProvider, IClioProcessGate processGate) {
		_httpClient = httpClient;
		_runner = runner;
		_processInspector = processInspector;
		_installation = installation;
		_stateStore = stateStore;
		_timeProvider = timeProvider;
		_processGate = processGate;
	}

	/// <inheritdoc />
	public async Task<ClioToolUpdateCheck?> CheckAsync(CancellationToken cancellationToken = default,
		bool force = false) {
		string targetPath = _installation.TargetPath;
		string? dotnetHostPath = _installation.DotNetHostPath;
		if (!_installation.IsInstalled || dotnetHostPath is null) {
			return null;
		}
		ClioUpdateState? cached = _stateStore.Read();
		DateTimeOffset now = _timeProvider.GetUtcNow();
		if (!force && cached is not null && cached.LastCheckedUtc <= now
			&& now - cached.LastCheckedUtc < CheckInterval) {
			return new ClioToolUpdateCheck(cached.InstalledVersion, cached.AvailableVersion, targetPath);
		}
		string? installedVersion = await ReadInstalledVersionAsync(dotnetHostPath, cancellationToken)
			.ConfigureAwait(false);
		if (installedVersion is null) {
			return null;
		}
		string? availableVersion = await FindLatestListedStableVersionAsync(cancellationToken)
			.ConfigureAwait(false);
		if (availableVersion is null) {
			return null;
		}
		_stateStore.Write(new ClioUpdateState(now, installedVersion, availableVersion,
			cached?.NotifiedVersion));
		return new ClioToolUpdateCheck(installedVersion, availableVersion, targetPath);
	}

	/// <inheritdoc />
	public async Task<ClioToolUpdateResult> UpdateAsync(ClioToolUpdateCheck update,
		CancellationToken cancellationToken = default) {
		if (!IsTrustedSnapshot(update)) {
			return Failed("The installed Release clio path changed. Restart Ring and check again.");
		}
		string? dotnetHostPath = _installation.DotNetHostPath;
		if (dotnetHostPath is null) {
			return Failed("The trusted dotnet host was not found. Repair the .NET installation and retry.");
		}
		await using IAsyncDisposable updateLease = await _processGate.AcquireUpdateLeaseAsync(cancellationToken)
			.ConfigureAwait(false);
		return await UpdateCoreAsync(update, dotnetHostPath, cancellationToken).ConfigureAwait(false);
	}

	private async Task<ClioToolUpdateResult> UpdateCoreAsync(ClioToolUpdateCheck update,
		string dotnetHostPath, CancellationToken cancellationToken) {
		string? currentVersion = await ReadInstalledVersionAsync(dotnetHostPath, cancellationToken)
			.ConfigureAwait(false);
		if (!string.Equals(currentVersion, update.InstalledVersion, StringComparison.OrdinalIgnoreCase)) {
			return new ClioToolUpdateResult(ClioToolUpdateOutcome.RefreshRequired,
				"The installed Release clio version changed. Ring is checking again.",
				Array.Empty<ClioToolProcess>());
		}
		IReadOnlyList<ClioToolProcess> processes =
			_processInspector.FindLockingTrustedProcesses(update.TargetPath);
		if (processes.Count > 0) {
			return new ClioToolUpdateResult(ClioToolUpdateOutcome.Blocked,
				"Running MCP servers are using the installed Release clio.", processes);
		}

		ClioToolProcessRunResult result;
		string configDirectory = Path.Combine(Path.GetTempPath(), $"clio-ring-nuget-{Guid.NewGuid():N}");
		string configPath = Path.Combine(configDirectory, "NuGet.Config");
		try {
			Directory.CreateDirectory(configDirectory);
			await File.WriteAllTextAsync(configPath, IsolatedNuGetConfig,
				System.Text.Encoding.UTF8, cancellationToken).ConfigureAwait(false);
			result = await _runner.RunAsync(dotnetHostPath,
				new[] { "tool", "update", "--global", "clio", "--version", update.AvailableVersion,
					"--configfile", configPath },
				UpdateTimeout, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			throw;
		}
		catch (Exception exception) when (exception is Win32Exception or IOException
			or InvalidOperationException or UnauthorizedAccessException) {
			return Failed("The clio update could not start. No processes were terminated.");
		}
		finally {
			TryDeleteUpdateConfig(configDirectory);
		}
		string? verifiedVersion = result.ExitCode == 0
			? await ReadInstalledVersionAsync(dotnetHostPath, cancellationToken).ConfigureAwait(false)
			: null;
		if (result.ExitCode == 0 && string.Equals(verifiedVersion,
			update.AvailableVersion, StringComparison.OrdinalIgnoreCase)) {
			ClioUpdateState? previous = _stateStore.Read();
			_stateStore.Write(new ClioUpdateState(_timeProvider.GetUtcNow(), update.AvailableVersion,
				update.AvailableVersion, previous?.NotifiedVersion));
			return new ClioToolUpdateResult(ClioToolUpdateOutcome.Success,
				$"clio {update.AvailableVersion} is installed.", Array.Empty<ClioToolProcess>());
		}

		return Failed(result.ExitCode == -1
			? "The clio update timed out. No processes were terminated."
			: "The clio update failed. No processes were terminated.");
	}

	/// <inheritdoc />
	public async Task<ClioToolUpdateResult> TerminateAndRetryAsync(ClioToolUpdateCheck update,
		IReadOnlyList<ClioToolProcess> confirmedProcesses, CancellationToken cancellationToken = default) {
		if (!IsTrustedSnapshot(update) || confirmedProcesses.Count == 0) {
			return Failed("The confirmed clio process list is no longer valid. Check for updates again.");
		}
		string? dotnetHostPath = _installation.DotNetHostPath;
		if (dotnetHostPath is null) {
			return Failed("The trusted dotnet host was not found. Repair the .NET installation and retry.");
		}
		await using IAsyncDisposable updateLease = await _processGate.AcquireUpdateLeaseAsync(cancellationToken)
			.ConfigureAwait(false);
		foreach (ClioToolProcess process in confirmedProcesses) {
			if (!PathsEqual(process.ExecutablePath, update.TargetPath)
				|| !await _processInspector.TerminateRevalidatedAsync(process, cancellationToken)
					.ConfigureAwait(false)) {
				return Failed("A confirmed clio process changed or exited. The retry was canceled; any clio processes already stopped remain stopped.");
			}
		}
		return await UpdateCoreAsync(update, dotnetHostPath, cancellationToken).ConfigureAwait(false);
	}

	private async Task<string?> FindLatestListedStableVersionAsync(CancellationToken cancellationToken) {
		using Stream stream = await _httpClient.GetStreamAsync(RegistrationIndex, cancellationToken)
			.ConfigureAwait(false);
		using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
			.ConfigureAwait(false);
		var versions = new List<string>();
		if (!document.RootElement.TryGetProperty("items", out JsonElement pages)
			|| pages.ValueKind != JsonValueKind.Array) {
			return null;
		}
		foreach (JsonElement page in pages.EnumerateArray()) {
			if (page.TryGetProperty("items", out JsonElement leaves) && leaves.ValueKind == JsonValueKind.Array) {
				CollectListedStableVersions(leaves, versions);
			}
			else if (page.TryGetProperty("@id", out JsonElement pageId)
				&& pageId.ValueKind == JsonValueKind.String) {
				using Stream pageStream = await _httpClient.GetStreamAsync(pageId.GetString()!, cancellationToken)
					.ConfigureAwait(false);
				using JsonDocument pageDocument = await JsonDocument.ParseAsync(pageStream,
					cancellationToken: cancellationToken).ConfigureAwait(false);
				if (pageDocument.RootElement.TryGetProperty("items", out JsonElement remoteLeaves)
					&& remoteLeaves.ValueKind == JsonValueKind.Array) {
					CollectListedStableVersions(remoteLeaves, versions);
				}
			}
		}
		return versions.OrderByDescending(version => version, Comparer<string>.Create(ClioToolVersion.Compare))
			.FirstOrDefault();
	}

	private static void CollectListedStableVersions(JsonElement leaves, ICollection<string> versions) {
		foreach (JsonElement leaf in leaves.EnumerateArray()) {
			if (!leaf.TryGetProperty("catalogEntry", out JsonElement catalogEntry)
				|| catalogEntry.ValueKind != JsonValueKind.Object
				|| !catalogEntry.TryGetProperty("version", out JsonElement versionElement)
				|| versionElement.ValueKind != JsonValueKind.String) {
				continue;
			}
			bool listed = !catalogEntry.TryGetProperty("listed", out JsonElement listedElement)
				|| listedElement.ValueKind != JsonValueKind.False;
			string version = versionElement.GetString()!;
			if (listed && ClioToolVersion.IsStable(version)) {
				versions.Add(version);
			}
		}
	}

	private bool IsTrustedSnapshot(ClioToolUpdateCheck update) =>
		ClioToolVersion.IsStable(update.InstalledVersion)
		&& ClioToolVersion.IsStable(update.AvailableVersion)
		&& PathsEqual(update.TargetPath, _installation.TargetPath)
		&& _installation.IsInstalled;

	private async Task<string?> ReadInstalledVersionAsync(string dotnetHostPath,
		CancellationToken cancellationToken) {
		ClioToolProcessRunResult inventory;
		try {
			inventory = await _runner.RunAsync(dotnetHostPath,
				new[] { "tool", "list", "--global", "--format", "json" }, InventoryTimeout,
				cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is Win32Exception or IOException
			or InvalidOperationException or UnauthorizedAccessException) {
			return null;
		}
		if (inventory.ExitCode != 0 || string.IsNullOrWhiteSpace(inventory.StandardOutput)) {
			return null;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(inventory.StandardOutput);
			if (!document.RootElement.TryGetProperty("data", out JsonElement tools)
				|| tools.ValueKind != JsonValueKind.Array) {
				return null;
			}
			foreach (JsonElement tool in tools.EnumerateArray()) {
				if (tool.TryGetProperty("packageId", out JsonElement packageId)
					&& string.Equals(packageId.GetString(), "clio", StringComparison.OrdinalIgnoreCase)
					&& tool.TryGetProperty("version", out JsonElement version)) {
					string? value = version.GetString();
					return value is not null && ClioToolVersion.IsStable(value) ? value : null;
				}
			}
			return null;
		}
		catch (JsonException) {
			return null;
		}
	}

	private static void TryDeleteUpdateConfig(string configDirectory) {
		try {
			if (Directory.Exists(configDirectory)) {
				Directory.Delete(configDirectory, recursive: true);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			// The fixed config contains no credentials. Cleanup remains best-effort after the updater exits.
		}
	}

	private static bool PathsEqual(string left, string right) =>
		string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
			OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

	private static ClioToolUpdateResult Failed(string message) =>
		new(ClioToolUpdateOutcome.Failed, message, Array.Empty<ClioToolProcess>());
}

/// <summary>Filesystem-backed trusted global-tool installation locator.</summary>
public sealed class ClioToolInstallation : IClioToolInstallation {
	/// <inheritdoc />
	public string TargetPath => Path.GetFullPath(ClioIpcSettings.Default.Command);

	/// <inheritdoc />
	public string? DotNetHostPath => ResolveDotNetHostPath();

	/// <inheritdoc />
	public bool IsInstalled {
		get {
			if (!File.Exists(TargetPath)) {
				return false;
			}
		DirectoryInfo? toolsDirectory = Directory.GetParent(TargetPath);
		if (toolsDirectory is null) {
			return false;
		}
		string storePath = Path.Combine(toolsDirectory.FullName, ".store", "clio");
		return Directory.Exists(storePath);
		}
	}

	private static string? ResolveDotNetHostPath() {
		string executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
		var candidates = new List<string?> {
			Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
			CombineRoot(Environment.GetEnvironmentVariable("DOTNET_ROOT"), executableName),
			CombineRoot(Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"), executableName)
		};
		if (OperatingSystem.IsWindows()) {
			candidates.Add(CombineRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
				"dotnet", executableName));
		}
		return candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate))
			.Select(candidate => Path.GetFullPath(candidate!))
			.FirstOrDefault(File.Exists);
	}

	private static string? CombineRoot(string? root, params string[] parts) =>
		string.IsNullOrWhiteSpace(root) ? null : Path.Combine(new[] { root }.Concat(parts).ToArray());
}

/// <summary>Default shell-free process runner for dotnet tool update.</summary>
public sealed class ClioToolProcessRunner : IClioToolProcessRunner {
	/// <inheritdoc />
	public async Task<ClioToolProcessRunResult> RunAsync(string executable, IReadOnlyList<string> arguments,
		TimeSpan timeout, CancellationToken cancellationToken) {
		var startInfo = new ProcessStartInfo(executable) {
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		foreach (string argument in arguments) {
			startInfo.ArgumentList.Add(argument);
		}
		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException($"Could not start {executable}.");
		Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutSource.CancelAfter(timeout);
		try {
			await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
			bool stopped = await TerminateOwnedProcessAsync(process).ConfigureAwait(false);
			return stopped
				? new ClioToolProcessRunResult(-1, await output.ConfigureAwait(false),
					await error.ConfigureAwait(false))
				: new ClioToolProcessRunResult(-1, string.Empty,
					"The updater did not stop within the cancellation window.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			await TerminateOwnedProcessAsync(process).ConfigureAwait(false);
			throw;
		}
		return new ClioToolProcessRunResult(process.ExitCode, await output.ConfigureAwait(false),
			await error.ConfigureAwait(false));
	}

	private static async Task<bool> TerminateOwnedProcessAsync(Process process) {
		try {
			if (!process.HasExited) { process.Kill(entireProcessTree: true); }
		}
		catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) {
			// The child exited or became inaccessible during the cancellation race.
		}
		using var waitSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		try {
			await process.WaitForExitAsync(waitSource.Token).ConfigureAwait(false);
			return true;
		}
		catch (Exception exception) when (exception is InvalidOperationException or Win32Exception
			or OperationCanceledException) {
			return false;
		}
	}
}
