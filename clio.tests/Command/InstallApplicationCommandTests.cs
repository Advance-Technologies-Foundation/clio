using System;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class InstallApplicationCommandTests : BaseCommandTests<InstallApplicationOptions> {
	private IApplicationInstaller _applicationInstaller;
	private ILogger _logger;
	private InstallApplicationCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationInstaller = Substitute.For<IApplicationInstaller>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_applicationInstaller);
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<InstallApplicationCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_applicationInstaller.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Logs a success info message and returns zero when the application installer completes successfully.")]
	public void Execute_Should_Log_Info_And_Return_Zero_On_Success() {
		// Arrange
		InstallApplicationOptions options = new() {
			Name = @"C:\Packages\app.gz",
			ReportPath = @"C:\Logs\install.log",
			CheckCompilationErrors = true
		};
		_applicationInstaller.Install(options.Name, Arg.Any<EnvironmentSettings>(), options.ReportPath, options.CheckCompilationErrors)
			.Returns(true);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "successful application installation should return a zero exit code");
		_applicationInstaller.Received(1).Install(
			options.Name,
			Arg.Any<EnvironmentSettings>(),
			options.ReportPath,
			options.CheckCompilationErrors);
		_logger.Received(1).WriteInfo("Done");
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("Logs an error message and returns one when the application installer reports failure.")]
	public void Execute_Should_Log_Error_And_Return_One_When_Installer_Fails() {
		// Arrange
		InstallApplicationOptions options = new() {
			Name = @"C:\Packages\app.gz"
		};
		_applicationInstaller.Install(options.Name, Arg.Any<EnvironmentSettings>(), options.ReportPath, options.CheckCompilationErrors)
			.Returns(false);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "failed application installation should return a non-zero exit code");
		_logger.Received(1).WriteError("Error");
		_logger.DidNotReceive().WriteInfo(Arg.Any<string>());
	}

	[Test]
	[Description("Logs a readable exception message and returns one when the application installer throws.")]
	public void Execute_Should_Log_Exception_And_Return_One_When_Installer_Throws() {
		// Arrange
		InstallApplicationOptions options = new() {
			Name = @"C:\Packages\app.gz"
		};
		_applicationInstaller.Install(options.Name, Arg.Any<EnvironmentSettings>(), options.ReportPath, options.CheckCompilationErrors)
			.Returns(_ => throw new InvalidOperationException("Install failed."));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "exceptional application installation should return a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(value =>
			value.Contains("Install failed.", StringComparison.Ordinal)));
		_logger.DidNotReceive().WriteInfo(Arg.Any<string>());
	}

	[Test]
	[Description("Returns five when Creatio reports that the application archive is an invalid GZip file.")]
	public void Execute_Should_Return_Five_When_Invalid_GZip_Archive_Is_Reported() {
		// Arrange
		InstallApplicationOptions options = new() {
			Name = @"C:\Packages\app.gz"
		};
		InvalidGZipArchiveInstallException exception = new("The file is invalid or corrupted.");
		_applicationInstaller.Install(options.Name, Arg.Any<EnvironmentSettings>(), options.ReportPath, options.CheckCompilationErrors)
			.Returns(_ => throw exception);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(5,
			because: "invalid GZip archive failures should have a dedicated exit code");
	}
}
