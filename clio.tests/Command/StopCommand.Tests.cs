using System.Collections.Generic;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.Common.IIS;
using Clio.Common.SystemServices;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Command;

[TestFixture]
public class StopCommandTestCase : BaseCommandTests<StopOptions>
{
	private ISettingsRepository _settingsRepository;
	private ISystemServiceManager _serviceManager;
	private ILogger _logger;
	private IFileSystem _fileSystem;
	private IIISAppPoolManager _iisAppPoolManager;
	private IIISSiteDetector _iisSiteDetector;
	private StopCommand _command;

	[SetUp]
	public override void Setup()
	{
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_serviceManager = Substitute.For<ISystemServiceManager>();
		_logger = Substitute.For<ILogger>();
		_fileSystem = Substitute.For<IFileSystem>();
		_iisAppPoolManager = Substitute.For<IIISAppPoolManager>();
		_iisSiteDetector = Substitute.For<IIISSiteDetector>();

		_command = new StopCommand(
			_settingsRepository,
			_serviceManager,
			_logger,
			_fileSystem,
			_iisAppPoolManager,
			_iisSiteDetector);
	}

	[Test]
	[Description("Stops IIS app pool when IIS site is detected for the environment")]
	public void Execute_StopsIISAppPool_WhenIISSiteDetected()
	{
		// Arrange
		string envPath = @"C:\inetpub\wwwroot\Creatio";
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StopOptions options = new() { Environment = "production", IsSilent = true };

		_settingsRepository.GetEnvironment("production").Returns(env);

		var iisSites = new List<IISSiteInfo>
		{
			new IISSiteInfo
			{
				SiteName = "Production",
				AppPoolName = "production",
				PhysicalPath = envPath,
				State = "Started",
				AppPoolState = "Started"
			}
		};

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(iisSites));
		_iisAppPoolManager.StopAppPool("production").Returns(Task.FromResult(true));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "stopping IIS app pool should succeed");
		_iisAppPoolManager.Received(1).StopAppPool("production");
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Stopping IIS application pool")));
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("stopped successfully")));
	}

	[Test]
	[Description("Attempts all stop methods when stopping environment")]
	public void Execute_AttemptsAllStopMethods_WhenStoppingEnvironment()
	{
		// Arrange
		string envPath = @"C:\Creatio\Test";
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StopOptions options = new() { Environment = "test", IsSilent = true };

		_settingsRepository.GetEnvironment("test").Returns(env);
		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(new List<IISSiteInfo>()));
		_serviceManager.IsServiceRunning(Arg.Any<string>()).Returns(Task.FromResult(false));

		// Act
		int result = _command.Execute(options);

		// Assert
		// Should attempt IIS, then OS service, then background process
		_iisSiteDetector.Received(1).GetSitesByPath(envPath);
		_serviceManager.Received().IsServiceRunning(Arg.Any<string>());
	}

	[Test]
	[Description("Skips IIS app pool stop when no IIS site found")]
	public void Execute_SkipsIISStop_WhenNoIISSiteFound()
	{
		// Arrange
		string envPath = @"C:\Creatio\Test";
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StopOptions options = new() { Environment = "test", IsSilent = true };

		_settingsRepository.GetEnvironment("test").Returns(env);
		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(new List<IISSiteInfo>()));
		_serviceManager.IsServiceRunning(Arg.Any<string>()).Returns(Task.FromResult(false));

		// Act
		int result = _command.Execute(options);

		// Assert
		_iisAppPoolManager.DidNotReceive().StopAppPool(Arg.Any<string>());
	}

	[Test]
	[Description("Continues with other stop methods when IIS app pool stop fails")]
	public void Execute_ContinuesWithOtherMethods_WhenIISStopFails()
	{
		// Arrange
		string envPath = @"C:\inetpub\wwwroot\Creatio";
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StopOptions options = new() { Environment = "production", IsSilent = true };

		_settingsRepository.GetEnvironment("production").Returns(env);

		var iisSites = new List<IISSiteInfo>
		{
			new IISSiteInfo { AppPoolName = "production", PhysicalPath = envPath }
		};

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(iisSites));
		_iisAppPoolManager.StopAppPool("production").Returns(Task.FromResult(false));
		_serviceManager.IsServiceRunning(Arg.Any<string>()).Returns(Task.FromResult(false));

		// Act
		int result = _command.Execute(options);

		// Assert
		_iisAppPoolManager.Received(1).StopAppPool("production");
		_serviceManager.Received().IsServiceRunning(Arg.Any<string>());
		_logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("Failed to stop")));
	}

	[Test]
	[Description("Reports success when at least one stop method succeeds")]
	public void Execute_ReportsSuccess_WhenAtLeastOneMethodSucceeds()
	{
		// Arrange
		string envPath = @"C:\inetpub\wwwroot\Creatio";
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StopOptions options = new() { Environment = "production", IsSilent = true };

		_settingsRepository.GetEnvironment("production").Returns(env);

		var iisSites = new List<IISSiteInfo>
		{
			new IISSiteInfo { AppPoolName = "production", PhysicalPath = envPath }
		};

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(iisSites));
		_iisAppPoolManager.StopAppPool("production").Returns(Task.FromResult(true));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "IIS app pool was stopped successfully");
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Successfully stopped")));
	}

	[Test]
	[Description("Returns error code when all stop methods fail")]
	public void Execute_ReturnsError_WhenAllStopMethodsFail()
	{
		// Arrange
		string envPath = @"C:\Creatio\Test";
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StopOptions options = new() { Environment = "test", IsSilent = true };

		_settingsRepository.GetEnvironment("test").Returns(env);
		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(new List<IISSiteInfo>()));
		_serviceManager.IsServiceRunning(Arg.Any<string>()).Returns(Task.FromResult(false));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "no running service or process was found");
		_logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("No running service or process found")));
	}

	[Test]
	[Description("Handles IIS app pool with Unknown name gracefully")]
	public void Execute_HandlesUnknownAppPoolName_Gracefully()
	{
		// Arrange
		string envPath = @"C:\inetpub\wwwroot\Creatio";
		EnvironmentSettings env = new() { EnvironmentPath = envPath };
		StopOptions options = new() { Environment = "production", IsSilent = true };

		_settingsRepository.GetEnvironment("production").Returns(env);

		var iisSites = new List<IISSiteInfo>
		{
			new IISSiteInfo
			{
				AppPoolName = "Unknown",
				PhysicalPath = envPath
			}
		};

		_iisSiteDetector.GetSitesByPath(envPath).Returns(Task.FromResult(iisSites));
		_serviceManager.IsServiceRunning(Arg.Any<string>()).Returns(Task.FromResult(false));

		// Act
		int result = _command.Execute(options);

		// Assert
		_iisAppPoolManager.DidNotReceive().StopAppPool("Unknown");
	}
}
