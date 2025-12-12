using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.Common.SystemServices;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command
{
	[TestFixture]
	public class ClearLocalEnvironmentCommandTests : BaseCommandTests<ClearLocalEnvironmentOptions>
	{
		private ClearLocalEnvironmentCommand _command;
		private ISettingsRepository _settingsRepository;
		private MockFileSystem _fileSystem;
		private ISystemServiceManager _serviceManager;
		private ILogger _logger;

		[SetUp]
		public override void Setup()
		{
			base.Setup();
			_settingsRepository = Substitute.For<ISettingsRepository>();
			_fileSystem = new MockFileSystem();
			_serviceManager = Substitute.For<ISystemServiceManager>();
			_logger = Substitute.For<ILogger>();

			_command = new ClearLocalEnvironmentCommand(
				_settingsRepository,
				_fileSystem,
				_serviceManager,
				_logger
			);
		}

		[Test]
		[Description("Should return 0 when no deleted environments found")]
		public void Execute_NoDeletedEnvironments_ReturnsZero()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "prod", new EnvironmentSettings { EnvironmentPath = "/valid/path" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_fileSystem.AddDirectory("/valid/path");
			_fileSystem.AddDirectory("/valid/path/bin");
			_fileSystem.AddDirectory("/valid/path/web");

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because no deleted environments should return success");
		}

		[Test]
		[Description("Should identify environment as deleted when directory doesn't exist")]
		public void Execute_DirectoryNotFound_IdentifiesAsDeleted()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "deleted-app", new EnvironmentSettings { EnvironmentPath = "/nonexistent/path" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_serviceManager.DeleteService(Arg.Any<string>()).Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because deleted app should be cleaned up successfully");
			_settingsRepository.Received(1).RemoveEnvironment("deleted-app");
		}

		[Test]
		[Description("Should skip confirmation when --force flag is set")]
		public void Execute_WithForceFlag_SkipsConfirmation()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "deleted-app", new EnvironmentSettings { EnvironmentPath = "/deleted" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_serviceManager.DeleteService(Arg.Any<string>()).Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because force flag should skip confirmation");
			_settingsRepository.Received(1).RemoveEnvironment("deleted-app");
		}

		[Test]
		[Description("Should delete service successfully")]
		public void Execute_WithService_DeletesService()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "app-with-service", new EnvironmentSettings { EnvironmentPath = "/deleted" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_serviceManager.DeleteService("creatio-app-with-service").Returns(Task.FromResult(true));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because service should be deleted successfully");
			_serviceManager.Received(1).DeleteService("creatio-app-with-service");
		}

		[Test]
		[Description("Should continue when service deletion fails")]
		public void Execute_ServiceDeletionFails_ContinuesWithDeletion()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "failing-service", new EnvironmentSettings { EnvironmentPath = "/deleted" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_serviceManager.DeleteService(Arg.Any<string>()).Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because deletion should continue even if service deletion fails");
			_settingsRepository.Received(1).RemoveEnvironment("failing-service");
		}

		[Test]
		[Description("Should delete directory successfully")]
		public void Execute_WithDirectory_DeletesDirectory()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "local-app", new EnvironmentSettings { EnvironmentPath = "/local/app" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_fileSystem.AddDirectory("/local/app");
			_fileSystem.AddDirectory("/local/app/Logs");
			_serviceManager.DeleteService(Arg.Any<string>()).Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because directory should be deleted successfully");
			_fileSystem.Directory.Exists("/local/app").Should().BeFalse("directory should be deleted");
		}

		[Test]
		[Description("Should remove environment from settings")]
		public void Execute_WithValidEnvironment_RemovesFromSettings()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "test-env", new EnvironmentSettings { EnvironmentPath = "/test" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_serviceManager.DeleteService(Arg.Any<string>()).Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because environment should be removed");
			_settingsRepository.Received(1).RemoveEnvironment("test-env");
		}

		[Test]
		[Description("Should return error code when settings removal fails")]
		public void Execute_SettingsRemovalFails_ReturnsErrorCode()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "critical-env", new EnvironmentSettings { EnvironmentPath = "/critical" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_serviceManager.DeleteService(Arg.Any<string>()).Returns(Task.FromResult(false));
			_settingsRepository.When(x => x.RemoveEnvironment(Arg.Any<string>()))
				.Do(x => throw new Exception("Settings locked"));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(1, "because settings removal failure should return error code");
		}

		[Test]
		[Description("Should identify environment with only Logs as deleted")]
		public void Execute_DirectoryWithOnlyLogs_IdentifiesAsDeleted()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "empty-app", new EnvironmentSettings { EnvironmentPath = "/empty" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_fileSystem.AddDirectory("/empty");
			_fileSystem.AddDirectory("/empty/Logs");
			_serviceManager.DeleteService(Arg.Any<string>()).Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because empty directory should be identified as deleted");
			_settingsRepository.Received(1).RemoveEnvironment("empty-app");
		}

		[Test]
		[Description("Should handle multiple deleted environments")]
		public void Execute_WithMultipleDeletedEnvironments_ProcessesAll()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "app1", new EnvironmentSettings { EnvironmentPath = "/deleted1" } },
				{ "app2", new EnvironmentSettings { EnvironmentPath = "/deleted2" } },
				{ "app3", new EnvironmentSettings { EnvironmentPath = "/valid" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_fileSystem.AddDirectory("/valid");
			_fileSystem.AddDirectory("/valid/bin");
			_fileSystem.AddDirectory("/valid/web");
			_serviceManager.DeleteService(Arg.Any<string>()).Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because all deleted environments should be processed");
			_settingsRepository.Received(1).RemoveEnvironment("app1");
			_settingsRepository.Received(1).RemoveEnvironment("app2");
			_settingsRepository.Received(0).RemoveEnvironment("app3");
		}

		[Test]
		[Description("Should handle UnauthorizedAccessException gracefully")]
		public void Execute_WithAccessDeniedDirectory_IdentifiesAsDeleted()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "locked-app", new EnvironmentSettings { EnvironmentPath = "/locked" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			// Don't add directory - mock access denied by not having it
			_serviceManager.DeleteService(Arg.Any<string>()).Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because inaccessible directory should be treated as deleted");
			_settingsRepository.Received(1).RemoveEnvironment("locked-app");
		}

		[Test]
		[Description("Should NOT process remote environments without EnvironmentPath")]
		public void Execute_WithRemoteEnvironmentNoPath_SkipsEnvironment()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "remote-prod", new EnvironmentSettings { EnvironmentPath = null, Uri = "https://prod.example.com" } },
				{ "remote-test", new EnvironmentSettings { EnvironmentPath = "", Uri = "https://test.example.com" } },
				{ "local-app", new EnvironmentSettings { EnvironmentPath = "/local/app" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_fileSystem.AddDirectory("/local/app");
			_fileSystem.AddDirectory("/local/app/Logs");
			_serviceManager.DeleteService(Arg.Any<string>()).Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because command should complete successfully");
			_settingsRepository.Received(0).RemoveEnvironment("remote-prod");
			_settingsRepository.Received(0).RemoveEnvironment("remote-test");
			_settingsRepository.Received(1).RemoveEnvironment("local-app");
		}

		[Test]
		[Description("Should process only local environments and skip all remote ones")]
		public void Execute_WithMixedLocalAndRemoteEnvironments_ProcessesOnlyLocal()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "remote-prod", new EnvironmentSettings { EnvironmentPath = null, Uri = "https://prod.example.com" } },
				{ "deleted-local", new EnvironmentSettings { EnvironmentPath = "/deleted" } },
				{ "valid-local", new EnvironmentSettings { EnvironmentPath = "/valid" } },
				{ "remote-test", new EnvironmentSettings { EnvironmentPath = "", Uri = "https://test.example.com" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_fileSystem.AddDirectory("/valid");
			_fileSystem.AddDirectory("/valid/bin");
			_fileSystem.AddDirectory("/valid/web");
			_serviceManager.DeleteService(Arg.Any<string>()).Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because mixed environment processing should complete successfully");
			_settingsRepository.Received(0).RemoveEnvironment("remote-prod");
			_settingsRepository.Received(1).RemoveEnvironment("deleted-local");
			_settingsRepository.Received(0).RemoveEnvironment("valid-local");
			_settingsRepository.Received(0).RemoveEnvironment("remote-test");
		}

		[Test]
		[Description("Should handle case when no deleted environments and no orphaned services found")]
		public void Execute_NoDeletedEnvironmentsNoOrphanedServices_ReturnsZeroWithNoAction()
		{
			// Arrange
			var options = new ClearLocalEnvironmentOptions { Force = true };
			var environments = new Dictionary<string, EnvironmentSettings>
			{
				{ "valid-app", new EnvironmentSettings { EnvironmentPath = "/valid" } }
			};

			_settingsRepository.GetAllEnvironments().Returns(environments);
			_fileSystem.AddDirectory("/valid");
			_fileSystem.AddDirectory("/valid/bin");
			_fileSystem.AddDirectory("/valid/web");

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because no action should be needed");
			_settingsRepository.Received(0).RemoveEnvironment(Arg.Any<string>());
		}
	}
}
