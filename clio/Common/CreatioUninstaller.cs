using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Clio.Common.db;
using Clio.Common.K8;
using Clio.Requests;
using Clio.UserEnvironment;
using DocumentFormat.OpenXml.Wordprocessing;
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
	///     <list type="number">
	///         This method performs the following operations:
	///         <item>Sends a request to gather all registered sites in IIS.</item>
	///         <item>Retrieves the environment settings based on the provided environment name.</item>
	///         <item>Checks if any site matches the environment's URL.</item>
	///         <item>
	///             If a matching site is found, proceeds to <see cref="UninstallByPath"> uninstall</see> Creatio from the
	///             directory associated with the site.
	///         </item>
	///         <item>Logs warnings if no sites are found or if the specified environment cannot be matched to any site.</item>
	///     </list>
	/// </remarks>
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
	///         This method performs the following operations:
	///         <item>IIS - Stop Application and AppPool.</item>
	///         <item>IIS - Delete Application and AppPool.</item>
	///         <item>Find DB from ConnectionString.</item>
	///         <item>If in Rancher, drop DB.</item>
	///         <item>Delete content in /wwwroot/{EnvironmentName}.</item>
	///         <item>Delete content for AppPool User (C:\Users\{AppPoolUser}).</item>
	///     </list>
	/// </remarks>
	/// <seealso cref="UninstallByPath" />
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
	private readonly Ik8Commands _k8Commands;
	private readonly IMssql _mssql;
	private readonly IPostgres _postgres;

	private readonly Action<string, k8Commands.ConnectionStringParams, ILogger, IPostgres> _dropPgDbByName
		= (dbName, cn, logger, db) => {
			db.Init("127.0.0.1", cn.DbPort, cn.DbUsername, cn.DbPassword);
			db.DropDb(dbName);
			logger.WriteInfo($"Postgres DB: {dbName} dropped 💀");
		};

	private readonly Action<string, k8Commands.ConnectionStringParams, ILogger, IMssql> _dropMsDbByName
		= (dbName, cn, logger, db) => {
			db.Init("127.0.0.1", cn.DbPort, cn.DbUsername, cn.DbPassword);
			db.DropDb(dbName);
			logger.WriteInfo($"MsSQL DB: {dbName} dropped 💀");
		};

	#endregion

	#region Constructors: Public

	public CreatioUninstaller(IFileSystem fileSystem, ISettingsRepository settingsRepository, 
		IMediator mediator, ILogger logger, Ik8Commands k8Commands, IMssql mssql, IPostgres postgres){
		_fileSystem = fileSystem;
		_settingsRepository = settingsRepository;
		_mediator = mediator;
		_logger = logger;
		_k8Commands = k8Commands;
		_mssql = mssql;
		_postgres = postgres;
	}

	#endregion

	#region Properties: Private

	private IEnumerable<IISScannerHandler.UnregisteredSite> AllSites { get; set; }

	private Action<IEnumerable<IISScannerHandler.UnregisteredSite>> OnAllSitesRequestCompleted =>
		sites => { AllSites = sites; };

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
										return new DbInfo(dbName, "PostgreSql");
									}
									return new Error();
								}
								if (connectionString.Contains(mssqlMarker)) {
									const string pattern = @"Catalog=([^;]+)";
									Match match = Regex.Match(connectionString, pattern);
									if (match.Success) {
										string dbName = match.Groups[1].Value;
										return new DbInfo(dbName, "MsSql");
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

	#endregion

	#region Methods: Public

	public void UninstallByEnvironmentName(string environmentName){
		AllSitesRequest request = new() {
			Callback = OnAllSitesRequestCompleted
		};
		_mediator.Send(request);

		EnvironmentSettings settings = _settingsRepository.GetEnvironment(environmentName);
		Uri envUri = new(settings.Uri);

		if (!AllSites.Any()) {
			_logger.WriteWarning("IIS does not have any sites. Nothing to uninstall.");
			return;
		}
		string directoryPath = AllSites.FirstOrDefault(all => all.Uris.Contains(envUri))?.siteBinding.path;
		string directoryPath2 = AllSites.FirstOrDefault(all => all.siteBinding.name == environmentName)?.siteBinding.path;
		if (string.IsNullOrEmpty(directoryPath) && string.IsNullOrEmpty(directoryPath2)) {
			_logger.WriteWarning($"Could not find IIS by environment name: {environmentName}");
			return;
		}
		_logger.WriteInfo($"Uninstalling Creatio from directory: {directoryPath}");
		UninstallByPath(directoryPath);
		
		_settingsRepository.RemoveEnvironment(environmentName);
		_logger.WriteInfo($"Unregisted {environmentName} from clio 💀");
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
	
	private void StopIISSite(string creatioDirectoryPath){
		if(AllSites is null) {
			AllSitesRequest request = new() {
				Callback = OnAllSitesRequestCompleted
			};
			_mediator.Send(request);
		}
		var site = AllSites.FirstOrDefault(all => all.siteBinding.path == creatioDirectoryPath);
		if(site is not null) {
			var removeRequest = new StopInstanceByNameRequest{SiteName = site.siteBinding.name};
			_mediator.Send(removeRequest);
			_logger.WriteInfo($"IIS Stopped: {removeRequest.SiteName} ⛔");
		}else {
			_logger.WriteWarning($"IIS NOT Stopped Name: {site.siteBinding.name} DIR: {creatioDirectoryPath}");
		}
	}
	
	private void DeleteIISSite(string creatioDirectoryPath){
		if(AllSites is null) {
			AllSitesRequest request = new() {
				Callback = OnAllSitesRequestCompleted
			};
			_mediator.Send(request);
		}
		var site = AllSites.FirstOrDefault(all => all.siteBinding.path == creatioDirectoryPath);
		if(site is not null) {
			var removeRequest = new DeleteInstanceByNameRequest{SiteName = site.siteBinding.name};
			_mediator.Send(removeRequest);
			_logger.WriteInfo($"IIS Removed: {removeRequest.SiteName} 💀");
		}else {
			_logger.WriteWarning($"IIS NOT Removed: {creatioDirectoryPath}");
		}
	}
	
	
	public void UninstallByPath(string creatioDirectoryPath){
		if (!_fileSystem.ExistsDirectory(creatioDirectoryPath)) {
			_logger.WriteWarning($"Directory {creatioDirectoryPath} does not exist.");
			return;
		}
		StopIISSite(creatioDirectoryPath);
		OneOf<DbInfo, Error> dbInfo = GetDbInfoFromConnectionStringsFile(creatioDirectoryPath);
		DeleteIISSite(creatioDirectoryPath);
		
		if (dbInfo.Value is Error or null or not DbInfo) {
			return;
		}
		DbInfo info = dbInfo.Value as DbInfo;
		_logger.WriteInfo($"Found db: {info!.DbName}, Server: {info!.DbType}");

		k8Commands.ConnectionStringParams cn = info.DbType switch {
			"MsSql" => _k8Commands.GetMssqlConnectionString(),
			"PostgreSql" => _k8Commands.GetPostgresConnectionString(),
			var _ => throw new Exception("Unknown db type")
		};

		if(info.DbType == "MsSql") {
			_dropMsDbByName(info.DbName, cn, _logger, _mssql);
		} else {
			_dropPgDbByName(info.DbName, cn, _logger, _postgres);
		}
		
		
		_fileSystem.DeleteDirectory(creatioDirectoryPath, true);
		_logger.WriteInfo($"Directory: {creatioDirectoryPath} deleted 💀");
	}

	#endregion

	private record DbInfo(string DbName, string DbType);

}