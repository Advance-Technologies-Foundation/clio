using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Clio.Command.McpServer.Progress;
using Clio.Common.DbHub;
using Clio.Common.db;
using Clio.Common.K8;
using Clio.Requests;
using Clio.UserEnvironment;
using Microsoft.Data.SqlClient;
using Npgsql;
using OneOf;
using OneOf.Types;

namespace Clio.Common;

/// <summary>
/// Raised to abort an uninstall run safely without performing any destructive step or reporting success.
/// </summary>
/// <remarks>
/// Used by <see cref="CreatioUninstaller"/> when a precondition for the destructive stages cannot be met
/// (for example the environment configuration cannot be read). By the time it is thrown the stage-event
/// stream has already emitted a <c>failed</c> stage plus a <c>run-completed</c> with <c>outcome=failure</c>,
/// so the caller only needs to translate it into a non-success exit code (never a silent success).
/// </remarks>
public sealed class CreatioUninstallAbortedException : Exception
{

	/// <summary>
	/// Initializes a new instance of the <see cref="CreatioUninstallAbortedException"/> class.
	/// </summary>
	/// <param name="message">A non-secret, user-facing explanation of why the uninstall was aborted.</param>
	public CreatioUninstallAbortedException(string message) : base(message) { }

}

public interface ICreatioUninstaller
{

	#region Methods: Public

	/// <summary>
	///     Uninstalls Creatio by the specified environment name.
	/// </summary>
	/// <param name="environmentName">The name of the environment to uninstall.</param>
	/// <remarks>
	///     <list type="number">
	///         <item>Retrieves the environment settings based on the provided environment name.</item>
	///         <item>Uses the registered <c>EnvironmentPath</c> as the destructive filesystem identity.</item>
	///         <item>Finds and prevalidates every IIS target mapped to that normalized path.</item>
	///         <item>
	///             Stops and removes all safe matching IIS targets, then proceeds to
	///             <see cref="UninstallByPath"> uninstall</see> Creatio from the registered directory.
	///         </item>
	///     </list>
	///     Unregistering the environment is the final stage and runs only after the destructive cleanup
	///     (drop-database, delete-files) has completed. When automatic dbHub synchronization is enabled,
	///     its clio-owned source is removed on a best-effort basis immediately before unregistering. On any failure the environment is left registered
	///     so the operation can be retried.
	/// </remarks>
	/// <exception cref="CreatioUninstallAbortedException">
	///     Thrown when the environment is not registered with a local path or an IIS target mapped to that path
	///     cannot be validated safely; the run is aborted before any destructive step.
	/// </exception>
	/// <seealso cref="UninstallByPath" />
	public void UninstallByEnvironmentName(string environmentName);

	/// <summary>
	///     Uninstalls Creatio by the specified directory path
	/// </summary>
	/// <param name="creatioDirectoryPath">
	///     Path to a directory where creatio is installed.
	///     Example: C:\inetpub\wwwroot\site_one
	/// </param>
	/// <remarks>
	///     <list type="number">
	///         This method performs the following ordered stages:
	///         <item>IIS - Stop the site / application pool (<c>stop-iis</c>).</item>
	///         <item>Read the database configuration from ConnectionStrings.config (<c>read-config</c>).</item>
	///         <item>IIS - Delete the site / application pool (<c>delete-iis</c>).</item>
	///         <item>Drop the application database (<c>drop-db</c>).</item>
	///         <item>Delete the application files under the install directory (<c>delete-files</c>).</item>
	///         <item>
	///             On Windows, the registered IIS virtual-account profile is deleted on a best-effort basis
	///             (<c>delete-apppool-profile</c>); exhausted native retries produce a warning and do not fail uninstall.
	///         </item>
	///         <item>Remove the clio-owned dbHub source on a best-effort basis (<c>remove-dbhub-source</c>).</item>
	///     </list>
	///     If <c>read-config</c> fails the run is aborted before any destructive step (no database is dropped,
	///     no files are deleted) and a <see cref="CreatioUninstallAbortedException"/> is thrown rather than
	///     silently skipping cleanup while reporting success.
	/// </remarks>
	/// <exception cref="CreatioUninstallAbortedException">
	///     Thrown when the database configuration cannot be read; the run is aborted before any destructive step.
	/// </exception>
	/// <seealso cref="UninstallByPath" />
	public void UninstallByPath(string creatioDirectoryPath);

	#endregion

}

public class CreatioUninstaller : ICreatioUninstaller, IStageEventSource
{

	#region Fields: Private

	private readonly IFileSystem _fileSystem;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IIisScanner _iisScanner;
	private readonly ILogger _logger;
	private readonly Ik8Commands _k8Commands;
	private readonly IMssql _mssql;
	private readonly IPostgres _postgres;
	private readonly IStageEventEmitter _stageEventEmitter;
	private readonly IAppPoolProfileCleaner _appPoolProfileCleaner;
	private readonly IDbHubSynchronizationService _dbHubSynchronizationService;

	#endregion

	#region Constructors: Public

	public CreatioUninstaller(IFileSystem fileSystem, ISettingsRepository settingsRepository,
		IIisScanner iisScanner, ILogger logger, Ik8Commands k8Commands, IMssql mssql, IPostgres postgres,
		IStageEventEmitter stageEventEmitter, IAppPoolProfileCleaner appPoolProfileCleaner,
		IDbHubSynchronizationService dbHubSynchronizationService = null){
		_fileSystem = fileSystem;
		_settingsRepository = settingsRepository;
		_iisScanner = iisScanner;
		_logger = logger;
		_k8Commands = k8Commands;
		_mssql = mssql;
		_postgres = postgres;
		_stageEventEmitter = stageEventEmitter;
		_appPoolProfileCleaner = appPoolProfileCleaner;
		_dbHubSynchronizationService = dbHubSynchronizationService;
	}

	#endregion

	#region Events: Public

	/// <inheritdoc />
	public event EventHandler<ClioStageEvent> StageChanged;

	#endregion

	#region Properties: Private

	#endregion

	#region Methods: Private

	private static OneOf<DbInfo, Error> GetDbInfoFromXmlContent(string csContent){
		XmlDocument doc = new();
		doc.LoadXml(csContent);

		const string mssqlMarker = "Data Source=";
		const string psqlMarker = "Server=";

		XmlNodeList nodes = doc.ChildNodes;
		foreach (object node in nodes) {
			if (node is XmlElement element && element.Name == "connectionStrings") {
				foreach (object childNode in element.ChildNodes) {
					if (childNode is XmlElement childElement && childElement.Name == "add") {
						string name = childElement.GetAttribute("name");
						if (name == "db") {
							string connectionString = childElement.GetAttribute("connectionString");
							{
								if (connectionString.Contains(psqlMarker)) {
									const string pattern = @"Database=([^;]+)";
									Match match = Regex.Match(connectionString, pattern);
									if (match.Success) {
										string dbName = match.Groups[1].Value;
										return new DbInfo(dbName, "PostgreSql", connectionString);
									}
									return new Error();
								}
								if (connectionString.Contains(mssqlMarker)) {
									const string pattern = @"Catalog=([^;]+)";
									Match match = Regex.Match(connectionString, pattern);
									if (match.Success) {
										string dbName = match.Groups[1].Value;
										return new DbInfo(dbName, "MsSql", connectionString);
									}
									return new Error();
								}
							}
						}
					}
				}
			}
		}
		return new Error();
	}

	private OneOf<DbInfo, Error> GetDbInfoFromConnectionStringsFile(string creatioDirectoryPath){
		const string connectionStringsFileName = "ConnectionStrings.config";
		string connectionStringsPath = Path.Join(creatioDirectoryPath, connectionStringsFileName);
		bool csExists = _fileSystem.ExistsFile(connectionStringsPath);
		if (!csExists) {
			_logger.WriteWarning($"ConnectionStrings file not found in: {creatioDirectoryPath}");
			return new Error();
		}
		string csPath = Path.Join(creatioDirectoryPath, connectionStringsFileName);
		string csContent = _fileSystem.ReadAllText(csPath);
		if (string.IsNullOrWhiteSpace(csContent)) {
			_logger.WriteWarning($"Could not read ConnectionStrings file from : {creatioDirectoryPath}");
			return new Error();
		}
		return GetDbInfoFromXmlContent(csContent);
	}

	private k8Commands.ConnectionStringParams ParseConnectionStringToParams(string connectionString, string dbType){
		if (dbType == "PostgreSql") {
			NpgsqlConnectionStringBuilder builder = new(connectionString);
			string host = builder.Host ?? "localhost";
			int port = builder.Port != 0 ? builder.Port : 5432;
			string username = builder.Username ?? "postgres";
			string password = builder.Password ?? "";
			
			_logger.WriteInfo($"Parsed PostgreSQL connection: Host={host}, Port={port}, User={username}");
			
			// Return with 0 for Redis ports as they're not used in this context
			return new k8Commands.ConnectionStringParams(port, port, 0, 0, username, password);
		}
		
		if (dbType == "MsSql") {
			SqlConnectionStringBuilder builder = new(connectionString);
			string dataSource = builder.DataSource ?? "localhost";
			
			// Parse host and port from DataSource (e.g., "server,1433" or "server\instance")
			string host = dataSource;
			int port = 1433; // Default MSSQL port
			
			// Check if DataSource contains port (e.g., "server,1433")
			if (dataSource.Contains(',')) {
				string[] parts = dataSource.Split(',');
				host = parts[0];
				if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort)) {
					port = parsedPort;
				}
			}
			
			string username = builder.UserID ?? "";
			string password = builder.Password ?? "";
			
			// Log if using Integrated Security
			if (builder.IntegratedSecurity) {
				_logger.WriteInfo($"Parsed MSSQL connection: Host={host}, Port={port}, Using Integrated Security");
			} else {
				_logger.WriteInfo($"Parsed MSSQL connection: Host={host}, Port={port}, User={username}");
			}
			
			// Return with 0 for Redis ports as they're not used in this context
			return new k8Commands.ConnectionStringParams(port, port, 0, 0, username, password);
		}
		
		throw new ArgumentException($"Unsupported database type: {dbType}");
	}

	#endregion

	#region Methods: Public

	public void UninstallByEnvironmentName(string environmentName){
		EnvironmentSettings settings = _settingsRepository.FindEnvironment(environmentName);
		if (settings is null || string.IsNullOrWhiteSpace(settings.EnvironmentPath)) {
			AbortUnresolvedTarget(
				$"Environment '{environmentName}' is not registered with a local EnvironmentPath.");
			return;
		}

		if (!TryCanonicalizeDestructivePath(settings.EnvironmentPath, out string resolvedPath)) {
			AbortUnresolvedTarget(
				$"Environment '{environmentName}' has an unsafe or invalid EnvironmentPath.");
			return;
		}
		_logger.WriteInfo($"Uninstalling Creatio from registered directory: {resolvedPath}");

		// The environment name flows into the pipeline so unregister runs as the final stage and only
		// after the destructive cleanup has succeeded (Correction C); a partial failure leaves it registered.
		RunUninstall(resolvedPath, environmentName);
	}

	private void AbortUnresolvedTarget(string message) {
		_logger.WriteError(message);
		_stageEventEmitter.Begin(ClioStageEventContract.Operations.Uninstall,
			BuildUninstallManifest(includeProfileStage: false), OnStageChanged);
		_stageEventEmitter.CompleteFailure("Uninstall target resolution failed", message,
			"uninstall-target-not-found");
		throw new CreatioUninstallAbortedException(message);
	}

	public void UninstallByPath(string creatioDirectoryPath){
		// No environment name: there is nothing to unregister, so the unregister stage is reported skipped.
		if (!TryCanonicalizeDestructivePath(creatioDirectoryPath, out string resolvedPath)) {
			AbortUnresolvedTarget("The specified uninstall path is unsafe or invalid.");
			return;
		}
		RunUninstall(resolvedPath, environmentName: null);
	}

	#endregion

	#region Methods: Private

	// Bubbles every emitted stage event to subscribers (the command re-raises this so the MCP tool can
	// subscribe uniformly, ADR D3). With no subscriber attached the raise is a no-op.
	private void OnStageChanged(ClioStageEvent stageEvent){
		StageChanged?.Invoke(this, stageEvent);
	}

	/// <summary>
	/// Builds the uninstall manifest from the resolved execution path (ADR fact 8): the six ordered stages,
	/// plus the conditional <c>delete-apppool-profile</c> stage inserted before <c>unregister</c> only when an
	/// actual IIS application-pool name was captured. <c>unregister</c> is always the final stage.
	/// </summary>
	internal static IReadOnlyList<StageDescriptor> BuildUninstallManifest(bool includeProfileStage,
		bool includeDbHubRemoval = false){
		List<StageDescriptor> stages = [
			new StageDescriptor(StageIds.ReadConfig, "Read configuration", false),
			new StageDescriptor(StageIds.StopIis, "Stop IIS site", false),
			new StageDescriptor(StageIds.DeleteIis, "Delete IIS site", false),
			new StageDescriptor(StageIds.DropDb, "Drop database", false),
			new StageDescriptor(StageIds.DeleteFiles, "Delete application files", false)
		];
		if (includeProfileStage) {
			stages.Add(new StageDescriptor(StageIds.DeleteApppoolProfile, "Delete application-pool profile", true));
		}
		if (includeDbHubRemoval) {
			stages.Add(new StageDescriptor(StageIds.RemoveDbHubSource, "Remove dbHub source", true));
		}
		stages.Add(new StageDescriptor(StageIds.Unregister, "Unregister environment", false));
		return stages;
	}

	// Drives the whole ordered uninstall through the shared stage-event emitter. The emitter is the single
	// redaction + failure-cascade boundary: if any wrapped stage throws it emits failed + cascades the
	// remaining stages as skipped(after-failure) + run-completed(failure) and rethrows, so unregister never
	// runs after a partial failure (Correction C). Emission is observational: with no subscriber it is a no-op.
	private void RunUninstall(string creatioDirectoryPath, string environmentName){
		if (string.IsNullOrEmpty(creatioDirectoryPath) || !_fileSystem.ExistsDirectory(creatioDirectoryPath)) {
			AbortUnresolvedTarget($"Directory '{creatioDirectoryPath}' does not exist. The uninstall target could not be resolved.");
		}

		bool hasEnvironment = !string.IsNullOrWhiteSpace(environmentName);
		bool synchronizeDbHub = hasEnvironment
			&& _dbHubSynchronizationService?.IsAutomaticSynchronizationEnabled() == true;
		IReadOnlyList<UnregisteredSite> targetSites = ResolveSites(creatioDirectoryPath);
		foreach (UnregisteredSite targetSite in targetSites) {
			if (!_iisScanner.IsIisTargetExclusive(targetSite.siteBinding.name)) {
				AbortUnresolvedTarget(
					$"IIS target '{targetSite.siteBinding.name}' contains other applications or could not be validated safely.");
			}
		}
		string[] targetNames = targetSites.Select(site => site.siteBinding.name).ToArray();
		IReadOnlyCollection<string> discoveredPools = [];
		if (targetNames.Length > 0
			&& !_iisScanner.TryFindAppPoolsForTargets(targetNames, out discoveredPools)) {
			AbortUnresolvedTarget("The IIS application-pool inventory could not be read completely.");
		}
		string[] appPoolNames = discoveredPools.ToArray();
		bool includeProfileStage = appPoolNames.Length > 0;
		Dictionary<string, AppPoolProfileCleanupTarget> profileTargets = appPoolNames.ToDictionary(
			name => name,
			name => _appPoolProfileCleaner.Prepare(name),
			StringComparer.OrdinalIgnoreCase);
		Dictionary<string, bool> appPoolDeleted = appPoolNames.ToDictionary(
			name => name,
			_ => false,
			StringComparer.OrdinalIgnoreCase);

		_stageEventEmitter.Begin(ClioStageEventContract.Operations.Uninstall,
			BuildUninstallManifest(includeProfileStage, synchronizeDbHub), OnStageChanged);

		DbInfo dbInfo = null;
		_stageEventEmitter.RunStage(StageIds.ReadConfig, () => {
			OneOf<DbInfo, Error> result = GetDbInfoFromConnectionStringsFile(creatioDirectoryPath);
			// Correction A: a config/connection read failure aborts the run before any destructive step
			// rather than silently skipping drop-db + delete-files while reporting success.
			if (result.Value is not DbInfo info) {
				throw new CreatioUninstallAbortedException(
					"Uninstall aborted: could not read the database configuration from ConnectionStrings.config. "
					+ "No database was dropped, no files were deleted, and the environment remains registered.");
			}
			dbInfo = info;
			_logger.WriteInfo($"Found db: {info.DbName}, Server: {info.DbType}");
		});

		_stageEventEmitter.RunStage(StageIds.StopIis,
			() => {
				foreach (UnregisteredSite targetSite in targetSites) {
					if (!_iisScanner.TryStopIisTarget(targetSite.siteBinding.name,
						targetSite.siteBinding.path, targetSite.siteBinding.appPoolName)) {
						throw new CreatioUninstallAbortedException(
							$"IIS target '{targetSite.siteBinding.name}' changed and cannot be stopped safely.");
					}
				}
				foreach (string appPoolName in appPoolNames) {
					if (_iisScanner.StopAppPoolIfOwnedByTargets(appPoolName, targetNames)
						== IisAppPoolMutationResult.Failed) {
						throw new CreatioUninstallAbortedException(
							$"Application pool '{appPoolName}' could not be validated or stopped safely.");
					}
				}
			});

		_stageEventEmitter.RunStage(StageIds.DeleteIis, () => {
			foreach (UnregisteredSite targetSite in targetSites) {
				if (!_iisScanner.TryDeleteIisTarget(targetSite.siteBinding.name,
					targetSite.siteBinding.path, targetSite.siteBinding.appPoolName)) {
					throw new CreatioUninstallAbortedException(
						$"IIS target '{targetSite.siteBinding.name}' changed or could not be removed safely.");
				}
			}
			foreach (string appPoolName in appPoolNames) {
				IisAppPoolMutationResult result = _iisScanner.DeleteAppPoolIfUnused(appPoolName);
				if (result == IisAppPoolMutationResult.Failed) {
					throw new CreatioUninstallAbortedException(
						$"Application pool '{appPoolName}' could not be validated or removed safely.");
				}
				appPoolDeleted[appPoolName] = result == IisAppPoolMutationResult.Completed;
			}
			if (!_iisScanner.TryFindAllIisTargets(out IReadOnlyList<UnregisteredSite> remainingTargets)
				|| remainingTargets.Any(site => PathsEqual(site.siteBinding.path, creatioDirectoryPath))) {
				throw new CreatioUninstallAbortedException(
					"IIS cleanup could not prove that every target mapped to the registered path was removed.");
			}
		});

		_stageEventEmitter.RunStage(StageIds.DropDb, () => DropDatabase(dbInfo));

		_stageEventEmitter.RunStage(StageIds.DeleteFiles, () => {
			_fileSystem.DeleteDirectory(creatioDirectoryPath, true);
			_logger.WriteInfo($"Directory: {creatioDirectoryPath} deleted");
		});

		List<AppPoolProfileCleanupResult> profileResults = [];
		List<string> profileWarnings = [];
		if (includeProfileStage) {
			foreach (string appPoolName in appPoolNames) {
				AppPoolProfileCleanupResult result = !appPoolDeleted[appPoolName]
					|| !_iisScanner.IsAppPoolAbsent(appPoolName)
					? new AppPoolProfileCleanupResult(AppPoolProfileCleanupStatus.NotApplicable)
					: _appPoolProfileCleaner.TryDelete(profileTargets[appPoolName]);
				profileResults.Add(result);
				if (result.Status == AppPoolProfileCleanupStatus.Deleted) {
					_logger.WriteInfo($"Application-pool profile '{result.ProfilePath}' deleted");
				}
				else if (result.Status == AppPoolProfileCleanupStatus.Warning) {
					string profileLabel = result.ProfilePath ?? $@"IIS APPPOOL\{appPoolName}";
					profileWarnings.Add(
						$"Application-pool profile '{profileLabel}' could not be removed because it is currently in use or Windows denied access. Delete it manually after the locking process exits.");
				}
			}
			if (profileWarnings.Count > 0) {
				AppPoolProfileCleanupResult[] warningResults = [.. profileResults
					.Where(result => result.Status == AppPoolProfileCleanupStatus.Warning)];
				string warningDetail = string.Join(" ", warningResults.Select(result => result.Detail));
				string warningErrorCode = warningResults.Length == 1
					? warningResults[0].ErrorCode
					: "APPPOOL_PROFILE_DELETE_WARNING";
				_stageEventEmitter.WarnStage(StageIds.DeleteApppoolProfile,
					"One or more application-pool profiles could not be removed.", warningDetail,
					warningErrorCode);
			}
			else if (profileResults.Any(result => result.Status == AppPoolProfileCleanupStatus.Deleted)) {
				_stageEventEmitter.RunStage(StageIds.DeleteApppoolProfile, () => { });
			}
			else {
				_stageEventEmitter.SkipStage(StageIds.DeleteApppoolProfile,
					ClioStageEventContract.SkipReasons.NotApplicable);
			}
		}

		DbHubWarning dbHubWarning = null;
		if (synchronizeDbHub) {
			DbHubSyncResult dbHubResult = _dbHubSynchronizationService.RemoveEnvironmentSource(environmentName);
			dbHubWarning = dbHubResult.Warning;
			if (dbHubWarning is not null) {
				_stageEventEmitter.WarnStage(StageIds.RemoveDbHubSource, dbHubWarning.Message,
					dbHubWarning.Detail, dbHubWarning.ErrorCode);
				_logger.WriteWarning(string.IsNullOrWhiteSpace(dbHubWarning.Detail)
					? dbHubWarning.Message
					: $"{dbHubWarning.Message} {dbHubWarning.Detail}");
			}
			else if (dbHubResult.Skipped) {
				_stageEventEmitter.SkipStage(StageIds.RemoveDbHubSource,
					ClioStageEventContract.SkipReasons.NotApplicable);
			}
			else {
				_stageEventEmitter.RunStage(StageIds.RemoveDbHubSource, () => { });
			}
		}

		// Correction C: unregister is the final stage and runs only after the destructive cleanup above
		// succeeded. Without an environment name there is nothing registered to remove.
		if (hasEnvironment) {
			_stageEventEmitter.RunStage(StageIds.Unregister, () => {
				_settingsRepository.RemoveEnvironment(environmentName);
				_logger.WriteInfo($"Unregisted {environmentName} from clio");
			});
		}
		else {
			_stageEventEmitter.SkipStage(StageIds.Unregister, ClioStageEventContract.SkipReasons.NotApplicable);
		}
		foreach (string profileWarning in profileWarnings) {
			_logger.WriteWarning($"Creatio was uninstalled, but {profileWarning}");
		}

		bool profileHasWarning = profileWarnings.Count > 0;
		AppPoolProfileCleanupResult[] warningProfileResults = [.. profileResults
			.Where(result => result.Status == AppPoolProfileCleanupStatus.Warning)];
		string profileWarningDetail = string.Join(" ", warningProfileResults.Select(result => result.Detail));
		string profileWarningErrorCode = warningProfileResults.Length == 1
			? warningProfileResults[0].ErrorCode
			: "APPPOOL_PROFILE_DELETE_WARNING";
		if (profileHasWarning && dbHubWarning is not null) {
			_stageEventEmitter.CompleteSuccessWithWarnings("Creatio was uninstalled with warnings.",
				$"{profileWarningDetail} {dbHubWarning.Detail}".Trim(), "UNINSTALL_COMPLETED_WITH_WARNINGS",
				creatioDirectoryPath);
		}
		else if (profileHasWarning) {
			_stageEventEmitter.CompleteSuccessWithWarnings(
				"Creatio was uninstalled, but one or more application-pool profiles could not be removed.",
				profileWarningDetail, profileWarningErrorCode, creatioDirectoryPath);
		}
		else if (dbHubWarning is not null) {
			_stageEventEmitter.CompleteSuccessWithWarnings("Creatio was uninstalled with a dbHub warning.",
				dbHubWarning.Detail, dbHubWarning.ErrorCode, creatioDirectoryPath);
		}
		else {
			_stageEventEmitter.CompleteSuccess("Uninstall completed", derivedPath: creatioDirectoryPath);
		}
	}

	private IReadOnlyList<UnregisteredSite> ResolveSites(string creatioDirectoryPath){
		if (!_iisScanner.TryFindAllIisTargets(out IReadOnlyList<UnregisteredSite> targets)) {
			AbortUnresolvedTarget("The IIS target inventory could not be read completely.");
		}
		UnregisteredSite[] matchingTargets = targets
			.Where(site => PathsEqual(site.siteBinding.path, creatioDirectoryPath))
			.ToArray();
		return matchingTargets
			.Where(candidate => !candidate.siteBinding.name.Contains('/')
				|| !matchingTargets.Any(root => !root.siteBinding.name.Contains('/')
					&& candidate.siteBinding.name.StartsWith(root.siteBinding.name + "/",
						StringComparison.OrdinalIgnoreCase)))
			.ToArray();
	}

	private static bool TryCanonicalizeDestructivePath(string path, out string canonicalPath) {
		canonicalPath = null;
		if (string.IsNullOrWhiteSpace(path)) {
			return false;
		}
		try {
			string expandedPath = Environment.ExpandEnvironmentVariables(path);
			if (!Path.IsPathFullyQualified(expandedPath)) {
				return false;
			}
			canonicalPath = Path.GetFullPath(expandedPath).TrimEnd(
				Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string root = Path.GetPathRoot(canonicalPath)?.TrimEnd(
				Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return !string.IsNullOrWhiteSpace(canonicalPath)
				&& !string.Equals(canonicalPath, root, StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException
			or PathTooLongException) {
			return false;
		}
	}

	internal static bool PathsEqual(string firstPath, string secondPath) {
		if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath)) {
			return false;
		}
		try {
			string normalizedFirst = Path.GetFullPath(Environment.ExpandEnvironmentVariables(firstPath)).TrimEnd(
				Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string normalizedSecond = Path.GetFullPath(Environment.ExpandEnvironmentVariables(secondPath)).TrimEnd(
				Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException
			or PathTooLongException) {
			return false;
		}
	}

	private void DropDatabase(DbInfo info){
		// Try to parse local connection string first, fallback to K8s if parsing fails
		k8Commands.ConnectionStringParams cn;
		string host;
		try {
			cn = ParseConnectionStringToParams(info.ConnectionString, info.DbType);
			_logger.WriteInfo("Using local database connection from ConnectionStrings.config");

			// Extract host from connection string for Init call
			if (info.DbType == "PostgreSql") {
				NpgsqlConnectionStringBuilder builder = new(info.ConnectionString);
				host = builder.Host ?? "localhost";
			} else {
				SqlConnectionStringBuilder builder = new(info.ConnectionString);
				string dataSource = builder.DataSource ?? "localhost";
				// Extract just the host part (before comma or backslash for named instances)
				if (dataSource.Contains(',')) {
					host = dataSource.Split(',')[0];
				} else if (dataSource.Contains('\\')) {
					host = dataSource; // Keep full instance name
				} else {
					host = dataSource;
				}
			}
		} catch (Exception ex) {
			_logger.WriteWarning($"Failed to parse connection string, falling back to K8s: {ex.Message}");
			cn = info.DbType switch {
				"MsSql" => _k8Commands.GetMssqlConnectionString(),
				"PostgreSql" => _k8Commands.GetPostgresConnectionString(),
				var _ => throw new ArgumentException($"Unknown db type: {info.DbType}")
			};
			host = BindingsModule.k8sDns;
		}

		if(info.DbType == "MsSql") {
			_mssql.Init(host, cn.DbPort, cn.DbUsername, cn.DbPassword);
			_mssql.DropDb(info.DbName);
			_logger.WriteInfo($"MsSQL DB: {info.DbName} dropped");
		} else {
			_postgres.Init(host, cn.DbPort, cn.DbUsername, cn.DbPassword);
			_postgres.DropDb(info.DbName);
			_logger.WriteInfo($"Postgres DB: {info.DbName} dropped");
		}
	}

	#endregion

	private record DbInfo(string DbName, string DbType, string ConnectionString);

}
