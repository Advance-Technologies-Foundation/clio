using System;
using System.Threading;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class TurnFsmCommandLoginRetryTests {

	[Test]
	[Description("Ensures TurnFsmCommand retries login after restart when the application is temporarily unavailable.")]
	public void Execute_RetriesLogin_AfterRestart() {
		// Arrange
		IValidator<SetFsmConfigOptions> validator = Substitute.For<IValidator<SetFsmConfigOptions>>();
		validator.Validate(Arg.Any<SetFsmConfigOptions>()).Returns(new ValidationResult());

		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>()).Returns(new EnvironmentSettings { IsNetCore = true });
		settingsRepository.GetEnvironment(Arg.Any<string>()).Returns(new EnvironmentSettings { IsNetCore = true });

		SetFsmConfigCommand setFsmConfigCommand = Substitute.ForPartsOf<SetFsmConfigCommand>(validator, settingsRepository);
		setFsmConfigCommand.Execute(Arg.Any<SetFsmConfigOptions>()).Returns(0);

		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		LoadPackagesToFileSystemCommand loadToFs = new(fileDesignModePackages);

		ILogger logger = Substitute.For<ILogger>();
		LoadPackagesToDbCommand loadToDb = new(fileDesignModePackages, logger);

		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{}");

		int loginAttempts = 0;
		applicationClient.When(c => c.Login()).Do(_ => {
			loginAttempts++;
			if (loginAttempts < 3) {
				throw new Exception("Connection refused");
			}
		});

		EnvironmentSettings envSettings = new() { IsNetCore = true, Uri = "http://localhost:1919" };
		RestartCommand restartCommand = Substitute.ForPartsOf<RestartCommand>(applicationClient, envSettings);
		restartCommand.Execute(Arg.Any<RestartOptions>()).Returns(0);

		TurnFsmCommand command = new(setFsmConfigCommand, loadToFs, loadToDb, applicationClient, envSettings, restartCommand);
		TurnFsmCommandOptions options = new() {
			IsFsm = "on",
			Uri = envSettings.Uri,
			IsNetCore = true
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the command should retry login until the application becomes available");
		loginAttempts.Should().BeGreaterOrEqualTo(3, "because it should retry login when the server is still restarting");
		fileDesignModePackages.Received(1).LoadPackagesToFileSystem();
	}
}
