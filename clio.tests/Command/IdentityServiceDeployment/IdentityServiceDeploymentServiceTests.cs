using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.IdentityServiceDeployment;
using Clio.Common;
using Clio.Common.IIS;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.IdentityServiceDeployment;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class IdentityServiceDeploymentServiceTests
{
	[Test]
	[Description("Deploy uses the persisted registered environment so EnvironmentPath survives option filling and ConnectionStrings.config can be read.")]
	[Platform("Win", Reason = "deploy-identity performs Windows-only IIS deployment (DeploymentStrategyFactory throws PlatformNotSupportedException off-Windows); skipped on non-Windows")]
	public void Deploy_Should_Use_Persisted_Environment_When_Resolving_Identity_Path_And_Db_Connection()
	{
		// Arrange
		string environmentPath = CreateCreatioEnvironmentPath();
		string identityArchivePath = CreateIdentityArchive();
		EnvironmentSettings persistedEnvironment = new() {
			Uri = "http://localhost:40085",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true,
			EnvironmentPath = environmentPath
		};
		EnvironmentSettings filledEnvironmentWithoutPath = new() {
			Uri = persistedEnvironment.Uri,
			Login = persistedEnvironment.Login,
			Password = persistedEnvironment.Password,
			IsNetCore = persistedEnvironment.IsNetCore
		};
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetActualEnvironmentName("bank").Returns("bank");
		settingsRepository.FindEnvironment("bank").Returns(persistedEnvironment);
		settingsRepository.GetEnvironment(Arg.Any<DeployIdentityOptions>()).Returns(filledEnvironmentWithoutPath);
		IIdentityServiceArchiveResolver archiveResolver = Substitute.For<IIdentityServiceArchiveResolver>();
		archiveResolver.Resolve(Arg.Any<string>(), Arg.Any<string>()).Returns(identityArchivePath);
		IIdentityServiceCreatioClient creatioClient = Substitute.For<IIdentityServiceCreatioClient>();
		creatioClient.GetDesignerClientSecret().Returns("designer-secret");
		string systemUserId = Guid.NewGuid().ToString();
		creatioClient.CreateClioClient(Arg.Any<DeployIdentityOptions>(), Arg.Any<string>())
			.Returns(new OAuthClientCredentials("client-id", "client-secret"));
		IIdentityServiceSystemUserResolver systemUserResolver = Substitute.For<IIdentityServiceSystemUserResolver>();
		systemUserResolver.ResolveSystemUserId(persistedEnvironment, "Supervisor").Returns(systemUserId);
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>()).Returns(true);
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult { ExitCode = 0 }));
		IAvailableIisPortService availableIisPortService = Substitute.For<IAvailableIisPortService>();
		IIdentityServiceRoleGrantService roleGrantService = Substitute.For<IIdentityServiceRoleGrantService>();
		IDeploymentTargetReservation targetReservation = CreateTargetReservation();
		IdentityServiceDeploymentService service = new(
			settingsRepository,
			archiveResolver,
			creatioClient,
			new StubHttpClientFactory(HttpStatusCode.OK),
			sysSettingsManager,
			processExecutor,
			availableIisPortService,
			roleGrantService,
			systemUserResolver,
			targetReservation,
			Substitute.For<ILogger>());
		DeployIdentityOptions options = new() {
			Environment = "bank",
			ZipFile = identityArchivePath,
			IdentitySitePort = 40086,
			IdentityPath = CreateIdentityTargetPath(),
			Overwrite = true
		};
		settingsRepository.ClearReceivedCalls();

		// Act
		IdentityServiceDeploymentResult result = service.Deploy(options);

		// Assert
		result.Success.Should().BeTrue(
			because: "a persisted EnvironmentPath should let deploy-identity read ConnectionStrings.config and complete");
		settingsRepository.DidNotReceive().GetEnvironment(Arg.Any<DeployIdentityOptions>());
		settingsRepository.Received(1).ConfigureEnvironment(
			"bank",
			Arg.Is<EnvironmentSettings>(settings => settings.EnvironmentPath == environmentPath));
		creatioClient.DidNotReceive().CreateTechnicalUser(Arg.Any<string>());
		systemUserResolver.Received(1).ResolveSystemUserId(persistedEnvironment, "Supervisor");
		roleGrantService.DidNotReceive().GrantSystemAdministratorRole(Arg.Any<EnvironmentSettings>(), Arg.Any<string>());
		targetReservation.Received(1).Acquire(options.IdentityPath);
	}

	[Test]
	[Description("Deploy auto-discovers IdentityService.zip under the registered environment and auto-picks a free IIS port when both optional arguments are omitted.")]
	[Platform("Win", Reason = "deploy-identity performs Windows-only IIS deployment (DeploymentStrategyFactory throws PlatformNotSupportedException off-Windows); skipped on non-Windows")]
	public void Deploy_Should_Auto_Discover_Zip_And_Port_When_Optional_Arguments_Are_Omitted()
	{
		// Arrange
		string environmentPath = CreateCreatioEnvironmentPath();
		string discoveredArchivePath = CreateIdentityArchive(environmentPath);
		string identityArchivePath = CreateIdentityArchive();
		EnvironmentSettings persistedEnvironment = new() {
			Uri = "http://localhost:40085",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true,
			EnvironmentPath = environmentPath
		};
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetActualEnvironmentName("bank").Returns("bank");
		settingsRepository.FindEnvironment("bank").Returns(persistedEnvironment);
		IIdentityServiceArchiveResolver archiveResolver = Substitute.For<IIdentityServiceArchiveResolver>();
		archiveResolver.Resolve(discoveredArchivePath, "IdentityService.zip").Returns(identityArchivePath);
		IIdentityServiceCreatioClient creatioClient = Substitute.For<IIdentityServiceCreatioClient>();
		creatioClient.GetDesignerClientSecret().Returns("designer-secret");
		string systemUserId = Guid.NewGuid().ToString();
		creatioClient.CreateClioClient(Arg.Any<DeployIdentityOptions>(), Arg.Any<string>())
			.Returns(new OAuthClientCredentials("client-id", "client-secret"));
		IIdentityServiceSystemUserResolver systemUserResolver = Substitute.For<IIdentityServiceSystemUserResolver>();
		systemUserResolver.ResolveSystemUserId(persistedEnvironment, "Supervisor").Returns(systemUserId);
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>()).Returns(true);
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult { ExitCode = 0 }));
		IAvailableIisPortService availableIisPortService = Substitute.For<IAvailableIisPortService>();
		availableIisPortService.FindAsync(40001, 40100).Returns(Task.FromResult(new FindAvailableIisPortResult(
			"available",
			"Port 40087 is available.",
			40001,
			40100,
			40087,
			0,
			0)));
		IIdentityServiceRoleGrantService roleGrantService = Substitute.For<IIdentityServiceRoleGrantService>();
		IdentityServiceDeploymentService service = new(
			settingsRepository,
			archiveResolver,
			creatioClient,
			new StubHttpClientFactory(HttpStatusCode.OK),
			sysSettingsManager,
			processExecutor,
			availableIisPortService,
			roleGrantService,
			systemUserResolver,
			CreateTargetReservation(),
			Substitute.For<ILogger>());
		DeployIdentityOptions options = new() {
			Environment = "bank",
			IdentityPath = CreateIdentityTargetPath(),
			Overwrite = true
		};

		// Act
		IdentityServiceDeploymentResult result = service.Deploy(options);

		// Assert
		result.IdentityServiceUrl.Should().Be("http://localhost:40087",
			because: "deploy-identity should auto-select the first free IIS port from the default range");
		archiveResolver.Received(1).Resolve(discoveredArchivePath, "IdentityService.zip");
		availableIisPortService.Received(1).FindAsync(40001, 40100);
	}

	[Test]
	[Description("Deploy creates and grants a new technical user only when create-tech-user is explicitly requested.")]
	[Platform("Win", Reason = "deploy-identity performs Windows-only IIS deployment (DeploymentStrategyFactory throws PlatformNotSupportedException off-Windows); skipped on non-Windows")]
	public void Deploy_Should_Create_Technical_User_Only_When_Requested()
	{
		// Arrange
		string environmentPath = CreateCreatioEnvironmentPath();
		string identityArchivePath = CreateIdentityArchive();
		EnvironmentSettings persistedEnvironment = new() {
			Uri = "http://localhost:40085",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true,
			EnvironmentPath = environmentPath
		};
		ISettingsRepository settingsRepository = CreateSettingsRepository(persistedEnvironment);
		IIdentityServiceArchiveResolver archiveResolver = Substitute.For<IIdentityServiceArchiveResolver>();
		archiveResolver.Resolve(identityArchivePath, "IdentityService.zip").Returns(identityArchivePath);
		IIdentityServiceCreatioClient creatioClient = Substitute.For<IIdentityServiceCreatioClient>();
		creatioClient.GetDesignerClientSecret().Returns("designer-secret");
		string systemUserId = Guid.NewGuid().ToString();
		creatioClient.CreateTechnicalUser("Supervisor").Returns(systemUserId);
		creatioClient.CreateClioClient(Arg.Any<DeployIdentityOptions>(), systemUserId)
			.Returns(new OAuthClientCredentials("client-id", "client-secret"));
		IIdentityServiceRoleGrantService roleGrantService = Substitute.For<IIdentityServiceRoleGrantService>();
		IIdentityServiceSystemUserResolver systemUserResolver = Substitute.For<IIdentityServiceSystemUserResolver>();
		IdentityServiceDeploymentService service = CreateService(
			settingsRepository,
			archiveResolver,
			creatioClient,
			roleGrantService,
			systemUserResolver);
		DeployIdentityOptions options = new() {
			Environment = "bank",
			ZipFile = identityArchivePath,
			IdentitySitePort = 40086,
			IdentityPath = CreateIdentityTargetPath(),
			Overwrite = true,
			CreateTechUser = true
		};

		// Act
		IdentityServiceDeploymentResult result = service.Deploy(options);

		// Assert
		result.ClientId.Should().Be("client-id",
			because: "create-tech-user should still create a verifiable clio OAuth app");
		creatioClient.Received(1).CreateTechnicalUser("Supervisor");
		roleGrantService.Received(1).GrantSystemAdministratorRole(persistedEnvironment, systemUserId);
		systemUserResolver.DidNotReceive().ResolveSystemUserId(Arg.Any<EnvironmentSettings>(), Arg.Any<string>());
	}

	[Test]
	[Description("Deploy with no-app connects Creatio to IdentityService but skips OAuth app creation, credential verification, and local clio credential persistence.")]
	[Platform("Win", Reason = "deploy-identity performs Windows-only IIS deployment (DeploymentStrategyFactory throws PlatformNotSupportedException off-Windows); skipped on non-Windows")]
	public void Deploy_Should_Skip_OAuth_App_When_NoApp_Is_Requested()
	{
		// Arrange
		string environmentPath = CreateCreatioEnvironmentPath();
		string identityArchivePath = CreateIdentityArchive();
		EnvironmentSettings persistedEnvironment = new() {
			Uri = "http://localhost:40085",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true,
			EnvironmentPath = environmentPath
		};
		ISettingsRepository settingsRepository = CreateSettingsRepository(persistedEnvironment);
		IIdentityServiceArchiveResolver archiveResolver = Substitute.For<IIdentityServiceArchiveResolver>();
		archiveResolver.Resolve(identityArchivePath, "IdentityService.zip").Returns(identityArchivePath);
		IIdentityServiceCreatioClient creatioClient = Substitute.For<IIdentityServiceCreatioClient>();
		creatioClient.GetDesignerClientSecret().Returns("designer-secret");
		IIdentityServiceRoleGrantService roleGrantService = Substitute.For<IIdentityServiceRoleGrantService>();
		IIdentityServiceSystemUserResolver systemUserResolver = Substitute.For<IIdentityServiceSystemUserResolver>();
		IdentityServiceDeploymentService service = CreateService(
			settingsRepository,
			archiveResolver,
			creatioClient,
			roleGrantService,
			systemUserResolver);
		DeployIdentityOptions options = new() {
			Environment = "bank",
			ZipFile = identityArchivePath,
			IdentitySitePort = 40086,
			IdentityPath = CreateIdentityTargetPath(),
			Overwrite = true,
			NoApp = true
		};

		// Act
		IdentityServiceDeploymentResult result = service.Deploy(options);

		// Assert
		result.ClientId.Should().BeEmpty(
			because: "no-app should report that no clio OAuth app was created");
		result.Message.Should().Contain("skipped",
			because: "operators should see that OAuth app creation was intentionally skipped");
		result.Message.Should().Contain("no clio client credentials were persisted",
			because: "operators should not expect local clio OAuth credentials after no-app deployment");
		result.Message.Should().Contain("token verification was skipped",
			because: "client_credentials verification cannot run when no OAuth app exists");
		creatioClient.DidNotReceive().CreateTechnicalUser(Arg.Any<string>());
		creatioClient.DidNotReceive().CreateClioClient(Arg.Any<DeployIdentityOptions>(), Arg.Any<string>());
		systemUserResolver.DidNotReceive().ResolveSystemUserId(Arg.Any<EnvironmentSettings>(), Arg.Any<string>());
		roleGrantService.DidNotReceive().GrantSystemAdministratorRole(Arg.Any<EnvironmentSettings>(), Arg.Any<string>());
		settingsRepository.DidNotReceive().ConfigureEnvironment(Arg.Any<string>(), Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("Deploy rejects no-app combined with create-tech-user because no OAuth app is created.")]
	public void Deploy_Should_Reject_NoApp_With_CreateTechUser()
	{
		// Arrange
		IdentityServiceDeploymentService service = CreateService();
		DeployIdentityOptions options = new() {
			NoApp = true,
			CreateTechUser = true
		};

		// Act
		Action act = () => service.Deploy(options);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*--no-app*--create-tech-user*",
				because: "the flags are mutually exclusive by design");
	}

	[Test]
	[Description("Deploy rejects no-app combined with user because no OAuth app is created.")]
	public void Deploy_Should_Reject_NoApp_With_User()
	{
		// Arrange
		IdentityServiceDeploymentService service = CreateService();
		DeployIdentityOptions options = new() {
			NoApp = true,
			SystemUser = "Supervisor"
		};

		// Act
		Action act = () => service.Deploy(options);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*--no-app*--user*",
				because: "user binding is irrelevant when OAuth app creation is skipped");
	}

	[Test]
	[Description("Uses the runtime db connection when both db and the legacy dbPostgreSql connection are present.")]
	public void ReadCreatioDbConnectionString_Should_Prefer_Db_When_Both_Connections_Are_Present()
	{
		// Arrange
		const string runtimeConnection = "Host=runtime;Database=creatio;Username=postgres;Password=secret";
		string environmentPath = CreateCreatioEnvironmentPath($$"""
			<connectionStrings>
			  <add name="dbPostgreSql" connectionString="Host=legacy;Database=stale;Username=postgres;Password=secret" />
			  <add name="db" connectionString="{{runtimeConnection}}" />
			</connectionStrings>
			""");
		EnvironmentSettings environment = new() { EnvironmentPath = environmentPath };

		// Act
		string connectionString = IdentityServiceDeploymentService.ReadCreatioDbConnectionString(environment);

		// Assert
		connectionString.Should().Be(runtimeConnection,
			because: "IdentityService must target the same database Creatio uses at runtime");
	}

	[Test]
	[Description("Falls back to the legacy dbPostgreSql connection when a runtime db connection is absent.")]
	public void ReadCreatioDbConnectionString_Should_Fallback_To_DbPostgreSql_When_Db_Is_Absent()
	{
		// Arrange
		const string legacyConnection = "Host=legacy;Database=creatio;Username=postgres;Password=secret";
		string environmentPath = CreateCreatioEnvironmentPath($$"""
			<connectionStrings>
			  <add name="dbPostgreSql" connectionString="{{legacyConnection}}" />
			</connectionStrings>
			""");
		EnvironmentSettings environment = new() { EnvironmentPath = environmentPath };

		// Act
		string connectionString = IdentityServiceDeploymentService.ReadCreatioDbConnectionString(environment);

		// Assert
		connectionString.Should().Be(legacyConnection,
			because: "older Creatio installations may expose only dbPostgreSql");
	}

	[Test]
	[Description("Refuses to overwrite a non-empty directory that is not an existing IdentityService deployment.")]
	public void ExtractIdentityService_Should_Reject_Unrecognized_NonEmpty_Target()
	{
		// Arrange
		string archivePath = CreateIdentityArchive();
		string targetPath = CreateIdentityTargetPath();
		Directory.CreateDirectory(targetPath);
		string unrelatedFile = Path.Combine(targetPath, "unrelated.txt");
		File.WriteAllText(unrelatedFile, "keep");

		// Act
		Action act = () => CreateService().ExtractIdentityService(
			archivePath, targetPath, overwrite: true, CreateEnvironmentSettings());

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*not a recognized IdentityService deployment*",
				because: "overwrite must not recursively delete or overlay an unrelated directory");
		File.Exists(unrelatedFile).Should().BeTrue(
			because: "a refused overwrite must leave unrelated files intact");
	}

	[Test]
	[Description("Replaces a recognized IdentityService directory so files absent from the new archive cannot remain stale.")]
	public void ExtractIdentityService_Should_Replace_Recognized_Target()
	{
		// Arrange
		string archivePath = CreateIdentityArchive();
		string targetPath = CreateIdentityTargetPath();
		Directory.CreateDirectory(targetPath);
		File.WriteAllText(Path.Combine(targetPath, "IdentityService.dll"), "old");
		File.WriteAllText(Path.Combine(targetPath, "appsettings.json"), "{}");
		string obsoleteFile = Path.Combine(targetPath, "obsolete-extension.dll");
		File.WriteAllText(obsoleteFile, "old");

		// Act
		CreateService().ExtractIdentityService(
			archivePath, targetPath, overwrite: true, CreateEnvironmentSettings());

		// Assert
		File.Exists(Path.Combine(targetPath, "appsettings.json")).Should().BeTrue(
			because: "the replacement archive should be extracted into the recognized target");
		File.Exists(obsoleteFile).Should().BeFalse(
			because: "a recognized deployment should be replaced cleanly so stale assemblies cannot survive an upgrade");
	}

	[Test]
	[Description("Keeps the working IdentityService directory intact when a replacement archive cannot be extracted.")]
	public void ExtractIdentityService_Should_Preserve_Recognized_Target_When_Staging_Fails()
	{
		// Arrange
		string archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
		using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create)) {
			archive.CreateEntry("conflict");
			archive.CreateEntry("conflict/file.txt");
		}
		string targetPath = CreateIdentityTargetPath();
		Directory.CreateDirectory(targetPath);
		File.WriteAllText(Path.Combine(targetPath, "IdentityService.dll"), "old");
		File.WriteAllText(Path.Combine(targetPath, "appsettings.json"), "{}");
		string workingFile = Path.Combine(targetPath, "working.txt");
		File.WriteAllText(workingFile, "original");

		// Act
		Action act = () => CreateService().ExtractIdentityService(
			archivePath, targetPath, overwrite: true, CreateEnvironmentSettings());

		// Assert
		act.Should().Throw<IOException>(
			because: "the conflicting archive entries cannot both be extracted into the staging directory");
		File.ReadAllText(workingFile).Should().Be("original",
			because: "the working deployment must remain untouched until the replacement archive extracts successfully");
	}

	[Test]
	[Description("Keeps the working IdentityService directory intact when the replacement ZIP is not an IdentityService archive.")]
	public void ExtractIdentityService_Should_Preserve_Recognized_Target_When_Archive_Is_Unrelated()
	{
		// Arrange
		string archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
		using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create)) {
			archive.CreateEntry("unrelated.txt");
		}
		string targetPath = CreateIdentityTargetPath();
		Directory.CreateDirectory(targetPath);
		File.WriteAllText(Path.Combine(targetPath, "IdentityService.dll"), "old");
		File.WriteAllText(Path.Combine(targetPath, "appsettings.json"), "{}");
		string workingFile = Path.Combine(targetPath, "working.txt");
		File.WriteAllText(workingFile, "original");

		// Act
		Action act = () => CreateService().ExtractIdentityService(
			archivePath, targetPath, overwrite: true, CreateEnvironmentSettings());

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*does not contain IdentityService.dll and appsettings.json*",
				because: "a valid but unrelated ZIP must not replace a working IdentityService deployment");
		File.ReadAllText(workingFile).Should().Be("original",
			because: "the working deployment must remain untouched until staged content is identified as IdentityService");
	}

	[Test]
	[Description("Keeps the working IdentityService directory intact when staged appsettings.json is malformed.")]
	public void ExtractIdentityService_Should_Preserve_Recognized_Target_When_AppSettings_Is_Malformed()
	{
		// Arrange
		string archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
		using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create)) {
			archive.CreateEntry("IdentityService.dll");
			ZipArchiveEntry appsettings = archive.CreateEntry("appsettings.json");
			using StreamWriter writer = new(appsettings.Open());
			writer.Write("not-json");
		}
		string targetPath = CreateIdentityTargetPath();
		Directory.CreateDirectory(targetPath);
		File.WriteAllText(Path.Combine(targetPath, "IdentityService.dll"), "old");
		File.WriteAllText(Path.Combine(targetPath, "appsettings.json"), "{}");
		string workingFile = Path.Combine(targetPath, "working.txt");
		File.WriteAllText(workingFile, "original");

		// Act
		Action act = () => CreateService().ExtractIdentityService(
			archivePath, targetPath, overwrite: true, CreateEnvironmentSettings());

		// Assert
		act.Should().Throw<JsonException>(
			because: "malformed staged configuration must fail before the working deployment is moved");
		File.ReadAllText(workingFile).Should().Be("original",
			because: "the working deployment must remain intact until staged configuration succeeds");
	}

	[Test]
	[Description("Refuses an IdentityService target that is redirected through a filesystem reparse point.")]
	public void ExtractIdentityService_Should_Reject_Reparse_Point_Target()
	{
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Windows directory-link credential redirection is Windows-specific.");
		}
		string rootPath = Path.Combine(Path.GetTempPath(), $"identity-link-{Guid.NewGuid():N}");
		string externalPath = Path.Combine(rootPath, "external");
		string targetPath = Path.Combine(rootPath, "target");
		Directory.CreateDirectory(externalPath);
		try {
			try {
				Directory.CreateSymbolicLink(targetPath, externalPath);
			}
			catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) {
				Assert.Ignore($"Directory-link creation is unavailable: {exception.Message}");
			}
			string archivePath = CreateIdentityArchive();

			// Act
			Action act = () => CreateService().ExtractIdentityService(
				archivePath, targetPath, overwrite: true, CreateEnvironmentSettings());

			// Assert
			act.Should().Throw<InvalidOperationException>().WithMessage("*filesystem reparse point*",
				because: "deployment files and database credentials must not be redirected outside the selected target");
			Directory.EnumerateFileSystemEntries(externalPath).Should().BeEmpty(
				because: "a rejected link target must receive neither binaries nor configured credentials");
		}
		finally {
			if (Directory.Exists(targetPath)) {
				Directory.Delete(targetPath);
			}
			if (Directory.Exists(rootPath)) {
				Directory.Delete(rootPath, recursive: true);
			}
		}
	}

	private static string CreateCreatioEnvironmentPath(string? connectionStrings = null)
	{
		string path = Path.Combine(Path.GetTempPath(), $"creatio-env-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		File.WriteAllText(Path.Combine(path, "ConnectionStrings.config"),
			connectionStrings ?? """
			<connectionStrings>
			  <add name="dbPostgreSql" connectionString="Server=localhost;Port=5432;Database=bank;User ID=postgres;Password=secret;" />
			</connectionStrings>
			""");
		return path;
	}

	private static EnvironmentSettings CreateEnvironmentSettings() => new() {
		EnvironmentPath = CreateCreatioEnvironmentPath()
	};

	private static IDeploymentTargetReservation CreateTargetReservation() {
		IDeploymentTargetReservation reservation = Substitute.For<IDeploymentTargetReservation>();
		reservation.Acquire(Arg.Any<string>()).Returns(Substitute.For<IDisposable>());
		return reservation;
	}

	private static string CreateIdentityArchive()
	{
		string archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
		using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
		ZipArchiveEntry appsettings = archive.CreateEntry("appsettings.json");
		using (StreamWriter writer = new(appsettings.Open())) {
			writer.Write("{}");
		}
		archive.CreateEntry("IdentityService.dll");
		return archivePath;
	}

	private static ISettingsRepository CreateSettingsRepository(EnvironmentSettings? environment = null)
	{
		EnvironmentSettings persistedEnvironment = environment ?? new EnvironmentSettings {
			Uri = "http://localhost:40085",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true,
			EnvironmentPath = CreateCreatioEnvironmentPath()
		};
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetActualEnvironmentName("bank").Returns("bank");
		settingsRepository.FindEnvironment("bank").Returns(persistedEnvironment);
		return settingsRepository;
	}

	private static IdentityServiceDeploymentService CreateService(
		ISettingsRepository? settingsRepository = null,
		IIdentityServiceArchiveResolver? archiveResolver = null,
		IIdentityServiceCreatioClient? creatioClient = null,
		IIdentityServiceRoleGrantService? roleGrantService = null,
		IIdentityServiceSystemUserResolver? systemUserResolver = null)
	{
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>()).Returns(true);
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult { ExitCode = 0 }));
		IAvailableIisPortService availableIisPortService = Substitute.For<IAvailableIisPortService>();
		return new IdentityServiceDeploymentService(
			settingsRepository ?? CreateSettingsRepository(),
			archiveResolver ?? Substitute.For<IIdentityServiceArchiveResolver>(),
			creatioClient ?? Substitute.For<IIdentityServiceCreatioClient>(),
			new StubHttpClientFactory(HttpStatusCode.OK),
			sysSettingsManager,
			processExecutor,
			availableIisPortService,
			roleGrantService ?? Substitute.For<IIdentityServiceRoleGrantService>(),
			systemUserResolver ?? Substitute.For<IIdentityServiceSystemUserResolver>(),
			CreateTargetReservation(),
			Substitute.For<ILogger>());
	}

	private static string CreateIdentityArchive(string directory)
	{
		string archivePath = Path.Combine(directory, "IdentityService.zip");
		using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
		archive.CreateEntry("IdentityService.dll");
		return archivePath;
	}

	private static string CreateIdentityTargetPath() =>
		Path.Combine(Path.GetTempPath(), $"identity-target-{Guid.NewGuid():N}");

	private sealed class StubHttpClientFactory : IHttpClientFactory
	{
		private readonly HttpStatusCode _statusCode;

		public StubHttpClientFactory(HttpStatusCode statusCode) {
			_statusCode = statusCode;
		}

		public HttpClient CreateClient(string name) => new(new StubHandler(_statusCode));
	}

	private sealed class StubHandler : HttpMessageHandler
	{
		private readonly HttpStatusCode _statusCode;

		public StubHandler(HttpStatusCode statusCode) {
			_statusCode = statusCode;
		}

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken) =>
			Task.FromResult(new HttpResponseMessage(_statusCode) {
				Content = new StringContent("{}")
			});
	}
}
