using System;
using System.Collections.Generic;
using Clio.Common;
using CommandLine;
using k8s;

namespace Clio.Command.CreatioInstallCommand;

/// <summary>
/// Options for the <c>deploy-creatio</c> command.
/// </summary>
[Verb("deploy-creatio", Aliases = ["dc", "ic", "install-creatio"], HelpText = "Deploy Creatio from zip file")]
public class PfInstallerOptions : EnvironmentNameOptions{
	#region Properties: Overrides

	internal override bool RequiredEnvironment => false;

	#endregion

	#region Fields: Private

	private readonly Dictionary<string, string> _productList = new() {
		{ "s", "Studio" },
		{ "semse", "SalesEnterprise_Marketing_ServiceEnterprise" },
		{ "bcj", "BankSales_BankCustomerJourney_Lending_Marketing" }
	};

	private CreatioDBType _dbType;
	private CreatioRuntimePlatform _platform;

	#endregion

	#region Properties: Protected

	internal CreatioDBType DBType {
		get {
			if (DB.ToLower() == "pg") {
				return CreatioDBType.PostgreSQL;
			}

			if (DB.ToLower() == "mssql") {
				return CreatioDBType.MSSQL;
			}

			return _dbType;
		}
		set => _dbType = value;
	}

	internal CreatioRuntimePlatform RuntimePlatform {
		get {
			if (Platform.ToLower() == "net6") {
				return CreatioRuntimePlatform.NET6;
			}

			if (Platform.ToLower() == "netframework" || Platform.ToLower() == "nf") {
				return CreatioRuntimePlatform.NETFramework;
			}

			return _platform;
		}
		set => _platform = value;
	}

	#endregion

	#region Properties: Public
	
	/// <summary>
	/// Gets or sets a value indicating whether force-password-reset disabling script may be executed.
	/// </summary>
	[Option("disable-reset-password", Required = false, Hidden = true, Default = true, HelpText = "Disables reset password after installation")]
	public bool DisableResetPassword { get; set; }
	
	/// <summary>
	/// Gets or sets the database engine type: <c>pg</c> or <c>mssql</c>.
	/// </summary>
	[Option("db", Required = false, HelpText = "DB type: pg|mssql")]
	public string DB { get; set; }

	/// <summary>
	/// Gets or sets the runtime platform: <c>net6</c> or <c>netframework</c>.
	/// </summary>
	[Option("platform", Required = false, HelpText = "Runtime platform: net6|netframework")]
	public string Platform { get; set; }

	/// <summary>
	/// Gets or sets the normalized product value used during deployment.
	/// </summary>
	public string Product {
		get {
			if (_productList.ContainsKey(ProductKey ?? string.Empty)) {
				return _productList[ProductKey];
			}

			return ProductKey;
		}
		set => ProductKey = value;
	}

	/// <summary>
	/// Gets or sets the raw product key specified by the user.
	/// </summary>
	[Option("product", Required = false, HelpText = "Product name")]
	public string ProductKey { get; set; }

	/// <summary>
	/// Gets or sets the site name for the deployed application.
	/// </summary>
	[Option("SiteName", Required = false, HelpText = "SiteName")]
	public string SiteName { get; set; }

	/// <summary>
	/// Gets or sets the site port for the deployed application.
	/// </summary>
	[Option("SitePort", Required = false, HelpText = "Site port")]
	public int SitePort { get; set; }

	/// <summary>
	/// Gets or sets the path to the Creatio application zip archive.
	/// </summary>
	[Option("ZipFile", Required = false, HelpText = "Sets Zip File path")]
	public string ZipFile { get; set; }

	/// <summary>
	/// Gets or sets the deployment method: <c>auto</c>, <c>iis</c>, or <c>dotnet</c>.
	/// </summary>
	[Option("deployment", Required = false, Default = "auto", HelpText = "Deployment method: auto|iis|dotnet")]
	public string DeploymentMethod { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether IIS should be skipped on Windows.
	/// </summary>
	[Option("no-iis", Required = false, Default = false,
		HelpText = "Don't use IIS on Windows (use dotnet run instead)")]
	public bool NoIIS { get; set; }

	/// <summary>
	/// Gets or sets the installation directory for the application.
	/// </summary>
	[Option("app-path", Required = false, HelpText = "Application installation path")]
	public string AppPath { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether HTTPS should be used for dotnet deployment.
	/// </summary>
	[Option("use-https", Required = false, Default = false, HelpText = "Use HTTPS (requires certificate for dotnet)")]
	public bool UseHttps { get; set; }

	/// <summary>
	/// Gets or sets the path to the SSL certificate file.
	/// </summary>
	[Option("cert-path", Required = false, HelpText = "Path to SSL certificate file (.pem or .pfx)")]
	public string CertificatePath { get; set; }

	/// <summary>
	/// Gets or sets the password for the SSL certificate.
	/// </summary>
	[Option("cert-password", Required = false, HelpText = "Password for SSL certificate")]
	public string CertificatePassword { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the application should be started after deployment.
	/// </summary>
	[Option("auto-run", Required = false, Default = true, HelpText = "Automatically run application after deployment")]
	public bool? AutoRun { get; set; }

	/// <summary>
	/// Gets or sets the Redis database index. Use <c>-1</c> to auto-detect.
	/// </summary>
	[Option("redis-db", Required = false, Default = -1,
		HelpText = "Redis database number (optional, auto-detect if not specified)")]
	public int RedisDb { get; set; }

	/// <summary>
	/// Gets or sets the configured local Redis server name from application settings.
	/// </summary>
	[Option("redis-server-name", Required = false,
		HelpText = "Name of Redis server configuration from appsettings.json for local deployment")]
	public string RedisServerName { get; set; }

	/// <summary>
	/// Gets or sets the configured local database server name from application settings.
	/// </summary>
	[Option("db-server-name", Required = false,
		HelpText = "Name of database server configuration from appsettings.json for local database restore")]
	public string DbServerName { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether an existing target database should be dropped automatically.
	/// </summary>
	[Option("drop-if-exists", Required = false, Default = false,
		HelpText = "Automatically drop existing database if present without prompting")]
	public bool DropIfExists { get; set; }

	#endregion
}

/// <summary>
/// Executes Creatio deployment using validated command options.
/// </summary>
public class InstallerCommand : Command<PfInstallerOptions>{
	#region Fields: Private

	private readonly ICreatioInstallerService _creatioInstallerService;
	private readonly IDbOperationLogSessionFactory _dbOperationLogSessionFactory;
	private readonly IKubernetes _kubernetes;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="InstallerCommand"/> class.
	/// </summary>
	/// <param name="creatioInstallerService">Service that performs deployment steps.</param>
	/// <param name="logger">Logger used for user-facing output.</param>
	/// <param name="kubernetes">Kubernetes client used to validate cluster availability.</param>
	/// <param name="dbOperationLogSessionFactory">Factory that creates per-invocation database operation log artifacts.</param>
	public InstallerCommand(
		ICreatioInstallerService creatioInstallerService,
		ILogger logger,
		IKubernetes kubernetes,
		IDbOperationLogSessionFactory dbOperationLogSessionFactory = null) {
		_creatioInstallerService = creatioInstallerService;
		_logger = logger;
		_kubernetes = kubernetes;
		_dbOperationLogSessionFactory = dbOperationLogSessionFactory ?? NullDbOperationLogSessionFactory.Instance;
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Executes Creatio deployment for the provided options.
	/// </summary>
	/// <param name="options">Deployment options parsed from command-line arguments.</param>
	/// <returns>
	/// <c>0</c> on success; non-zero value when execution cannot continue or deployment fails.
	/// </returns>
	public override int Execute(PfInstallerOptions options) {
		using IDbOperationLogSession dbOperationLogSession = _dbOperationLogSessionFactory.BeginSession("deploy-creatio");
		try {
			if (_kubernetes is FakeKubernetes && string.IsNullOrEmpty(options.DbServerName)) {
				_logger.WriteError(
					"Could not detect kubectl config, and db server name (db-server-name) is not specified.");
				return 1;
			}

			int result = _creatioInstallerService.Execute(options);
			if (!options.IsSilent) {
				_logger.WriteLine("Press enter to exit...");
				Console.ReadLine();
			}

			return result;
		}
		finally {
			_logger.WriteInfo($"Database operation log: {dbOperationLogSession.LogFilePath}");
		}
	}

	#endregion
}
