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

}