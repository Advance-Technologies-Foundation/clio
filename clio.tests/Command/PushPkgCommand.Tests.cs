using System;
using Clio.Command;
using Clio.Package;
using Clio.Project;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class PushPkgCommandTestCase : BaseCommandTests<PushPkgOptions>
{

	private readonly ICompileConfigurationCommand _compileConfigurationCommand = Substitute.For<ICompileConfigurationCommand>();
	private readonly IPackageInstaller _packageInstaller = Substitute.For<IPackageInstaller>();
	private readonly IMarketplace _marketplace = Substitute.For<IMarketplace>();

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_compileConfigurationCommand);
		containerBuilder.AddSingleton(_packageInstaller);
		containerBuilder.AddSingleton(_marketplace);
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

}