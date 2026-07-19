using System.Collections.Generic;
using System.IO;
using Clio.Common.db;
using Clio.Common.DbHub;
using Clio.Command.McpServer.Knowledge;

namespace Clio.UserEnvironment
{
	/// <summary>
	/// Provides access to persisted clio settings and registered environment definitions.
	/// </summary>
	public interface ISettingsRepository
	{
		/// <summary>
		/// Gets the path to the clio appsettings file.
		/// </summary>
		string AppSettingsFilePath
		{
			get;
		}

		/// <summary>
		/// Returns all registered environments keyed by their clio name.
		/// </summary>
		Dictionary<string, EnvironmentSettings> GetAllEnvironments();

		/// <summary>
		/// Determines whether a named environment exists in settings.
		/// </summary>
		/// <param name="name">The clio environment key.</param>
		/// <returns><c>true</c> when the environment exists; otherwise <c>false</c>.</returns>
		bool IsEnvironmentExists(string name);

		/// <summary>
		/// Finds the first registered environment name that matches the supplied uri.
		/// </summary>
		/// <param name="uri">The environment uri to search for.</param>
		/// <returns>The matching environment name.</returns>
		string FindEnvironmentNameByUri(string uri);

		/// <summary>
		/// Gets a registered environment by name or the active environment when omitted.
		/// </summary>
		/// <param name="name">Optional clio environment key.</param>
		/// <returns>The resolved environment settings.</returns>
		EnvironmentSettings GetEnvironment(string name = null);

		/// <summary>
		/// Resolves an environment using command options that may contain environment name or uri data.
		/// </summary>
		/// <param name="options">Command options that identify the target environment.</param>
		/// <returns>The resolved environment settings.</returns>
		EnvironmentSettings GetEnvironment(EnvironmentOptions options);
		
		/// <summary>
		/// Finds a registered environment by name or active selection without throwing when it does not exist.
		/// </summary>
		/// <param name="name">Optional clio environment key.</param>
		/// <returns>The matching environment settings, or <c>null</c>.</returns>
		EnvironmentSettings? FindEnvironment(string name = null);
			
		/// <summary>
		/// Marks the supplied environment as active.
		/// </summary>
		/// <param name="name">The clio environment key to activate.</param>
		void SetActiveEnvironment(string name);

		/// <summary>
		/// Creates or updates the specified environment configuration.
		/// </summary>
		/// <param name="name">The clio environment key.</param>
		/// <param name="environment">The settings to persist.</param>
		void ConfigureEnvironment(string name, EnvironmentSettings environment);

		/// <summary>
		/// Removes the specified environment from settings.
		/// </summary>
		/// <param name="name">The clio environment key to remove.</param>
		void RemoveEnvironment(string name);

		/// <summary>
		/// Writes settings to the supplied text writer.
		/// </summary>
		/// <param name="textWriter">The output writer.</param>
		/// <param name="name">Optional environment name filter.</param>
		/// <param name="showShort">When <c>true</c>, emits the short format.</param>
		void ShowSettingsTo(TextWriter textWriter, string name, bool showShort = false);

		/// <summary>
		/// Opens the settings file in the associated editor.
		/// </summary>
		void OpenFile();

		/// <summary>
		/// Removes all registered environments from settings.
		/// </summary>
		void RemoveAllEnvironment();
		
		/// <summary>
		/// Gets the configured IIS clio root path.
		/// </summary>
		string GetIISClioRootPath();

		/// <summary>
		/// Gets the configured Creatio products folder path.
		/// </summary>
		string GetCreatioProductsFolder();

		/// <summary>
		/// Gets the configured remote artifact server path.
		/// </summary>
		string GetRemoteArtefactServerPath();

		/// <summary>
		/// Gets the configured global workspaces root path.
		/// </summary>
		string GetWorkspacesRoot();

		/// <summary>
		/// Gets the explicitly configured root directory for installed Clio knowledge.
		/// </summary>
		/// <returns>The configured absolute path, or <c>null</c> when it has not been initialized.</returns>
		string GetKnowledgeRootPath();

		/// <summary>
		/// Persists the root directory used for installed Clio knowledge.
		/// </summary>
		/// <param name="path">The absolute knowledge root path.</param>
		void SetKnowledgeRootPath(string path);

		/// <summary>
		/// Returns the configured knowledge root or atomically persists the supplied default when absent.
		/// </summary>
		/// <param name="defaultPath">The absolute default path to persist when no value exists.</param>
		/// <returns>The configured or newly persisted absolute path.</returns>
		string GetOrCreateKnowledgeRootPath(string defaultPath);

		/// <summary>
		/// Gets a detached, validated snapshot of the multi-source knowledge configuration.
		/// </summary>
		/// <returns>The configured root, sources, and topic pins.</returns>
		KnowledgeConfiguration GetKnowledgeConfiguration();

		/// <summary>
		/// Replaces the complete multi-source knowledge configuration atomically.
		/// </summary>
		/// <param name="configuration">The validated configuration to persist.</param>
		void SetKnowledgeConfiguration(KnowledgeConfiguration configuration);

		/// <summary>
		/// Adds or replaces one knowledge source without overwriting concurrent changes to other settings.
		/// </summary>
		/// <param name="alias">The operator-friendly source alias.</param>
		/// <param name="source">The trusted source configuration.</param>
		void UpsertKnowledgeSource(string alias, KnowledgeSourceConfiguration source);

		/// <summary>
		/// Adds one knowledge source only when neither its alias nor stable library identity is configured.
		/// </summary>
		/// <param name="alias">The operator-friendly source alias.</param>
		/// <param name="source">The trusted source configuration.</param>
		/// <returns><c>true</c> when the source was added; otherwise, <c>false</c>.</returns>
		bool TryAddKnowledgeSource(string alias, KnowledgeSourceConfiguration source);

		/// <summary>
		/// Removes one configured knowledge source while leaving its installed cache untouched.
		/// </summary>
		/// <param name="alias">The operator-friendly source alias.</param>
		/// <returns><c>true</c> when the source existed and was removed.</returns>
		bool RemoveKnowledgeSource(string alias);

		/// <summary>
		/// Removes one configured knowledge source only when it still matches the supplied snapshot.
		/// </summary>
		/// <param name="alias">The operator-friendly source alias.</param>
		/// <param name="expected">The exact source snapshot observed before the operation.</param>
		/// <returns><c>true</c> when the unchanged source existed and was removed.</returns>
		bool TryRemoveKnowledgeSource(string alias, KnowledgeSourceConfiguration expected);

		/// <summary>
		/// Persists a discovered Git branch only when the source still matches the supplied snapshot.
		/// </summary>
		/// <param name="alias">The operator-friendly source alias.</param>
		/// <param name="expected">The exact source snapshot used for retrieval.</param>
		/// <param name="branch">The verified remote default branch.</param>
		/// <returns><c>true</c> when the branch was stored or already matched.</returns>
		bool TrySetKnowledgeSourceBranch(string alias, KnowledgeSourceConfiguration expected, string branch);

		/// <summary>
		/// Enables or disables one configured knowledge source without deleting its installed cache.
		/// </summary>
		/// <param name="alias">The operator-friendly source alias.</param>
		/// <param name="enabled">Whether the source should participate.</param>
		void SetKnowledgeSourceEnabled(string alias, bool enabled);

		/// <summary>
		/// Gets the configured container image CLI used by build-docker-image.
		/// </summary>
		/// <returns>The configured container image CLI name.</returns>
		string GetContainerImageCli();

		/// <summary>
		/// Gets the product telemetry upload configuration.
		/// </summary>
		/// <returns>The configured telemetry settings; never <c>null</c>.</returns>
		TelemetrySettings GetTelemetrySettings();

		/// <summary>
		/// Gets a named local database server configuration.
		/// </summary>
		/// <param name="name">The configured local database server name.</param>
		/// <returns>The matching local database server configuration.</returns>
		LocalDbServerConfiguration GetLocalDbServer(string name);

		/// <summary>
		/// Lists configured local database server names.
		/// </summary>
		/// <returns>The configured local database server names.</returns>
		IEnumerable<string> GetLocalDbServerNames();

		/// <summary>
		/// Gets a named local Redis server configuration.
		/// </summary>
		/// <param name="name">The configured local Redis server name.</param>
		/// <returns>The matching local Redis server configuration.</returns>
		LocalRedisServerConfiguration GetLocalRedisServer(string name);

		/// <summary>
		/// Lists configured local Redis server names.
		/// </summary>
		/// <returns>The configured local Redis server names.</returns>
		IEnumerable<string> GetLocalRedisServerNames();

		/// <summary>
		/// Gets the default local Redis server name.
		/// </summary>
		/// <returns>The configured default local Redis server name.</returns>
		string GetDefaultLocalRedisServerName();

		/// <summary>
		/// Determines whether local Redis servers are configured.
		/// </summary>
		/// <returns><c>true</c> when at least one local Redis server is configured; otherwise <c>false</c>.</returns>
		bool HasLocalRedisServersConfiguration();

		/// <summary>
		/// Gets the default values applied to the <c>deploy-creatio</c> command when the matching option is
		/// not supplied on the command line.
		/// </summary>
		/// <returns>The configured deploy-creatio defaults; never <c>null</c> (an empty instance when none are set).</returns>
		DeployCreatioDefaults GetDeployCreatioDefaults();

		/// <summary>
		/// Persists the default values applied to the <c>deploy-creatio</c> command. Passing <c>null</c> or an
		/// empty instance clears the stored defaults.
		/// </summary>
		/// <param name="defaults">The defaults to persist, or <c>null</c>/empty to clear them.</param>
		void SetDeployCreatioDefaults(DeployCreatioDefaults defaults);

		/// <summary>Gets the preferred LocalMachine/My certificate thumbprint for IIS HTTPS deployment.</summary>
		/// <returns>The normalized thumbprint, or <c>null</c> when no preference is configured.</returns>
		string GetPinnedIisCertificateThumbprint();

		/// <summary>Persists or clears the preferred LocalMachine/My certificate thumbprint.</summary>
		/// <param name="thumbprint">The normalized thumbprint, or <c>null</c> to clear it.</param>
		void SetPinnedIisCertificateThumbprint(string thumbprint);

		/// <summary>Gets a detached snapshot of the local dbHub integration settings.</summary>
		/// <returns>Configured settings, or safe disabled defaults when absent.</returns>
		DbHubSettings GetDbHubSettings();

		/// <summary>Persists local dbHub integration settings.</summary>
		/// <param name="settings">Validated settings to persist.</param>
		void SetDbHubSettings(DbHubSettings settings);

		/// <summary>
		/// Gets the default active environment name.
		/// </summary>
		/// <returns>The configured default environment name.</returns>
		string GetDefaultEnvironmentName();
		
		/// <summary>
		/// Gets the actual environment name to use based on the supplied environment name or default when omitted.
		/// </summary>
		/// <param name="environmentName"></param>
		/// <returns></returns>
		string GetActualEnvironmentName(string environmentName);

		/// <summary>
		/// Gets the AutoUpdate setting. Returns true when not explicitly configured (opt-out model).
		/// </summary>
		bool GetAutoupdate();

		/// <summary>
		/// Persists the AutoUpdate setting.
		/// </summary>
		void SetAutoupdate(bool value);

		/// <summary>
		/// Determines whether the named feature flag is enabled.
		/// </summary>
		/// <param name="featureName">
		/// The feature key. A <c>null</c> or whitespace value is treated as disabled. Feature keys are
		/// matched case-insensitively (<see cref="System.StringComparer.OrdinalIgnoreCase"/>), so
		/// <c>AiAssist</c>, <c>aiassist</c>, and <c>AIASSIST</c> all resolve to the same flag.
		/// </param>
		/// <returns>
		/// <c>true</c> when the flag exists and is set to <c>true</c>; otherwise <c>false</c>
		/// (absent key, <c>false</c> value, or an empty/whitespace name).
		/// </returns>
		bool IsFeatureEnabled(string featureName);

		/// <summary>
		/// Creates or updates the named feature flag and persists the change.
		/// </summary>
		/// <param name="featureName">
		/// The feature key. Keys are matched case-insensitively, so re-setting an existing flag with
		/// a different casing updates the same entry rather than creating a duplicate. The key is
		/// stored as supplied on first write.
		/// </param>
		/// <param name="enabled">Whether the feature should be enabled.</param>
		/// <exception cref="System.ArgumentException">
		/// Thrown when <paramref name="featureName"/> is <c>null</c>, empty, or whitespace.
		/// </exception>
		void SetFeature(string featureName, bool enabled);

		/// <summary>
		/// Gets a snapshot of all configured feature flags keyed by feature name.
		/// </summary>
		/// <returns>A copy of the current feature flags; mutating it does not affect stored settings.</returns>
		IReadOnlyDictionary<string, bool> GetFeatures();
	}
}
