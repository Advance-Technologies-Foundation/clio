using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Autofac;
using Clio.Common;
using Clio.Common.db;
using Clio.Common.K8;
using Clio.Requests;
using Clio.Tests.Command;
using Clio.UserEnvironment;
using MediatR;
using NSubstitute;
using NUnit.Framework;
using ILogger = Clio.Common.ILogger;

namespace Clio.Tests.Common;

public class CreatioUninstallerTestFixture : BaseClioModuleTests
{

	#region Constants: Private

	private const string ConnectionStringsFileName = "ConnectionStrings.config";
	private const string EnvironmentName = "work";
	private const string InstalledCreatioPath = @"C:\inetpub\wwwroot\work";

	#endregion

	#region Fields: Private

	private readonly ISettingsRepository _settingsRepositoryMock = Substitute.For<ISettingsRepository>();
	private ICreatioUninstaller _sut;
	private readonly IMediator _mediatorMock = Substitute.For<IMediator>();
	private readonly ILogger _loggerMock = Substitute.For<ILogger>();
	private readonly Ik8Commands _k8CommandsMock = Substitute.For<Ik8Commands>();
	private readonly IMssql _mssqlMock = Substitute.For<IMssql>();
	private readonly IPostgres _postgresMock = Substitute.For<IPostgres>();

	#endregion

	#region Properties: Private

	private Action<IEnumerable<IISScannerHandler.UnregisteredSite>> MockMediator =>
		allSitesMock => {
			_mediatorMock.When(i =>
					i.Send(Arg.Any<AllUnregisteredSitesRequest>()))
				.Do(i => {
						AllUnregisteredSitesRequest allUnregisteredSitesRequest = i[0] as AllUnregisteredSitesRequest;
						allUnregisteredSitesRequest?.Callback.Invoke(allSitesMock);
					}
				);
		};

	#endregion

	#region Methods: Private

	private void MockNoSitesFound(){
		IEnumerable<IISScannerHandler.UnregisteredSite> allSitesMock = [];
		MockMediator(allSitesMock);
	}

	private void MockStartedSite(string url = "", string siteName = EnvironmentName){
		IEnumerable<IISScannerHandler.UnregisteredSite> allSitesMock = [
			new IISScannerHandler.UnregisteredSite(
				new IISScannerHandler.SiteBinding(siteName, "Started", "", InstalledCreatioPath),
				[
					string.IsNullOrWhiteSpace(url) ?
						new Uri(EnvironmentSettings.Uri) : new Uri(url)
				],
				IISScannerHandler.SiteType.NetFramework)
		];
		MockMediator(allSitesMock);
	}

	#endregion

	#region Methods: Protected

	private readonly k8Commands.ConnectionStringParams _cnpMs = new (0, 0, 0, 0, "", "");
	private readonly k8Commands.ConnectionStringParams _cnpPg = new (0, 0, 0, 0, "", "");
	
	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.RegisterInstance(_settingsRepositoryMock).As<ISettingsRepository>();
		containerBuilder.RegisterInstance(_mediatorMock).As<IMediator>();
		containerBuilder.RegisterInstance(_loggerMock).As<ILogger>();
		containerBuilder.RegisterInstance(_k8CommandsMock).As<Ik8Commands>();
		containerBuilder.RegisterInstance(_mssqlMock).As<IMssql>();
		containerBuilder.RegisterInstance(_postgresMock).As<IPostgres>();
	}

	#endregion

	#region Methods: Public

	public override void Setup(){
		EnvironmentSettings = new EnvironmentSettings {
			Uri = "http://kkrylovn.tscrm.com:40090",
			Login = "",
			Password = ""
		};
		base.Setup();
		_settingsRepositoryMock.GetEnvironment(EnvironmentName).Returns(EnvironmentSettings);
		
		_k8CommandsMock.GetMssqlConnectionString().Returns(_cnpMs);
		_k8CommandsMock.GetPostgresConnectionString().Returns(_cnpPg);
		
		_sut = Container.Resolve<ICreatioUninstaller>();
		FileSystem.AddDirectory(InstalledCreatioPath);
		
		// Clear all mock call history before each test to ensure test isolation
		_loggerMock.ClearReceivedCalls();
		_mssqlMock.ClearReceivedCalls();
		_postgresMock.ClearReceivedCalls();
	}

	#endregion

	[Test]
	[Description("UninstallByEnvironmentName should exit early and log warning when no IIS site matches the environment URL")]
	public void UninstallByEnvironmentName_Exits_WhenNoSiteFoundByUrl(){
		//Arrange
		MockStartedSite("https://google.ca","fake");

		//Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		//Assert
		// The command should warn when the environment cannot be matched to any IIS site
		_loggerMock.Received(1).WriteWarning($"Could not find IIS by environment name: {EnvironmentName}");
	}

	[Test]
	[Description("UninstallByEnvironmentName should exit early and log warning when IIS has no sites registered")]
	public void UninstallByEnvironmentName_Exits_WhenNoSiteFoundInIIS(){
		//Arrange
		MockNoSitesFound();

		//Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		//Assert
		// The command should inform the user that there are no IIS sites to uninstall
		_loggerMock.Received(1).WriteWarning("IIS does not have any sites. Nothing to uninstall.");
	}

	[Test]
	[Description("UninstallByEnvironmentName should identify the correct installation directory path from the IIS site")]
	public void UninstallByEnvironmentName_FindsDirPath(){
		//Arrange
		MockStartedSite();

		//Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		//Assert
		// The command should log the directory path where Creatio will be uninstalled from
		_loggerMock.Received(1).WriteInfo($"Uninstalling Creatio from directory: {InstalledCreatioPath}");
	}

	[TestCase("ConnectionStrings_PG")]
	[TestCase("ConnectionStrings_MS")]
	[Description("UninstallByPath should parse local database connection strings and drop the database using parsed parameters")]
	public void UninstallByPath_DropsDb(string fileName){
		//Arrange
		MockStartedSite();
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		string csContent = File.ReadAllText($"Examples/CreatioInstalledDir/{fileName}.config");
		FileSystem.AddFile(csPath, new MockFileData(csContent));
		string dbType = fileName == "ConnectionStrings_PG" ? "PostgreSql" : "MsSql";
		const string  dbNameInFile = "dbname";
		
		//Act
		_sut.UninstallByPath(InstalledCreatioPath);

		//Assert
		// The command should log the database name and type found in ConnectionStrings.config
		_loggerMock.Received(1).WriteInfo($"Found db: dbname, Server: {dbType}");
		// The command should indicate that it's using a local connection instead of K8s
		_loggerMock.Received(1).WriteInfo("Using local database connection from ConnectionStrings.config");

		if (fileName == "ConnectionStrings_PG") {
			// Verify PostgresSQL connection parameters are parsed from connection string
			// The command should log the parsed PostgreSQL connection parameters
			_loggerMock.Received(1).WriteInfo("Parsed PostgreSQL connection: Host=127.0.0.1, Port=5432, User=postgres");
			// The database connection should be initialized with parsed parameters from the connection string
			_postgresMock.Received(1).Init("127.0.0.1", 5432, "postgres", "root");
			// The database should be dropped after a successful connection
			_postgresMock.Received(1).DropDb(dbNameInFile);
			// The command should confirm that the database was successfully dropped
			_loggerMock.Received(1).WriteInfo($"Postgres DB: {dbNameInFile} dropped");
			
			// K8s connection should not be used when a local connection string is successfully parsed
			_k8CommandsMock.DidNotReceive().GetPostgresConnectionString();
		}
		if (fileName == "ConnectionStrings_MS") {
			// Verify MSSQL connection parameters are parsed from connection string
			// The command should log the parsed MSSQL connection parameters
			_loggerMock.Received(1).WriteInfo("Parsed MSSQL connection: Host=127.0.0.1, Port=1433, User=SA");
			// The database connection should be initialized with parsed parameters from the connection string
			_mssqlMock.Received(1).Init("127.0.0.1", 1433, "SA", "$Zarelon01$Zarelon01");
			// The database should be dropped after a successful connection
			_mssqlMock.Received(1).DropDb(dbNameInFile);
			// The command should confirm that the database was successfully dropped
			_loggerMock.Received(1).WriteInfo($"MsSQL DB: {dbNameInFile} dropped");
			
			// K8s connection should not be used when a local connection string is successfully parsed
			_k8CommandsMock.DidNotReceive().GetMssqlConnectionString();
		}
	}

	[TestCase("ConnectionStrings_PG")]
	[TestCase("ConnectionStrings_MS")]
	[Description("UninstallByPath should process connection string and identify database type even with valid files")]
	public void UninstallByPath_Returns_When_ConnectionString_Invalid(string fileName){
		//Arrange
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		string csContent = File.ReadAllText($"Examples/CreatioInstalledDir/{fileName}.config");
		FileSystem.AddFile(csPath, new MockFileData(csContent));
		string dbType = fileName == "ConnectionStrings_PG" ? "PostgreSql" : "MsSql";

		//Act
		_sut.UninstallByPath(InstalledCreatioPath);

		//Assert
		// The command should successfully extract database information from a valid ConnectionStrings.config file
		_loggerMock.Received(1).WriteInfo($"Found db: dbname, Server: {dbType}");
	}

	[Test]
	[Description("UninstallByPath should log warning and exit early when ConnectionStrings.config file does not exist")]
	public void UninstallByPath_Returns_When_ConnectionString_NotExist(){
		//Arrange
		// No additional setup needed

		//Act
		_sut.UninstallByPath(InstalledCreatioPath);

		//Assert
		// The command should warn the user when it cannot find the ConnectionStrings.config file
		_loggerMock.Received(1).WriteWarning($"ConnectionStrings file not found in: {InstalledCreatioPath}");
	}

	[Test]
	[Description("UninstallByPath should log warning and exit early when the specified directory does not exist")]
	public void UninstallByPath_Returns_When_DirectoryDoesNotExist(){
		//Arrange
		const string creatioDirectoryPath = @"C:\random_dir";

		//Act
		_sut.UninstallByPath(creatioDirectoryPath);

		//Assert
		// The command should warn the user when the specified directory does not exist
		_loggerMock.Received(1).WriteWarning($"Directory {creatioDirectoryPath} does not exist.");
	}

	[Test]
	[Description("UninstallByPath should handle invalid connection string gracefully and exit without dropping any database")]
	public void UninstallByPath_ExitsGracefully_WhenConnectionStringInvalid(){
		//Arrange
		MockStartedSite();
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		// Invalid connection string that cannot be parsed to extract the database name
		const string csContent = """
								 <?xml version="1.0" encoding="utf-8"?>
								 <connectionStrings>
								   <add name="db" connectionString="InvalidConnectionString" />
								 </connectionStrings>
								 """;
		FileSystem.AddFile(csPath, new MockFileData(csContent));
		
		//Act
		_sut.UninstallByPath(InstalledCreatioPath);

		//Assert
		// No MSSQL database should be dropped when the connection string is invalid
		_mssqlMock.DidNotReceive().DropDb(Arg.Any<string>());
		// No PostgreSQL database should be dropped when the connection string is invalid
		_postgresMock.DidNotReceive().DropDb(Arg.Any<string>());
	}

	[Test]
	[Description("UninstallByPath should parse and use MSSQL connection string with Integrated Security (Windows Authentication)")]
	public void UninstallByPath_HandlesMssqlIntegratedSecurity(){
		//Arrange
		MockStartedSite();
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		const string csContent = """
								 <?xml version="1.0" encoding="utf-8"?>
								 <connectionStrings>
								   <add name="db" connectionString="Data Source=ts1-agent39;Initial Catalog=dbname;Integrated Security=SSPI;MultipleActiveResultSets=True;Pooling=true;Max Pool Size=100; Encrypt=False; TrustServerCertificate=True;" />
								 </connectionStrings>
								 """;
		FileSystem.AddFile(csPath, new MockFileData(csContent));
		
		//Act
		_sut.UninstallByPath(InstalledCreatioPath);

		//Assert
		// The command should identify the database name and type from the connection string
		_loggerMock.Received(1).WriteInfo("Found db: dbname, Server: MsSql");
		// The command should log that it's using Integrated Security (Windows Authentication)
		_loggerMock.Received(1).WriteInfo("Parsed MSSQL connection: Host=ts1-agent39, Port=1433, Using Integrated Security");
		// Integrated Security should be initialized with an empty username and password
		_mssqlMock.Received(1).Init("ts1-agent39", 1433, "", "");
		// The database should be dropped using Windows Authentication
		_mssqlMock.Received(1).DropDb("dbname");
	}

	[Test]
	[Description("UninstallByPath should correctly parse and preserve MSSQL named instance in connection string")]
	public void UninstallByPath_HandlesMssqlNamedInstance(){
		//Arrange
		MockStartedSite();
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		const string csContent = """
								 <?xml version="1.0" encoding="utf-8"?>
								 <connectionStrings>
								   <add name="db" connectionString="Data Source=tscore-ms-01\mssql2008;Initial Catalog=dbname;Persist Security Info=True;MultipleActiveResultSets=True;Integrated Security=SSPI;Pooling=true;Max Pool Size=100" />
								 </connectionStrings>
								 """;
		FileSystem.AddFile(csPath, new MockFileData(csContent));
		
		//Act
		_sut.UninstallByPath(InstalledCreatioPath);

		//Assert
		// The command should identify the database name and type from the connection string
		_loggerMock.Received(1).WriteInfo("Found db: dbname, Server: MsSql");
		// Named instance (server\instance) should be preserved as-is in the host parameter
		_mssqlMock.Received(1).Init(@"tscore-ms-01\mssql2008", 1433, "", "");
		// The database should be dropped using the named instance connection
		_mssqlMock.Received(1).DropDb("dbname");
	}

	[Test]
	[Description("UninstallByPath should correctly parse MSSQL connection string with explicit port specified")]
	public void UninstallByPath_ParsesMssqlWithExplicitPort(){
		//Arrange
		MockStartedSite();
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		const string csContent = """
								 <?xml version="1.0" encoding="utf-8"?>
								 <connectionStrings>
								   <add name="db" connectionString="Data Source=server,1450;Initial Catalog=dbname;User ID=testuser;Password=testpass;" />
								 </connectionStrings>
								 """;
		FileSystem.AddFile(csPath, new MockFileData(csContent));
		
		//Act
		_sut.UninstallByPath(InstalledCreatioPath);

		//Assert
		// The command should identify the database name and type from the connection string
		_loggerMock.Received(1).WriteInfo("Found db: dbname, Server: MsSql");
		// The command should log the parsed connection parameters including the explicit port
		_loggerMock.Received(1).WriteInfo("Parsed MSSQL connection: Host=server, Port=1450, User=testuser");
		// The database connection should be initialized with the explicit port (1450), not the default (1433)
		_mssqlMock.Received(1).Init("server", 1450, "testuser", "testpass");
		// The database should be dropped after a successful connection
		_mssqlMock.Received(1).DropDb("dbname");
	}

}
