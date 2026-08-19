using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command.McpServer.Knowledge;

/// <summary>
/// Defines the built-in Creatio-curated knowledge source shipped by Clio.
/// </summary>
internal static class CuratedKnowledgeSourceDefaults {
	internal const string Alias = "creatio-curated";
	internal const string LibraryId = "com.creatio.clio";

	/// <summary>
	/// The GitHub REST API origin the built-in source is discovered through.
	/// </summary>
	/// <remarks>
	/// Only the API origin is configurable; the repository identity and asset name are fixed below so
	/// the built-in source can never be pointed at an arbitrary URL. The origin exists as a value
	/// rather than a literal so the hermetic end-to-end tests can substitute a loopback server.
	/// </remarks>
	internal const string Location = "https://api.github.com/";

	/// <summary>The GitHub owner publishing the curated knowledge library.</summary>
	internal const string RepositoryOwner = "Advance-Technologies-Foundation";

	/// <summary>The GitHub repository publishing the curated knowledge library.</summary>
	internal const string RepositoryName = "clio-knowledge";

	/// <summary>The canonical Git repository URI accepted for an explicit developer override.</summary>
	internal const string GitRepositoryLocation = "https://github.com/Advance-Technologies-Foundation/clio-knowledge.git";

	/// <summary>The fixed release-asset file name carrying the signed bundle.</summary>
	internal const string AssetName = "clio-knowledge-bundle.zip";

	/// <summary>
	/// Overrides the built-in source's GitHub API origin.
	/// </summary>
	/// <remarks>
	/// Present so a hermetic test can point the built-in source at a loopback Releases API without a
	/// second bootstrap code path. It accepts only a loopback HTTPS or HTTP origin: a value naming any
	/// other host is ignored, so the variable cannot redirect a real installation to a foreign server.
	/// </remarks>
	internal const string LocationOverrideVariable = "CLIO_KNOWLEDGE_CURATED_API_BASE_URL";

	internal const string LegacyAlias = "creatio-poc";
	internal const int Priority = 100;
	internal const int StartupInstallDeadlineMilliseconds = 5_000;

	/// <summary>
	/// How long a locally cached generation may stay active before a warm start reports it as stale.
	/// </summary>
	/// <remarks>
	/// A warm artifact-backed start never contacts the publisher, so the age of the activation marker's
	/// last publication or successful publisher check
	/// is the only staleness signal available without giving up the offline guarantee. Three days is
	/// short enough to surface a missed release on the next working day while an explicit successful
	/// update check renews the signal without replacing the active generation.
	/// </remarks>
	internal const int StaleCacheThresholdDays = 3;

	internal static KnowledgeSourceConfiguration CreateConfiguration() => new() {
		LibraryId = LibraryId,
		Type = KnowledgeSourceType.GitHubRelease,
		Location = ResolveLocation(),
		RepositoryOwner = RepositoryOwner,
		RepositoryName = RepositoryName,
		AssetName = AssetName,
		Enabled = true,
		Priority = Priority,
		Participation = KnowledgeSourceParticipation.Authoritative
	};

	/// <summary>
	/// Returns the API origin to configure, honoring a loopback-only test override.
	/// </summary>
	/// <returns>The canonical GitHub API origin, or a loopback override when one is set.</returns>
	internal static string ResolveLocation() {
		string? candidate = Environment.GetEnvironmentVariable(LocationOverrideVariable);
		return Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
			&& uri.IsLoopback
			&& (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
			&& string.IsNullOrEmpty(uri.UserInfo)
			&& string.IsNullOrEmpty(uri.Query)
			&& string.IsNullOrEmpty(uri.Fragment)
				? uri.AbsoluteUri
				: Location;
	}
}

/// <summary>
/// Describes the outcome of ensuring the built-in curated knowledge source is configured and installed.
/// </summary>
/// <param name="Success">Whether the phase completed without a fault.</param>
/// <param name="Enabled">Whether the built-in source is enabled.</param>
/// <param name="Installed">Whether local content is available to activate.</param>
/// <param name="Message">The diagnostic text for the phase.</param>
/// <param name="StalenessWarning">
/// Set when the cached activation candidate is older than
/// <see cref="CuratedKnowledgeSourceDefaults.StaleCacheThresholdDays"/>, so a successful warm start
/// reports that the candidate may be behind the published release before later activation validates it.
/// Trailing and optional: a caller that does not care about staleness is unaffected.
/// </param>
public sealed record CuratedKnowledgeBootstrapResult(
	bool Success,
	bool Enabled,
	bool Installed,
	string Message,
	string? StalenessWarning = null);

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
	TimeProvider timeProvider,
	KnowledgeBundleClientCapabilities capabilities) : ICuratedKnowledgeBootstrapService {
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
			// The settings write lock waits up to thirty seconds on a contended appsettings file and
			// takes no cancellation token, so it cannot be part of a five-second pre-serve budget.
			// In the steady state nothing needs writing: read first, and take the lock only on the
			// first run or when an earlier version left a non-canonical entry behind.
			KnowledgeSourceConfiguration source =
				current.Sources.TryGetValue(CuratedKnowledgeSourceDefaults.Alias, out KnowledgeSourceConfiguration? existing)
				&& IsCanonical(existing)
					? existing
					: settingsRepository.EnsureKnowledgeSource(
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
			if (Remaining <= TimeSpan.Zero) {
				return BudgetExhausted();
			}
			// A file-marker probe, not an inspection. GetInfo cannot be used here: for a single
			// source it bypasses batch bounding and runs with a fixed thirty-second operation
			// deadline, and the Git validation underneath opens a further thirty-second window of
			// its own. Whether the cached content is actually usable is decided by activation, which
			// never blocks on the source mutation lock and falls back when it is not.
			//
			// This branch is what keeps a warm artifact-backed start offline. Git deliberately falls
			// through to synchronization so an operator's branch, tag, or commit change cannot leave a
			// stale checkout active under the newly configured reference.
			// Git normally falls through to a startup re-sync so a branch/tag/commit change is honored.
			// LOCAL DEV TOGGLE (knowledge-allow-unsequenced): when on, treat an already-installed Git source
			// like an artifact-backed one -- take the offline fast-path and skip the startup network re-sync.
			// This keeps a server start reliable for local branch testing (activation reads the local cache
			// offline); ref changes are applied explicitly by install-knowledge, not the startup sync.
			bool offlineEligible = source.Type != KnowledgeSourceType.Git
					|| capabilities.AllowUnsequencedGitBundles;
			if (offlineEligible
					&& IsLocallyInstalled(source.Type, CuratedKnowledgeSourceDefaults.Alias)) {
				return new CuratedKnowledgeBootstrapResult(
					true,
					true,
					true,
					$"Built-in knowledge source '{CuratedKnowledgeSourceDefaults.Alias}' is ready from its local cache.",
					DescribeStaleCache());
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

	/// <summary>
	/// Reports whether a persisted entry already matches the built-in definition.
	/// </summary>
	/// <remarks>
	/// <see cref="KnowledgeSourceConfiguration.Enabled"/> is deliberately excluded: the kill switch
	/// is operator-owned and a disabled source is still canonical.
	/// </remarks>
	/// <param name="candidate">The persisted entry.</param>
	/// <returns><see langword="true"/> when nothing has to be written.</returns>
	private static bool IsCanonical(KnowledgeSourceConfiguration candidate) {
		if (IsCuratedGitOverride(candidate)) {
			return true;
		}
		KnowledgeSourceConfiguration expected = CuratedKnowledgeSourceDefaults.CreateConfiguration();
		return string.Equals(candidate.LibraryId, expected.LibraryId, StringComparison.OrdinalIgnoreCase)
			&& candidate.Type == expected.Type
			&& string.Equals(candidate.Location, expected.Location, StringComparison.Ordinal)
			&& string.Equals(candidate.RepositoryOwner, expected.RepositoryOwner, StringComparison.Ordinal)
			&& string.Equals(candidate.RepositoryName, expected.RepositoryName, StringComparison.Ordinal)
			&& string.Equals(candidate.AssetName, expected.AssetName, StringComparison.Ordinal)
			&& string.IsNullOrWhiteSpace(candidate.Branch)
			&& string.IsNullOrWhiteSpace(candidate.Tag)
			&& string.IsNullOrWhiteSpace(candidate.Commit)
			&& candidate.Priority == expected.Priority
			&& candidate.Participation == expected.Participation;
	}

	/// <summary>
	/// Reports whether an operator explicitly selected the canonical curated repository for Git-based development.
	/// </summary>
	private static bool IsCuratedGitOverride(KnowledgeSourceConfiguration candidate) =>
		candidate.Type == KnowledgeSourceType.Git
		&& string.Equals(candidate.LibraryId, CuratedKnowledgeSourceDefaults.LibraryId, StringComparison.OrdinalIgnoreCase)
		&& string.Equals(candidate.Location, CuratedKnowledgeSourceDefaults.GitRepositoryLocation, StringComparison.Ordinal)
		&& candidate.Priority == CuratedKnowledgeSourceDefaults.Priority
		&& candidate.Participation == KnowledgeSourceParticipation.Authoritative;

	/// <summary>
	/// Describes the cached activation marker when it is older than the staleness threshold.
	/// </summary>
	/// <remarks>
	/// The whole point of this check is that it stays on the offline warm-start path: it reads the same
	/// activation marker the install probe already relies on and compares its activation timestamp with
	/// the injected clock, so it makes no transport call and adds no measurable time to the startup
	/// budget. It is a report, never a decision — nothing here refreshes, deactivates, or rejects a
	/// cache, because an operator with no network must keep serving the bundle they have. An unreadable
	/// marker is reported as unknown rather than silently treated as a valid warm cache.
	/// </remarks>
	/// <returns>The warning text, or <see langword="null"/> when the cache is fresh.</returns>
	private string? DescribeStaleCache() {
		KnowledgeSourceStartupState? state =
			installationStore.TryReadStartupState(CuratedKnowledgeSourceDefaults.Alias);
		if (state is null) {
			return $"Built-in knowledge source '{CuratedKnowledgeSourceDefaults.Alias}' has an unreadable activation "
				+ $"marker; check for a valid cached generation with update-knowledge --source "
				+ $"{CuratedKnowledgeSourceDefaults.Alias}.";
		}
		KnowledgeSourceGenerationPointer active = state.Active;
		DateTimeOffset freshness = state.LastPublisherCheckAtUtc is { } checkedAt
			&& checkedAt > active.ActivatedAtUtc
				? checkedAt
				: active.ActivatedAtUtc;
		DateTimeOffset now = timeProvider.GetUtcNow();
		if (freshness > now + TimeSpan.FromMinutes(5)) {
			return string.Format(
				CultureInfo.InvariantCulture,
				"Built-in knowledge source '{0}' cache marker has a future freshness timestamp ({1:u}); "
				+ "check the system clock and run update-knowledge --source {0}.",
				CuratedKnowledgeSourceDefaults.Alias,
				freshness.UtcDateTime);
		}
		TimeSpan age = now - freshness;
		if (age <= TimeSpan.FromDays(CuratedKnowledgeSourceDefaults.StaleCacheThresholdDays)) {
			return null;
		}
		return string.Format(
			CultureInfo.InvariantCulture,
			"Built-in knowledge source '{0}' cache marker references library version {1} (sequence {2}), installed or last "
			+ "confirmed current on {3:yyyy-MM-dd} and now {4} day(s) old; a warm start never checks the publisher, "
			+ "so the cached candidate may be behind the "
			+ "current release. Check for updates with update-knowledge --source {0}.",
			CuratedKnowledgeSourceDefaults.Alias,
			TextUtilities.SanitizeForDisplay(active.LibraryVersion, maxLength: 128),
			active.Sequence,
			freshness.UtcDateTime,
			(int)age.TotalDays);
	}

	/// <summary>
	/// Reports whether the alias already has content on disk that activation could serve.
	/// </summary>
	/// <remarks>
	/// Purely local and lock-free by design: it must never contact a transport. A repository transport
	/// keeps a checkout, every artifact transport keeps published generations behind an activation
	/// marker, so each shape gets the probe that matches how it stores content.
	/// </remarks>
	/// <param name="type">The configured transport type.</param>
	/// <param name="alias">The configured source alias.</param>
	/// <returns><see langword="true"/> when local content is present.</returns>
	private bool IsLocallyInstalled(KnowledgeSourceType type, string alias) => type == KnowledgeSourceType.Git
		? installationStore.IsGitRepositoryInstalled(alias)
		: installationStore.IsBundleGenerationInstalled(alias);

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
