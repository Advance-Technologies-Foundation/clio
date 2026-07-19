using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using Clio.UserEnvironment;

namespace Clio.Command.McpServer.Knowledge;

internal interface IKnowledgeInstallationService {
	KnowledgeInstallationResult Install();

	KnowledgeInstallationResult Update();

	KnowledgeInstallationInfo GetInfo(bool checkUpdates);

	KnowledgeInstallationResult Delete(bool confirmed);
}

internal sealed class KnowledgeInstallationService : IKnowledgeInstallationService {
	private const int MaxCandidateAttempts = 64;
	private const int OperationDeadlineMilliseconds = 30_000;

	private readonly IKnowledgeInstallationStore _store;
	private readonly IKnowledgeBundlePackageClient _packageClient;
	private readonly IKnowledgeBundleRuntime _runtime;
	private readonly ISettingsRepository _settingsRepository;

	public KnowledgeInstallationService(
		IKnowledgeInstallationStore store,
		IKnowledgeBundlePackageClient packageClient,
		IKnowledgeBundleRuntime runtime,
		ISettingsRepository settingsRepository) {
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_packageClient = packageClient ?? throw new ArgumentNullException(nameof(packageClient));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
	}

	public KnowledgeInstallationResult Install() {
		try {
			string root = _store.GetRootPath();
			KnowledgeCurrentState? current = _store.ReadCurrent(out string? markerDiagnostic);
			if (markerDiagnostic is not null) {
				return Failed(markerDiagnostic, root);
			}
			if (current is not null) {
				if (IsCurrentValid(current, out _)) {
					return new KnowledgeInstallationResult(
						KnowledgeInstallationStatus.AlreadyInstalled,
						$"Knowledge {current.Active.PackageVersion} is already installed at {root}.",
						current.Active.PackageVersion,
						root);
				}
				return DownloadValidateAndPublish(
					activeVersion: null,
					isUpdate: true,
					root,
					current.Active,
					exactRepairVersion: current.Active.PackageVersion);
			}
			return DownloadValidateAndPublish(
				activeVersion: null,
				isUpdate: false,
				root,
				expectedActive: null,
				exactRepairVersion: null);
		} catch (Exception exception) when (IsOperationalFailure(exception)) {
			return Failed(exception.Message);
		}
	}

	public KnowledgeInstallationResult Update() {
		try {
			string root = _store.GetRootPath();
			KnowledgeCurrentState? current = _store.ReadCurrent(out string? markerDiagnostic);
			if (markerDiagnostic is not null) {
				return Failed(markerDiagnostic, root);
			}
			if (current is null) {
				return new KnowledgeInstallationResult(
					KnowledgeInstallationStatus.NotInstalled,
					"Knowledge is not installed; use install-knowledge.",
					RootPath: root);
			}
			if (!IsCurrentValid(current, out _)) {
				return DownloadValidateAndPublish(
					activeVersion: null,
					isUpdate: true,
					root,
					current.Active,
					exactRepairVersion: null);
			}

			KnowledgeBundlePackageCatalogResult catalog = _packageClient.GetCatalog();
			if (!catalog.IsAvailable) {
				return new KnowledgeInstallationResult(
					KnowledgeInstallationStatus.Unavailable,
					$"Knowledge update could not be checked: {catalog.Diagnostic}",
					current.Active.PackageVersion,
					root);
			}
			if (catalog.LatestVersion is null
					|| !KnowledgeBundleNuGetClient.IsVersionGreaterThan(
						catalog.LatestVersion,
						current.Active.PackageVersion)) {
				return new KnowledgeInstallationResult(
					KnowledgeInstallationStatus.UpToDate,
					$"Knowledge {current.Active.PackageVersion} is up to date.",
					current.Active.PackageVersion,
					root);
			}
			return DownloadValidateAndPublish(
				current.Active.PackageVersion,
				isUpdate: true,
				root,
				current.Active,
				exactRepairVersion: null);
		} catch (Exception exception) when (IsOperationalFailure(exception)) {
			return Failed(exception.Message);
		}
	}

	public KnowledgeInstallationInfo GetInfo(bool checkUpdates) {
		string root;
		try {
			root = _store.GetRootPath();
		} catch (Exception exception) when (IsOperationalFailure(exception)) {
			return new KnowledgeInstallationInfo(
				string.Empty,
				string.Empty,
				false,
				false,
				null,
				null,
				null,
				null,
				null,
				null,
				KnowledgeUpdateAvailability.Unknown,
				null,
				exception.Message);
		}

		KnowledgeCurrentState? current = _store.ReadCurrent(out string? diagnostic);
		KnowledgeInstallMetadata? metadata = null;
		bool valid = false;
		string? activeContentPath = null;
		string? candidateDiagnostic = null;
		if (current is not null
				&& _store.TryReadCandidate(current.Active, out InstalledKnowledgeCandidate? candidate,
					out candidateDiagnostic)) {
			using MemoryStream stream = new(candidate!.BundleBytes, writable: false);
			KnowledgeBundleValidationResult validation = _runtime.Validate(stream, current.Active.PackageVersion);
			valid = validation.Status == KnowledgeBundleActivationStatus.Activated
				&& validation.CandidateSequence == current.Active.Sequence;
			diagnostic = valid ? null : validation.Diagnostic ?? "Installed knowledge sequence is invalid.";
			activeContentPath = Path.GetDirectoryName(candidate.BundlePath);
			if (valid && !_store.TryValidateInstallation(current, out string? installationDiagnostic)) {
				valid = false;
				diagnostic = installationDiagnostic;
			}
			metadata = _store.ReadActiveMetadata(current, out string? metadataDiagnostic);
			if (metadataDiagnostic is not null) {
				valid = false;
				diagnostic = metadataDiagnostic;
			}
		} else if (candidateDiagnostic is not null) {
			diagnostic = candidateDiagnostic;
		}

		KnowledgeUpdateAvailability availability = current is null
			? KnowledgeUpdateAvailability.NotInstalled
			: KnowledgeUpdateAvailability.Unknown;
		string? latestVersion = null;
		if (checkUpdates) {
			KnowledgeBundlePackageCatalogResult catalog = _packageClient.GetCatalog();
			latestVersion = catalog.LatestVersion;
			if (!catalog.IsAvailable) {
				diagnostic = CombineDiagnostic(diagnostic, $"Update check failed: {catalog.Diagnostic}");
			} else if (current is null) {
				availability = KnowledgeUpdateAvailability.NotInstalled;
			} else if (latestVersion is not null
					&& KnowledgeBundleNuGetClient.IsVersionGreaterThan(
						latestVersion,
						current.Active.PackageVersion)) {
				availability = KnowledgeUpdateAvailability.Available;
			} else {
				availability = KnowledgeUpdateAvailability.UpToDate;
			}
		}

		return new KnowledgeInstallationInfo(
			SettingsFilePath(),
			root,
			current is not null,
			valid,
			current?.Active.PackageVersion,
			current?.Previous?.PackageVersion,
			activeContentPath,
			current is null ? _packageClient.GetConfiguration()?.Source : metadata?.Source,
			current is null ? _packageClient.GetConfiguration()?.PackageId : metadata?.PackageId,
			metadata?.InstalledAtUtc,
			availability,
			latestVersion,
			diagnostic);
	}

	public KnowledgeInstallationResult Delete(bool confirmed) {
		try {
			return _store.Delete(confirmed);
		} catch (Exception exception) when (IsOperationalFailure(exception)) {
			return Failed(exception.Message);
		}
	}

	private KnowledgeInstallationResult DownloadValidateAndPublish(
		string? activeVersion,
		bool isUpdate,
		string root,
		KnowledgeVersionPointer? expectedActive,
		string? exactRepairVersion) {
		KnowledgeBundlePackageConfiguration? configuration = _packageClient.GetConfiguration();
		if (configuration is null) {
			return new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Unavailable,
				"Knowledge NuGet source or package ID is not configured.",
				activeVersion,
				root);
		}
		HashSet<string> rejectedVersions = new(StringComparer.Ordinal);
		string? highestObserved = null;
		string? fallbackCeiling = null;
		string? catalogFingerprint = null;
		string? lastRejectedVersion = null;
		string? lastDiagnostic = null;
		Stopwatch operation = Stopwatch.StartNew();
		for (int attempt = 0; attempt < MaxCandidateAttempts; attempt++) {
			int remainingMilliseconds = OperationDeadlineMilliseconds - (int)Math.Min(
				operation.ElapsedMilliseconds,
				OperationDeadlineMilliseconds);
			if (remainingMilliseconds <= 0) {
				lastDiagnostic = "the operation-wide download deadline elapsed";
				break;
			}
			KnowledgeBundlePackageDownloadResult download = _packageClient.DownloadNext(
				rejectedVersions,
				activeVersion,
				highestObserved,
				fallbackCeiling,
				catalogFingerprint,
				remainingMilliseconds);
			if (download is null) {
				break;
			}
			catalogFingerprint = download.CatalogFingerprint ?? catalogFingerprint;
			if (download.Status == KnowledgeBundlePackageDownloadStatus.NoCandidate) {
				break;
			}
			if (download.PackageVersion is null) {
				break;
			}
			highestObserved = KnowledgeBundleNuGetClient.GreaterVersion(
				highestObserved,
				download.PackageVersion);
			fallbackCeiling = download.PackageVersion;
			lastRejectedVersion = download.PackageVersion;
			if (download.Status == KnowledgeBundlePackageDownloadStatus.Rejected
					|| download.BundleBytes is null) {
				lastDiagnostic = "the NuGet package did not contain a supported knowledge bundle";
				rejectedVersions.Add(download.PackageVersion);
				continue;
			}
			if (expectedActive is not null
					&& !string.Equals(download.PackageVersion, expectedActive.PackageVersion, StringComparison.Ordinal)
					&& !KnowledgeBundleNuGetClient.IsVersionGreaterThan(
						download.PackageVersion,
						expectedActive.PackageVersion)) {
				lastDiagnostic = "the package is older than the active installation";
				rejectedVersions.Add(download.PackageVersion);
				continue;
			}
			if (exactRepairVersion is not null
					&& !string.Equals(download.PackageVersion, exactRepairVersion, StringComparison.Ordinal)) {
				lastDiagnostic = "install-knowledge repairs only the active immutable package version";
				rejectedVersions.Add(download.PackageVersion);
				continue;
			}

			using MemoryStream stream = new(download.BundleBytes, writable: false);
			KnowledgeBundleValidationResult validation = _runtime.Validate(stream, download.PackageVersion);
			if (validation.Status != KnowledgeBundleActivationStatus.Activated
					|| validation.CandidateSequence is null) {
				lastDiagnostic = validation.Diagnostic ?? "bundle validation failed";
				rejectedVersions.Add(download.PackageVersion);
				continue;
			}
			return _store.Publish(
				configuration.PackageId,
				download.PackageVersion,
				validation.CandidateSequence.Value,
				configuration.Source,
				download.BundleBytes,
				isUpdate,
				expectedActive);
		}
		if (lastRejectedVersion is not null) {
			return new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Rejected,
				$"No compatible knowledge package was found after rejecting {lastRejectedVersion}: {lastDiagnostic}",
				lastRejectedVersion,
				root);
		}
		return new KnowledgeInstallationResult(
			KnowledgeInstallationStatus.Unavailable,
			isUpdate ? "No downloadable knowledge update is available." : "Knowledge could not be downloaded.",
			activeVersion,
			root);
	}

	private bool IsCurrentValid(KnowledgeCurrentState current, out string? diagnostic) {
		if (!_store.TryReadCandidate(
				current.Active,
				out InstalledKnowledgeCandidate? candidate,
				out diagnostic)) {
			return false;
		}
		using MemoryStream stream = new(candidate!.BundleBytes, writable: false);
		KnowledgeBundleValidationResult validation = _runtime.Validate(stream, current.Active.PackageVersion);
		if (validation.Status != KnowledgeBundleActivationStatus.Activated
				|| validation.CandidateSequence != current.Active.Sequence) {
			diagnostic = validation.Diagnostic ?? "Installed knowledge bundle is invalid.";
			return false;
		}
		if (!_store.TryValidateInstallation(current, out diagnostic)) {
			return false;
		}
		diagnostic = null;
		return true;
	}

	private string SettingsFilePath() => _settingsRepository.AppSettingsFilePath;

	private KnowledgeInstallationResult Failed(string message, string? root = null) => new(
		KnowledgeInstallationStatus.Failed,
		message,
		RootPath: root);

	private static string CombineDiagnostic(string? first, string second) =>
		string.IsNullOrWhiteSpace(first) ? second : $"{first} {second}";

	private static bool IsOperationalFailure(Exception exception) => exception is IOException
		or UnauthorizedAccessException
		or TimeoutException
		or InvalidOperationException
		or ArgumentException
		or NotSupportedException
		or JsonException;
}
