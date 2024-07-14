using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Clio.Common.db;
using Clio.Requests;
using Clio.UserEnvironment;
using MediatR;
using OneOf;
using OneOf.Types;

namespace Clio.Common;

public interface ICreatioUninstaller
{

	#region Methods: Public

	/// <summary>
	///     Uninstalls Creatio by the specified environment name.
	/// </summary>
	/// <param name="environmentName">The name of the environment to uninstall.</param>
	/// <remarks>
	///	<list type="number">
	///     This method performs the following operations:
	///     <item>Sends a request to gather all registered sites in IIS.</item>
	///     <item>Retrieves the environment settings based on the provided environment name.</item>
	///     <item>Checks if any site matches the environment's URL.</item>
	///     <item>If a matching site is found, proceeds to <see cref="UninstallByPath"> uninstall</see> Creatio from the directory associated with the site.</item>
	///     <item>Logs warnings if no sites are found or if the specified environment cannot be matched to any site.</item>
	/// </list>
	/// </remarks>
	///	<seealso cref="UninstallByPath"/>
	public void UninstallByEnvironmentName(string environmentName);

	/// <summary>
	///     Uninstalls Creatio by the specified directory path
	/// </summary>
	/// <param name="creatioDirectoryPath">Path to a directory where creatio is installed.
	///		Example: C:\inetpub\wwwroot\site_one
	/// </param>
	/// <remarks>
	///	<list type="number">
	///     This method performs the following operations:
	///     <item>IIS - Stop Application and AppPool.</item>
	///     <item>IIS - Delete Application and AppPool.</item>
	///     <item>Find DB from ConnectionString.</item>
	///     <item>If in Rancher, drop DB.</item>
	///     <item>Delete content in /wwwroot/{EnvironmentName}.</item>
	///     <item>Delete content for AppPool User (C:\Users\{AppPoolUser}).</item> 
	/// </list>
	/// </remarks>
	///	<seealso cref="UninstallByPath"/>
	public void UninstallByPath(string creatioDirectoryPath);

	#endregion
}

public class CreatioUninstaller : ICreatioUninstaller
{

	#region Fields: Private

	private readonly IFileSystem _fileSystem;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IMediator _mediator;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public CreatioUninstaller(IFileSystem fileSystem, ISettingsRepository settingsRepository, IMediator mediator,
		ILogger logger){
		_fileSystem = fileSystem;
		_settingsRepository = settingsRepository;
		_mediator = mediator;
		_logger = logger;
	}

	#endregion

	#region Properties: Private

	private IEnumerable<IISScannerHandler.UnregisteredSite> AllSites { get; set; }

	private Action<IEnumerable<IISScannerHandler.UnregisteredSite>> OnAllSitesRequestCompleted =>
		sites => { AllSites = sites; };

	#endregion

	#region Methods: Public
	
	public void UninstallByEnvironmentName(string environmentName){
		AllSitesRequest request = new() {
			Callback = OnAllSitesRequestCompleted
		};
		_mediator.Send(request);

		EnvironmentSettings settings = _settingsRepository.GetEnvironment(environmentName);
		Uri envUri = new Uri(settings.Uri);

		if (!AllSites.Any()) {
			_logger.WriteWarning("IIS does not have any sites. Nothing to uninstall.");
			return;
		}
		string directoryPath = AllSites.FirstOrDefault(all => all.Uris.Contains(envUri))?.siteBinding.path;
		if (string.IsNullOrEmpty(directoryPath)) {
			_logger.WriteWarning($"Could not find IIS by environment name: {environmentName}");
			return;
		}
		_logger.WriteInfo($"Uninstalling Creatio from directory: {directoryPath}");
		UninstallByPath(directoryPath);
	}

	/* ALGORITHM
	* 1. Find environment by name
	* 2. Find Application in IIS by URL from Environment.URL
	* 3. IIS - Stop Application and AppPool.
	* 4. IIS - Delete Application and AppPool.
	* 5. Find DB from ConnectionString.
	* 6. If in Rancher, drop DB.
	* 7. Delete content in /wwwroot/{EnvironmentName}.
	* 8. Delete content for AppPool User (C:\Users\{AppPoolUser}).
	*/
	public void UninstallByPath(string creatioDirectoryPath){
		if(!_fileSystem.ExistsDirectory(creatioDirectoryPath)){
			_logger.WriteWarning($"Directory {creatioDirectoryPath} does not exist.");
			return;
		}

		OneOf<DbInfo, Error> dbInfo = GetDbInfoFromConnectionStringsFile(creatioDirectoryPath);
		if(dbInfo.Value is Error or null or not DbInfo)  {
			return;
		}
		DbInfo info = dbInfo.Value as DbInfo;
		_logger.WriteInfo($"Found db: {info!.DbName}, Server: {info!.DbType}");
		
		
		
		
		
	}

	private OneOf.OneOf<DbInfo, Error> GetDbInfoFromConnectionStringsFile(string creatioDirectoryPath){
		const string connectionStringsFileName = "ConnectionStrings.config";
		string connectionStringsPath = System.IO.Path.Join(creatioDirectoryPath, connectionStringsFileName);
		var csExists = _fileSystem.ExistsFile(connectionStringsPath);
		if (!csExists) {
			_logger.WriteWarning($"ConnectionStrings file not found in: {creatioDirectoryPath}");
			return new Error();
		}
		string csPath = System.IO.Path.Join(creatioDirectoryPath, connectionStringsFileName);
		var csContent  = _fileSystem.ReadAllText(csPath);
		if(string.IsNullOrWhiteSpace(csContent)) {
			_logger.WriteWarning($"Could not read ConnectionStrings file from : {creatioDirectoryPath}");
			return new Error();
		}
		return GetDbInfoFromXmlContent(csContent);
	}

	private static OneOf<DbInfo, Error> GetDbInfoFromXmlContent(string csContent){
		XmlDocument doc = new XmlDocument();
		doc.LoadXml(csContent);
		
		const string mssqlMarker = "Data Source=";
		const string psqlMarker = "Server=";
		
		var nodes = doc.ChildNodes;
		foreach (object node in nodes) {
			if(node is XmlElement element && element.Name == "connectionStrings") {
				foreach (object childNode in element.ChildNodes) {
					if(childNode is XmlElement childElement && childElement.Name == "add") {
						var name = childElement.GetAttribute("name");
						if(name == "db") {
							var connectionString = childElement.GetAttribute("connectionString");
							{
								if(connectionString.Contains(psqlMarker)) {
									const string pattern = @"Database=([^;]+)";
									Match match = Regex.Match(connectionString, pattern);
									if(match.Success) {
										var dbName = match.Groups[1].Value;
										return new DbInfo(dbName, "PostgreSql");
									}else {
										return new Error();
									}
								}
								if(connectionString.Contains(mssqlMarker)) {
									const string pattern = @"Catalog=([^;]+)";
									Match match = Regex.Match(connectionString, pattern);
									if(match.Success) {
										var dbName = match.Groups[1].Value;
										return new DbInfo(dbName, "MsSql");
									}else {
										return new Error();
									}
								}
							}
						}
					}
				}
			}
		}
		return new Error();
	}

	#endregion

	private record DbInfo(string DbName, string DbType);
}