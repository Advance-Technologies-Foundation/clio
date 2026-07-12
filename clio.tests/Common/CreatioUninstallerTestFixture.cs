using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command.McpServer.Progress;
using Clio.Common;
using Clio.Common.db;
using Clio.Common.K8;
using Clio.Requests;
using Clio.Tests.Command;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ILogger = Clio.Common.ILogger;

namespace Clio.Tests.Common;

[Property("Module", "Common")]
public class CreatioUninstallerTestFixture : BaseClioModuleTests
{

	#region Constants: Private

	private const string ConnectionStringsFileName = "ConnectionStrings.config";
	private const string EnvironmentName = "work";
	private const string InstalledCreatioPath = @"C:\inetpub\wwwroot\work";
	private const string ProfileDirectoryPath = @"C:\Users\work";

	#endregion

	#region Fields: Private

	private readonly ISettingsRepository _settingsRepositoryMock = Substitute.For<ISettingsRepository>();
	private ICreatioUninstaller _sut;
	private readonly IIisScanner _iisScannerMock = Substitute.For<IIisScanner>();
	private readonly ILogger _loggerMock = Substitute.For<ILogger>();
	private readonly Ik8Commands _k8CommandsMock = Substitute.For<Ik8Commands>();
	private readonly IMssql _mssqlMock = Substitute.For<IMssql>();
	private readonly IPostgres _postgresMock = Substitute.For<IPostgres>();

	#endregion

	#region Methods: Private

	private void MockNoSitesFound(){
		IEnumerable<UnregisteredSite> allSitesMock = [];
		_iisScannerMock.FindAllCreatioSites().Returns(allSitesMock);
	}

	private void MockStartedSite(string url = "", string siteName = EnvironmentName){
		IEnumerable<UnregisteredSite> allSitesMock = [
			new UnregisteredSite(
				new SiteBinding(siteName, "Started", "", InstalledCreatioPath),
				[
					string.IsNullOrWhiteSpace(url) ?
						new Uri(EnvironmentSettings.Uri) : new Uri(url)
				],
				SiteType.NetFramework)
		];
		_iisScannerMock.FindAllCreatioSites().Returns(allSitesMock);
	}

	// Subscribes to the uninstaller stage-event seam and returns the list the events are collected into.
	// Must be called before the act so the up-front manifest event is captured.
	private List<ClioStageEvent> CaptureStageEvents(){
		List<ClioStageEvent> events = [];
		((IStageEventSource)_sut).StageChanged += (_, stageEvent) => events.Add(stageEvent);
		return events;
	}

	private static IEnumerable<ClioStageDetail> StagesWithStatus(IEnumerable<ClioStageEvent> events, string stageId){
		return events
			.Where(e => e.EventType == ClioStageEventContract.EventTypes.Stage && e.Stage!.StageId == stageId)
			.Select(e => e.Stage);
	}

	private void AddPostgresConnectionStringFile(){
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		string csContent = File.ReadAllText("Examples/CreatioInstalledDir/ConnectionStrings_PG.config");
		FileSystem.AddFile(csPath, new MockFileData(csContent));
	}

	// Writes a Postgres ConnectionStrings.config whose database name is caller-controlled, so a test that
	// configures the shared postgres substitute to throw can scope the throw to a unique db name and never
	// contaminate the other tests that share the same substitute instance.
	private void AddPostgresConnectionStringFileWithDb(string dbName){
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		string csContent = $"""
							<?xml version="1.0" encoding="utf-8"?>
							<connectionStrings>
							  <add name="db" connectionString="Server=127.0.0.1;Port=5432;Database={dbName};User ID=postgres;Password=root;" />
							</connectionStrings>
							""";
		FileSystem.AddFile(csPath, new MockFileData(csContent));
	}

	#endregion

	#region Methods: Protected

	private readonly k8Commands.ConnectionStringParams _cnpMs = new (0, 0, 0, 0, "", "");
	private readonly k8Commands.ConnectionStringParams _cnpPg = new (0, 0, 0, 0, "", "");
	
	protected override void AdditionalRegistrations(IServiceCollection containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton<ISettingsRepository>(_settingsRepositoryMock);
		containerBuilder.AddSingleton<IIisScanner>(_iisScannerMock);
		containerBuilder.AddSingleton<ILogger>(_loggerMock);
		containerBuilder.AddSingleton<Ik8Commands>(_k8CommandsMock);
		containerBuilder.AddSingleton<IMssql>(_mssqlMock);
		containerBuilder.AddSingleton<IPostgres>(_postgresMock);
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
		
		_sut = Container.GetRequiredService<ICreatioUninstaller>();
		FileSystem.AddDirectory(InstalledCreatioPath);
		
		// Clear all mock call history before each test to ensure test isolation
		_loggerMock.ClearReceivedCalls();
		_mssqlMock.ClearReceivedCalls();
		_postgresMock.ClearReceivedCalls();
	}

	#endregion

	[Test]
	[Description("UninstallByEnvironmentName aborts with an error when no IIS site matches the environment URL.")]
	public void UninstallByEnvironmentName_Exits_WhenNoSiteFoundByUrl(){
		//Arrange
		MockStartedSite("https://google.ca");

		//Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		//Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "a URI mismatch must fail rather than report a successful uninstall of a same-named site");
		_loggerMock.Received(1).WriteError($"Could not correlate environment '{EnvironmentName}' with an IIS site URI.");
	}

	[Test]
	[Description("UninstallByEnvironmentName aborts with a typed terminal failure when IIS has no sites registered.")]
	public void UninstallByEnvironmentName_Exits_WhenNoSiteFoundInIIS(){
		//Arrange
		MockNoSitesFound();
		List<ClioStageEvent> events = CaptureStageEvents();

		//Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		//Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "missing IIS state means the requested destructive target cannot be proven");
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure,
			because: "MCP progress consumers need an honest terminal failure event");
		events.Last().RunCompleted!.ErrorCode.Should().Be("uninstall-target-not-found",
			because: "the failure should be machine-classifiable");
	}

	[Test]
	[Description("UninstallByEnvironmentName should identify the correct installation directory path from the IIS site before running the pipeline")]
	public void UninstallByEnvironmentName_ShouldLogResolvedDirectory_WhenSiteMatches(){
		// Arrange
		MockStartedSite();

		// Act
		// No ConnectionStrings.config is present, so read-config aborts the run; the directory is still
		// resolved and logged before the pipeline starts.
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			"because the environment resolves to a directory but its configuration cannot be read");
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
	[Category("Unit")]
	[Description("Correction A: UninstallByPath should abort (not silently return) and warn when ConnectionStrings.config is missing")]
	public void UninstallByPath_ShouldAbortAndWarn_WhenConnectionStringFileDoesNotExist(){
		// Arrange
		MockStartedSite();

		// Act
		Action act = () => _sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			"because a missing configuration must abort the run instead of silently skipping destructive steps");
		_loggerMock.Received(1).WriteWarning($"ConnectionStrings file not found in: {InstalledCreatioPath}");
		_mssqlMock.DidNotReceive().DropDb(Arg.Any<string>());
		_postgresMock.DidNotReceive().DropDb(Arg.Any<string>());
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
	[Category("Unit")]
	[Description("Correction A: UninstallByPath should abort without dropping any database when the connection string cannot be parsed")]
	public void UninstallByPath_ShouldAbortWithoutDroppingDb_WhenConnectionStringInvalid(){
		// Arrange
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

		// Act
		Action act = () => _sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			"because an unreadable connection string must abort before any destructive step");
		_mssqlMock.DidNotReceive().DropDb(Arg.Any<string>());
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

	[Test]
	[Category("Unit")]
	[Description("The uninstall manifest lists the six ordered stages with unregister last, zero-based index and total equal to the manifest length.")]
	public void UninstallByPath_ShouldEmitUninstallManifestInOrder_WhenRunStarts(){
		// Arrange
		MockStartedSite();
		AddPostgresConnectionStringFile();
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		ClioStageEvent manifest = events.First();
		manifest.EventType.Should().Be(ClioStageEventContract.EventTypes.Manifest,
			"because the first event of a run is the manifest");
		manifest.Operation.Should().Be(ClioStageEventContract.Operations.Uninstall,
			"because this is an uninstall run");
		manifest.Stages!.Select(s => s.StageId).Should().Equal(
			[StageIds.StopIis, StageIds.ReadConfig, StageIds.DeleteIis, StageIds.DropDb, StageIds.DeleteFiles, StageIds.Unregister],
			"because the manifest must list the six uninstall stages in execution order with unregister last (AC-01)");
		manifest.Stages.Select(s => s.Index).Should().Equal(Enumerable.Range(0, 6),
			"because manifest indexes are zero-based and contiguous");
		manifest.Stages.Should().OnlyContain(s => s.Total == 6,
			"because total equals the manifest length for every entry");
	}

	[Test]
	[Category("Unit")]
	[Description("Each uninstall stage emits running then done in order when the run succeeds, ending with a run-completed success.")]
	public void UninstallByPath_ShouldEmitRunningThenDoneForEachStage_WhenUninstallSucceeds(){
		// Arrange
		MockStartedSite();
		AddPostgresConnectionStringFile();
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		foreach (string stageId in new[] { StageIds.StopIis, StageIds.ReadConfig, StageIds.DeleteIis, StageIds.DropDb, StageIds.DeleteFiles }) {
			StagesWithStatus(events, stageId).Select(s => s.Status).Should().Equal(
				[ClioStageEventContract.StageStatuses.Running, ClioStageEventContract.StageStatuses.Done],
				$"because stage '{stageId}' must transition running then done (AC-02)");
		}

		events.Last().EventType.Should().Be(ClioStageEventContract.EventTypes.RunCompleted,
			"because a run-completed event terminates the stream");
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Success,
			"because every stage succeeded");
		events.Select(e => e.Sequence).Should().BeInAscendingOrder(
			"because the sequence increases monotonically across the run");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-06: when a destructive stage throws, it is emitted failed, the remaining stages are skipped after-failure, and the run completes with failure.")]
	public void UninstallByPath_ShouldFailStageAndSkipRemaining_WhenStageThrows(){
		// Arrange
		MockStartedSite();
		// Scope the drop failure to a unique db name so the persistent NSubstitute When/Do callback (which
		// ClearReceivedCalls does not remove) cannot contaminate other PG tests that drop the default "dbname".
		const string failDbName = "story3-fail-db";
		AddPostgresConnectionStringFileWithDb(failDbName);
		_postgresMock.When(p => p.DropDb(failDbName)).Do(_ => throw new InvalidOperationException("drop failed"));
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		Action act = () => _sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			"because the emitter rethrows so the caller's control flow is unchanged");
		StagesWithStatus(events, StageIds.DropDb).Should().Contain(s => s.Status == ClioStageEventContract.StageStatuses.Failed,
			"because the drop-db stage threw (AC-06)");
		StagesWithStatus(events, StageIds.DeleteFiles).Should().Contain(
			s => s.Status == ClioStageEventContract.StageStatuses.Skipped && s.SkipReason == ClioStageEventContract.SkipReasons.AfterFailure,
			"because stages after the failed one are skipped with reason after-failure (AC-06)");
		StagesWithStatus(events, StageIds.Unregister).Should().Contain(
			s => s.Status == ClioStageEventContract.StageStatuses.Skipped && s.SkipReason == ClioStageEventContract.SkipReasons.AfterFailure,
			"because unregister never runs after a partial failure (Correction C)");
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure,
			"because the run failed");
	}

	[Test]
	[Category("Unit")]
	[Description("Correction A / AC-03: an unreadable configuration emits read-config failed, skips destructive stages, and completes with failure - never success.")]
	public void UninstallByPath_ShouldEmitReadConfigFailedAndRunFailure_WhenConfigUnreadable(){
		// Arrange
		MockStartedSite();
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		Action act = () => _sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			"because a config-read failure aborts the run safely");
		StagesWithStatus(events, StageIds.ReadConfig).Should().Contain(s => s.Status == ClioStageEventContract.StageStatuses.Failed,
			"because read-config failed (AC-03)");
		StagesWithStatus(events, StageIds.DropDb).Should().Contain(
			s => s.Status == ClioStageEventContract.StageStatuses.Skipped && s.SkipReason == ClioStageEventContract.SkipReasons.AfterFailure,
			"because the destructive drop-db stage must be skipped, not silently succeeded (AC-03)");
		StagesWithStatus(events, StageIds.DeleteFiles).Should().Contain(s => s.Status == ClioStageEventContract.StageStatuses.Skipped,
			"because the destructive delete-files stage must be skipped after a config-read failure");
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure,
			"because the run must report failure, never success, when configuration cannot be read (AC-03)");
		_postgresMock.DidNotReceive().DropDb(Arg.Any<string>());
		_mssqlMock.DidNotReceive().DropDb(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Correction B / AC-04: when an app-pool profile directory exists the delete-apppool-profile stage is emitted skipped with reason not-supported and no deletion occurs.")]
	public void UninstallByPath_ShouldEmitProfileSkippedNotSupported_WhenProfileDirectoryExists(){
		// Arrange
		MockStartedSite();
		AddPostgresConnectionStringFile();
		FileSystem.AddDirectory(ProfileDirectoryPath);
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		events.First().Stages!.Select(s => s.StageId).Should().Contain(StageIds.DeleteApppoolProfile,
			"because the profile stage is present in the manifest when a profile directory exists (AC-04)");
		StagesWithStatus(events, StageIds.DeleteApppoolProfile).Should().Contain(
			s => s.Status == ClioStageEventContract.StageStatuses.Skipped && s.SkipReason == ClioStageEventContract.SkipReasons.NotSupported,
			"because profile deletion is not supported and must be reported honestly, never silently succeeded (AC-04)");
		FileSystem.Directory.Exists(ProfileDirectoryPath).Should().BeTrue(
			"because the profile directory is left in place (no deletion is performed)");
	}

	[Test]
	[Category("Unit")]
	[Description("Correction B / AC-04: when no app-pool profile directory exists the delete-apppool-profile stage is absent from the manifest and never emitted.")]
	public void UninstallByPath_ShouldOmitProfileStage_WhenNoProfileDirectory(){
		// Arrange
		MockStartedSite();
		AddPostgresConnectionStringFile();
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		events.First().Stages!.Select(s => s.StageId).Should().NotContain(StageIds.DeleteApppoolProfile,
			"because the profile stage is absent from the manifest when no profile directory exists (AC-04)");
		StagesWithStatus(events, StageIds.DeleteApppoolProfile).Should().BeEmpty(
			"because a non-existent profile is never emitted");
	}

	[Test]
	[Category("Unit")]
	[Description("Correction C / AC-05: unregister runs last (RemoveEnvironment called) only after the destructive cleanup succeeds, ending with run-completed success.")]
	public void UninstallByEnvironmentName_ShouldUnregisterLastAfterCleanup_WhenUninstallSucceeds(){
		// Arrange
		MockStartedSite();
		AddPostgresConnectionStringFile();
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		StagesWithStatus(events, StageIds.Unregister).Select(s => s.Status).Should().Equal(
			[ClioStageEventContract.StageStatuses.Running, ClioStageEventContract.StageStatuses.Done],
			"because unregister runs as the final stage after cleanup succeeds (AC-05)");
		_settingsRepositoryMock.Received(1).RemoveEnvironment(EnvironmentName);
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Success,
			"because the run succeeded");
	}

	[Test]
	[Category("Unit")]
	[Description("Correction C / AC-05: on a partial cleanup failure the environment is preserved - RemoveEnvironment is not called and unregister is skipped after-failure.")]
	public void UninstallByEnvironmentName_ShouldPreserveRegistration_WhenCleanupFails(){
		// Arrange
		MockStartedSite();
		// Scope the drop failure to a unique db name so the persistent NSubstitute When/Do callback (which
		// ClearReceivedCalls does not remove) cannot contaminate other PG tests that drop the default "dbname".
		const string failDbName = "story3-fail-db";
		AddPostgresConnectionStringFileWithDb(failDbName);
		_postgresMock.When(p => p.DropDb(failDbName)).Do(_ => throw new InvalidOperationException("drop failed"));
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			"because the failing stage rethrows");
		_settingsRepositoryMock.DidNotReceive().RemoveEnvironment(Arg.Any<string>());
		StagesWithStatus(events, StageIds.Unregister).Should().Contain(
			s => s.Status == ClioStageEventContract.StageStatuses.Skipped && s.SkipReason == ClioStageEventContract.SkipReasons.AfterFailure,
			"because registration is preserved for recovery on partial failure (AC-05 / Correction C)");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-12: no emitted stage-event field contains the connection-string password from the resolved configuration.")]
	public void UninstallByPath_ShouldNotEmitSecrets_WhenConnectionStringContainsPassword(){
		// Arrange
		MockStartedSite();
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		const string secret = "S3cr3t-P@ssw0rd-Value";
		string csContent = $"""
							<?xml version="1.0" encoding="utf-8"?>
							<connectionStrings>
							  <add name="db" connectionString="Server=127.0.0.1;Port=5432;Database=dbname;User ID=postgres;Password={secret};" />
							</connectionStrings>
							""";
		FileSystem.AddFile(csPath, new MockFileData(csContent));
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		string serialized = string.Join("\n", events.Select(e =>
			System.Text.Json.JsonSerializer.Serialize(e, ClioStageEventContract.SerializerOptions)));
		serialized.Should().NotContain(secret,
			"because secrets are excluded at the single emitter redaction boundary and never reach any event field (AC-12)");
	}

}
