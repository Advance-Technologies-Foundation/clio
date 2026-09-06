using System;
using System.IO.Abstractions;
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
[Category("Unit")]
[Property("Module", "Command")]
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
		ILogger logger = Substitute.For<ILogger>();

		SetFsmConfigCommand setFsmConfigCommand = Substitute.ForPartsOf<SetFsmConfigCommand>(
			validator,
			settingsRepository,
			new Clio.Common.FileSystem(new System.IO.Abstractions.FileSystem()),
			logger,
			Substitute.For<Clio.Requests.IIisScanner>(),
			Substitute.For<IFsmModeStatusService>(),
			Substitute.For<Clio.Package.IFileDesignModePackages>());
		setFsmConfigCommand.Execute(Arg.Any<SetFsmConfigOptions>()).Returns(0);

		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		// The loader reports whether the packages were actually loaded; turn-fsm derives its own exit
		// code from it, so the successful-retry scenario must arrange a successful load.
		fileDesignModePackages.LoadPackagesToFileSystem().Returns(FileDesignModeLoadResult.Completed);
		LoadPackagesToFileSystemCommand loadToFs = new(fileDesignModePackages, logger);

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
		RestartCommand restartCommand = Substitute.ForPartsOf<RestartCommand>(
			applicationClient, envSettings, Substitute.For<IServerReadinessWaiter>());
		restartCommand.Execute(Arg.Any<RestartOptions>()).Returns(0);

		IRetryDelay retryDelay = Substitute.For<IRetryDelay>();
		TurnFsmCommand command = new(setFsmConfigCommand, loadToFs, loadToDb, applicationClient, envSettings,
			restartCommand, Substitute.For<Clio.Common.ILogger>(), retryDelay);
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
		retryDelay.Received(2).Wait(TimeSpan.FromSeconds(3));
		fileDesignModePackages.Received(1).LoadPackagesToFileSystem();
	}

	[Test]
	[Description("Applies the file system mode configuration when turning FSM off on an environment that already reports file design mode as disabled, because that state is the goal of the off direction and never had anything to import.")]
	public void Execute_WritesConfiguration_WhenTurningFsmOff_OnAlreadyDisabledEnvironment() {
		// Arrange
		TurnFsmTestContext context = BuildContext();
		context.FileDesignModePackages.LoadPackagesToDb().Returns(FileDesignModeLoadResult.FileDesignModeDisabled);
		TurnFsmCommandOptions options = new() { IsFsm = "off", Uri = EnvironmentUri };

		// Act
		int result = context.Command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "an environment that already has file design mode disabled is in the target state of " +
			"'turn-fsm off', so the command must finish by writing the configuration instead of failing");
		context.SetFsmConfigCommand.Received(1).Execute(options);
	}

	[Test]
	[Description("Fails and leaves the configuration unchanged when turning FSM off and the platform refuses the database import, because unimported file system work would otherwise be orphaned.")]
	public void Execute_LeavesConfigurationUnchanged_WhenTurningFsmOff_AndImportIsRefused() {
		// Arrange
		TurnFsmTestContext context = BuildContext();
		context.FileDesignModePackages.LoadPackagesToDb().Returns(FileDesignModeLoadResult.LoadRefused);
		TurnFsmCommandOptions options = new() { IsFsm = "off", Uri = EnvironmentUri };

		// Act
		int result = context.Command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "a refused import means file system work has not reached the database, so switching file " +
			"system mode off would orphan it");
		context.SetFsmConfigCommand.DidNotReceive().Execute(Arg.Any<SetFsmConfigOptions>());
	}

	[Test]
	[Description("Returns a non-zero exit code when turning FSM on and the file system export fails, even though the configuration has already been applied at that point.")]
	public void Execute_ReturnsOne_WhenTurningFsmOn_AndFileSystemExportFails() {
		// Arrange
		TurnFsmTestContext context = BuildContext();
		context.FileDesignModePackages.LoadPackagesToFileSystem().Returns(FileDesignModeLoadResult.LoadRefused);
		TurnFsmCommandOptions options = new() { IsFsm = "on", Uri = EnvironmentUri };

		// Act
		int result = context.Command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "packages that were not exported must not be reported to the caller as a completed switch " +
			"to file system mode");
		context.SetFsmConfigCommand.Received(1).Execute(options);
		context.FileDesignModePackages.Received(1).LoadPackagesToFileSystem();
	}

	private const string EnvironmentUri = "http://localhost:1919";

	// A .NET Framework environment is used so the on-direction skips the restart-and-relogin block and the
	// test exercises only the export step whose exit code changed.
	private static TurnFsmTestContext BuildContext() {
		IValidator<SetFsmConfigOptions> validator = Substitute.For<IValidator<SetFsmConfigOptions>>();
		validator.Validate(Arg.Any<SetFsmConfigOptions>()).Returns(new ValidationResult());
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>()).Returns(new EnvironmentSettings { IsNetCore = false });
		settingsRepository.GetEnvironment(Arg.Any<string>()).Returns(new EnvironmentSettings { IsNetCore = false });
		ILogger logger = Substitute.For<ILogger>();
		SetFsmConfigCommand setFsmConfigCommand = Substitute.ForPartsOf<SetFsmConfigCommand>(
			validator,
			settingsRepository,
			new Clio.Common.FileSystem(new System.IO.Abstractions.FileSystem()),
			logger,
			Substitute.For<Clio.Requests.IIisScanner>(),
			Substitute.For<IFsmModeStatusService>(),
			Substitute.For<IFileDesignModePackages>());
		setFsmConfigCommand.Execute(Arg.Any<SetFsmConfigOptions>()).Returns(0);
		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings environmentSettings = new() { IsNetCore = false, Uri = EnvironmentUri };
		RestartCommand restartCommand = Substitute.ForPartsOf<RestartCommand>(
			applicationClient, environmentSettings, Substitute.For<IServerReadinessWaiter>());
		restartCommand.Execute(Arg.Any<RestartOptions>()).Returns(0);
		TurnFsmCommand command = new(setFsmConfigCommand,
			new LoadPackagesToFileSystemCommand(fileDesignModePackages, logger),
			new LoadPackagesToDbCommand(fileDesignModePackages, logger),
			applicationClient, environmentSettings, restartCommand, logger,
			Substitute.For<IRetryDelay>());
		return new TurnFsmTestContext(command, setFsmConfigCommand, fileDesignModePackages);
	}

	private sealed record TurnFsmTestContext(
		TurnFsmCommand Command,
		SetFsmConfigCommand SetFsmConfigCommand,
		IFileDesignModePackages FileDesignModePackages);
}
