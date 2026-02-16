using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.Common.IIS;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Command;

[TestFixture]
public class StartCommandTestCase : BaseCommandTests<StartOptions>
{
	private ISettingsRepository _settingsRepository;
	private IDotnetExecutor _dotnetExecutor;
	private ILogger _logger;
	private IFileSystem _fileSystem;
	private ICreatioHostService _creatioHostService;
	private IIISAppPoolManager _iisAppPoolManager;
	private IIISSiteDetector _iisSiteDetector;
	private IApplicationClient _applicationClient;
	private StartCommand _command;

	[SetUp]
	public void Setup()
	{
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_dotnetExecutor = Substitute.For<IDotnetExecutor>();
		_logger = Substitute.For<ILogger>();
		_fileSystem = Substitute.For<IFileSystem>();
		_creatioHostService = Substitute.For<ICreatioHostService>();
		_iisAppPoolManager = Substitute.For<IIISAppPoolManager>();
		_iisSiteDetector = Substitute.For<IIISSiteDetector>();
		_applicationClient = Substitute.For<IApplicationClient>();

		_command = new StartCommand(
			_settingsRepository,
			_dotnetExecutor,
			_logger,
			_fileSystem,
			_creatioHostService,
			_iisAppPoolManager,
			_iisSiteDetector,
			_applicationClient);
	}

	[Test]
	[Description("Starts IIS site and app pool when IIS site is detected for the environment")]
	public void Execute_StartsIISSiteAndAppPool_WhenIISSiteDetected()
	{
		// Arrange
		string envPath = @"C:\inetpub\wwwroot\Creatio";
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StartOptions options = new() { Environment = "production" };

		_settingsRepository.GetEnvironment("production").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);

		var iisSites = new List<IISSiteInfo>
		{
			new IISSiteInfo
			{
				SiteName = "Production",
				AppPoolName = "production",
				PhysicalPath = envPath,
				State = "Stopped",
				AppPoolState = "Stopped"
			}
		};

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(iisSites));
		_iisAppPoolManager.IsSiteRunning("Production").Returns(Task.FromResult(false));
		_iisAppPoolManager.IsAppPoolRunning("production").Returns(Task.FromResult(false));
		_iisAppPoolManager.StartAppPool("production").Returns(Task.FromResult(true));
		_iisAppPoolManager.StartSite("Production").Returns(Task.FromResult(true));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "starting IIS site and app pool should succeed");
		_iisAppPoolManager.Received(1).StartAppPool("production");
		_iisAppPoolManager.Received(1).StartSite("Production");
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Application pool")));
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("IIS site")));
	}

	[Test]
	[Description("Reports success when IIS site and app pool are already running")]
	public void Execute_ReportsSuccess_WhenIISSiteAndAppPoolAlreadyRunning()
	{
		// Arrange
		string envPath = @"C:\inetpub\wwwroot\Creatio";
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StartOptions options = new() { Environment = "production" };

		_settingsRepository.GetEnvironment("production").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);

		var iisSites = new List<IISSiteInfo>
		{
			new IISSiteInfo { SiteName = "Production", AppPoolName = "production", PhysicalPath = envPath }
		};

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(iisSites));
		_iisAppPoolManager.IsSiteRunning("Production").Returns(Task.FromResult(true));
		_iisAppPoolManager.IsAppPoolRunning("production").Returns(Task.FromResult(true));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "site and app pool are already running");
		_iisAppPoolManager.DidNotReceive().StartAppPool(Arg.Any<string>());
		_iisAppPoolManager.DidNotReceive().StartSite(Arg.Any<string>());
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("already running")));
	}

	[Test]
	[Description("Falls back to .NET Core deployment when no IIS site detected")]
	public void Execute_FallsBackToDotNetCore_WhenNoIISSiteDetected()
	{
		// Arrange
		string envPath = @"C:\Creatio\Development";
		string dllPath = Path.Combine(envPath, "Terrasoft.WebHost.dll");
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StartOptions options = new() { Environment = "development" };

		_settingsRepository.GetEnvironment("development").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);
		_fileSystem.ExistsFile(dllPath).Returns(true);

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(new List<IISSiteInfo>()));
		_creatioHostService.StartInBackground(envPath).Returns(12345);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "starting .NET Core app should succeed");
		_creatioHostService.Received(1).StartInBackground(envPath);
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("background service")));
	}

	[Test]
	[Description("Starts in terminal window when --terminal option is used for .NET Core")]
	public void Execute_StartsInTerminal_WhenTerminalOptionUsed()
	{
		// Arrange
		string envPath = @"C:\Creatio\Development";
		string dllPath = Path.Combine(envPath, "Terrasoft.WebHost.dll");
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StartOptions options = new() { Environment = "development", Terminal = true };

		_settingsRepository.GetEnvironment("development").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);
		_fileSystem.ExistsFile(dllPath).Returns(true);

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(new List<IISSiteInfo>()));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "starting in terminal should succeed");
		_creatioHostService.Received(1).StartInNewTerminal(envPath, "development");
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("terminal window")));
	}

	[Test]
	[Description("Returns error when environment path does not exist")]
	public void Execute_ReturnsError_WhenEnvironmentPathDoesNotExist()
	{
		// Arrange
		string envPath = @"C:\NonExistent\Path";
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StartOptions options = new() { Environment = "test" };

		_settingsRepository.GetEnvironment("test").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(false);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "environment path does not exist");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("does not exist")));
	}

	[Test]
	[Description("Returns error when EnvironmentPath is not configured")]
	public void Execute_ReturnsError_WhenEnvironmentPathNotConfigured()
	{
		// Arrange
		EnvironmentSettings env = new() { EnvironmentPath = null };
		StartOptions options = new() { Environment = "test" };

		_settingsRepository.GetEnvironment("test").Returns(env);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "EnvironmentPath is required");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("not configured")));
	}

	[Test]
	[Description("Returns error when no IIS site and no .NET Core deployment found")]
	public void Execute_ReturnsError_WhenNoDeploymentTypeDetected()
	{
		// Arrange
		string envPath = @"C:\EmptyPath";
		string dllPath = Path.Combine(envPath, "Terrasoft.WebHost.dll");
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StartOptions options = new() { Environment = "test" };

		_settingsRepository.GetEnvironment("test").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);
		_fileSystem.ExistsFile(dllPath).Returns(false);

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(new List<IISSiteInfo>()));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "no valid deployment type was detected");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Terrasoft.WebHost.dll not found")));
	}

	[Test]
	[Description("Returns error when IIS app pool fails to start")]
	public void Execute_ReturnsError_WhenIISAppPoolFailsToStart()
	{
		// Arrange
		string envPath = @"C:\inetpub\wwwroot\Creatio";
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StartOptions options = new() { Environment = "production" };

		_settingsRepository.GetEnvironment("production").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);

		var iisSites = new List<IISSiteInfo>
		{
			new IISSiteInfo { SiteName = "Production", AppPoolName = "production", PhysicalPath = envPath }
		};

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(iisSites));
		_iisAppPoolManager.IsSiteRunning("Production").Returns(Task.FromResult(false));
		_iisAppPoolManager.IsAppPoolRunning("production").Returns(Task.FromResult(false));
		_iisAppPoolManager.StartAppPool("production").Returns(Task.FromResult(false));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "IIS app pool failed to start");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Failed to start")));
	}

	[Test]
	[Description("Uses default environment when no environment specified")]
	public void Execute_UsesDefaultEnvironment_WhenNoEnvironmentSpecified()
	{
		// Arrange
		string envPath = @"C:\Creatio\Default";
		string dllPath = Path.Combine(envPath, "Terrasoft.WebHost.dll");
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StartOptions options = new() { Environment = null };

		_settingsRepository.FindEnvironment(null).Returns(env);
		_settingsRepository.GetEnvironment((string)null).Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);
		_fileSystem.ExistsFile(dllPath).Returns(true);

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(new List<IISSiteInfo>()));
		_creatioHostService.StartInBackground(envPath).Returns(12345);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "default environment should be used");
		_creatioHostService.Received(1).StartInBackground(envPath);
	}

	[Test]
	[Description("Pings site after starting IIS site successfully")]
	public void Execute_PingsSite_AfterStartingIISSite()
	{
		// Arrange
		string envPath = @"C:\inetpub\wwwroot\Creatio";
		string uri = "https://mysite.com";
		EnvironmentSettings env = new() { EnvironmentPath = envPath, Uri = uri };
		StartOptions options = new() { Environment = "production" };

		_settingsRepository.GetEnvironment("production").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);

		var iisSites = new List<IISSiteInfo>
		{
			new IISSiteInfo { SiteName = "Production", AppPoolName = "production", PhysicalPath = envPath }
		};

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(iisSites));
		_iisAppPoolManager.IsSiteRunning("Production").Returns(Task.FromResult(false));
		_iisAppPoolManager.IsAppPoolRunning("production").Returns(Task.FromResult(false));
		_iisAppPoolManager.StartAppPool("production").Returns(Task.FromResult(true));
		_iisAppPoolManager.StartSite("Production").Returns(Task.FromResult(true));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "site started and ping succeeded");
		_applicationClient.Received(1).ExecuteGetRequest($"{uri}/ping", 30000, 3, 2);
	}

	[Test]
	[Description("Pings site after starting .NET Core background service")]
	public void Execute_PingsSite_AfterStartingDotNetCoreBackgroundService()
	{
		// Arrange
		string envPath = @"C:\Creatio\Development";
		string uri = "https://localhost:5000";
		string dllPath = Path.Combine(envPath, "Terrasoft.WebHost.dll");
		EnvironmentSettings env = new() { EnvironmentPath = envPath, Uri = uri };
		StartOptions options = new() { Environment = "development" };

		_settingsRepository.GetEnvironment("development").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);
		_fileSystem.ExistsFile(dllPath).Returns(true);

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(new List<IISSiteInfo>()));
		_creatioHostService.StartInBackground(envPath).Returns(12345);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "background service started and ping succeeded");
		_creatioHostService.Received(1).StartInBackground(envPath);
		_applicationClient.Received(1).ExecuteGetRequest($"{uri}/ping", 30000, 3, 2);
	}

	[Test]
	[Description("Pings site after starting .NET Core in terminal mode")]
	public void Execute_PingsSite_AfterStartingDotNetCoreInTerminal()
	{
		// Arrange
		string envPath = @"C:\Creatio\Development";
		string uri = "https://localhost:5000";
		string dllPath = Path.Combine(envPath, "Terrasoft.WebHost.dll");
		EnvironmentSettings env = new() { EnvironmentPath = envPath, Uri = uri };
		StartOptions options = new() { Environment = "development", Terminal = true };

		_settingsRepository.GetEnvironment("development").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);
		_fileSystem.ExistsFile(dllPath).Returns(true);

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(new List<IISSiteInfo>()));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "terminal started and ping succeeded");
		_creatioHostService.Received(1).StartInNewTerminal(envPath, "development");
		_applicationClient.Received(1).ExecuteGetRequest($"{uri}/ping", 30000, 3, 2);
	}

	[Test]
	[Description("Skips ping when environment URI is not configured")]
	public void Execute_SkipsPing_WhenEnvironmentUriNotConfigured()
	{
		// Arrange
		string envPath = @"C:\Creatio\Development";
		string dllPath = Path.Combine(envPath, "Terrasoft.WebHost.dll");
		EnvironmentSettings env = new() { EnvironmentPath = envPath, Uri = null };
		StartOptions options = new() { Environment = "development" };

		_settingsRepository.GetEnvironment("development").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);
		_fileSystem.ExistsFile(dllPath).Returns(true);

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(new List<IISSiteInfo>()));
		_creatioHostService.StartInBackground(envPath).Returns(12345);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "service started successfully, ping skipped");
		_applicationClient.DidNotReceive().ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Skipping ping")));
	}

	[Test]
	[Description("Returns success when ping fails but logs warning")]
	public void Execute_ReturnsSuccess_WhenPingFailsButSiteStarted()
	{
		// Arrange
		string envPath = @"C:\Creatio\Development";
		string uri = "https://localhost:5000";
		string dllPath = Path.Combine(envPath, "Terrasoft.WebHost.dll");
		EnvironmentSettings env = new() { EnvironmentPath = envPath, Uri = uri };
		StartOptions options = new() { Environment = "development" };

		_settingsRepository.GetEnvironment("development").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);
		_fileSystem.ExistsFile(dllPath).Returns(true);

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(new List<IISSiteInfo>()));
		_creatioHostService.StartInBackground(envPath).Returns(12345);
		
		// Make ping fail
		_applicationClient.When(x => x.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()))
			.Do(x => throw new Exception("Connection refused"));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "service started successfully even though ping failed");
		_logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("Site started but ping failed")));
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("may still be starting up")));
	}

	[Test]
	[Description("Pings site with correct retry parameters")]
	public void Execute_PingsSiteWithCorrectRetryParameters()
	{
		// Arrange
		string envPath = @"C:\Creatio\Development";
		string uri = "https://mysite.com";
		string dllPath = Path.Combine(envPath, "Terrasoft.WebHost.dll");
		EnvironmentSettings env = new() { EnvironmentPath = envPath, Uri = uri };
		StartOptions options = new() { Environment = "development" };

		_settingsRepository.GetEnvironment("development").Returns(env);
		_fileSystem.ExistsDirectory(envPath).Returns(true);
		_fileSystem.ExistsFile(dllPath).Returns(true);

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(new List<IISSiteInfo>()));
		_creatioHostService.StartInBackground(envPath).Returns(12345);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0);
		_applicationClient.Received(1).ExecuteGetRequest(
			$"{uri}/ping",
			30000,  // 30 second timeout
			3,      // 3 retries
			2       // 2 second delay between retries
		);
	}
}

