using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command.McpServer.Progress;
using Clio.Common;
using Clio.Common.db;
using Clio.Common.DbHub;
using Clio.Common.K8;
using Clio.Common.IIS;
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
	private const string AppPoolName = "custom-work-pool";
	private const string ProfileDirectoryPath = @"C:\Users\custom-work-pool";

	#endregion

	#region Fields: Private

	private readonly ISettingsRepository _settingsRepositoryMock = Substitute.For<ISettingsRepository>();
	private ICreatioUninstaller _sut;
	private readonly IIisScanner _iisScannerMock = Substitute.For<IIisScanner>();
	private readonly ILogger _loggerMock = Substitute.For<ILogger>();
	private readonly Ik8Commands _k8CommandsMock = Substitute.For<Ik8Commands>();
	private readonly IMssql _mssqlMock = Substitute.For<IMssql>();
	private readonly IPostgres _postgresMock = Substitute.For<IPostgres>();
	private readonly IAppPoolProfileCleaner _profileCleanerMock = Substitute.For<IAppPoolProfileCleaner>();
	private readonly IDbHubSynchronizationService _dbHubSynchronizationServiceMock =
		Substitute.For<IDbHubSynchronizationService>();
	private readonly IDeploymentTargetReservation _deploymentTargetReservationMock =
		Substitute.For<IDeploymentTargetReservation>();

	#endregion

	#region Methods: Private

	private void MockNoSitesFound(){
		IReadOnlyList<UnregisteredSite> allSitesMock = [];
		_iisScannerMock.TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>())
			.Returns(call => {
				call[0] = allSitesMock;
				return true;
			});
	}

	private void MockStartedSite(string url = "", string siteName = EnvironmentName, string appPoolName = null){
		IReadOnlyList<UnregisteredSite> allSitesMock = [
			new UnregisteredSite(
				new SiteBinding(siteName, "Started", "", InstalledCreatioPath, appPoolName),
				[
					string.IsNullOrWhiteSpace(url) ?
						new Uri(EnvironmentSettings.Uri) : new Uri(url)
				],
				SiteType.NetFramework)
		];
		int scan = 0;
		_iisScannerMock.TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>())
			.Returns(call => {
				call[0] = scan++ == 0 ? allSitesMock : Array.Empty<UnregisteredSite>();
				return true;
			});
		_iisScannerMock.TryFindAppPoolsForTargets(Arg.Any<IReadOnlyCollection<string>>(),
			out Arg.Any<IReadOnlyCollection<string>>()).Returns(call => {
				call[1] = string.IsNullOrWhiteSpace(appPoolName) ? Array.Empty<string>() : [appPoolName];
				return true;
			});
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
		containerBuilder.AddSingleton<IAppPoolProfileCleaner>(_profileCleanerMock);
		containerBuilder.AddSingleton<IDbHubSynchronizationService>(_dbHubSynchronizationServiceMock);
		containerBuilder.AddSingleton<IDeploymentTargetReservation>(_deploymentTargetReservationMock);
	}

	#endregion

	#region Methods: Public

	public override void Setup(){
		EnvironmentSettings = new EnvironmentSettings {
			Uri = "http://kkrylovn.tscrm.com:40090",
			EnvironmentPath = InstalledCreatioPath,
			Login = "",
			Password = ""
		};
		base.Setup();
		_settingsRepositoryMock.FindEnvironment(EnvironmentName).Returns(EnvironmentSettings);
		_settingsRepositoryMock.FindCurrentEnvironment(EnvironmentName).Returns(EnvironmentSettings);
		_settingsRepositoryMock.RemoveEnvironmentIfPathMatches(EnvironmentName, InstalledCreatioPath)
			.Returns(true);
		_settingsRepositoryMock.EnvironmentPathMatches(EnvironmentName, InstalledCreatioPath)
			.Returns(true);
		
		_k8CommandsMock.GetMssqlConnectionString().Returns(_cnpMs);
		_k8CommandsMock.GetPostgresConnectionString().Returns(_cnpPg);
		
		_sut = Container.GetRequiredService<ICreatioUninstaller>();
		FileSystem.AddDirectory(InstalledCreatioPath);
		
		// Clear all mock call history before each test to ensure test isolation
		_loggerMock.ClearReceivedCalls();
		_settingsRepositoryMock.ClearReceivedCalls();
		_iisScannerMock.ClearReceivedCalls();
		_mssqlMock.ClearReceivedCalls();
		_postgresMock.ClearReceivedCalls();
		_postgresMock.DropDb(Arg.Any<string>()).Returns(true);
		_profileCleanerMock.ClearReceivedCalls();
		_deploymentTargetReservationMock.ClearReceivedCalls();
		_iisScannerMock.IsIisTargetExclusive(Arg.Any<string>()).Returns(true);
		_iisScannerMock.TryStopIisTarget(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		_iisScannerMock.TryDeleteIisTarget(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
		_iisScannerMock.StopAppPoolIfOwnedByTargets(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>())
			.Returns(IisAppPoolMutationResult.Completed);
		_iisScannerMock.DeleteAppPoolIfUnused(Arg.Any<string>()).Returns(IisAppPoolMutationResult.Completed);
		_iisScannerMock.TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>())
			.Returns(call => {
				call[0] = Array.Empty<UnregisteredSite>();
				return true;
			});
		_iisScannerMock.TryFindAppPoolsForTargets(Arg.Any<IReadOnlyCollection<string>>(),
			out Arg.Any<IReadOnlyCollection<string>>()).Returns(call => {
				call[1] = Array.Empty<string>();
				return true;
			});
		_iisScannerMock.IsAppPoolAbsent(Arg.Any<string>()).Returns(true);
		_dbHubSynchronizationServiceMock.ClearReceivedCalls();
		_dbHubSynchronizationServiceMock.IsAutomaticSynchronizationEnabled().Returns(false);
		_dbHubSynchronizationServiceMock.RemoveEnvironmentSource(Arg.Any<string>()).Returns(DbHubSyncResult.Unchanged());
		_profileCleanerMock.Prepare(Arg.Any<string>()).Returns(
			new AppPoolProfileCleanupTarget(new WindowsProfileRegistration("S-1-5-82-1", ProfileDirectoryPath)));
		_profileCleanerMock.TryDelete(Arg.Any<AppPoolProfileCleanupTarget>()).Returns(
			new AppPoolProfileCleanupResult(AppPoolProfileCleanupStatus.NotApplicable));
	}

	#endregion

	[Test]
	[Description("UninstallByEnvironmentName uses registered EnvironmentPath even when IIS bindings do not match the registered URI.")]
	public void UninstallByEnvironmentName_UsesEnvironmentPath_WhenUriDoesNotMatch(){
		//Arrange
		MockStartedSite("https://google.ca");
		AddPostgresConnectionStringFile();

		//Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		//Assert
		_iisScannerMock.Received(1).TryDeleteIisTarget(EnvironmentName, InstalledCreatioPath, null);
		_settingsRepositoryMock.Received(1).RemoveEnvironmentIfPathMatches(EnvironmentName, InstalledCreatioPath);
	}

	[Test]
	[Description("UninstallByEnvironmentName removes a registered local non-IIS deployment using EnvironmentPath.")]
	public void UninstallByEnvironmentName_RemovesLocalDeployment_WhenIisHasNoSites(){
		//Arrange
		MockNoSitesFound();
		AddPostgresConnectionStringFile();
		List<ClioStageEvent> events = CaptureStageEvents();

		//Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		//Assert
		_iisScannerMock.DidNotReceive().TryStopIisTarget(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
		_iisScannerMock.DidNotReceive().TryDeleteIisTarget(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
		_settingsRepositoryMock.Received(1).RemoveEnvironmentIfPathMatches(EnvironmentName, InstalledCreatioPath);
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Success,
			because: "the registered path is sufficient authority for a local deployment that does not use IIS");
	}

	[Test]
	[Description("UninstallByEnvironmentName logs the registered EnvironmentPath before running the pipeline.")]
	public void UninstallByEnvironmentName_ShouldLogRegisteredDirectory(){
		// Arrange
		MockStartedSite();

		// Act
		// No ConnectionStrings.config is present, so read-config aborts the run; the directory is still
		// resolved and logged before the pipeline starts.
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			"because the environment resolves to a directory but its configuration cannot be read");
		_loggerMock.Received(1).WriteInfo($"Uninstalling Creatio from registered directory: {InstalledCreatioPath}");
	}

	[Test]
	[Description("UninstallByEnvironmentName removes every safe IIS site mapped to the registered EnvironmentPath.")]
	public void UninstallByEnvironmentName_ShouldRemoveAllIisSites_WhenPathIsShared(){
		// Arrange
		const string secondSiteName = "work-alias";
		const string secondPoolName = "work-alias-pool";
		IReadOnlyList<UnregisteredSite> sites = [
			new UnregisteredSite(
				new SiteBinding(EnvironmentName, "Started", "", InstalledCreatioPath, AppPoolName),
				[new Uri(EnvironmentSettings.Uri)], SiteType.Core),
			new UnregisteredSite(
				new SiteBinding(secondSiteName, "Started", "", InstalledCreatioPath, secondPoolName),
				[new Uri("https://alias.example.test:40100")], SiteType.Core)
		];
		int scan = 0;
		_iisScannerMock.TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>())
			.Returns(call => {
				call[0] = scan++ == 0 ? sites : Array.Empty<UnregisteredSite>();
				return true;
			});
		_iisScannerMock.TryFindAppPoolsForTargets(Arg.Any<IReadOnlyCollection<string>>(),
			out Arg.Any<IReadOnlyCollection<string>>()).Returns(call => {
				call[1] = new[] { AppPoolName, secondPoolName };
				return true;
			});
		AddPostgresConnectionStringFile();

		// Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		_iisScannerMock.Received(1).TryStopIisTarget(EnvironmentName, InstalledCreatioPath, AppPoolName);
		_iisScannerMock.Received(1).TryStopIisTarget(secondSiteName, InstalledCreatioPath, secondPoolName);
		_iisScannerMock.Received(1).TryDeleteIisTarget(EnvironmentName, InstalledCreatioPath, AppPoolName);
		_iisScannerMock.Received(1).TryDeleteIisTarget(secondSiteName, InstalledCreatioPath, secondPoolName);
		_iisScannerMock.Received(1).StopAppPoolIfOwnedByTargets(AppPoolName,
			Arg.Is<IReadOnlyCollection<string>>(names => names.Contains(EnvironmentName) && names.Contains(secondSiteName)));
		_iisScannerMock.Received(1).StopAppPoolIfOwnedByTargets(secondPoolName,
			Arg.Is<IReadOnlyCollection<string>>(names => names.Contains(EnvironmentName) && names.Contains(secondSiteName)));
		_iisScannerMock.Received(1).DeleteAppPoolIfUnused(AppPoolName);
		_iisScannerMock.Received(1).DeleteAppPoolIfUnused(secondPoolName);
		_profileCleanerMock.Received(1).Prepare(AppPoolName);
		_profileCleanerMock.Received(1).Prepare(secondPoolName);
		_settingsRepositoryMock.Received(1).RemoveEnvironmentIfPathMatches(EnvironmentName, InstalledCreatioPath);
		_deploymentTargetReservationMock.Received(1).Acquire(InstalledCreatioPath);
	}

	[Test]
	[Description("A classic nested deployment treats its loader and slash-zero application as one removable target.")]
	public void UninstallByEnvironmentName_ShouldCollapseClassicNestedTarget_WithSlashZeroChild() {
		// Arrange
		const string loaderName = "default/work";
		const string webAppName = "default/work/0";
		const string loaderPool = "work-loader";
		const string webAppPool = "work-webapp";
		string webAppPath = Path.Combine(InstalledCreatioPath, "Terrasoft.WebApp");
		IReadOnlyList<UnregisteredSite> sites = [
			new(new SiteBinding(loaderName, "Started", "", InstalledCreatioPath, loaderPool),
				[new Uri(EnvironmentSettings.Uri)], SiteType.NetFramework),
			new(new SiteBinding(webAppName, "Started", "", webAppPath, webAppPool),
				[new Uri(EnvironmentSettings.Uri)], SiteType.NetFramework)
		];
		int scan = 0;
		_iisScannerMock.TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>())
			.Returns(call => {
				call[0] = scan++ == 0 ? sites : Array.Empty<UnregisteredSite>();
				return true;
			});
		_iisScannerMock.TryFindAppPoolsForTargets(Arg.Any<IReadOnlyCollection<string>>(),
			out Arg.Any<IReadOnlyCollection<string>>()).Returns(call => {
			call[1] = new[] { loaderPool, webAppPool };
			return true;
		});
		AddPostgresConnectionStringFile();

		// Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		Received.InOrder(() => {
			_iisScannerMock.TryDeleteIisTarget(webAppName, webAppPath, webAppPool);
			_iisScannerMock.TryDeleteIisTarget(loaderName, InstalledCreatioPath, loaderPool);
		});
		_iisScannerMock.Received(1).TryFindAppPoolsForTargets(
			Arg.Is<IReadOnlyCollection<string>>(names => names.Contains(loaderName) && names.Contains(webAppName)),
			out Arg.Any<IReadOnlyCollection<string>>());
	}

	[Test]
	[Description("A slash-zero application is matched to its registered deployment root when no loader target is present.")]
	public void SiteUsesDeploymentPath_ShouldMatchSlashZeroChild_ByParentDirectory() {
		// Arrange
		UnregisteredSite slashZero = new(new SiteBinding("default/0", "Started", "",
			Path.Combine(InstalledCreatioPath, "Terrasoft.WebApp"), "webapp"),
			[new Uri(EnvironmentSettings.Uri)], SiteType.NetFramework);

		// Act
		bool result = CreatioUninstaller.SiteUsesDeploymentPath(slashZero, InstalledCreatioPath);

		// Assert
		result.Should().BeTrue(
			because: "classic registrations store the parent of the slash-zero physical directory");
	}

	[Test]
	[Description("An unrelated slash-zero IIS application under another child directory is not owned by Creatio.")]
	public void SiteUsesDeploymentPath_ShouldRejectUnrelatedSlashZeroChild() {
		// Arrange
		UnregisteredSite unrelated = new(new SiteBinding("default/unrelated/0", "Started", "",
			Path.Combine(InstalledCreatioPath, "OtherProduct"), "foreign"),
			[new Uri(EnvironmentSettings.Uri)], SiteType.NotCreatioSite);

		// Act
		bool result = CreatioUninstaller.SiteUsesDeploymentPath(unrelated, InstalledCreatioPath);

		// Assert
		result.Should().BeFalse(
			because: "only Creatio's known Terrasoft.WebApp layout may map a slash-zero child to its parent");
	}

	[Test]
	[Description("Uninstall reports failure instead of unregistering a same-name replacement whose path changed.")]
	public void UninstallByEnvironmentName_ShouldFailUnregister_WhenRegistrationChanged() {
		// Arrange
		MockNoSitesFound();
		AddPostgresConnectionStringFile();
		_settingsRepositoryMock.RemoveEnvironmentIfPathMatches(EnvironmentName, InstalledCreatioPath)
			.Returns(false);

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>().WithMessage("*changed*not unregistered*",
			because: "the final settings mutation must compare the authority used by the completed cleanup");
		_settingsRepositoryMock.Received(1).RemoveEnvironmentIfPathMatches(EnvironmentName, InstalledCreatioPath);
	}

	[Test]
	[Description("A changed registration preserves its same-name dbHub source before conditional unregister.")]
	public void UninstallByEnvironmentName_ShouldPreserveDbHubSource_WhenRegistrationChanged() {
		// Arrange
		MockNoSitesFound();
		AddPostgresConnectionStringFile();
		_dbHubSynchronizationServiceMock.IsAutomaticSynchronizationEnabled().Returns(true);
		_settingsRepositoryMock.EnvironmentPathMatches(EnvironmentName, InstalledCreatioPath).Returns(false);
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>().WithMessage("*changed*",
			because: "name-scoped integration cleanup must not affect a concurrent same-name replacement");
		_dbHubSynchronizationServiceMock.DidNotReceive().RemoveEnvironmentSource(EnvironmentName);
		_settingsRepositoryMock.DidNotReceive().RemoveEnvironmentIfPathMatches(
			EnvironmentName, InstalledCreatioPath);
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure,
			because: "a concurrent registration change must terminate the typed progress stream");
	}

	[Test]
	[Description("IIS path identity ignores Windows path casing and trailing directory separators.")]
	public void PathsEqual_ShouldNormalizeCaseAndTrailingSeparators(){
		// Arrange
		string path = Path.Combine(Path.GetTempPath(), "clio", "work");
		string alternate = path.ToUpperInvariant() + Path.DirectorySeparatorChar;

		// Act
		bool equal = CreatioUninstaller.PathsEqual(path, alternate);

		// Assert
		equal.Should().BeTrue(
			because: "IIS may return the registered physical path with different casing or a trailing separator");
	}

	[Test]
	[Description("Existing Windows directory aliases resolve to the same destructive filesystem identity.")]
	public void PathsEqual_ShouldResolveExtendedWindowsPathAlias() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Extended Windows path aliases are Windows-specific.");
		}
		string path = Path.Combine(Path.GetTempPath(), $"clio-path-identity-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		try {
			string extendedPath = @"\\?\" + path;

			// Act
			bool equal = CreatioUninstaller.PathsEqual(path, extendedPath);

			// Assert
			equal.Should().BeTrue(
				because: "IIS and appsettings may use different representations of one existing directory");
		}
		finally {
			Directory.Delete(path);
		}
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
		_loggerMock.Received(1).WriteInfo("Using database connection from ConnectionStrings.config");

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
	[Description("UninstallByPath aborts with a typed failure when the specified directory does not exist.")]
	public void UninstallByPath_Aborts_When_DirectoryDoesNotExist(){
		//Arrange
		const string creatioDirectoryPath = @"C:\random_dir";
		List<ClioStageEvent> events = CaptureStageEvents();

		//Act
		Action act = () => _sut.UninstallByPath(creatioDirectoryPath);

		//Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "a missing physical target must not report a successful uninstall");
		events.Last().RunCompleted!.ErrorCode.Should().Be("uninstall-target-not-found",
			because: "MCP consumers need a stable missing-target failure classification");
	}

	[Test]
	[Description("UninstallByEnvironmentName aborts with typed failure when the environment is not registered.")]
	public void UninstallByEnvironmentName_Aborts_WhenEnvironmentIsMissing() {
		// Arrange
		_settingsRepositoryMock.FindCurrentEnvironment("missing").Returns((EnvironmentSettings)null);
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		Action act = () => _sut.UninstallByEnvironmentName("missing");

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "an unregistered environment cannot authorize destructive cleanup");
		events.First().EventType.Should().Be(ClioStageEventContract.EventTypes.Manifest,
			because: "even lookup failures must emit the typed manifest first");
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure,
			because: "the typed stream must terminate honestly");
		_iisScannerMock.DidNotReceive().TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>());
	}

	[Test]
	[Description("Malformed local database authority aborts instead of dropping a same-named Kubernetes database.")]
	public void UninstallByPath_ShouldAbortWithoutFallback_WhenDatabaseConnectionIsMalformed() {
		// Arrange
		MockStartedSite();
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		FileSystem.AddFile(csPath, new MockFileData("""
			<connectionStrings>
			  <add name="db" connectionString="Server=127.0.0.1;Port=invalid;Database=unsafe-fallback;User ID=postgres;Password=root;" />
			</connectionStrings>
			"""));

		// Act
		Action act = () => _sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		act.Should().Throw<Exception>(
			because: "malformed local connection authority must fail closed");
		_k8CommandsMock.DidNotReceive().GetPostgresConnectionString();
		_postgresMock.DidNotReceive().DropDb(Arg.Any<string>());
		_mssqlMock.DidNotReceive().DropDb(Arg.Any<string>());
		FileSystem.Directory.Exists(InstalledCreatioPath).Should().BeTrue(
			because: "database authority failure must preserve application files");
	}

	[Test]
	[Description("A concurrent named uninstall collision emits a complete typed failure stream before returning the busy error.")]
	public void UninstallByEnvironmentName_EmitsTerminalFailure_WhenEnvironmentLeaseIsBusy() {
		// Arrange
		const string busyEnvironment = "busy-environment";
		_deploymentTargetReservationMock.AcquireEnvironment(busyEnvironment).Returns(_ =>
			throw new InvalidOperationException("environment is already being changed"));
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(busyEnvironment);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*already being changed*",
			because: "the competing process must keep its exclusive environment lease");
		events.First().EventType.Should().Be(ClioStageEventContract.EventTypes.Manifest,
			because: "Ring and MCP consumers require a manifest for every uninstall run");
		events.Last().RunCompleted!.ErrorCode.Should().Be("uninstall-target-busy",
			because: "the progress stream must terminate with a stable busy classification");
	}

	[Test]
	[Description("UninstallByEnvironmentName aborts before discovery when the registration has no EnvironmentPath.")]
	public void UninstallByEnvironmentName_Aborts_WhenEnvironmentPathIsMissing() {
		// Arrange
		EnvironmentSettings.EnvironmentPath = null;
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "a registered URI is not filesystem authority for destructive cleanup");
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure,
			because: "the missing local identity must surface as a typed terminal failure");
		_iisScannerMock.DidNotReceive().TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>());
	}

	[TestCase("relative-creatio")]
	[TestCase(@"C:\")]
	[TestCase("%TEMP%\\creatio")]
	[Description("UninstallByEnvironmentName rejects relative and filesystem-root EnvironmentPath values before discovery or deletion.")]
	public void UninstallByEnvironmentName_Aborts_WhenEnvironmentPathIsUnsafe(string unsafePath) {
		// Arrange
		EnvironmentSettings.EnvironmentPath = unsafePath;

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "registered settings must never authorize relative or filesystem-root recursive deletion");
		_iisScannerMock.DidNotReceive().TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>());
		_postgresMock.DidNotReceive().DropDb(Arg.Any<string>());
		_settingsRepositoryMock.DidNotReceive().RemoveEnvironmentIfPathMatches(EnvironmentName, InstalledCreatioPath);
	}

	[Test]
	[Description("Uninstall rejects the current filesystem root before inventory or destructive cleanup.")]
	public void UninstallByPath_Aborts_WhenPathIsFilesystemRoot() {
		// Arrange
		string root = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory))!;

		// Act
		Action act = () => _sut.UninstallByPath(root);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "recursive uninstall must never authorize a volume or Unix filesystem root");
		_iisScannerMock.DidNotReceive().TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>());
	}

	[Test]
	[Description("UninstallByEnvironmentName aborts before database and file changes when complete IIS inventory cannot be proven.")]
	public void UninstallByEnvironmentName_Aborts_WhenIisInventoryFails() {
		// Arrange
		_iisScannerMock.TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>())
			.Returns(call => {
				call[0] = Array.Empty<UnregisteredSite>();
				return false;
			});

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "an incomplete raw IIS inventory cannot prove that no path-owned site remains");
		_postgresMock.DidNotReceive().DropDb(Arg.Any<string>());
		FileSystem.Directory.Exists(InstalledCreatioPath).Should().BeTrue(
			because: "filesystem deletion must not begin after discovery failure");
		_settingsRepositoryMock.DidNotReceive().RemoveEnvironmentIfPathMatches(EnvironmentName, InstalledCreatioPath);
	}

	[Test]
	[Description("Uninstall aborts before database and files when the final IIS scan still contains the target path.")]
	public void UninstallByEnvironmentName_Aborts_WhenFinalIisScanStillContainsTarget() {
		// Arrange
		IReadOnlyList<UnregisteredSite> sites = [new(new SiteBinding(EnvironmentName, "Started", "",
			InstalledCreatioPath, AppPoolName), [new Uri(EnvironmentSettings.Uri)], SiteType.Core)];
		_iisScannerMock.TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>())
			.Returns(call => {
				call[0] = sites;
				return true;
			});
		_iisScannerMock.TryFindAppPoolsForTargets(Arg.Any<IReadOnlyCollection<string>>(),
			out Arg.Any<IReadOnlyCollection<string>>()).Returns(call => {
			call[1] = Array.Empty<string>();
			return true;
		});
		AddPostgresConnectionStringFile();

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "database and file deletion require fresh proof that every matching IIS target is absent");
		_postgresMock.DidNotReceive().DropDb(Arg.Any<string>());
		FileSystem.Directory.Exists(InstalledCreatioPath).Should().BeTrue(
			because: "the target directory must remain after failed IIS absence proof");
	}

	[Test]
	[Description("Uninstall aborts before IIS deletion and database cleanup when an owned pool cannot be stopped.")]
	public void UninstallByEnvironmentName_Aborts_WhenOwnedPoolStopFails() {
		// Arrange
		MockStartedSite(appPoolName: AppPoolName);
		_iisScannerMock.StopAppPoolIfOwnedByTargets(AppPoolName, Arg.Any<IReadOnlyCollection<string>>())
			.Returns(IisAppPoolMutationResult.Failed);
		AddPostgresConnectionStringFile();

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "a pool mutation failure leaves IIS ownership unresolved");
		_iisScannerMock.DidNotReceive().TryDeleteIisTarget(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
		_postgresMock.DidNotReceive().DropDb(Arg.Any<string>());
	}

	[Test]
	[Description("Uninstall aborts before database and files when an unused pool cannot be deleted safely.")]
	public void UninstallByEnvironmentName_Aborts_WhenUnusedPoolDeleteFails() {
		// Arrange
		MockStartedSite(appPoolName: AppPoolName);
		_iisScannerMock.DeleteAppPoolIfUnused(AppPoolName).Returns(IisAppPoolMutationResult.Failed);
		AddPostgresConnectionStringFile();

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "pool cleanup must be verified before the database and installation directory are removed");
		_postgresMock.DidNotReceive().DropDb(Arg.Any<string>());
		FileSystem.Directory.Exists(InstalledCreatioPath).Should().BeTrue(
			because: "filesystem deletion must not begin after pool cleanup failure");
	}

	[Test]
	[Description("A partial multi-site failure still removes captured pools orphaned by targets already deleted.")]
	public void UninstallByEnvironmentName_ConvergesOrphanedPool_WhenLaterTargetDeleteFails() {
		// Arrange
		const string firstSite = "work-first";
		const string secondSite = "work-second";
		const string firstPool = "work-first-pool";
		const string secondPool = "work-second-pool";
		IReadOnlyList<UnregisteredSite> sites = [
			new(new SiteBinding(firstSite, "Started", "", InstalledCreatioPath, firstPool),
				[new Uri(EnvironmentSettings.Uri)], SiteType.Core),
			new(new SiteBinding(secondSite, "Started", "", InstalledCreatioPath, secondPool),
				[new Uri(EnvironmentSettings.Uri)], SiteType.Core)
		];
		_iisScannerMock.TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>())
			.Returns(call => {
				call[0] = sites;
				return true;
			});
		_iisScannerMock.TryFindAppPoolsForTargets(Arg.Any<IReadOnlyCollection<string>>(),
			out Arg.Any<IReadOnlyCollection<string>>()).Returns(call => {
			call[1] = new[] { firstPool, secondPool };
			return true;
		});
		_iisScannerMock.TryDeleteIisTarget(firstSite, InstalledCreatioPath, firstPool).Returns(true);
		_iisScannerMock.TryDeleteIisTarget(secondSite, InstalledCreatioPath, secondPool).Returns(false);
		_iisScannerMock.DeleteAppPoolIfUnused(firstPool).Returns(IisAppPoolMutationResult.Completed);
		_iisScannerMock.DeleteAppPoolIfUnused(secondPool).Returns(IisAppPoolMutationResult.PreservedShared);
		AppPoolProfileCleanupTarget firstProfile = new(
			new WindowsProfileRegistration("S-1-5-82-101", @"C:\Users\work-first-pool"));
		AppPoolProfileCleanupTarget secondProfile = new(
			new WindowsProfileRegistration("S-1-5-82-102", @"C:\Users\work-second-pool"));
		_profileCleanerMock.Prepare(firstPool).Returns(firstProfile);
		_profileCleanerMock.Prepare(secondPool).Returns(secondProfile);
		AddPostgresConnectionStringFile();

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "the changed second target must still fail the uninstall");
		_iisScannerMock.Received(1).DeleteAppPoolIfUnused(firstPool);
		_profileCleanerMock.Received(1).TryDelete(firstProfile);
		_profileCleanerMock.DidNotReceive().TryDelete(secondProfile);
		_postgresMock.DidNotReceive().DropDb(Arg.Any<string>());
	}

	[Test]
	[Description("A failed PostgreSQL drop preserves application files and environment registration.")]
	public void UninstallByEnvironmentName_ShouldAbortLaterCleanup_WhenPostgresDropReturnsFalse() {
		// Arrange
		MockNoSitesFound();
		AddPostgresConnectionStringFile();
		_postgresMock.DropDb(Arg.Any<string>()).Returns(false);

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>().WithMessage("*could not be dropped*",
			because: "a false database result must fail the drop-db stage rather than report success");
		FileSystem.Directory.Exists(InstalledCreatioPath).Should().BeTrue(
			because: "files must remain when database cleanup fails");
		_settingsRepositoryMock.DidNotReceive().RemoveEnvironmentIfPathMatches(
			EnvironmentName, InstalledCreatioPath);
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
		_mssqlMock.Received(1).Init("ts1-agent39", 1433, "", "", true);
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
		_mssqlMock.Received(1).Init(@"tscore-ms-01\mssql2008", 0, "", "", true);
		// The database should be dropped using the named instance connection
		_mssqlMock.Received(1).DropDb("dbname");
	}

	[Test]
	[Description("UninstallByPath should preserve a SQL-authenticated MSSQL named instance without adding a default port")]
	public void UninstallByPath_HandlesSqlAuthenticatedMssqlNamedInstance(){
		// Arrange
		MockStartedSite();
		string csPath = Path.Join(InstalledCreatioPath, ConnectionStringsFileName);
		const string csContent = """
								 <?xml version="1.0" encoding="utf-8"?>
								 <connectionStrings>
								   <add name="db" connectionString="Data Source=.\SQLEXPRESS;Initial Catalog=dbname;User ID=testuser;Password=testpass;" />
								 </connectionStrings>
								 """;
		FileSystem.AddFile(csPath, new MockFileData(csContent));

		// Act
		_sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		_mssqlMock.Received(1).Init(@".\SQLEXPRESS", 0, "testuser", "testpass", false);
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
			[StageIds.ReadConfig, StageIds.StopIis, StageIds.DeleteIis, StageIds.DropDb, StageIds.DeleteFiles, StageIds.Unregister],
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
		foreach (string stageId in new[] { StageIds.ReadConfig, StageIds.StopIis, StageIds.DeleteIis, StageIds.DropDb, StageIds.DeleteFiles }) {
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
		StagesWithStatus(events, StageIds.StopIis).Should().Contain(
			s => s.Status == ClioStageEventContract.StageStatuses.Skipped && s.SkipReason == ClioStageEventContract.SkipReasons.AfterFailure,
			"because configuration must be validated before the working IIS site is stopped");
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
	[Description("A missing registered application-pool profile is emitted as skipped and not applicable.")]
	public void UninstallByPath_ShouldEmitProfileSkippedNotApplicable_WhenProfileIsAbsent(){
		// Arrange
		MockStartedSite(appPoolName: AppPoolName);
		AddPostgresConnectionStringFile();
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		events.First().Stages!.Select(s => s.StageId).Should().Contain(StageIds.DeleteApppoolProfile,
			"because the profile stage is present in the manifest when a profile directory exists (AC-04)");
		StagesWithStatus(events, StageIds.DeleteApppoolProfile).Should().Contain(
			s => s.Status == ClioStageEventContract.StageStatuses.Skipped && s.SkipReason == ClioStageEventContract.SkipReasons.NotApplicable,
			"because an absent ProfileList registration has nothing to delete");
		_profileCleanerMock.Received(1).Prepare(AppPoolName);
		_profileCleanerMock.Received(1).TryDelete(Arg.Any<AppPoolProfileCleanupTarget>());
	}

	[Test]
	[Category("Unit")]
	[Description("Omits profile cleanup when IIS does not expose an actual application-pool name.")]
	public void UninstallByPath_ShouldOmitProfileStage_WhenAppPoolNameIsUnavailable(){
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
			"because no arbitrary profile can be inferred from site name or path");
		_profileCleanerMock.DidNotReceive().Prepare(Arg.Any<string>());
		_profileCleanerMock.DidNotReceive().TryDelete(Arg.Any<AppPoolProfileCleanupTarget>());
	}

	[Test]
	[Category("Unit")]
	[Description("A failed profile cleanup emits one warning, completes with warnings, and still unregisters the environment.")]
	public void UninstallByEnvironmentName_ShouldWarnAndContinueUnregister_WhenProfileDeletionFails() {
		// Arrange
		MockStartedSite(appPoolName: AppPoolName);
		AddPostgresConnectionStringFile();
		_profileCleanerMock.TryDelete(Arg.Any<AppPoolProfileCleanupTarget>()).Returns(new AppPoolProfileCleanupResult(
			AppPoolProfileCleanupStatus.Warning, ProfileDirectoryPath, "Access is denied.",
			WindowsAppPoolProfileCleaner.ProfileDeleteFailedErrorCode));
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		StagesWithStatus(events, StageIds.DeleteApppoolProfile).Should().ContainSingle(stage =>
			stage.Status == ClioStageEventContract.StageStatuses.Warning
			&& stage.ErrorCode == WindowsAppPoolProfileCleaner.ProfileDeleteFailedErrorCode,
			because: "profile exhaustion is one non-fatal typed warning instead of a failed-stage cascade");
		_settingsRepositoryMock.Received(1).RemoveEnvironmentIfPathMatches(EnvironmentName, InstalledCreatioPath);
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.SuccessWithWarnings,
			because: "the typed terminal must distinguish a successful uninstall that retained a warning");
		_loggerMock.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains(ProfileDirectoryPath) && message.Contains("Delete it manually")));
	}

	[Test]
	[Category("Unit")]
	[Description("A successful cleanup uses the actual IIS application-pool name and emits the profile stage as done.")]
	public void UninstallByPath_ShouldDeleteActualAppPoolProfile_WhenCleanupSucceeds() {
		// Arrange
		MockStartedSite(appPoolName: AppPoolName);
		AddPostgresConnectionStringFile();
		_profileCleanerMock.TryDelete(Arg.Any<AppPoolProfileCleanupTarget>()).Returns(new AppPoolProfileCleanupResult(
			AppPoolProfileCleanupStatus.Deleted, ProfileDirectoryPath));
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		_iisScannerMock.Received(1).TryStopIisTarget(EnvironmentName, InstalledCreatioPath, AppPoolName);
		_iisScannerMock.Received(1).TryDeleteIisTarget(EnvironmentName, InstalledCreatioPath, AppPoolName);
		_iisScannerMock.Received(1).DeleteAppPoolIfUnused(AppPoolName);
		Received.InOrder(() => {
			_profileCleanerMock.Prepare(AppPoolName);
			_iisScannerMock.TryDeleteIisTarget(EnvironmentName, InstalledCreatioPath, AppPoolName);
			_iisScannerMock.DeleteAppPoolIfUnused(AppPoolName);
			_profileCleanerMock.TryDelete(Arg.Any<AppPoolProfileCleanupTarget>());
		});
		StagesWithStatus(events, StageIds.DeleteApppoolProfile).Should().Contain(stage =>
			stage.Status == ClioStageEventContract.StageStatuses.Done,
			because: "successful Windows cleanup removes both profile registration and files");
	}

	[Test]
	[Category("Unit")]
	[Description("A shared application pool is neither stopped nor deleted and its Windows profile is left untouched.")]
	public void UninstallByPath_ShouldPreserveAppPoolAndProfile_WhenPoolIsShared() {
		// Arrange
		MockStartedSite(appPoolName: AppPoolName);
		AddPostgresConnectionStringFile();
		_iisScannerMock.DeleteAppPoolIfUnused(AppPoolName).Returns(IisAppPoolMutationResult.PreservedShared);
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		_iisScannerMock.Received(1).TryStopIisTarget(EnvironmentName, InstalledCreatioPath, AppPoolName);
		_iisScannerMock.Received(1).TryDeleteIisTarget(EnvironmentName, InstalledCreatioPath, AppPoolName);
		_iisScannerMock.Received(1).DeleteAppPoolIfUnused(AppPoolName);
		_profileCleanerMock.DidNotReceive().TryDelete(Arg.Any<AppPoolProfileCleanupTarget>());
		StagesWithStatus(events, StageIds.DeleteApppoolProfile).Should().ContainSingle(stage =>
			stage.Status == ClioStageEventContract.StageStatuses.Skipped
			&& stage.SkipReason == ClioStageEventContract.SkipReasons.NotApplicable,
			because: "a profile owned by another IIS app must remain available to that app");
	}

	[Test]
	[Category("Unit")]
	[Description("An IIS target with sibling applications aborts before any destructive uninstall action.")]
	public void UninstallByPath_ShouldAbortBeforeDestruction_WhenIisTargetIsNotExclusive() {
		// Arrange
		MockStartedSite(appPoolName: AppPoolName);
		_iisScannerMock.IsIisTargetExclusive(EnvironmentName).Returns(false);

		// Act
		Action act = () => _sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "removing a root site with sibling IIS applications would delete unrelated applications");
		_iisScannerMock.DidNotReceive().TryStopIisTarget(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
		_iisScannerMock.DidNotReceive().TryDeleteIisTarget(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
		_iisScannerMock.DidNotReceive().DeleteAppPoolIfUnused(Arg.Any<string>());
		_profileCleanerMock.DidNotReceive().TryDelete(Arg.Any<AppPoolProfileCleanupTarget>());
	}

	[Test]
	[Category("Unit")]
	[Description("A target that changes before IIS deletion fails the stage and prevents later destructive cleanup.")]
	public void UninstallByPath_ShouldAbortRemainingCleanup_WhenIisTargetChangesBeforeDelete() {
		// Arrange
		MockStartedSite(appPoolName: AppPoolName);
		AddPostgresConnectionStringFile();
		_iisScannerMock.TryDeleteIisTarget(EnvironmentName, InstalledCreatioPath, AppPoolName).Returns(false);
		_iisScannerMock.DeleteAppPoolIfUnused(AppPoolName).Returns(IisAppPoolMutationResult.PreservedShared);

		// Act
		Action act = () => _sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "a fresh topology check or verified deletion failure must stop database and file removal");
		_iisScannerMock.Received(1).TryDeleteIisTarget(EnvironmentName, InstalledCreatioPath, AppPoolName);
		_iisScannerMock.Received(1).DeleteAppPoolIfUnused(AppPoolName);
		_profileCleanerMock.DidNotReceive().TryDelete(Arg.Any<AppPoolProfileCleanupTarget>());
	}

	[Test]
	[Category("Unit")]
	[Description("A same-name IIS replacement aborts before the stop operation and never reaches deletion.")]
	public void UninstallByPath_ShouldNotStopOrDelete_WhenIisIdentityChangesBeforeStop() {
		// Arrange
		MockStartedSite(appPoolName: AppPoolName);
		AddPostgresConnectionStringFile();
		_iisScannerMock.TryStopIisTarget(EnvironmentName, InstalledCreatioPath, AppPoolName).Returns(false);

		// Act
		Action act = () => _sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		act.Should().Throw<CreatioUninstallAbortedException>(
			because: "destructive authority must remain bound to the originally resolved IIS identity");
		_iisScannerMock.Received(1).TryStopIisTarget(EnvironmentName, InstalledCreatioPath, AppPoolName);
		_iisScannerMock.DidNotReceive().TryDeleteIisTarget(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("A recreated application pool prevents cleanup of the newly active Windows profile.")]
	public void UninstallByPath_ShouldPreserveProfile_WhenAppPoolReappearsBeforeProfileCleanup() {
		// Arrange
		MockStartedSite(appPoolName: AppPoolName);
		AddPostgresConnectionStringFile();
		_iisScannerMock.IsAppPoolAbsent(AppPoolName).Returns(false);
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByPath(InstalledCreatioPath);

		// Assert
		_iisScannerMock.Received(1).IsAppPoolAbsent(AppPoolName);
		_profileCleanerMock.DidNotReceive().TryDelete(Arg.Any<AppPoolProfileCleanupTarget>());
		StagesWithStatus(events, StageIds.DeleteApppoolProfile).Should().ContainSingle(stage =>
			stage.Status == ClioStageEventContract.StageStatuses.Skipped
			&& stage.SkipReason == ClioStageEventContract.SkipReasons.NotApplicable,
			because: "a pool recreated after deletion owns the profile and must remain untouched");
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
		_settingsRepositoryMock.Received(1).RemoveEnvironmentIfPathMatches(EnvironmentName, InstalledCreatioPath);
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Success,
			"because the run succeeded");
	}

	[Test]
	[Category("Unit")]
	[Description("Automatic dbHub synchronization removes its owned source after cleanup and before unregistering.")]
	public void UninstallByEnvironmentName_ShouldRemoveDbHubSourceBeforeUnregister_WhenEnabled(){
		// Arrange
		MockStartedSite();
		AddPostgresConnectionStringFile();
		_dbHubSynchronizationServiceMock.IsAutomaticSynchronizationEnabled().Returns(true);
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		string[] manifest = events.First().Stages!.Select(stage => stage.StageId).ToArray();
		manifest.Should().ContainInOrder([StageIds.DeleteFiles, StageIds.RemoveDbHubSource, StageIds.Unregister],
			because: "dbHub removal must follow destructive cleanup and precede environment unregister");
		StagesWithStatus(events, StageIds.RemoveDbHubSource).Select(stage => stage.Status).Should().Equal(
			[ClioStageEventContract.StageStatuses.Running, ClioStageEventContract.StageStatuses.Done],
			because: "successful source removal is visible as a completed lifecycle stage");
		Received.InOrder(() => {
			_dbHubSynchronizationServiceMock.RemoveEnvironmentSource(EnvironmentName);
			_settingsRepositoryMock.RemoveEnvironmentIfPathMatches(EnvironmentName, InstalledCreatioPath);
		});
	}

	[Test]
	[Category("Unit")]
	[Description("A dbHub removal warning remains non-fatal and is visible in CLI and typed progress.")]
	public void UninstallByEnvironmentName_ShouldWarnAndContinueUnregister_WhenDbHubRemovalWarns(){
		// Arrange
		MockStartedSite();
		AddPostgresConnectionStringFile();
		_dbHubSynchronizationServiceMock.IsAutomaticSynchronizationEnabled().Returns(true);
		_dbHubSynchronizationServiceMock.RemoveEnvironmentSource(EnvironmentName).Returns(new DbHubSyncResult(
			Changed: true, Skipped: false, Warning: new DbHubWarning("dbHub live verification was skipped.",
				"The TOML update was retained while dbHub was offline.", "DBHUB_LIVE_VERIFICATION_SKIPPED")));
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		_sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		StagesWithStatus(events, StageIds.RemoveDbHubSource).Should().ContainSingle(stage =>
			stage.Status == ClioStageEventContract.StageStatuses.Warning
			&& stage.ErrorCode == "DBHUB_LIVE_VERIFICATION_SKIPPED",
			because: "offline live verification is a typed warning rather than an uninstall failure");
		_settingsRepositoryMock.Received(1).RemoveEnvironmentIfPathMatches(EnvironmentName, InstalledCreatioPath);
		events.Last().RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.SuccessWithWarnings,
			because: "the primary uninstall succeeded while retaining the dbHub warning");
		_loggerMock.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("dbHub live verification") && message.Contains("offline")));
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
		_dbHubSynchronizationServiceMock.IsAutomaticSynchronizationEnabled().Returns(true);
		List<ClioStageEvent> events = CaptureStageEvents();

		// Act
		Action act = () => _sut.UninstallByEnvironmentName(EnvironmentName);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			"because the failing stage rethrows");
		_settingsRepositoryMock.DidNotReceive().RemoveEnvironmentIfPathMatches(
			Arg.Any<string>(), Arg.Any<string>());
		_dbHubSynchronizationServiceMock.DidNotReceive().RemoveEnvironmentSource(Arg.Any<string>());
		StagesWithStatus(events, StageIds.RemoveDbHubSource).Should().Contain(
			s => s.Status == ClioStageEventContract.StageStatuses.Skipped
				&& s.SkipReason == ClioStageEventContract.SkipReasons.AfterFailure,
			because: "a failed destructive stage must retain the dbHub source for reconciliation or retry");
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
