using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Clio.Package;
using Clio.WebApplication;

namespace Clio.Common;

/// <summary>
/// Result of a ClioGate ensure operation.
/// </summary>
/// <param name="AlreadyInstalled">True when ClioGate was already present — no action taken.</param>
/// <param name="JustInstalled">True when ClioGate was absent and was installed during this call.</param>
/// <param name="Warning">Non-null when something noteworthy happened (auto-install or failure).</param>
public record ClioGateEnsureResult(bool AlreadyInstalled, bool JustInstalled, string? Warning) {
	public static ClioGateEnsureResult Present() =>
		new(AlreadyInstalled: true, JustInstalled: false, Warning: null);

	public static ClioGateEnsureResult Installed(string envUri) =>
		new(AlreadyInstalled: false, JustInstalled: true,
			Warning: $"ClioGate was not installed on '{envUri}'. It was installed automatically and Creatio was restarted.");

	public static ClioGateEnsureResult Failed(string envUri, string reason) =>
		new(AlreadyInstalled: false, JustInstalled: false,
			Warning: $"ClioGate is not installed on '{envUri}' and auto-install failed: {reason}. OAuth SecureText settings (e.g. IdentityServerClientSecret) may not be readable.");
}

/// <summary>
/// Ensures ClioGate is installed on the active Creatio environment.
/// Silently installs when absent and caches confirmed environments to avoid repeated checks.
/// </summary>
public interface IClioGateEnsurer {
	/// <summary>
	/// Checks whether ClioGate is installed. Installs it automatically if absent.
	/// Uses the <see cref="EnvironmentSettings"/> injected for the current DI container scope.
	/// </summary>
	ClioGateEnsureResult EnsureInstalled();
}

/// <inheritdoc/>
public sealed class ClioGateEnsurer : IClioGateEnsurer {

	// Shared across all container instances — keyed by normalized URI.
	private static readonly ConcurrentDictionary<string, bool> ConfirmedEnvironments =
		new(StringComparer.OrdinalIgnoreCase);

	// Per-environment install lock — prevents concurrent installs for the same URI.
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> InstallLocks =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IClioGateway _clioGateway;
	private readonly IPackageInstaller _packageInstaller;
	private readonly IApplication _application;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;

	public ClioGateEnsurer(
		EnvironmentSettings environmentSettings,
		IClioGateway clioGateway,
		IPackageInstaller packageInstaller,
		IApplication application,
		IWorkingDirectoriesProvider workingDirectoriesProvider) {
		_environmentSettings = environmentSettings;
		_clioGateway = clioGateway;
		_packageInstaller = packageInstaller;
		_application = application;
		_workingDirectoriesProvider = workingDirectoriesProvider;
	}

	/// <inheritdoc/>
	public ClioGateEnsureResult EnsureInstalled() {
		string cacheKey = NormalizeUri(_environmentSettings?.Uri);

		// Fast path — lock-free read.
		if (ConfirmedEnvironments.ContainsKey(cacheKey)) {
			return ClioGateEnsureResult.Present();
		}

		// Slow path — serialize per environment to prevent duplicate installs.
		SemaphoreSlim gate = InstallLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
		gate.Wait();
		try {
			// Double-check: another thread may have installed while we were waiting.
			if (ConfirmedEnvironments.ContainsKey(cacheKey)) {
				return ClioGateEnsureResult.Present();
			}

			try {
				if (_clioGateway.GetInstalledVersion() != null) {
					ConfirmedEnvironments.TryAdd(cacheKey, true);
					return ClioGateEnsureResult.Present();
				}
			} catch {
				// Cannot check — proceed to install attempt.
			}

			return TryInstall(cacheKey);
		} finally {
			gate.Release();
		}
	}

	private ClioGateEnsureResult TryInstall(string cacheKey) {
		string uri = _environmentSettings?.Uri ?? "(unknown)";
		try {
			string packagePath = GetPackagePath();
			EnvironmentSettings installSettings = BuildInstallSettings();

			bool success = _packageInstaller.Install(
				packagePath,
				installSettings,
				packageInstallOptions: null,
				reportPath: null,
				createBackup: true);

			if (!success) {
				return ClioGateEnsureResult.Failed(uri, "package installer returned failure");
			}

			_application.Restart();
			ConfirmedEnvironments.TryAdd(cacheKey, true);
			return ClioGateEnsureResult.Installed(uri);
		} catch (Exception ex) {
			return ClioGateEnsureResult.Failed(uri, ex.Message);
		}
	}

	private string GetPackagePath() {
		string packageName = (_environmentSettings?.IsNetCore ?? false) ? "cliogate_netcore" : "cliogate";
		return Path.Combine(_workingDirectoriesProvider.ExecutingDirectory, "cliogate", $"{packageName}.gz");
	}

	private EnvironmentSettings BuildInstallSettings() {
		EnvironmentSettings settings = new();
		settings.Merge(_environmentSettings);
		settings.DeveloperModeEnabled = false;
		return settings;
	}

	private static string NormalizeUri(string? uri) {
		if (string.IsNullOrWhiteSpace(uri)) {
			return string.Empty;
		}
		return uri.Trim().TrimEnd('/').ToLowerInvariant();
	}
}

