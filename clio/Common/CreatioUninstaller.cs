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
using Microsoft.Data.SqlClient;
using Npgsql;
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
		AllUnregisteredSitesRequest request = new() {
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
		_logger.WriteInfo($"Unregisted {environmentName} from clio");
	}

	private void StopIISSite(string creatioDirectoryPath){
		if(AllSites is null) {
			AllUnregisteredSitesRequest request = new() {
				Callback = OnAllSitesRequestCompleted
			};
			_mediator.Send(request);
		}
		var site = AllSites.FirstOrDefault(all => all.siteBinding.path == creatioDirectoryPath);
		if(site is not null) {
			var removeRequest = new StopInstanceByNameRequest{SiteName = site.siteBinding.name};
			_mediator.Send(removeRequest);
			_logger.WriteInfo($"IIS Stopped: {removeRequest.SiteName}");
		}else {
			_logger.WriteWarning($"IIS NOT Stopped Name: {site.siteBinding.name} DIR: {creatioDirectoryPath}");
		}
	}
	
	private void DeleteIISSite(string creatioDirectoryPath){
		if(AllSites is null) {
			AllUnregisteredSitesRequest request = new() {
				Callback = OnAllSitesRequestCompleted
			};
			_mediator.Send(request);
		}
		var site = AllSites.FirstOrDefault(all => all.siteBinding.path == creatioDirectoryPath);
		if(site is not null) {
			var removeRequest = new DeleteInstanceByNameRequest{SiteName = site.siteBinding.name};
			_mediator.Send(removeRequest);
			_logger.WriteInfo($"IIS Removed: {removeRequest.SiteName}");
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
				var _ => throw new Exception("Unknown db type")
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
		
		_fileSystem.DeleteDirectory(creatioDirectoryPath, true);
		_logger.WriteInfo($"Directory: {creatioDirectoryPath} deleted");
	}

	#endregion

	private record DbInfo(string DbName, string DbType, string ConnectionString);

}
