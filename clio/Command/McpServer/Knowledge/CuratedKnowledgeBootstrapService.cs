using System;
using System.Linq;
using System.Threading;
using Clio.Command.McpServer.Tools;
using Clio.UserEnvironment;

namespace Clio.Command.McpServer.Knowledge;

/// <summary>
/// Defines the built-in Creatio-curated knowledge source shipped by Clio.
/// </summary>
internal static class CuratedKnowledgeSourceDefaults {
	internal const string Alias = "creatio-curated";
	internal const string LibraryId = "com.creatio.clio";
	internal const string Location = "https://github.com/Advance-Technologies-Foundation/clio-knowledge.git";
	internal const string Branch = "master";
	internal const string LegacyAlias = "creatio-poc";
	internal const int Priority = 100;
	internal const int StartupInstallDeadlineMilliseconds = 5_000;

	internal static KnowledgeSourceConfiguration CreateConfiguration() => new() {
		LibraryId = LibraryId,
		Type = KnowledgeSourceType.Git,
		Location = Location,
		Branch = Branch,
		Enabled = true,
		Priority = Priority,
		Participation = KnowledgeSourceParticipation.Authoritative
	};
}

/// <summary>
/// Describes the outcome of ensuring the built-in curated knowledge source is configured and installed.
/// </summary>
public sealed record CuratedKnowledgeBootstrapResult(
	bool Success,
	bool Enabled,
	bool Installed,
	string Message);

/// <summary>
/// Ensures Clio's built-in curated knowledge source is configured and installed before MCP serves requests.
/// </summary>
public interface ICuratedKnowledgeBootstrapService {
	/// <summary>
	/// Restores the canonical source configuration, preserves its enabled kill switch, and migrates a legacy local alias.
	/// </summary>
	/// <returns>A non-throwing result suitable for synchronous host startup diagnostics.</returns>
	CuratedKnowledgeBootstrapResult Prepare();

	/// <summary>
	/// Uses a valid local checkout or installs the source previously prepared by <see cref="Prepare"/>.
	/// </summary>
	/// <param name="cancellationToken">Stops bounded startup installation work when requested.</param>
	/// <returns>A non-throwing installation result.</returns>
	CuratedKnowledgeBootstrapResult InstallPreparedSource(CancellationToken cancellationToken = default);

	/// <summary>
	/// Runs both bootstrap phases for explicit callers and focused validation.
	/// </summary>
	/// <param name="cancellationToken">Stops installation work when requested.</param>
	/// <returns>A non-throwing result suitable for diagnostics.</returns>
	CuratedKnowledgeBootstrapResult Bootstrap(CancellationToken cancellationToken = default);
}

internal sealed class CuratedKnowledgeBootstrapService(
	ISettingsRepository settingsRepository,
	IKnowledgeSourceInstallationStore installationStore,
	IKnowledgeSourceManagementService sourceManagementService) : ICuratedKnowledgeBootstrapService {
	private string[] _migrationAliases = [CuratedKnowledgeSourceDefaults.LegacyAlias];

	public CuratedKnowledgeBootstrapResult Prepare() {
		try {
			KnowledgeConfiguration current = settingsRepository.GetKnowledgeConfiguration();
			string? previousAlias = current.Sources
				.Where(pair => string.Equals(
					pair.Value.LibraryId,
					CuratedKnowledgeSourceDefaults.LibraryId,
					StringComparison.OrdinalIgnoreCase))
				.Select(pair => pair.Key)
				.FirstOrDefault(alias => !string.Equals(
					alias,
					CuratedKnowledgeSourceDefaults.Alias,
					StringComparison.OrdinalIgnoreCase));
			_migrationAliases = new[] { previousAlias, CuratedKnowledgeSourceDefaults.LegacyAlias }
				.Where(alias => !string.IsNullOrWhiteSpace(alias))
				.Select(alias => alias!)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			foreach (string migrationAlias in _migrationAliases) {
				installationStore.TryMigrateGitRepository(
					migrationAlias,
					CuratedKnowledgeSourceDefaults.Alias);
			}
			KnowledgeSourceConfiguration source = settingsRepository.EnsureKnowledgeSource(
				CuratedKnowledgeSourceDefaults.Alias,
				CuratedKnowledgeSourceDefaults.CreateConfiguration());
			if (!source.Enabled) {
				return new CuratedKnowledgeBootstrapResult(
					true,
					false,
					false,
					$"Built-in knowledge source '{CuratedKnowledgeSourceDefaults.Alias}' is disabled; its cache was retained.");
			}
			return new CuratedKnowledgeBootstrapResult(
				true,
				true,
				false,
				$"Built-in knowledge source '{CuratedKnowledgeSourceDefaults.Alias}' is configured and ready for installation.");
		} catch (Exception exception) when (exception is not OutOfMemoryException) {
			return Failure(exception);
		}
	}

	public CuratedKnowledgeBootstrapResult InstallPreparedSource(CancellationToken cancellationToken = default) {
		try {
			foreach (string migrationAlias in _migrationAliases) {
				installationStore.MigrateGitRepository(
					migrationAlias,
					CuratedKnowledgeSourceDefaults.Alias);
			}
			KnowledgeConfiguration configuration = settingsRepository.GetKnowledgeConfiguration();
			if (!configuration.Sources.TryGetValue(
					CuratedKnowledgeSourceDefaults.Alias,
					out KnowledgeSourceConfiguration? source)
					|| !string.Equals(
						source.LibraryId,
						CuratedKnowledgeSourceDefaults.LibraryId,
						StringComparison.OrdinalIgnoreCase)) {
				return new CuratedKnowledgeBootstrapResult(
					false,
					false,
					false,
					"Built-in curated knowledge source is not prepared.");
			}
			if (!source.Enabled) {
				return new CuratedKnowledgeBootstrapResult(
					true,
					false,
					false,
					$"Built-in knowledge source '{CuratedKnowledgeSourceDefaults.Alias}' was disabled before installation; its cache was retained.");
			}
			KnowledgeSourceInfoResult info = sourceManagementService.GetInfo(
				CuratedKnowledgeSourceDefaults.Alias,
				checkUpdates: false,
				cancellationToken);
			KnowledgeSourceInfo? installed = info.Sources.SingleOrDefault();
			if (info.Success && installed is { IsInstalled: true, IsValid: true }) {
				return new CuratedKnowledgeBootstrapResult(
					true,
					true,
					true,
					$"Built-in knowledge source '{CuratedKnowledgeSourceDefaults.Alias}' is ready from its local cache.");
			}

			KnowledgeSourceBatchResult installation = sourceManagementService.Install(
				CuratedKnowledgeSourceDefaults.Alias,
				CuratedKnowledgeSourceDefaults.StartupInstallDeadlineMilliseconds,
				cancellationToken);
			KnowledgeSourceOperationResult? operation = installation.Sources.SingleOrDefault();
			if (installation.Success && operation is { Success: true }) {
				return new CuratedKnowledgeBootstrapResult(
					true,
					true,
					true,
					operation.Message);
			}
			return new CuratedKnowledgeBootstrapResult(
				false,
				true,
				false,
				operation?.Message ?? installation.Message);
		} catch (OperationCanceledException) {
			return new CuratedKnowledgeBootstrapResult(
				false,
				true,
				false,
				"Built-in curated knowledge bootstrap was cancelled.");
		} catch (Exception exception) when (exception is not OutOfMemoryException) {
			return Failure(exception);
		}
	}

	public CuratedKnowledgeBootstrapResult Bootstrap(CancellationToken cancellationToken = default) {
		CuratedKnowledgeBootstrapResult preparation = Prepare();
		return !preparation.Success || !preparation.Enabled
			? preparation
			: InstallPreparedSource(cancellationToken);
	}

	private static CuratedKnowledgeBootstrapResult Failure(Exception exception) => new(
		false,
		true,
		false,
		SensitiveErrorTextRedactor.Redact(exception.Message));
}
