using System;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Project;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class PushPkgCommandTestCase : BaseCommandTests<PushPkgOptions>
{

	private ICompileConfigurationCommand _compileConfigurationCommand;
	private IPackageInstaller _packageInstaller;
	private IMarketplace _marketplace;
	private ILogger _logger;

	/// <remarks>
	/// The substitutes are created here rather than in field initializers because
	/// <see cref="BaseClioModuleTests.Setup"/> calls this method for every test.
	/// <c>ClearReceivedCalls</c> would only drop recorded calls, not the configured
	/// <c>Returns</c>/<c>Arg.Do</c> behavior, so a throwing or sequenced stub set up by one test
	/// would leak into the next one.
	/// </remarks>
	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_compileConfigurationCommand = Substitute.For<ICompileConfigurationCommand>();
		_packageInstaller = Substitute.For<IPackageInstaller>();
		_marketplace = Substitute.For<IMarketplace>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_compileConfigurationCommand);
		containerBuilder.AddSingleton(_packageInstaller);
		containerBuilder.AddSingleton(_marketplace);
		containerBuilder.AddSingleton(_logger);
	}

	public override void TearDown() {
		base.TearDown();
		_compileConfigurationCommand.ClearReceivedCalls();
		_packageInstaller.ClearReceivedCalls();
		_marketplace.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test, Category("Unit")]
	public void Execute_RunsForceCompilation() {
		_compileConfigurationCommand.ClearReceivedCalls();
		PushPackageCommand command = Container.GetRequiredService<PushPackageCommand>();
		PushPkgOptions options = new() {
			ForceCompilation = true
		};
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
				Arg.Any<PackageInstallOptions>(), Arg.Any<string>())
			.Returns(true);
		_compileConfigurationCommand.Execute(Arg.Any<CompileConfigurationOptions>())
			.Returns(0);
		int result = command.Execute(options);
		result.Should().Be(0);
		_compileConfigurationCommand.Received(1).Execute(Arg.Any<CompileConfigurationOptions>());
	}

	[Test, Category("Unit")]
	public void Execute_DoesNotRunningCompilation_WhenInstallFails() {
		PushPackageCommand command = Container.GetRequiredService<PushPackageCommand>();
		PushPkgOptions options = new() {
			ForceCompilation = true
		};
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
				Arg.Any<PackageInstallOptions>(), Arg.Any<string>())
			.Returns(false);
		int result = command.Execute(options);
		result.Should().Be(1);
		_compileConfigurationCommand.DidNotReceive().Execute(Arg.Any<CompileConfigurationOptions>());
	}

	[Test, Category("Unit")]
	public void Execute_DoesNotRunningCompilation_WhenCompilationOptionsFalse() {
		PushPackageCommand command = Container.GetRequiredService<PushPackageCommand>();
		PushPkgOptions options = new() {
			ForceCompilation = false
		};
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
				Arg.Any<PackageInstallOptions>(), Arg.Any<string>())
			.Returns(true);
		int result = command.Execute(options);
		result.Should().Be(0);
		_compileConfigurationCommand.DidNotReceive().Execute(Arg.Any<CompileConfigurationOptions>());
	}

	[Test, Category("Unit")]
	public void Execute_ReturnsFalse_WhenCompilationFails() {
		_compileConfigurationCommand.ClearReceivedCalls();
		PushPackageCommand command = Container.GetRequiredService<PushPackageCommand>();
		PushPkgOptions options = new() {
			ForceCompilation = true
		};
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
				Arg.Any<PackageInstallOptions>(), Arg.Any<string>())
			.Returns(true);
		_compileConfigurationCommand.Execute(Arg.Any<CompileConfigurationOptions>())
			.Returns(1);
		int result = command.Execute(options);
		result.Should().Be(1);
	}

	[Test]
	[Description("Passes createBackup=true to the package installer when skip-backup is not specified so existing CLI behavior is preserved.")]
	public void Execute_Should_Preserve_Backup_When_SkipBackup_Is_Not_Specified() {
		// Arrange
		PushPackageCommand command = Container.GetRequiredService<PushPackageCommand>();
		PushPkgOptions options = new() {
			Name = "Pkg"
		};
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
				Arg.Any<PackageInstallOptions>(), Arg.Any<string>(), Arg.Any<bool>())
			.Returns(true);

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "the command should succeed when package installation succeeds");
		_packageInstaller.Received(1).Install(
			options.Name,
			Arg.Any<EnvironmentSettings>(),
			Arg.Any<PackageInstallOptions>(),
			options.ReportPath,
			true);
	}

	[Test]
	[Description("Passes createBackup=false to the package installer only when skip-backup is explicitly set to true.")]
	public void Execute_Should_Disable_Backup_When_SkipBackup_Is_True() {
		// Arrange
		PushPackageCommand command = Container.GetRequiredService<PushPackageCommand>();
		PushPkgOptions options = new() {
			Name = "Pkg",
			SkipBackup = true
		};
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
				Arg.Any<PackageInstallOptions>(), Arg.Any<string>(), Arg.Any<bool>())
			.Returns(true);

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "the command should still install successfully when backup is explicitly skipped");
		_packageInstaller.Received(1).Install(
			options.Name,
			Arg.Any<EnvironmentSettings>(),
			Arg.Any<PackageInstallOptions>(),
			options.ReportPath,
			false);
	}

	[Test]
	[Description("GH-1299: a completed installation prints \"Done\" and exits 0, so a green install stays green.")]
	public void Execute_ShouldReportDoneAndExitZero_WhenInstallationSucceeds() {
		// Arrange
		PushPackageCommand command = Container.GetRequiredService<PushPackageCommand>();
		PushPkgOptions options = new() { Name = "UsrIssue1299.gz" };
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
				Arg.Any<PackageInstallOptions>(), Arg.Any<string>(), Arg.Any<bool>())
			.Returns(true);

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a completed installation must exit successfully");
		_logger.Received(1).WriteLine("Done");
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("GH-1299: a genuine installation failure exits non-zero with a message that names the package instead of the bare \"Error\" line.")]
	public void Execute_ShouldReportTheFailedPackage_WhenInstallationFails() {
		// Arrange
		PushPackageCommand command = Container.GetRequiredService<PushPackageCommand>();
		PushPkgOptions options = new() { Name = "UsrIssue1299.gz" };
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
				Arg.Any<PackageInstallOptions>(), Arg.Any<string>(), Arg.Any<bool>())
			.Returns(false);
		string reportedError = null;
		_logger.WriteError(Arg.Do<string>(value => reportedError = value));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a real installation failure must keep the non-zero exit code");
		reportedError.Should().NotBeNullOrWhiteSpace(
			because: "the closing line used to be the bare word \"Error\", which said nothing (GH-1299)");
		reportedError.Should().Contain("UsrIssue1299.gz",
			because: "the operator has to learn which package failed");
	}

	[Test]
	[Description("GH-1299: a failed marketplace installation names the application ids, because no package name is supplied on that path.")]
	public void Execute_ShouldReportTheFailedMarketplaceIds_WhenMarketplaceInstallationFails() {
		// Arrange
		PushPackageCommand command = Container.GetRequiredService<PushPackageCommand>();
		PushPkgOptions options = new() { MarketplaceIds = new[] {1299} };
		_marketplace.GetFileByIdAsync(Arg.Any<int>()).Returns(Task.FromResult("app.gz"));
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
				Arg.Any<PackageInstallOptions>(), Arg.Any<string>(), Arg.Any<bool>())
			.Returns(false);
		string reportedError = null;
		_logger.WriteError(Arg.Do<string>(value => reportedError = value));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "the marketplace loop used to overwrite the outcome with success regardless of the installs");
		reportedError.Should().Contain("1299",
			because: "with no package name the failing marketplace application id is the only identifier available");
	}

	[Test]
	[Description("GH-1299: an exception whose Message is empty still produces a non-empty error line naming the exception type.")]
	public void Execute_ShouldReportTheExceptionType_WhenTheThrownMessageIsEmpty() {
		// Arrange
		PushPackageCommand command = Container.GetRequiredService<PushPackageCommand>();
		PushPkgOptions options = new() { Name = "UsrIssue1299.gz" };
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
				Arg.Any<PackageInstallOptions>(), Arg.Any<string>(), Arg.Any<bool>())
			.Returns(_ => throw new InvalidOperationException(string.Empty));
		string firstError = null;
		_logger.WriteError(Arg.Do<string>(value => firstError ??= value));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "an exception during installation is a genuine failure");
		firstError.Should().Contain(nameof(InvalidOperationException),
			because: "an empty exception message would otherwise leave the operator with a blank error line");
	}

	[Test]
	[Description("GH-1299: a partially failed marketplace batch names only the applications that failed, not the ones that installed.")]
	public void Execute_ShouldNameOnlyTheFailedIds_WhenPartOfAMarketplaceBatchFails() {
		// Arrange
		PushPackageCommand command = Container.GetRequiredService<PushPackageCommand>();
		PushPkgOptions options = new() { MarketplaceIds = new[] {1299, 1300} };
		_marketplace.GetFileByIdAsync(Arg.Any<int>()).Returns(Task.FromResult("app.gz"));
		_packageInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>(),
				Arg.Any<PackageInstallOptions>(), Arg.Any<string>(), Arg.Any<bool>())
			.Returns(true, false);
		string reportedError = null;
		_logger.WriteError(Arg.Do<string>(value => reportedError = value));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, because: "one application of the batch failed to install");
		reportedError.Should().Contain("1300",
			because: "the failing application id is what the operator has to act on");
		reportedError.Should().NotContain("1299",
			because: "the application that installed successfully must not be reported as failed");
	}

	[Test]
	[Description("GH-1299: BuildFailureMessage never returns a blank line, whatever identification the options carry.")]
	public void BuildFailureMessage_ShouldNeverBeBlank_WhenOptionsCarryNoIdentification() {
		// Act
		string message = PushPackageCommand.BuildFailureMessage(new PushPkgOptions());

		// Assert
		message.Should().NotBeNullOrWhiteSpace(
			because: "the closing failure line is the only thing a script operator sees");
	}

	[Test]
	[Description("GH-1299: the closing line carries only the identity of what failed, so it does not repeat the reason line the installer already wrote or point at a log that may not exist.")]
	public void BuildFailureMessage_ShouldCarryIdentityOnly_WhenThePackageIsNamed() {
		// Act
		string message = PushPackageCommand.BuildFailureMessage(new PushPkgOptions { Name = "UsrIssue1299.gz" });

		// Assert
		message.Should().Contain("UsrIssue1299.gz",
			because: "the identity of what failed is the one thing the installer's own reason line does not carry");
		message.Should().NotContain("Package installation failed",
			because: "BasePackageInstaller already writes \"Package installation failed: <reason>\" on the preceding line");
		message.Should().NotContain("See the installation log above",
			because: "no installation runs when the package is not found by path, so the suffix pointed at output that does not exist");
	}

}
