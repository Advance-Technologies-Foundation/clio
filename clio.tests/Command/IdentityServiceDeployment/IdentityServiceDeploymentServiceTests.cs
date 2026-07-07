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

	private static string CreateCreatioEnvironmentPath()
	{
		string path = Path.Combine(Path.GetTempPath(), $"creatio-env-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		File.WriteAllText(Path.Combine(path, "ConnectionStrings.config"),
			"""
			<connectionStrings>
			  <add name="dbPostgreSql" connectionString="Server=localhost;Port=5432;Database=bank;User ID=postgres;Password=secret;" />
			</connectionStrings>
			""");
		return path;
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
