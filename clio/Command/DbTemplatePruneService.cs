using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common.db;
using Clio.UserEnvironment;
using Npgsql;

namespace Clio.Command;

/// <summary>Result of discovering eligible configured PostgreSQL servers.</summary>
/// <param name="Success">Whether settings were read successfully.</param>
/// <param name="Servers">Eligible configured server names.</param>
/// <param name="Error">Actionable error when discovery failed.</param>
public sealed record DbTemplateServerListResult(bool Success, IReadOnlyList<string> Servers, string Error = null);

/// <summary>Structured result of a managed-template inventory request.</summary>
/// <param name="Success">Whether the inventory query completed successfully.</param>
/// <param name="DbServerName">Configured server name requested by the caller.</param>
/// <param name="Templates">Eligible clio-managed templates; empty is a successful inventory outcome.</param>
/// <param name="ErrorCategory">Stable failure category, or <c>null</c> on success.</param>
/// <param name="Error">Actionable error, or <c>null</c> on success.</param>
public sealed record DbTemplateInventoryResult(
	bool Success,
	string DbServerName,
	IReadOnlyList<PostgresManagedTemplate> Templates,
	string ErrorCategory = null,
	string Error = null);

/// <summary>Outcome of one explicitly requested template deletion.</summary>
/// <param name="DatabaseName">Database name supplied by the caller.</param>
/// <param name="Outcome"><c>deleted</c>, <c>skipped</c>, or <c>failed</c>.</param>
/// <param name="Message">Human-readable outcome detail.</param>
public sealed record DbTemplatePruneItemResult(string DatabaseName, string Outcome, string Message);

/// <summary>Structured result of an explicitly targeted template-pruning batch.</summary>
/// <param name="Success">Whether every requested template was deleted.</param>
/// <param name="Status"><c>complete-success</c>, <c>partial-failure</c>, or <c>complete-failure</c>.</param>
/// <param name="DbServerName">Configured server name requested by the caller.</param>
/// <param name="Results">One outcome for every distinct requested database name.</param>
/// <param name="ErrorCategory">Stable request-level failure category, or <c>null</c>.</param>
/// <param name="Error">Request-level error, or <c>null</c>.</param>
public sealed record DbTemplatePruneResult(
	bool Success,
	string Status,
	string DbServerName,
	IReadOnlyList<DbTemplatePruneItemResult> Results,
	string ErrorCategory = null,
	string Error = null);

/// <summary>Inventories and selectively deletes clio-managed PostgreSQL templates.</summary>
public interface IDbTemplatePruneService {
	/// <summary>Returns enabled configured PostgreSQL server names.</summary>
	/// <returns>A structured server-discovery result.</returns>
	DbTemplateServerListResult GetEligibleServers();

	/// <summary>Inventories clio-managed templates on one configured PostgreSQL server.</summary>
	/// <param name="dbServerName">Configured local database server name.</param>
	/// <returns>A structured inventory result.</returns>
	DbTemplateInventoryResult Inventory(string dbServerName);

	/// <summary>Deletes only the explicitly requested, freshly revalidated managed templates.</summary>
	/// <param name="dbServerName">Configured local database server name.</param>
	/// <param name="databaseNames">Explicit database names to revalidate and delete.</param>
	/// <param name="itemCompleted">Optional callback invoked after each distinct requested item is processed.</param>
	/// <returns>A structured per-database and batch result.</returns>
	DbTemplatePruneResult Prune(string dbServerName, IReadOnlyCollection<string> databaseNames,
		Action itemCompleted = null);
}

/// <inheritdoc />
public sealed class DbTemplatePruneService(
	ISettingsRepository settingsRepository,
	IDbClientFactory dbClientFactory) : IDbTemplatePruneService {
	internal const string CompleteSuccessStatus = "complete-success";
	internal const string PartialFailureStatus = "partial-failure";
	internal const string CompleteFailureStatus = "complete-failure";
	internal const string DeletedOutcome = "deleted";
	internal const string SkippedOutcome = "skipped";
	internal const string FailedOutcome = "failed";

	private readonly ISettingsRepository _settingsRepository = settingsRepository
		?? throw new ArgumentNullException(nameof(settingsRepository));
	private readonly IDbClientFactory _dbClientFactory = dbClientFactory
		?? throw new ArgumentNullException(nameof(dbClientFactory));

	/// <inheritdoc />
	public DbTemplateServerListResult GetEligibleServers() {
		if (!TryReloadSettings(out string reloadError)) {
			return new DbTemplateServerListResult(false, [], reloadError);
		}
		string[] servers = _settingsRepository.GetLocalDbServerNames()
			.Where(name => IsPostgres(_settingsRepository.GetLocalDbServer(name)))
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		return new DbTemplateServerListResult(true, servers);
	}

	/// <inheritdoc />
	public DbTemplateInventoryResult Inventory(string dbServerName) {
		if (!TryResolveServer(dbServerName, out LocalDbServerConfiguration configuration,
			out string errorCategory, out string error)) {
			return new DbTemplateInventoryResult(false, dbServerName, [], errorCategory, error);
		}
		try {
			Postgres postgres = CreatePostgres(configuration);
			IReadOnlyList<PostgresManagedTemplate> templates = postgres.GetManagedTemplates();
			return new DbTemplateInventoryResult(true, dbServerName, templates);
		}
		catch (Exception exception) when (IsDatabaseException(exception)) {
			(string category, string message) = DescribeDatabaseFailure(exception, dbServerName);
			return new DbTemplateInventoryResult(false, dbServerName, [], category, message);
		}
	}

	/// <inheritdoc />
	public DbTemplatePruneResult Prune(string dbServerName, IReadOnlyCollection<string> databaseNames,
		Action itemCompleted = null) {
		if (databaseNames is null || databaseNames.Count == 0 || databaseNames.Any(string.IsNullOrWhiteSpace)) {
			return Failure(dbServerName, [], "validation",
				"At least one non-empty database name must be supplied; no templates were deleted.");
		}
		string[] requestedNames = databaseNames
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		if (!TryResolveServer(dbServerName, out LocalDbServerConfiguration configuration,
			out string errorCategory, out string error)) {
			return Failure(dbServerName, requestedNames, errorCategory, error);
		}

		Postgres postgres = CreatePostgres(configuration);
		List<DbTemplatePruneItemResult> results = [];
		foreach (string requestedName in requestedNames) {
			results.Add(PruneOne(postgres, requestedName));
			itemCompleted?.Invoke();
		}

		int deletedCount = results.Count(result => result.Outcome == DeletedOutcome);
		string status = deletedCount == results.Count
			? CompleteSuccessStatus
			: deletedCount > 0 ? PartialFailureStatus : CompleteFailureStatus;
		return new DbTemplatePruneResult(status == CompleteSuccessStatus, status, dbServerName, results);
	}

	private DbTemplatePruneItemResult PruneOne(Postgres postgres, string requestedName) {
		try {
			PostgresManagedTemplate template = postgres.GetManagedTemplate(requestedName);
			if (template is null) {
				return new DbTemplatePruneItemResult(requestedName, SkippedOutcome,
					"Skipped because the database is missing or is no longer an eligible clio-managed template.");
			}
			string canonicalName = template.DatabaseName;
			if (postgres.CountActiveSessions(canonicalName) > 0) {
				return new DbTemplatePruneItemResult(requestedName, SkippedOutcome,
					"Skipped because the template currently has active database sessions.");
			}
			return DropRevalidatedTemplate(postgres, requestedName, canonicalName);
		}
		catch (Exception exception) when (IsDatabaseException(exception)) {
			return new DbTemplatePruneItemResult(requestedName, FailedOutcome,
				"Failed while revalidating or inspecting the template; verify database reachability and permissions.");
		}
	}

	private static DbTemplatePruneItemResult DropRevalidatedTemplate(Postgres postgres,
		string requestedName, string canonicalName) {
		try {
			postgres.SetTemplateFlag(canonicalName, false);
			postgres.DropDatabaseWithoutForce(canonicalName);
			return new DbTemplatePruneItemResult(requestedName, DeletedOutcome,
				$"Deleted template '{canonicalName}'.");
		}
		catch (Exception exception) when (IsDatabaseException(exception)) {
			try {
				if (postgres.DatabaseExists(canonicalName)) {
					postgres.SetTemplateFlag(canonicalName, true);
					return new DbTemplatePruneItemResult(requestedName, FailedOutcome,
						"Deletion failed; the database remains available and its template flag was restored.");
				}
				return new DbTemplatePruneItemResult(requestedName, FailedOutcome,
					"Drop did not complete normally and the database no longer exists; the outcome is reported as failed.");
			}
			catch (Exception recoveryException) when (IsDatabaseException(recoveryException)) {
				return new DbTemplatePruneItemResult(requestedName, FailedOutcome,
					"Drop failed and clio could not verify or restore the template flag; inspect the database before retrying.");
			}
		}
	}

	private bool TryResolveServer(string dbServerName, out LocalDbServerConfiguration configuration,
		out string errorCategory, out string error) {
		configuration = null;
		errorCategory = null;
		error = null;
		if (string.IsNullOrWhiteSpace(dbServerName)) {
			errorCategory = "configuration";
			error = "A configured PostgreSQL server name is required.";
			return false;
		}
		if (!TryReloadSettings(out error)) {
			errorCategory = "configuration";
			return false;
		}
		configuration = _settingsRepository.GetLocalDbServer(dbServerName);
		if (configuration is null) {
			errorCategory = "configuration";
			error = $"Database server '{dbServerName}' was not found or is disabled.";
			return false;
		}
		if (!IsPostgres(configuration)) {
			errorCategory = "configuration";
			error = $"Database server '{dbServerName}' is not PostgreSQL.";
			configuration = null;
			return false;
		}
		if (string.IsNullOrWhiteSpace(configuration.Hostname)
			|| configuration.Port is <= 0 or > 65535
			|| string.IsNullOrWhiteSpace(configuration.Username)) {
			errorCategory = "configuration";
			error = $"Database server '{dbServerName}' has incomplete PostgreSQL connection settings.";
			configuration = null;
			return false;
		}
		return true;
	}

	private bool TryReloadSettings(out string error) {
		SettingsReloadResult result = _settingsRepository.Reload();
		if (result is not { Reloaded: false }) {
			error = null;
			return true;
		}
		error = $"Unable to reload clio settings. {result.Warning}".Trim();
		return false;
	}

	private Postgres CreatePostgres(LocalDbServerConfiguration configuration) =>
		_dbClientFactory.CreatePostgres(configuration.Hostname, configuration.Port,
			configuration.Username, configuration.Password);

	private static bool IsPostgres(LocalDbServerConfiguration configuration) =>
		configuration?.DbType is not null
		&& (configuration.DbType.Equals("postgres", StringComparison.OrdinalIgnoreCase)
			|| configuration.DbType.Equals("postgresql", StringComparison.OrdinalIgnoreCase));

	private static DbTemplatePruneResult Failure(string dbServerName, IReadOnlyCollection<string> names,
		string errorCategory, string error) {
		DbTemplatePruneItemResult[] results = names
			.Select(name => new DbTemplatePruneItemResult(name, FailedOutcome, error))
			.ToArray();
		return new DbTemplatePruneResult(false, CompleteFailureStatus, dbServerName, results,
			errorCategory, error);
	}

	private static bool IsDatabaseException(Exception exception) =>
		exception is NpgsqlException or TimeoutException or InvalidOperationException;

	private static (string Category, string Message) DescribeDatabaseFailure(Exception exception,
		string dbServerName) {
		if (exception is PostgresException postgresException
			&& postgresException.SqlState.StartsWith("28", StringComparison.Ordinal)) {
			return ("authentication",
				$"PostgreSQL authentication failed for configured server '{dbServerName}'; verify its credentials.");
		}
		if (exception is PostgresException { SqlState: "42501" }) {
			return ("permission",
				$"PostgreSQL denied access on configured server '{dbServerName}'; verify the configured role's permissions.");
		}
		return ("connection",
			$"Unable to inventory configured PostgreSQL server '{dbServerName}'; verify reachability and permissions.");
	}
}
