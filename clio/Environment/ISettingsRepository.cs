using System.Collections.Generic;
using System.IO;
using Clio.Common.db;

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
		/// Gets the configured container image CLI used by build-docker-image.
		/// </summary>
		/// <returns>The configured container image CLI name.</returns>
		string GetContainerImageCli();

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
		/// Gets the AutoUpdate setting.
		/// </summary>
		/// <returns>true if auto-update is enabled; otherwise false.</returns>
		bool GetAutoupdate();
	}
}
