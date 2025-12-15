using System;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.Update;
using Clio.Common;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command {

	[TestFixture]
	public class UpdateCliCommandTests : BaseCommandTests<UpdateCliOptions> {

		private IAppUpdater _mockAppUpdater;
		private IUserPromptService _mockPromptService;
		private ILogger _mockLogger;
		private UpdateCliCommand _command;

		[SetUp]
		public override void Setup() {
			base.Setup();
			_mockAppUpdater = Substitute.For<IAppUpdater>();
			_mockPromptService = Substitute.For<IUserPromptService>();
			_mockLogger = Substitute.For<ILogger>();
			_command = new UpdateCliCommand(_mockAppUpdater, _mockPromptService, _mockLogger);
		}

		[Test]
		[Description("Should return 0 when already on latest version")]
		public void Execute_AlreadyLatestVersion_ReturnsZero() {
			// Arrange
			var options = new UpdateCliOptions { Global = true, NoPrompt = false };
			_mockAppUpdater.GetCurrentVersion().Returns("8.0.1.85");
			_mockAppUpdater.GetLatestVersionFromNuget().Returns("8.0.1.85");
			_mockAppUpdater.IsUpdateAvailableAsync().Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because update is not available");
		}

		[Test]
		[Description("Should return 1 when user declines update")]
		public void Execute_UserDeclinesUpdate_ReturnsOne() {
			// Arrange
			var options = new UpdateCliOptions { Global = true, NoPrompt = false };
			_mockAppUpdater.GetCurrentVersion().Returns("8.0.1.80");
			_mockAppUpdater.GetLatestVersionFromNuget().Returns("8.0.1.85");
			_mockAppUpdater.IsUpdateAvailableAsync().Returns(Task.FromResult(true));
			_mockPromptService.PromptForConfirmationAsync("8.0.1.80", "8.0.1.85").Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(1, "because user cancelled the update");
		}

		[Test]
		[Description("Should return 0 when update succeeds with user confirmation")]
		public void Execute_UpdateSucceedsWithConfirmation_ReturnsZero() {
			// Arrange
			var options = new UpdateCliOptions { Global = true, NoPrompt = false };
			_mockAppUpdater.GetCurrentVersion().Returns("8.0.1.80");
			_mockAppUpdater.GetLatestVersionFromNuget().Returns("8.0.1.85");
			_mockAppUpdater.IsUpdateAvailableAsync().Returns(Task.FromResult(true));
			_mockPromptService.PromptForConfirmationAsync("8.0.1.80", "8.0.1.85").Returns(Task.FromResult(true));
			_mockAppUpdater.ExecuteUpdateAsync(true).Returns(Task.FromResult(0));
			_mockAppUpdater.VerifyInstallationAsync("8.0.1.85").Returns(Task.FromResult(true));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because update completed successfully");
		}

		[Test]
		[Description("Should skip prompt when NoPrompt option is enabled")]
		public void Execute_NoPromptOption_SkipsPromptAndUpdates() {
			// Arrange
			var options = new UpdateCliOptions { Global = true, NoPrompt = true };
			_mockAppUpdater.GetCurrentVersion().Returns("8.0.1.80");
			_mockAppUpdater.GetLatestVersionFromNuget().Returns("8.0.1.85");
			_mockAppUpdater.IsUpdateAvailableAsync().Returns(Task.FromResult(true));
			_mockAppUpdater.ExecuteUpdateAsync(true).Returns(Task.FromResult(0));
			_mockAppUpdater.VerifyInstallationAsync("8.0.1.85").Returns(Task.FromResult(true));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because update succeeds with auto-confirm");
			_mockPromptService.DidNotReceive().PromptForConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
		}

		[Test]
		[Description("Should return 1 when update execution fails")]
		public void Execute_UpdateFails_ReturnsOne() {
			// Arrange
			var options = new UpdateCliOptions { Global = true, NoPrompt = true };
			_mockAppUpdater.GetCurrentVersion().Returns("8.0.1.80");
			_mockAppUpdater.GetLatestVersionFromNuget().Returns("8.0.1.85");
			_mockAppUpdater.IsUpdateAvailableAsync().Returns(Task.FromResult(true));
			_mockAppUpdater.ExecuteUpdateAsync(true).Returns(Task.FromResult(1));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(1, "because update execution failed");
		}

		[Test]
		[Description("Should return 1 when installation verification fails")]
		public void Execute_VerificationFails_ReturnsOne() {
			// Arrange
			var options = new UpdateCliOptions { Global = true, NoPrompt = true };
			_mockAppUpdater.GetCurrentVersion().Returns("8.0.1.80");
			_mockAppUpdater.GetLatestVersionFromNuget().Returns("8.0.1.85");
			_mockAppUpdater.IsUpdateAvailableAsync().Returns(Task.FromResult(true));
			_mockAppUpdater.ExecuteUpdateAsync(true).Returns(Task.FromResult(0));
			_mockAppUpdater.VerifyInstallationAsync("8.0.1.85").Returns(Task.FromResult(false));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(1, "because installation verification failed");
		}

		[Test]
		[Description("Should return 2 when unable to check for updates")]
		public void Execute_UnableToCheckVersion_ReturnsTwo() {
			// Arrange
			var options = new UpdateCliOptions { Global = true, NoPrompt = false };
			_mockAppUpdater.GetCurrentVersion().Returns("");
			_mockAppUpdater.GetLatestVersionFromNuget().Returns((string)null);

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(2, "because unable to detect versions");
		}

		[Test]
		[Description("Should pass Global option to ExecuteUpdateAsync")]
		public void Execute_GlobalOptionFalse_PassesOptionToExecuteAsync() {
			// Arrange
			var options = new UpdateCliOptions { Global = false, NoPrompt = true };
			_mockAppUpdater.GetCurrentVersion().Returns("8.0.1.80");
			_mockAppUpdater.GetLatestVersionFromNuget().Returns("8.0.1.85");
			_mockAppUpdater.IsUpdateAvailableAsync().Returns(Task.FromResult(true));
			_mockAppUpdater.ExecuteUpdateAsync(false).Returns(Task.FromResult(0));
			_mockAppUpdater.VerifyInstallationAsync("8.0.1.85").Returns(Task.FromResult(true));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because update succeeds");
			_mockAppUpdater.Received(1).ExecuteUpdateAsync(false);
		}

	}

}
