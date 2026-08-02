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
	IKnowledgeSourceManagementService sourceManagementService,
	TimeProvider timeProvider) : ICuratedKnowledgeBootstrapService {
	private string[] _migrationAliases = [CuratedKnowledgeSourceDefaults.LegacyAlias];
	private long? _budgetStartedAt;
	private readonly TimeSpan _budget = TimeSpan.FromMilliseconds(
		CuratedKnowledgeSourceDefaults.StartupInstallDeadlineMilliseconds);

	/// <summary>
	/// The startup budget still available, never negative.
	/// </summary>
	/// <remarks>
	/// The advertised pre-serve bound is only real if every phase spends from one absolute budget.
	/// Migration, local inspection, and installation each take what is left rather than each
	/// starting a fresh timer.
	/// </remarks>
	private TimeSpan Remaining {
		get {
			// A caller that skips Prepare() still gets a bounded phase rather than an already-expired
			// one: the budget starts the first time it is read.
			_budgetStartedAt ??= timeProvider.GetTimestamp();
			TimeSpan remaining = _budget - timeProvider.GetElapsedTime(_budgetStartedAt.Value);
			return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
		}
	}

	public CuratedKnowledgeBootstrapResult Prepare() {
		try {
			_budgetStartedAt = timeProvider.GetTimestamp();
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
		using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		try {
			// Non-blocking migration: the waiting variant acquires two source mutation locks with the
			// store's own 30-second timeout each and ignores the token, so on contention it alone can
			// overrun the whole advertised startup bound. A skipped migration only leaves the cache
			// under its previous alias, which the next run retries.
			foreach (string migrationAlias in _migrationAliases) {
				if (Remaining <= TimeSpan.Zero) {
					return BudgetExhausted();
				}
				installationStore.TryMigrateGitRepository(
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
			TimeSpan remainingBeforeInspection = Remaining;
			if (remainingBeforeInspection <= TimeSpan.Zero) {
				return BudgetExhausted();
			}
			// Local inspection can run real Git validation, so it is bounded too rather than being
			// treated as free work that happens before the deadline-aware install.
			budget.CancelAfter(remainingBeforeInspection);
			KnowledgeSourceInfoResult info = sourceManagementService.GetInfo(
				CuratedKnowledgeSourceDefaults.Alias,
				checkUpdates: false,
				budget.Token);
			KnowledgeSourceInfo? installed = info.Sources.SingleOrDefault();
			if (info.Success && installed is { IsInstalled: true, IsValid: true }) {
				return new CuratedKnowledgeBootstrapResult(
					true,
					true,
					true,
					$"Built-in knowledge source '{CuratedKnowledgeSourceDefaults.Alias}' is ready from its local cache.");
			}

			TimeSpan remainingBeforeInstall = Remaining;
			if (remainingBeforeInstall <= TimeSpan.Zero) {
				return BudgetExhausted();
			}
			KnowledgeSourceBatchResult installation = sourceManagementService.Install(
				CuratedKnowledgeSourceDefaults.Alias,
				(int)remainingBeforeInstall.TotalMilliseconds,
				budget.Token);
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
		// Prepare() starts the budget; Bootstrap must not restart it, or the two phases would each
		// get a full five seconds.

		return !preparation.Success || !preparation.Enabled
			? preparation
			: InstallPreparedSource(cancellationToken);
	}

	private static CuratedKnowledgeBootstrapResult BudgetExhausted() => new(
		false,
		true,
		false,
		"Built-in curated knowledge bootstrap exceeded its startup budget before the source was installed; "
		+ $"retry with install-knowledge --source {CuratedKnowledgeSourceDefaults.Alias}.");

	private static CuratedKnowledgeBootstrapResult Failure(Exception exception) => new(
		false,
		true,
		false,
		SensitiveErrorTextRedactor.Redact(exception.Message));
}
